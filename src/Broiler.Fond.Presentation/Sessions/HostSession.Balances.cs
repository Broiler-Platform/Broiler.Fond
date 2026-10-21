using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Connections.Orchestration;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Presentation.Sessions;

public partial class HostSession
{
    public BankBalanceContext GetBankBalanceContext(ulong accountId)
    {
        lock (_syncRoot)
        {
            var snapshot = RequireSetupSnapshot();
            var sources = snapshot.Accounts.Bindings.Where(b => b.AccountId == accountId && b.Connector == "fints" && b.State == "Active").ToArray();
            if (sources.Length != 1) throw new InvalidOperationException("Review and verify one bank source for this account before refreshing its balance.");
            var source = sources[0];
            var setup = GetConnectionSetupContext(source.ConnectionId);
            var known = BankDiscoveryPlanner.KnownBindings(snapshot, source.ConnectionId).Single(b => b.BindingId.Value == source.Id);
            var caps = snapshot.Accounts.Capabilities.Where(c => c.AccountId == accountId && c.ConnectionId == source.ConnectionId && c.OperationCode == "HKSAL" && c.Status == "Active").ToArray();
            if (caps.Length != 1 || caps[0].OperationVersion is not (6 or 7 or 8))
                throw new InvalidOperationException("Discover and review a supported balance capability for this account first.");
            var context = new BankBalanceContext(this, setup, known, snapshot.Accounts.Incarnations.Single(i => i.Id == source.IncarnationId), caps[0]);
            _ = RequireBalanceContext(context);
            return context;
        }
    }

    // Unrelated saves may advance the generation. Source identity, lifetime, capabilities and session epoch must remain exact.
    private StoreSnapshot RequireBalanceContext(BankBalanceContext context, StoreSnapshot? current = null)
    {
        var snapshot = current ?? RequireSetupSnapshot();
        var setup = context.Connection;
        if (!ReferenceEquals(context.Owner, this) || _refreshDisposed || _isSafeModeActive || _profileStore is null || _profileStore.IsReadOnly ||
            _refreshEpoch != setup.SessionEpoch || snapshot.Profile.ProfileId != setup.ProfileId || snapshot.Profile.BranchId != setup.BranchId ||
            !snapshot.Connections.Connections.Contains(setup.Connection) || !snapshot.Institutions.Institutions.Contains(setup.Institution) ||
            !ProductConfiguration.IsConfigured || setup.Institution.VerificationState != "Confirmed" || !setup.Connection.IsSynchronized ||
            setup.Connection.State != "Connected" || string.IsNullOrEmpty(setup.Connection.SystemId))
            throw new InvalidOperationException("The verified bank context changed or requires authentication.");
        var bindings = BankDiscoveryPlanner.KnownBindings(snapshot, setup.Connection.Id);
        var target = bindings.SingleOrDefault(b => b.BindingId == context.Binding.BindingId);
        if (target is null || target.State != AccountBindingState.Active || target.AccountId != context.Binding.AccountId ||
            target.IncarnationId != context.Binding.IncarnationId || !target.Locator.Equals(context.Binding.Locator) ||
            target.Locator.Endpoint != setup.Institution.Endpoint || string.IsNullOrEmpty(target.Locator.Iban) ||
            target.Locator.BankIdentifiers.Count(i => i.Kind == "bic" && !string.IsNullOrEmpty(i.Value)) != 1 ||
            !snapshot.Accounts.Incarnations.Contains(context.Incarnation) || context.Incarnation.LifetimeEvidence is not ("Unspecified" or "LiveDialogue") ||
            snapshot.Accounts.Accounts.Single(a => a.Id == context.AccountId).Currency != target.Locator.Currency ||
            snapshot.Accounts.Bindings.Count(b => b.AccountId == context.AccountId && b.Connector == "fints" && b.State == "Active") != 1 ||
            !snapshot.Accounts.Capabilities.Contains(context.Capability) || snapshot.Accounts.Capabilities.Count(c => c.AccountId == context.AccountId &&
                c.ConnectionId == setup.Connection.Id && c.OperationCode == "HKSAL" && c.Status == "Active") != 1)
            throw new InvalidOperationException("The verified account identity, lifetime or balance permission changed.");
        return snapshot;
    }

    public Task<BankBalanceResult> RefreshBankAccountBalanceAsync(BankBalanceContext context, FinTsSessionCredential pin,
        FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection, CancellationToken cancellationToken = default) =>
        RefreshBankAccountBalanceCoreAsync(context, pin, procedures, selection, null, null, cancellationToken);

    /// <summary>Collects a separate disposable bank PIN and owns it through the single-account operation.</summary>
    public Task<BankBalanceResult> AuthenticateAndRefreshBankAccountBalanceAsync(BankBalanceContext context, IBankCredentialPrompt prompt,
        FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection, CancellationToken cancellationToken = default) =>
        AuthenticateAndRefreshBankAccountBalanceCoreAsync(context, prompt, procedures, selection, null, cancellationToken);

    internal async Task<BankBalanceResult> AuthenticateAndRefreshBankAccountBalanceCoreAsync(BankBalanceContext context, IBankCredentialPrompt prompt,
        FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection, Func<IFinTsTransport>? transportFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(prompt);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        FinTsSessionCredential? pin = null;
        bool entered = false;
        using var clear = lifetime.Token.Register(() => { lock (_syncRoot) { pin?.Dispose(); } });
        void Invalidate()
        {
            lock (_syncRoot)
            {
                try { _ = RequireBalanceContext(context); }
                catch (InvalidOperationException) { try { _ = lifetime.CancelAsync(); } catch (ObjectDisposedException) { } }
            }
        }
        RefreshContextChanged += Invalidate;
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                _ = RequireBalanceContext(context);
                _ = new FinTsProductionCandidateFactory(BankConnectionSetup.ToDomain(context.Connection.Connection, context.Connection.Institution),
                    ProductConfiguration.Registration!, procedures, selection);
            }
            await BankPromptGate.WaitAsync(lifetime.Token); entered = true;
            Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
            var acquired = await prompt.AcquireBankCredentialAsync(new("Refresh Bank Balance", "Enter your banking PIN, not the profile passphrase.",
                context.Connection.Institution.DisplayName), lifetime.Token);
            lock (_syncRoot) { pin = acquired; }
            BankPromptGate.Release(); entered = false;
            Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
            return pin is null ? new(BankBalanceOutcome.Cancelled, "Balance refresh was cancelled before bank sign-in.", context.AccountId)
                : await RefreshBankAccountBalanceCoreAsync(context, pin, procedures, selection, transportFactory, cancellationToken: lifetime.Token);
        }
        catch (OperationCanceledException)
        { return new(BankBalanceOutcome.Cancelled, "Balance sign-in was cancelled, expired, or its account context changed.", context.AccountId); }
        catch (FinTsCandidateException)
        { return new(BankBalanceOutcome.Unsupported, "The selected bank procedure cannot prepare this balance refresh.", context.AccountId); }
        catch (Exception)
        { return new(BankBalanceOutcome.Failed, "Balance sign-in could not be prepared or completed. Previous saved balances were retained.", context.AccountId); }
        finally
        {
            RefreshContextChanged -= Invalidate;
            pin?.Dispose();
            if (entered) BankPromptGate.Release();
        }
    }

    internal async Task<BankBalanceResult> RefreshBankAccountBalanceCoreAsync(BankBalanceContext context, FinTsSessionCredential pin,
        FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection, Func<IFinTsTransport>? transportFactory,
        Action<StoreSnapshot>? beforeSave = null, CancellationToken cancellationToken = default)
        => await RefreshBankAccountBalanceReservedAsync(context, pin, procedures, selection, transportFactory, beforeSave, cancellationToken);

    internal async Task<BankBalanceResult> RefreshBankAccountBalanceReservedAsync(BankBalanceContext context, FinTsSessionCredential pin,
        FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection, Func<IFinTsTransport>? transportFactory,
        Action<StoreSnapshot>? beforeSave, CancellationToken cancellationToken, BankConnectionBatchLease? lease = null, Action? saving = null)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(pin);
        bool reserved = false, entered = false, received = false, saved = false, institutionEntered = false;
        var setup = context.Connection;
        var institutionGate = BankInstitutionGate(setup.Institution.Id);
        BankSavedBalance? savedBalance = null;
        int savedCount = 0;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        BankBalanceResult Result(BankBalanceOutcome outcome, string message, int count = 0, FinTsReadExecutionStatus? status = null) => new(outcome, message, context.AccountId,
            saved ? savedCount : count, status, saved ? savedBalance : null);
        bool IsCurrent()
        {
            lock (_syncRoot)
            {
                try { _ = RequireBalanceContext(context); return true; }
                catch (InvalidOperationException) { return false; }
            }
        }
        void Invalidate()
        { if (!IsCurrent()) { try { _ = lifetime.CancelAsync(); } catch (ObjectDisposedException) { } } }
        RefreshContextChanged += Invalidate;
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            StoreSnapshot snapshot;
            BankConnection connection;
            FinTsProductionCandidateFactory factory;
            lock (_syncRoot)
            {
                snapshot = RequireBalanceContext(context);
                connection = BankConnectionSetup.ToDomain(setup.Connection, setup.Institution);
                factory = new(connection, ProductConfiguration.Registration!, procedures, selection);
                if (lease is not null)
                {
                    if (!lease.Matches(this, setup.SessionEpoch, setup.Connection.Id)) throw new InvalidOperationException("Invalid batch lease.");
                }
                else
                {
                    if (!TryReserveBankOperation(setup.SessionEpoch, setup.Connection.Id))
                        return Result(BankBalanceOutcome.AlreadyRunning, "Another bank operation is already active for this connection.");
                    reserved = true;
                }
            }
            if (lease is null) { await institutionGate.WaitAsync(lifetime.Token); institutionEntered = true; }
            await WaitForBankOperationSlotAsync(lifetime.Token); entered = true;
            Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
            string bic = context.Binding.Locator.BankIdentifiers.Single(i => i.Kind == "bic").Value;
            var options = new FinTsReadExecutionOptions
            {
                Kind = FinTsReadExecutionKind.BalanceRefresh,
                VerifiedTargetOnly = true,
                TargetAccountBinding = context.Binding,
                ProductRegistration = ProductConfiguration.Registration!,
                MaximumBalancePages = 16,
                AllowAutomaticQueries = false,
                InitializationCandidateFactory = _ => factory.CreateInitialization(lifetime.Token),
                BalanceFirstReadCandidateFactory = attempt =>
                {
                    var accounts = attempt.RequirementsEvidence!.ReadParameters!.Source.Accounts.Where(a =>
                        FinTsTargetBalanceIngestion.MatchesParameter(connection, context.Binding, a, bic)).ToArray();
                    if (accounts.Length != 1) throw new FinTsCandidateException(FinTsCandidateFailure.UnqualifiedEvidence);
                    var candidate = factory.CreateBalance(attempt, accounts[0], new(iban: context.Binding.Locator.Iban!, bic: bic), lifetime.Token);
                    if (candidate.Context.Signature.Request.Request.Source.Version != context.Capability.OperationVersion)
                        throw new FinTsCandidateException(FinTsCandidateFailure.UnsupportedVersion);
                    return candidate;
                },
                BalanceContinuationCandidateFactory = a => factory.CreateBalanceContinuation(a, lifetime.Token),
                ClosingCandidateFactory = a => factory.CreateClosing(a, lifetime.Token)
            };
            using var transport = transportFactory?.Invoke() ?? new ConfirmedBankTransport(setup, ProductConfiguration, IsCurrent);
            var result = await FinTsReadExecutionPipeline.ExecuteAsync(connection, transport, pin, BankDiscoveryPlanner.DetachedLedger(snapshot),
                BankDiscoveryPlanner.KnownBindings(snapshot, setup.Connection.Id), options, lifetime.Token);
            Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
                return Result(result.Status switch
                {
                    FinTsReadExecutionStatus.Cancelled => BankBalanceOutcome.Cancelled,
                    FinTsReadExecutionStatus.RejectedInvalidCredential or FinTsReadExecutionStatus.RejectedDialogueOutcome => BankBalanceOutcome.RequiresAuthentication,
                    FinTsReadExecutionStatus.RejectedMissingCandidate => BankBalanceOutcome.Unsupported,
                    _ => BankBalanceOutcome.Failed
                }, "The bank balance exchange did not complete with qualified target evidence and a closed dialogue. Previous saved balances were retained; authentication or account discovery may be required.", status: result.Status);
            received = true;
            saving?.Invoke();
            int count = 0;
            lock (_syncRoot)
            {
                _ = RequireBalanceContext(context);
                lifetime.Token.ThrowIfCancellationRequested();
                _profileStore!.MutateAndSave(latest =>
                {
                    _ = RequireBalanceContext(context, latest);
                    lifetime.Token.ThrowIfCancellationRequested();
                    // Reallocate against the latest durable ledger; detached network allocations never own profile IDs.
                    var ingestion = FinTsTargetBalanceIngestion.Apply(connection, context.Binding, result.SessionResult!,
                        BankDiscoveryPlanner.KnownBindings(latest, setup.Connection.Id), BankDiscoveryPlanner.DetachedLedger(latest),
                        result.IngestionResult!.IngestedSnapshots[0].RetrievedAt, lifetime.Token);
                    if (!ingestion.IsSuccess) throw new InvalidOperationException("Target balance ingestion no longer qualifies.");
                    var updated = MultibankSnapshotMutator.ApplyIngestionResult(latest, ingestion.UpdatedConnection, ingestion, DateTimeOffset.UtcNow);
                    var ledger = ingestion.CommittedSnapshot!;
                    updated = updated with
                    {
                        Profile = updated.Profile with { NextId = ledger.NextId },
                        Ledger = updated.Ledger with
                        {
                            Records = ledger.Records.Select(r => new StoreLedgerRecord(r.Id.Value, r.Kind.ToString(), r.SupersedesId?.Value,
                            r.References.Select(x => (x.TargetId.Value, x.ExpectedKind.ToString())).ToArray())).ToArray()
                        }
                    };
                    StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(updated);
                    beforeSave?.Invoke(updated);
                    count = ingestion.IngestedSnapshots.Count;
                    savedCount = count;
                    decimal? Amount(BalanceKind kind) => ingestion.IngestedSnapshots.Single(b => b.Kind == kind).Value?.Amount;
                    savedBalance = new(context.Binding.Locator.Currency!, Amount(BalanceKind.Booked), Amount(BalanceKind.Available), Amount(BalanceKind.CreditLine),
                        ingestion.IngestedSnapshots[0].RetrievedAt);
                    return updated;
                });
                saved = true;
                ReloadDataInternal();
            }
            return Result(BankBalanceOutcome.Saved, "The bank balance was validated and saved. Missing available balance is shown as unavailable.", count);
        }
        catch (OperationCanceledException)
        { return Result(IsCurrent() ? BankBalanceOutcome.Cancelled : BankBalanceOutcome.ContextChanged, "Balance refresh was cancelled or its bank/account context changed. Previous saved balances were retained."); }
        catch (FinTsCandidateException)
        { return Result(BankBalanceOutcome.Unsupported, "The current bank parameters no longer qualify the reviewed account or balance version. Discover and review this account again."); }
        catch (Exception)
        {
            return Result(saved ? BankBalanceOutcome.Saved : !IsCurrent() ? BankBalanceOutcome.ContextChanged : received ? BankBalanceOutcome.ReceivedNotSaved : BankBalanceOutcome.Failed,
                saved ? "The balance was saved, but the local view could not reload. Reopen the profile to view it." : received
                    ? "A validated bank balance was received but could not be saved. The previous saved balance is still authoritative."
                    : "Balance refresh could not be prepared or completed. Previous saved balances were retained.");
        }
        finally
        {
            RefreshContextChanged -= Invalidate;
            if (entered) ReleaseBankOperationSlot();
            if (institutionEntered) institutionGate.Release();
            if (reserved) ReleaseBankOperation(setup.SessionEpoch, setup.Connection.Id);
            if (saved)
            {
                try { OnStateChanged(); }
                catch (Exception) { /* A view observer cannot undo a durable balance commit or replace its result. */ }
            }
        }
    }
}
