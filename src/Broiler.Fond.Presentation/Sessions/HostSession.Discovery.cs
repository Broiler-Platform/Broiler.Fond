using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Presentation.Sessions;

public partial class HostSession
{
    /// <summary>Runs a closed discovery with caller-owned PIN. Does not mutate the profile until review is committed.</summary>
    public Task<BankDiscoveryResult> DiscoverBankAccountsAsync(ConnectionSetupContext context, FinTsSessionCredential pin,
        FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection, CancellationToken cancellationToken = default) =>
        DiscoverBankAccountsCoreAsync(context, pin, procedures, selection, null, cancellationToken);

    internal async Task<BankDiscoveryResult> DiscoverBankAccountsCoreAsync(ConnectionSetupContext context, FinTsSessionCredential pin,
        FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection,
        Func<IFinTsTransport>? transportFactory, CancellationToken cancellationToken = default, BankConnectionBatchLease? lease = null)
    {
        ArgumentNullException.ThrowIfNull(pin);
        bool reserved = false, entered = false, institutionEntered = false;
        var institutionGate = BankInstitutionGate(context.Institution.Id);
        StoreSnapshot Current() => lease is null ? RequireCurrentSetup(context) : RequireBatchConnection(context);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        void Invalidate()
        {
            lock (_syncRoot)
            {
                try { _ = Current(); }
                catch (InvalidOperationException) { try { _ = lifetime.CancelAsync(); } catch (ObjectDisposedException) { } }
            }
        }
        RefreshContextChanged += Invalidate;
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            StoreSnapshot snapshot;
            BankConnection connection;
            FinTsProductionCandidateFactory factory;
            lock (_syncRoot)
            {
                snapshot = Current();
                if (!ProductConfiguration.IsConfigured || context.Institution.VerificationState != "Confirmed")
                    return new(BankDiscoveryOutcome.Blocked, "Confirm the bank endpoint and configure the assigned application product ID before discovery.");
                connection = BankConnectionSetup.ToDomain(context.Connection, context.Institution);
                if (!connection.IsSynchronized || connection.State != BankConnectionState.Connected || string.IsNullOrEmpty(connection.SystemId))
                    return new(BankDiscoveryOutcome.Blocked, "Synchronize this bank connection before discovering accounts.");
                factory = new(connection, ProductConfiguration.Registration!, procedures, selection);
                if (lease is not null)
                {
                    if (!lease.Matches(this, context.SessionEpoch, context.Connection.Id)) throw new InvalidOperationException("Invalid batch lease.");
                }
                else
                {
                    if (!TryReserveBankOperation(context.SessionEpoch, context.Connection.Id))
                        return new(BankDiscoveryOutcome.Blocked, "Another bank operation is already active for this connection.");
                    reserved = true;
                }
            }
            if (lease is null) { await institutionGate.WaitAsync(lifetime.Token); institutionEntered = true; }
            Invalidate();
            await WaitForBankOperationSlotAsync(lifetime.Token);
            entered = true;
            lifetime.Token.ThrowIfCancellationRequested();
            var versions = new Dictionary<AccountSourceLocator, int>();
            var options = FinTsReadExecutionOptions.Discovery(_ => factory.CreateInitialization(lifetime.Token),
                a => factory.CreateDiscovery(a, lifetime.Token), attempt =>
            {
                // Probe bounded production builders using UPD and the accepted HISPA BIC. No balance request is sent.
                var reported = attempt.AllAccountEvidence?.ReadData.Discovery.SelectMany(d => d.Accounts).ToArray() ?? [];
                foreach (var parameter in attempt.RequirementsEvidence!.ReadParameters!.Source.Accounts)
                {
                    var locator = FinTsAccountSourceLocatorFactory.CreateLocator(connection, parameter);
                    var fields = parameter.AccountConnection.Elements;
                    string Field(int i) => fields.Count > i ? fields[i].HeaderText() : "";
                    var matches = reported.Where(a => a.Iban == locator.Iban && a.Number == (locator.DomesticAccountNumber ?? "") &&
                        a.Subaccount == (locator.Subaccount ?? "") && a.Institution == (locator.DomesticBankCode ?? "") && a.Country == Field(2)).ToArray();
                    if (matches.Length != 1) continue;
                    try
                    {
                        var input = string.IsNullOrEmpty(locator.Iban) ? new FinTsReadAccountInput(Field(0), Field(1), Field(2), Field(3))
                            : new FinTsReadAccountInput(iban: locator.Iban, bic: matches[0].Bic);
                        var balance = factory.CreateBalance(attempt, parameter, input, lifetime.Token);
                        versions[BankDiscoveryPlanner.WithDiscoveryBic(connection, locator, matches[0])] = balance.Context.Signature.Request.Request.Source.Version;
                    }
                    catch (Exception ex) when (ex is FinTsCandidateException or FinTsFormatException or ArgumentException)
                    { /* Unsupported balance operation remains unavailable for this account. */ }
                }
                return factory.CreateClosing(attempt, lifetime.Token);
            }, productRegistration: ProductConfiguration.Registration, allowAutomaticQueries: false);
            using var transport = transportFactory?.Invoke() ?? (lease is null ? CreateBankTransport(context) :
                new ConfirmedBankTransport(context, ProductConfiguration, () => { try { _ = Current(); return true; } catch (InvalidOperationException) { return false; } }));
            var result = await FinTsReadExecutionPipeline.ExecuteAsync(connection, transport, pin,
                BankDiscoveryPlanner.DetachedLedger(snapshot), BankDiscoveryPlanner.KnownBindings(snapshot, connection.Id.Value), options, lifetime.Token);
            Invalidate();
            lifetime.Token.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
                return new(result.Status == FinTsReadExecutionStatus.Cancelled ? BankDiscoveryOutcome.Cancelled :
                    result.Status is FinTsReadExecutionStatus.RejectedMissingCandidate or FinTsReadExecutionStatus.RejectedDialogueOutcome
                        ? BankDiscoveryOutcome.Unsupported : BankDiscoveryOutcome.Failed,
                    result.Status switch
                    {
                        FinTsReadExecutionStatus.RejectedTransportFailure => "The bank could not be reached or its connection failed. Check the confirmed endpoint and try discovery again. No profile data was saved.",
                        FinTsReadExecutionStatus.RejectedInvalidCredential => "An available banking PIN is required. Start discovery again; the profile passphrase cannot be used as a bank credential. No profile data was saved.",
                        FinTsReadExecutionStatus.RejectedDialogueOutcome => "The bank requires authentication review or an approval procedure this workflow cannot complete yet. No profile data was saved.",
                        FinTsReadExecutionStatus.RejectedMissingCandidate => "The bank's discovery request version or selected procedure is unsupported. No profile data was saved.",
                        _ => "Bank discovery did not return a supported, validated account list and closed dialogue. Previous account bindings and balances were retained."
                    }, ProtocolStatus: result.Status);
            lock (_syncRoot)
            {
                var latest = Current();
                return new(BankDiscoveryOutcome.ReviewRequired, "Review the bank-reported accounts before saving associations.",
                    BankDiscoveryPlanner.Create(this, lease is null ? context : GetConnectionSetupContext(context.Connection.Id),
                        lease is null ? snapshot : latest, result, versions));
            }
        }
        catch (OperationCanceledException)
        {
            return new(cancellationToken.IsCancellationRequested ? BankDiscoveryOutcome.Cancelled : BankDiscoveryOutcome.ContextChanged,
            "Discovery was cancelled or the profile/connection changed. No profile data was saved.");
        }
        catch (FinTsCandidateException)
        { return new(BankDiscoveryOutcome.Unsupported, "The selected bank procedure or discovery request is unsupported. No profile data was saved."); }
        catch (Exception)
        { return new(BankDiscoveryOutcome.Failed, "Bank account discovery failed. Review the connection and try again. No profile data was saved."); }
        finally
        {
            RefreshContextChanged -= Invalidate;
            if (entered) ReleaseBankOperationSlot();
            if (institutionEntered) institutionGate.Release();
            if (reserved) ReleaseBankOperation(context.SessionEpoch, context.Connection.Id);
        }
    }

    public BankDiscoveryResult ConfirmBankAccountDiscovery(BankDiscoveryReview review, IReadOnlyCollection<int> confirmedRows)
    {
        ArgumentNullException.ThrowIfNull(review); ArgumentNullException.ThrowIfNull(confirmedRows);
        bool committed = false;
        try
        {
            lock (_syncRoot)
            {
                if (!ReferenceEquals(review.Owner, this) || review.Consumed ||
                    System.Diagnostics.Stopwatch.GetElapsedTime(review.StartedAt) > TimeSpan.FromMinutes(10))
                    return new(BankDiscoveryOutcome.ContextChanged, "This discovery review expired or was already used. Discover the accounts again.");
                var snapshot = RequireCurrentSetup(review.Context);
                var updated = BankDiscoveryPlanner.Apply(snapshot, review, confirmedRows);
                // A failed save leaves the original profile authoritative; the token is not reusable after an attempted save.
                review.Consumed = true;
                _profileStore!.Save(updated);
                committed = true;
                ReloadDataInternal();
            }
            OnStateChanged();
            return new(BankDiscoveryOutcome.Saved, "Reviewed bank account bindings and qualified balance capabilities were saved. Existing names and history were preserved.");
        }
        catch (Exception) when (committed)
        { return new(BankDiscoveryOutcome.Saved, "Reviewed accounts were saved, but the display could not reload. Reopen the profile before continuing."); }
        catch (InvalidOperationException)
        { return new(BankDiscoveryOutcome.ContextChanged, "The profile or bank setup changed. Discover and review the accounts again."); }
        catch (ArgumentException)
        { return new(BankDiscoveryOutcome.Blocked, "The review included an unavailable or ambiguous association. No profile data was saved."); }
        catch (Exception)
        { return new(BankDiscoveryOutcome.Failed, "Bank accounts were received but could not be saved. Previous saved data was retained."); }
    }

    /// <summary>Owns the banking PIN only through discovery, then presents credential-free account review.</summary>
    public Task<BankDiscoveryResult> DiscoverAndReviewBankAccountsAsync(ConnectionSetupContext context, IBankCredentialPrompt prompt,
        IBankAccountDiscoveryReview reviewer, FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection,
        CancellationToken cancellationToken = default) =>
        DiscoverAndReviewBankAccountsCoreAsync(context, prompt, reviewer, procedures, selection, null, cancellationToken);

    internal async Task<BankDiscoveryResult> DiscoverAndReviewBankAccountsCoreAsync(ConnectionSetupContext context, IBankCredentialPrompt prompt,
        IBankAccountDiscoveryReview reviewer, FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection,
        Func<IFinTsTransport>? transportFactory, CancellationToken cancellationToken = default,
        Action? reviewing = null, Action? saving = null)
    {
        ArgumentNullException.ThrowIfNull(prompt); ArgumentNullException.ThrowIfNull(reviewer);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        FinTsSessionCredential? pin = null;
        using var clear = lifetime.Token.Register(() => { lock (_syncRoot) { pin?.Dispose(); } });
        void Invalidate()
        {
            lock (_syncRoot)
            {
                try { _ = RequireCurrentSetup(context); }
                catch (InvalidOperationException) { try { _ = lifetime.CancelAsync(); } catch (ObjectDisposedException) { } }
            }
        }
        RefreshContextChanged += Invalidate;
        bool entered = false, reserved = false, institutionEntered = false;
        var institutionGate = BankInstitutionGate(context.Institution.Id);
        try
        {
            lock (_syncRoot)
            {
                _ = RequireCurrentSetup(context);
                if (!ProductConfiguration.IsConfigured || context.Institution.VerificationState != "Confirmed" ||
                    !context.Connection.IsSynchronized || context.Connection.State != "Connected")
                    return new(BankDiscoveryOutcome.Blocked, "Confirm and synchronize the bank connection before account discovery.");
                _ = new FinTsProductionCandidateFactory(BankConnectionSetup.ToDomain(context.Connection, context.Institution),
                    ProductConfiguration.Registration!, procedures, selection);
            }
            lock (_syncRoot)
            {
                _ = RequireCurrentSetup(context);
                if (!TryReserveBankOperation(context.SessionEpoch, context.Connection.Id))
                    return new(BankDiscoveryOutcome.Blocked, "Another bank operation is already active for this connection. Wait or cancel it first.");
                reserved = true;
            }
            await institutionGate.WaitAsync(lifetime.Token); institutionEntered = true;
            using var lease = new BankConnectionBatchLease(this, context.SessionEpoch, context.Connection.Id);
            reserved = false;
            await BankPromptGate.WaitAsync(lifetime.Token);
            entered = true;
            Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
            var acquired = await prompt.AcquireBankCredentialAsync(new("Discover Bank Accounts", "Enter your banking PIN, not the profile passphrase.", context.Institution.DisplayName), lifetime.Token);
            lock (_syncRoot) { pin = acquired; }
            BankPromptGate.Release(); entered = false;
            Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
            if (pin is null) return new(BankDiscoveryOutcome.Cancelled, "Account discovery was cancelled.");
            var discovered = await DiscoverBankAccountsCoreAsync(context, pin, procedures, selection, transportFactory, lifetime.Token, lease);
            pin.Dispose(); pin = null;
            if (discovered.Review is not { } review) return discovered;
            await BankPromptGate.WaitAsync(lifetime.Token); entered = true;
            reviewing?.Invoke();
            var confirmed = await reviewer.ReviewBankAccountsAsync(review, lifetime.Token);
            BankPromptGate.Release(); entered = false;
            lifetime.Token.ThrowIfCancellationRequested();
            if (confirmed is null) return new(BankDiscoveryOutcome.Cancelled, "Discovery review was cancelled. No profile data was saved.");
            saving?.Invoke();
            lifetime.Token.ThrowIfCancellationRequested();
            return ConfirmBankAccountDiscovery(review, confirmed);
        }
        catch (OperationCanceledException)
        { return new(BankDiscoveryOutcome.Cancelled, "Account discovery or review was cancelled, expired, or its profile changed."); }
        catch (Exception)
        { return new(BankDiscoveryOutcome.Failed, "Account discovery could not be completed. No associations were saved."); }
        finally
        {
            RefreshContextChanged -= Invalidate;
            pin?.Dispose();
            if (entered) BankPromptGate.Release();
            if (institutionEntered) institutionGate.Release();
            if (reserved) ReleaseBankOperation(context.SessionEpoch, context.Connection.Id);
        }
    }
}
