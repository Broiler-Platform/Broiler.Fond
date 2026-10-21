using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankDiscoveryTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Discovery.bank-responses.json")!);
        var vector = fixture.RootElement.GetProperty("vectors")[0];
        var context = vector.GetProperty("initialization").GetProperty("context");
        var origin = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Bytes(context, "originRequestBase64")));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Bytes(context, "procedureResponseBase64")));
        var procedures = new FinTsPinTanProcedureContext(origin, FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response));
        var selection = new FinTsPinTanSignatureSelection(context.GetProperty("profileVersion").GetInt32(), context.GetProperty("securityFunction").GetInt32(), context.GetProperty("tanSegmentVersion").GetInt32());
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_BR05_" + Guid.NewGuid().ToString("N"));
        const string iban = "DE89370400440532013000";
        using var pin = FinTsSessionCredential.CaptureAndClear("PIN-MARKER"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(3));
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"));
            session.CreateNewProfile(directory, "DiscoveryTests123!", "Discovery");
            ulong id = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://bank.example/fints");
            var manual = session.AddManualAccount(id, "My everyday account", iban, "EUR");
            var unsynced = await session.DiscoverBankAccountsCoreAsync(session.GetConnectionSetupContext(id), pin, procedures, selection,
                () => throw new InvalidOperationException("must not create transport"));
            check(unsynced.Outcome == BankDiscoveryOutcome.Blocked, "Discovery requires synchronized system state before dispatch.");
            session.GetStore()!.MutateAndSave(s => s with
            {
                Connections = s.Connections with
                {
                    Connections = s.Connections.Connections.Select(c =>
                c.Id == id ? c with { IsSynchronized = true, SystemId = "PUBLIC-SYSTEM", State = "Connected", LastSuccessfulContact = DateTimeOffset.UtcNow } : c).ToArray()
                }
            });
            session.ReloadData();
            ulong generation = session.Metadata!.Generation;
            var transport = Transport(balance: true);
            var discovery = await Discover(transport);
            check(discovery.Outcome == BankDiscoveryOutcome.ReviewRequired && discovery.Review is not null,
                $"Production discovery reaches explicit review after validated initialization, HKSPA and close: {discovery.Explanation}");
            if (discovery.Review is not { } review) return;
            check(transport.RecordedTransmissions.Count == 3 && transport.RecordedTransmissions.Any(t => Encoding.Latin1.GetString(t.Payload).Contains("HKSPA:3:1")),
                "Production discovery sends the signed HKSPA operation with actual dialogue counters.");
            check(session.Metadata.Generation == generation && session.CurrentSnapshot!.Accounts.Bindings.All(b => b.Connector == "Manual"),
                "Discovery alone cannot alter the profile or activate a manual binding.");
            var row = review.Accounts.Single();
            check(row.Match == BankDiscoveryMatch.ManualAccount && row.AccountId == manual.AccountId && row.DisplayName == "My everyday account",
                "Review proposes the unique same-connection manual identity and preserves its local display name.");
            check(row.BalanceVersion == 7, "Balance capability version derives from the accepted bank advertisement and qualified production builder.");
            check(new BroilerDialogService().ReviewBankAccountsAsync(review).GetAwaiter().GetResult() is null,
                "Discovery review without a window or headless reviewer cancels instead of silently authorizing an association.");
            var invalid = session.ConfirmBankAccountDiscovery(review, [999]);
            check(invalid.Outcome == BankDiscoveryOutcome.Blocked && session.Metadata.Generation == generation, "Invalid review selections cannot save or allocate identities.");
            var saved = session.ConfirmBankAccountDiscovery(review, [row.Index]);
            check(saved.Outcome == BankDiscoveryOutcome.Saved, $"A confirmed account association saves: {saved.Explanation}");
            var snapshot = session.CurrentSnapshot!;
            check(snapshot.Accounts.Accounts.Count == 1 && snapshot.Accounts.Accounts.Single().Id == manual.AccountId && snapshot.Accounts.Accounts.Single().DisplayName == "My everyday account",
                "Reviewed discovery preserves the manual account ID and name without creating an account shell.");
            check(snapshot.Accounts.Bindings.Count == 2 && snapshot.Accounts.Bindings.Any(b => b.Connector == "Manual" && b.State == "Quarantined") &&
                snapshot.Accounts.Bindings.Any(b => b.Connector == "fints" && b.State == "Active" && b.AccountId == manual.AccountId),
                "Bank provenance uses a newly allocated binding; the original manual binding is never relabeled or activated.");
            check(snapshot.Accounts.Bindings.Single(b => b.Connector == "fints").BankIdentifiers.Any(i => i.Kind == "bic" && i.Value == "PUBLIC-BIC"),
                "Bank binding retains the discovered BIC alongside UPD identity provenance for later qualified reads.");
            check(snapshot.Accounts.Capabilities.Single() is { OperationCode: "HKSAL", OperationVersion: 7, Status: "Active" } && snapshot.Balances.Balances.Count == 0,
                "Discovery saves the supported balance operation without inventing any balance.");
            check(!JsonSerializer.Serialize(snapshot).Contains("PIN-MARKER"), "Discovery stores no banking PIN marker.");
            check(session.ConfirmBankAccountDiscovery(review, [row.Index]).Outcome == BankDiscoveryOutcome.ContextChanged, "Review tokens cannot be replayed.");
            session.Lock(); session.Unlock(directory, "DiscoveryTests123!");
            check(session.CurrentSnapshot!.Accounts.Bindings.Any(b => b.Connector == "fints" && b.State == "Active") && session.CurrentSnapshot.Accounts.Capabilities.Single().OperationVersion == 7,
                "Verified bank identities, ledger references and capability versions survive profile reopening.");

            // A cached historical balance keeps its origin binding/incarnation through rediscovery and capability changes.
            session.GetStore()!.MutateAndSave(s =>
            {
                var binding = s.Accounts.Bindings.Single(b => b.Connector == "fints");
                ulong observation = s.Profile.NextId, balance = observation + 1;
                return s with
                {
                    Profile = s.Profile with { NextId = balance + 1 },
                    Balances = s.Balances with
                    {
                        Balances = [new(balance, observation, binding.AccountId, binding.IncarnationId, binding.Id,
                        "Booked", "123.45", "EUR", null, DateTimeOffset.UtcNow, true, "Historical test observation")]
                    },
                    Ledger = s.Ledger with
                    {
                        Records = [.. s.Ledger.Records,
                        new StoreLedgerRecord(observation, "Observation", null, [(binding.Id, "AccountBinding")]),
                        new StoreLedgerRecord(balance, "BalanceSnapshot", null, [(observation, "Observation")])]
                    }
                };
            });
            session.ReloadData();
            var historicalBalance = session.CurrentSnapshot!.Balances.Balances.Single();

            var repeated = await Discover(Transport(balance: true));
            check(repeated.Review!.Accounts.Single().Match == BankDiscoveryMatch.KnownBinding, "Repeated discovery reuses the exact active source identity.");
            session.ConfirmBankAccountDiscovery(repeated.Review, []);
            check(session.CurrentSnapshot!.Accounts.Bindings.Count == 2 && session.CurrentSnapshot.Accounts.Accounts.Count == 1,
                "Repeated discovery creates no duplicate bank binding or local account.");
            check(session.CurrentSnapshot.Balances.Balances.Single() == historicalBalance,
                "Discovery never rewrites a cached balance or its historical account/binding/incarnation provenance.");
            var unsupported = await Discover(Transport());
            check(unsupported.Review!.Accounts.Single() is { Match: BankDiscoveryMatch.KnownBinding, BalanceVersion: null },
                "Missing balance advertisement is unavailable instead of inferred from a known account.");
            session.ConfirmBankAccountDiscovery(unsupported.Review, []);
            check(session.CurrentSnapshot!.Accounts.Capabilities.All(c => c.OperationCode != "HKSAL"), "A new discovery revokes previously cached unsupported balance capabilities.");

            var changed = await Discover(Transport(balance: true));
            ulong beforeChanged = session.Metadata!.Generation;
            session.AddInstitutionAndConnection("Other bank", "OTHER", "https://other.example/fints", "other-user");
            check(session.ConfirmBankAccountDiscovery(changed.Review!, []).Outcome == BankDiscoveryOutcome.ContextChanged && session.Metadata.Generation == beforeChanged + 1,
                "A profile change invalidates reviewed discovery before saving stale identities.");
            var closeFailure = Transport(balance: true, closeFailure: true);
            ulong beforeFailed = session.Metadata.Generation;
            var failed = await Discover(closeFailure);
            check(failed.Review is null && failed.Outcome == BankDiscoveryOutcome.Failed && session.Metadata.Generation == beforeFailed,
                "A failed dialogue close cannot produce review evidence or mutate profile identities.");

            var blocked = await Discover(Transport(balance: true, currency: "USD"));
            check(blocked.Review!.Accounts.All(a => !a.CanConfirm), "A changed bank currency cannot merge into an existing account lifetime.");
            check(session.ConfirmBankAccountDiscovery(blocked.Review, [0]).Outcome == BankDiscoveryOutcome.Blocked,
                "Caller-supplied review indexes cannot override a currency or identity conflict.");

            FinTsSessionCredential? ownedPin = null;
            var dialogs = new BroilerDialogService
            {
                HeadlessBankCredentialHandler = (_, _) =>
                {
                    ownedPin = FinTsSessionCredential.CaptureAndClear("PIN-MARKER"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1));
                    return Task.FromResult<FinTsSessionCredential?>(ownedPin);
                },
                HeadlessBankDiscoveryReviewHandler = (_, _) =>
                {
                    check(ownedPin!.GetSnapshot().State == FinTsCredentialState.Disposed, "Banking PIN is disposed before credential-free account review opens.");
                    return Task.FromResult<IReadOnlyCollection<int>?>(null);
                }
            };
            ulong beforeDeclined = session.Metadata.Generation;
            var declined = await session.DiscoverAndReviewBankAccountsCoreAsync(session.GetConnectionSetupContext(id), dialogs, dialogs,
                procedures, selection, () => Transport(balance: true));
            check(declined.Outcome == BankDiscoveryOutcome.Cancelled && session.Metadata.Generation == beforeDeclined,
                "Cancelling authenticated discovery review preserves all saved identities and capabilities.");
            dialogs.HeadlessBankDiscoveryReviewHandler = (r, _) => Task.FromResult<IReadOnlyCollection<int>?>([]);
            EventHandler brokenDisplay = (_, _) => throw new InvalidOperationException("display failure after commit");
            session.StateChanged += brokenDisplay;
            var approved = await session.DiscoverAndReviewBankAccountsCoreAsync(session.GetConnectionSetupContext(id), dialogs, dialogs,
                procedures, selection, () => Transport(balance: true));
            session.StateChanged -= brokenDisplay;
            check(approved.Outcome == BankDiscoveryOutcome.Saved && ownedPin!.GetSnapshot().State == FinTsCredentialState.Disposed,
                "Authenticated discovery reports a committed save even when a display observer fails after commit.");

            var duplicates = await Discover(Transport(balance: true, discoveryBody: body =>
                body.Replace("HISPA:4:1:3+", "HISPA:4:1:3+J:DE89370400440532013000:PUBLIC-BIC:PUBLIC-001::280:PUBLIC-BANK+", StringComparison.Ordinal)));
            check(duplicates.Review is null || duplicates.Review.Accounts.All(a => !a.CanConfirm && a.Match != BankDiscoveryMatch.KnownBinding),
                "Duplicate bank-reported account occurrences cannot authorize reuse or a manual association.");

            var absent = await Discover(Transport(balance: true, unavailable: true));
            check(absent.Review is null && absent.Outcome is BankDiscoveryOutcome.Failed or BankDiscoveryOutcome.Unsupported,
                "An unavailable discovery produces no account authorization or review evidence.");
            check(session.CurrentSnapshot!.Accounts.Bindings.Any(b => b.Connector == "fints" && b.State == "Active"),
                "Transient discovery unavailability preserves prior saved bank bindings.");

            session.GetStore()!.MutateAndSave(s => s with
            {
                Accounts = s.Accounts with
                {
                    Incarnations = s.Accounts.Incarnations.Select(i =>
                i.AccountId == manual.AccountId ? i with { LifetimeEvidence = "ClosedReported" } : i).ToArray()
                }
            });
            session.ReloadData();
            var closed = await Discover(Transport(balance: true));
            check(closed.Review!.Accounts.All(a => !a.CanConfirm && a.Match != BankDiscoveryMatch.KnownBinding),
                "A closed account incarnation cannot be reused merely because a bank reports the same IBAN again.");
            session.GetStore()!.MutateAndSave(s => s with
            {
                Accounts = s.Accounts with
                {
                    Incarnations = s.Accounts.Incarnations.Select(i =>
                i.AccountId == manual.AccountId ? i with { LifetimeEvidence = "Unspecified" } : i).ToArray()
                }
            });
            session.ReloadData();

            ulong otherId = session.AddInstitutionAndConnection("Separate connection", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(otherId), "https://bank.example/fints");
            session.GetStore()!.MutateAndSave(s => s with
            {
                Connections = s.Connections with
                {
                    Connections = s.Connections.Connections.Select(c =>
                c.Id == otherId ? c with { IsSynchronized = true, SystemId = "PUBLIC-SYSTEM", State = "Connected", LastSuccessfulContact = DateTimeOffset.UtcNow } : c).ToArray()
                }
            });
            session.ReloadData();
            var other = await session.DiscoverBankAccountsCoreAsync(session.GetConnectionSetupContext(otherId), pin, procedures, selection, () => Transport(balance: true));
            check(other.Review!.Accounts.Single() is { Match: BankDiscoveryMatch.NewAccount, AccountId: null },
                "The same IBAN under a separate bank connection never automatically merges into a manual or verified account.");
            var otherSaved = session.ConfirmBankAccountDiscovery(other.Review, [0]);
            check(otherSaved.Outcome == BankDiscoveryOutcome.Saved && session.CurrentSnapshot!.Accounts.Accounts.Count == 2 &&
                session.CurrentSnapshot.Accounts.Bindings.Single(b => b.ConnectionId == otherId).AccountId != manual.AccountId,
                "Explicitly accepting a separate bank source creates a distinct local identity through the ledger.");
            check(session.CurrentSnapshot!.Balances.Balances.Single() == historicalBalance, "Adding a separate bank account preserves historical balances of the original account.");

            var current = await Discover(Transport(balance: true));
            var completeListWithoutOldSource = new BankDiscoveryReview(session, current.Review!.Context, current.Review.Connection, []);
            var absentSnapshot = BankDiscoveryPlanner.Apply(session.CurrentSnapshot!, completeListWithoutOldSource, []);
            check(absentSnapshot.Accounts.Bindings.Single(b => b.ConnectionId == id && b.Connector == "fints").State == "Quarantined" &&
                absentSnapshot.Accounts.Capabilities.All(c => c.ConnectionId != id || c.OperationCode != "HKSAL") &&
                absentSnapshot.Balances.Balances.Single() == historicalBalance,
                "The pure save planner revokes an absent source's permission while preserving its historical balances.");

            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var slow = new FinTsSimulatedTransport();
            slow.EnqueueHandler(async (_, _, ct) => { pending.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException(); });
            var running = Discover(slow);
            await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = await Discover(Transport());
            check(duplicate.Outcome == BankDiscoveryOutcome.Blocked, "Discovery shares the per-connection operation reservation with synchronization and refresh.");
            bool unexpectedPin = false;
            dialogs.HeadlessBankCredentialHandler = (_, _) => { unexpectedPin = true; return Task.FromResult<FinTsSessionCredential?>(null); };
            var busyPrompt = await session.DiscoverAndReviewBankAccountsCoreAsync(session.GetConnectionSetupContext(id), dialogs, dialogs,
                procedures, selection, () => throw new InvalidOperationException("must not dispatch"));
            check(busyPrompt.Outcome == BankDiscoveryOutcome.Blocked && !unexpectedPin, "Owned discovery reserves the connection before requesting another banking PIN.");
            session.Lock();
            var stale = await running.WaitAsync(TimeSpan.FromSeconds(5));
            check(stale.Outcome == BankDiscoveryOutcome.ContextChanged && stale.Review is null, "Profile lock cancels discovery and discards its uncommitted identity allocations.");

            Task<BankDiscoveryResult> Discover(FinTsSimulatedTransport t) => session.DiscoverBankAccountsCoreAsync(session.GetConnectionSetupContext(id), pin,
                procedures, selection, () => t);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        FinTsSimulatedTransport Transport(bool balance = false, bool closeFailure = false, string currency = "EUR",
            Func<string, string>? discoveryBody = null, bool unavailable = false)
        {
            var t = new FinTsSimulatedTransport();
            byte[] initialization = Bytes(vector.GetProperty("initialization"), "responseBase64");
            initialization = ChangeWrappedBody(initialization, body =>
            {
                body = body.Replace("+EUR+Public owner", $"+{currency}+Public owner", StringComparison.Ordinal);
                return balance ? body.Replace("++++HKSPA:1'", "++++HKSAL:1+HKSPA:1'", StringComparison.Ordinal)
                    .Replace("HISPAS:10:1:4", "HISALS:10:7:4+1+1+0'HISPAS:11:1:4", StringComparison.Ordinal) : body;
            });
            t.EnqueueResponse(initialization);
            byte[] discoveryWire = Bytes((unavailable ? fixture.RootElement.GetProperty("vectors")[1] : vector).GetProperty("allAccount"), "responseBase64");
            t.EnqueueResponse(discoveryBody is null ? discoveryWire : ChangeWrappedBody(discoveryWire, discoveryBody));
            if (closeFailure) t.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.NetworkFailure, "PIN-MARKER", new Uri("https://bank.example/fints")));
            else t.EnqueueResponse(Bytes(vector.GetProperty("closing"), "responseBase64"));
            return t;
        }
    }

    private static byte[] Bytes(JsonElement element, string key) => Convert.FromBase64String(element.GetProperty(key).GetString()!);
    internal static byte[] ChangeWrappedBody(byte[] wire, Func<string, string> change)
    {
        string raw = Encoding.Latin1.GetString(wire);
        var marker = Regex.Match(raw, "HNVSD:999:1\\+@([0-9]+)@");
        int start = marker.Index + marker.Length, length = int.Parse(marker.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        string body = change(raw.Substring(start, length));
        string result = raw[..marker.Index] + $"HNVSD:999:1+@{body.Length}@" + body + raw[(start + length)..];
        int tail = Regex.Matches(body, "(?:^|')([A-Z]{5,6}):([0-9]+):").Select(m => int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)).Max() + 1;
        result = Regex.Replace(result, "HNHBS:[0-9]+:1", $"HNHBS:{tail}:1");
        result = Regex.Replace(result, "(?<=HNHBK:1:3\\+)[0-9]{12}", result.Length.ToString("D12", System.Globalization.CultureInfo.InvariantCulture));
        return Encoding.Latin1.GetBytes(result);
    }
}
