namespace Broiler.Fond.Presentation.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.UI;
using Broiler.UI.Dialog;
using Broiler.UI.Window;

/// <summary>
/// First-party Broiler dialog service implementation presenting modal banking workflows
/// via standard Broiler.UI dialog components.
/// </summary>
public sealed class BroilerDialogService : IDialogService, IBankCredentialPrompt, IBankAccountDiscoveryReview, IBankProcedureChoice
{
    private Func<UiWindow?>? _getWindow;

    public Func<UiWindow?>? WindowAccessor
    {
        get => _getWindow;
        set => _getWindow = value;
    }

    public Func<string, string, Task<bool>>? HeadlessConfirmationHandler { get; set; }
    public Func<string, string, Task>? HeadlessAlertHandler { get; set; }
    public Func<string, Task<ProfileUnlockResult>>? HeadlessProfileUnlockHandler { get; set; }
    public Func<CredentialPromptRequest, Task<CredentialPromptResult>>? HeadlessCredentialPromptHandler { get; set; }
    public Func<CredentialPromptRequest, CancellationToken, Task<Broiler.Fond.Kernel.FinTs.FinTsSessionCredential?>>? HeadlessBankCredentialHandler { get; set; }
    public Func<BankDiscoveryReview, CancellationToken, Task<IReadOnlyCollection<int>?>>? HeadlessBankDiscoveryReviewHandler { get; set; }
    public Func<BankProcedureChoiceRequest, CancellationToken, Task<string?>>? HeadlessBankProcedureChoiceHandler { get; set; }
    public async Task<string?> ChooseBankProcedureAsync(BankProcedureChoiceRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (HeadlessBankProcedureChoiceHandler is not null)
        {
            var selected = await HeadlessBankProcedureChoiceHandler(request, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); return selected;
        }
        var window = _getWindow?.Invoke();
        if (window is null) return null;
        using var dialog = new BroilerBankProcedureDialog(request);
        try { await dialog.ShowModalAsync(window).WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { if (!dialog.Dialog.IsDisposed) dialog.Dialog.Cancel(); throw; }
        return dialog.GetSelection();
    }

    public async Task<IReadOnlyCollection<int>?> ReviewBankAccountsAsync(BankDiscoveryReview review, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (HeadlessBankDiscoveryReviewHandler is not null)
        {
            var answer = await HeadlessBankDiscoveryReviewHandler(review, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return answer;
        }
        var window = _getWindow?.Invoke();
        if (window is null) return null;
        var confirmed = new List<int>();
        foreach (var account in review.Accounts.Where(a => a.CanConfirm))
        {
            string details = $"Bank: {review.BankName}\nEndpoint: {review.Endpoint}\nBank login: {review.UserId}\n" +
                $"Bank account: {account.Iban ?? account.AccountNumber}\nBIC: {account.Bic}\nBank currency: {account.Currency}\n" +
                $"Local account: {account.DisplayName ?? "Create a new account"}\n" +
                $"Balance request: {(account.BalanceVersion is int version ? $"HKSAL version {version}" : "Unavailable")}\n{account.Explanation}";
            using var dialog = new BroilerConfirmationDialog("Review Bank Account", details, "Confirm Association", "Skip Account");
            UiDialogResult result;
            try { result = await dialog.ShowModalAsync(window).WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { if (!dialog.Dialog.IsDisposed) dialog.Dialog.Cancel(); throw; }
            if (result.Kind == UiDialogResultKind.Accepted) confirmed.Add(account.Index);
        }
        string summary = string.Join("\n", review.Accounts.Select(a => $"{a.Iban ?? a.AccountNumber}: {a.Match}. {a.Explanation}"));
        using var save = new BroilerConfirmationDialog("Save Discovery Review",
            $"{review.BankName}\n{review.Endpoint}\nConfirmed associations: {confirmed.Count}\n{summary}\n" +
            "Only confirmed associations will be added. Existing bank accounts absent or changed in this discovery lose their previous balance permission. Cached balances and history remain.",
            "Save Review", "Cancel");
        UiDialogResult final;
        try { final = await save.ShowModalAsync(window).WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { if (!save.Dialog.IsDisposed) save.Dialog.Cancel(); throw; }
        cancellationToken.ThrowIfCancellationRequested();
        return final.Kind == UiDialogResultKind.Accepted ? confirmed.AsReadOnly() : null;
    }

    public async Task<Broiler.Fond.Kernel.FinTs.FinTsSessionCredential?> AcquireBankCredentialAsync(
        CredentialPromptRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Broiler.Fond.Kernel.FinTs.FinTsSessionCredential? credential;
        if (HeadlessBankCredentialHandler is not null)
            credential = await HeadlessBankCredentialHandler(request, cancellationToken);
        else
        {
            var window = _getWindow?.Invoke();
            if (window is null) return null;
            using var dialog = new BroilerCredentialPromptDialog(request);
            try { await dialog.ShowModalAsync(window).WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { dialog.Cancel(); throw; }
            credential = dialog.TakeCredential();
        }
        if (cancellationToken.IsCancellationRequested)
        {
            credential?.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
        }
        return credential;
    }
    public Func<PaymentReviewRequest, Task<PaymentReviewResult>>? HeadlessPaymentReviewHandler { get; set; }
    public Func<InstitutionSetupRequest, Task<InstitutionSetupResult>>? HeadlessInstitutionSetupHandler { get; set; }
    public Func<string, Task<bool>>? HeadlessEndpointConfirmationHandler { get; set; }

    public async Task<bool> ShowEndpointConfirmationAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (HeadlessEndpointConfirmationHandler is not null) return await HeadlessEndpointConfirmationHandler(endpoint);
        var window = _getWindow?.Invoke();
        if (window is null) return false;
        using var dialog = new BroilerEndpointConfirmationDialog(endpoint);
        await dialog.ShowModalAsync(window);
        return dialog.IsConfirmed;
    }
    public Func<AccountSetupRequest, Task<AccountSetupResult>>? HeadlessAccountSetupHandler { get; set; }

    public async Task<AccountSetupResult> ShowAccountSetupAsync(AccountSetupRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (HeadlessAccountSetupHandler is not null)
            return await HeadlessAccountSetupHandler(request);
        var window = _getWindow?.Invoke();
        if (window is null)
            return new(false, 0, string.Empty, string.Empty, string.Empty, true);
        using var dialog = new BroilerAccountSetupDialog(request);
        await dialog.ShowModalAsync(window);
        return dialog.GetResult();
    }

    public BroilerDialogService(Func<UiWindow?>? getWindow = null)
    {
        _getWindow = getWindow;
    }

    public async Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string? confirmLabel = null,
        string? cancelLabel = null,
        CancellationToken cancellationToken = default)
    {
        if (HeadlessConfirmationHandler != null)
        {
            return await HeadlessConfirmationHandler(title, message);
        }

        var window = _getWindow?.Invoke();
        if (window == null)
        {
            return true;
        }

        using var dialog = new BroilerConfirmationDialog(title, message, confirmLabel, cancelLabel);
        var result = await dialog.ShowModalAsync(window);
        return result.Kind == UiDialogResultKind.Accepted;
    }

    public async Task ShowAlertAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (HeadlessAlertHandler != null)
        {
            await HeadlessAlertHandler(title, message);
            return;
        }

        var window = _getWindow?.Invoke();
        if (window == null)
        {
            return;
        }

        using var dialog = new BroilerAlertDialog(title, message);
        await dialog.ShowModalAsync(window);
    }

    public async Task<ProfileUnlockResult> ShowProfileUnlockAsync(
        string profileDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (HeadlessProfileUnlockHandler != null)
        {
            var result = await HeadlessProfileUnlockHandler(profileDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        var window = _getWindow?.Invoke();
        if (window == null)
        {
            return new ProfileUnlockResult(false, null, true);
        }

        using var dialog = new BroilerProfileUnlockDialog(profileDirectory);
        try { await dialog.ShowModalAsync(window).WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { if (!dialog.Dialog.IsDisposed) dialog.Dialog.Cancel(); throw; }
        return dialog.GetResult();
    }

    public async Task<CredentialPromptResult> ShowCredentialPromptAsync(
        CredentialPromptRequest request,
        CancellationToken cancellationToken = default)
    {
        if (HeadlessCredentialPromptHandler != null)
        {
            return await HeadlessCredentialPromptHandler(request);
        }

        var window = _getWindow?.Invoke();
        if (window == null)
        {
            return new CredentialPromptResult(false, null, true);
        }

        using var dialog = new BroilerCredentialPromptDialog(request);
        await dialog.ShowModalAsync(window);
        return dialog.GetResult();
    }

    public async Task<PaymentReviewResult> ShowPaymentReviewAsync(
        PaymentReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        if (HeadlessPaymentReviewHandler != null)
        {
            return await HeadlessPaymentReviewHandler(request);
        }

        var window = _getWindow?.Invoke();
        if (window == null)
        {
            return new PaymentReviewResult(true, false);
        }

        using var dialog = new BroilerPaymentReviewDialog(request);
        await dialog.ShowModalAsync(window);
        return dialog.GetResult();
    }

    public async Task<InstitutionSetupResult> ShowInstitutionSetupAsync(
        InstitutionSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        if (HeadlessInstitutionSetupHandler != null)
        {
            return await HeadlessInstitutionSetupHandler(request);
        }

        var window = _getWindow?.Invoke();
        if (window == null)
        {
            return new InstitutionSetupResult(false, request.InitialName, request.InitialBankCode, request.InitialEndpoint, true);
        }

        using var dialog = new BroilerInstitutionSetupDialog(request);
        await dialog.ShowModalAsync(window);
        return dialog.GetResult();
    }
}
