using System.Text.Json;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Services;

/// <summary>One shared, cancellable desktop refresh interaction with safe feedback and opt-in diagnostics.</summary>
public sealed class BankRefreshPresentation : IDisposable
{
    private readonly HostSession _session;
    private CancellationTokenSource? _active;
    private int _running;
    private bool _disposed;
    private long _epoch;
    private bool _monitorContext;
    private bool _suppressFeedback;
    private readonly List<RefreshDiagnostic> _diagnostics = [];
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public string ProgressText { get; private set; } = "Bank refresh: Ready";
    public string? CorrelationId { get; private set; }
    public BankBatchResult? LastResult { get; private set; }
    public event EventHandler? Changed;
    internal BankRefreshPresentation(HostSession session) { _session = session; session.RefreshContextChanged += ContextChanged; }

    public void Cancel()
    { try { _ = _active?.CancelAsync(); } catch (ObjectDisposedException) { } }
    internal void CancelForWindowClose() { _suppressFeedback = true; Cancel(); }
    private void ContextChanged()
    {
        if (_session.CaptureRefreshState().Epoch != _epoch) { LastResult = null; if (IsRunning && _monitorContext) Cancel(); }
    }
    public async Task RunAsync(IDialogService dialogs, BankBatchRequest? request = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        if (_disposed || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _active = cancellation;
        _epoch = _session.CaptureRefreshState().Epoch;
        _monitorContext = false;
        _suppressFeedback = false;
        LastResult = null;
        CorrelationId = Guid.NewGuid().ToString("N")[..16];
        _diagnostics.Clear();
        SetProgress("Checking profile and bank setup…");
        string title = "Bank Refresh Failed", message = "The refresh could not be completed. Previous saved balances were retained.";
        bool show = true;
        try
        {
            if (!await _session.EnsureUnlockedAsync(dialogs, cancellationToken: cancellation.Token)) { show = false; SetProgress("Bank refresh: Profile unavailable"); return; }
            _epoch = _session.CaptureRefreshState().Epoch;
            _monitorContext = true;
            cancellation.Token.ThrowIfCancellationRequested();
            var state = _session.CaptureRefreshState();
            if (state.SafeMode || state.ReadOnly || state.Disposed || !_session.ProductConfiguration.IsConfigured)
            { title = "Bank Refresh Unavailable"; message = BankRefreshFeedback.BuildMessage(_session); SetProgress("Bank refresh: Unavailable"); Record("SetupUnavailable"); return; }
            if (dialogs is not IBankCredentialPrompt prompt)
            { title = "Bank Refresh Unavailable"; message = "No bank request was sent. This host does not provide a disposable banking PIN prompt."; return; }
            var uiContext = SynchronizationContext.Current;
            var progress = new RefreshProgress(value =>
            {
                void Update()
                {
                    if (!IsRunning || _disposed) return;
                    Record(value.Stage.ToString());
                    SetProgress(StageText(value.Stage));
                }
                if (uiContext is not null && SynchronizationContext.Current != uiContext) uiContext.Post(_ => Update(), null);
                else Update();
            });
            var result = await _session.BankBatchRefresh.RefreshAsync(request ?? new(), prompt, _session.BankProcedures,
                dialogs as IBankAccountDiscoveryReview, progress, cancellation.Token);
            bool sameProfile = _session.CaptureRefreshState().Epoch == _epoch;
            LastResult = sameProfile ? result : null;
            title = result.Updated + result.Unchanged > 0 ? result.Failed + result.Cancelled + result.Unavailable + result.Unsupported > 0
                ? "Bank Refresh Partially Completed" : "Bank Refresh Completed" : result.Cancelled > 0 ? "Bank Refresh Cancelled" : "Bank Refresh Unavailable";
            message = FormatResult(result, sameProfile);
            Record(result.Failed > 0 ? "CompletedWithFailures" : result.Cancelled > 0 ? "Cancelled" : "Completed");
            SetProgress($"Refresh: {result.Updated} updated · {result.Unchanged} unchanged · {result.Failed} failed · {result.Cancelled} cancelled");
        }
        catch (OperationCanceledException)
        { title = "Bank Refresh Cancelled"; message = "Refresh was cancelled. Completed account saves remain valid; other accounts keep their previous saved balances."; SetProgress("Bank refresh: Cancelled"); Record("Cancelled"); }
        catch (Exception)
        { message = "The profile could not be opened or the refresh could not complete. Check bank setup and profile access. Previous saved balances remain authoritative."; SetProgress("Bank refresh: Failed"); Record("Failed"); }
        finally
        {
            _active = null;
            _monitorContext = false;
            Volatile.Write(ref _running, 0);
            Notify();
            if (show && !_disposed && !_suppressFeedback)
            {
                try { await dialogs.ShowAlertAsync(title, message + $"\n\nSupport reference: {CorrelationId}"); }
                catch (Exception) { /* Closing a window or a failing dialog must not escape an async click handler. */ }
            }
        }
    }

    internal string FormatResult(BankBatchResult result, bool includeDetails = true)
    {
        string text = $"Updated: {result.Updated} · Unchanged: {result.Unchanged} · Unavailable: {result.Unavailable}\n" +
            $"Unsupported: {result.Unsupported} · Failed: {result.Failed} · Cancelled: {result.Cancelled}\n" +
            "Updated and unchanged accounts were validated and saved. Other accounts retain their previous saved balances and retrieval times.";
        if (!includeDetails) return text + "\nThe profile changed or was locked; account details are hidden.";
        if (result.Connections.Count == 0) text += "\nNo bank connections are configured. Use Connections → Add Connection.";
        foreach (var connection in result.Connections)
        {
            var current = _session.CurrentSnapshot;
            string bank = current?.Institutions.Institutions.FirstOrDefault(i => i.Id == current.Connections.Connections.FirstOrDefault(c => c.Id == connection.ConnectionId)?.InstitutionId)?.DisplayName
                ?? $"Connection {connection.ConnectionId}";
            text += $"\n\n{bank}: {connection.Outcome}";
            if (connection.BlockReason != BankBatchBlockReason.None) text += "\n" + ConnectionRecovery(connection.BlockReason);
            else if (connection.Accounts.Count == 0 && connection.Outcome == BankBatchConnectionOutcome.AlreadyRunning)
                text += "\nAnother bank operation is active. Wait or cancel it before refreshing.";
            else if (connection.Accounts.Count == 0 && connection.Outcome == BankBatchConnectionOutcome.Cancelled)
                text += "\nThe bank operation was cancelled. No balance save is reported for this connection.";
            else if (connection.Accounts.Count == 0 && connection.Outcome == BankBatchConnectionOutcome.Failed)
                text += "\nThe bank operation failed before any account balance was saved. Review setup and connectivity before retrying.";
            else if (connection.Accounts.Count == 0) text += "\nUse Connections → Discover Accounts and review bank identities. Manual account entry is optional.";
            foreach (var account in connection.Accounts)
            {
                string name = current?.Accounts.Accounts.FirstOrDefault(a => a.Id == account.AccountId)?.DisplayName ?? $"Account {account.AccountId}";
                text += $"\n{name}: {account.Outcome}. {Recovery(account)}";
                if (account.IsDurableSuccess && account.RetrievedAt is { } retrieved)
                    text += $" Retrieved: {retrieved.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)}.";
            }
        }
        foreach (var account in result.UnassignedAccounts) text += $"\nAccount {account.AccountId}: No source in the selected connections. Discover and review its bank identity.";
        foreach (var total in result.CurrencyTotals.Where(t => t.SavedAccounts > 0))
            text += $"\nRefreshed booked total · {total.Currency}: {(total.RefreshedBookedTotal is decimal amount ? amount.ToString(System.Globalization.CultureInfo.CurrentCulture) : "Unavailable")}";
        return text;
    }
    private static string ConnectionRecovery(BankBatchBlockReason reason) => reason switch
    {
        BankBatchBlockReason.ProfileUnavailable => "Unlock a writable profile outside Safe Mode and configure the application product ID.",
        BankBatchBlockReason.ConnectionSetupRequired => "Use Connections → Connect to confirm and synchronize bank access before refreshing.",
        BankBatchBlockReason.DiscoveryRequired => "Use Connections → Discover Accounts and explicitly review bank identities and supported balance capabilities. Manual account entry is optional.",
        BankBatchBlockReason.ProcedureRequired => "Current bank parameters and procedure qualification are unavailable. Use Connections → Refresh Balances to choose and qualify a supported procedure again.",
        BankBatchBlockReason.RetryBackoff => "Connection retry backoff is active. Wait before refreshing again.",
        BankBatchBlockReason.ReviewUnavailable => "This host must provide explicit bank-account review before discovery can save associations.",
        _ => "Select an available connection and accounts before refreshing."
    };
    private static string Recovery(BankBatchAccountResult account) => account.Detail switch
    {
        BankBalanceOutcome.ReceivedNotSaved => "Bank data was received but not saved. Check profile storage access before retrying.",
        BankBalanceOutcome.RequiresAuthentication => "Review bank login and the supported approval procedure before signing in again. Rejected credentials were not retried.",
        BankBalanceOutcome.AlreadyRunning => "Another bank operation is active. Wait for it to finish or cancel it.",
        _ => account.Outcome switch
        {
            BankBatchAccountOutcome.Updated or BankBatchAccountOutcome.Unchanged => "Saved successfully; the displayed retrieval time reflects this read.",
            BankBatchAccountOutcome.Unsupported => "Current bank parameters or a supported approval procedure are unavailable. Complete supported bank sign-in and discovery setup.",
            BankBatchAccountOutcome.Unavailable => "Review endpoint confirmation, synchronization, account verification and retry backoff. Manually entered accounts require bank discovery and review.",
            BankBatchAccountOutcome.Cancelled => "Start a new refresh when ready; completed account saves were preserved.",
            _ => "The exchange failed. Check bank connectivity and setup before retrying; cached balances were preserved."
        }
    };
    internal static string StageText(BankBatchStage stage) => stage switch
    {
        BankBatchStage.Checking => "Checking bank setup…",
        BankBatchStage.Waiting => "Waiting for a bank connection slot…",
        BankBatchStage.SigningIn => "Signing in — enter the banking PIN…",
        BankBatchStage.Discovering => "Discovering bank accounts…",
        BankBatchStage.Reviewing => "Review the bank-reported accounts…",
        BankBatchStage.Reading => "Retrieving bank balances…",
        BankBatchStage.Saving => "Saving validated bank balances…",
        _ => "Finishing bank connection…"
    };
    private void Record(string code)
    { if (_session.IsDiagnosticsEnabled && _diagnostics.Count < 128) _diagnostics.Add(new(DateTimeOffset.UtcNow, code)); }
    public string GetSupportPreviewJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject(); json.WriteString("correlationId", CorrelationId);
            json.WriteBoolean("detailedDiagnosticsEnabled", _session.IsDiagnosticsEnabled);
            json.WriteStartArray("stages");
            if (_session.IsDiagnosticsEnabled)
                foreach (var entry in _diagnostics)
                { json.WriteStartObject(); json.WriteString("timestamp", entry.Timestamp); json.WriteString("stage", entry.Stage); json.WriteEndObject(); }
            json.WriteEndArray(); json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
    private void SetProgress(string text) { ProgressText = text; Notify(); }
    private void Notify()
    {
        if (_disposed) return;
        foreach (EventHandler handler in Changed?.GetInvocationList() ?? []) { try { handler(this, EventArgs.Empty); } catch (Exception) { } }
    }
    public void Dispose() { _disposed = true; Cancel(); LastResult = null; _diagnostics.Clear(); _session.RefreshContextChanged -= ContextChanged; }
    private sealed record RefreshDiagnostic(DateTimeOffset Timestamp, string Stage);
    private sealed class RefreshProgress(Action<BankBatchProgress> report) : IProgress<BankBatchProgress>
    { public void Report(BankBatchProgress value) => report(value); }
}
