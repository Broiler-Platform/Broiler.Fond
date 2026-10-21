namespace Broiler.Fond.Presentation.Views;

using System;
using System.Linq;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.ViewModels;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;
using Broiler.UI.Window;

/// <summary>
/// Payments view displaying payment drafts, execution attempts, standing orders, and returns.
/// </summary>
public sealed class PaymentsView : IDisposable
{
    private readonly HostSession _session;
    private readonly IDialogService _dialogService;
    private readonly StandardPanel _root;
    private readonly StandardToolbar _toolbar;
    private readonly StandardButton _newTransferButton;
    private readonly StandardButton _standingOrdersButton;
    private readonly StandardButton _directDebitReturnButton;
    private readonly StandardListView _draftsList;

    public StandardPanel Root => _root;
    public StandardToolbar Toolbar => _toolbar;
    public StandardButton NewTransferButton => _newTransferButton;
    public StandardButton StandingOrdersButton => _standingOrdersButton;
    public StandardButton DirectDebitReturnButton => _directDebitReturnButton;
    public StandardListView DraftsList => _draftsList;

    public PaymentsView(HostSession session, IDialogService? dialogService = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dialogService = dialogService ?? new BroilerDialogService();

        _root = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Dock,
            Spacing = 12
        };

        _toolbar = new StandardToolbar
        {
            Overflow = UiToolbarOverflow.Wrap,
            Spacing = 8,
            PreferredSize = new BSize(0, 38)
        };

        _newTransferButton = new StandardButton
        {
            Text = "New Transfer",
            AccessibleName = "Initiate New SEPA Transfer"
        };
        _newTransferButton.Clicked += async (_, _) =>
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService))
            {
                return;
            }

            if (_session.IsSafeModeActive)
            {
                await _dialogService.ShowAlertAsync("Safe Mode Active", "Payment transfers are disabled while in Read-Only Safe Mode.");
                return;
            }

            var win = GetWindow();
            if (win == null)
            {
                return;
            }

            using var dlg = new BroilerPaymentInitiationDialog(_session);
            var res = await dlg.ShowModalAsync(win);
            if (res.Kind == UiDialogResultKind.Accepted)
            {
                var sourceAcc = _session.Accounts.FirstOrDefault();
                if (sourceAcc != null)
                {
                    _session.CreatePaymentDraft(
                        sourceAcc.AccountId,
                        dlg.Beneficiary,
                        dlg.Iban,
                        null,
                        dlg.Amount,
                        dlg.RemittanceText,
                        null,
                        dlg.IsInstant ? "Instant" : "Standard",
                        DateOnly.FromDateTime(DateTime.UtcNow));
                    RefreshDrafts();
                }
                else
                {
                    await _dialogService.ShowAlertAsync("No Source Account", "Please set up a bank connection first.");
                }
            }
        };
        _toolbar.AddChild(_newTransferButton);

        _standingOrdersButton = new StandardButton
        {
            Text = "Standing Orders",
            AccessibleName = "Manage Standing Orders"
        };
        _standingOrdersButton.Clicked += async (_, _) =>
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService))
            {
                return;
            }

            var win = GetWindow();
            if (win == null)
            {
                return;
            }

            using var dlg = new BroilerStandingOrdersDialog(_session);
            await dlg.ShowModalAsync(win);
        };
        _toolbar.AddChild(_standingOrdersButton);

        _directDebitReturnButton = new StandardButton
        {
            Text = "SEPA Return",
            AccessibleName = "Return SEPA Direct Debit"
        };
        _directDebitReturnButton.Clicked += async (_, _) =>
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService))
            {
                return;
            }

            var win = GetWindow();
            if (win == null)
            {
                return;
            }

            using var dlg = new BroilerDirectDebitReturnDialog(_session);
            await dlg.ShowModalAsync(win);
        };
        _toolbar.AddChild(_directDebitReturnButton);

        _root.AddChild(_toolbar);
        _root.SetDock(_toolbar, UiDock.Top);

        _draftsList = new StandardListView
        {
            PreferredSize = new BSize(600, 380),
            AccessibleName = "Payment Drafts List"
        };
        _root.AddChild(_draftsList);
        _root.SetDock(_draftsList, UiDock.Fill);

        _session.StateChanged += OnStateChanged;
        RefreshDrafts();
    }

    private void OnStateChanged(object? sender, EventArgs e) => RefreshDrafts();

    private UiWindow? GetWindow() => (_dialogService as BroilerDialogService)?.WindowAccessor?.Invoke();

    public void RefreshDrafts()
    {
        if (_draftsList.IsDisposed) return;

        var items = _session.PaymentDrafts.Select(d => new UiListItem(
            d.DraftId.ToString(),
            $"Draft #{d.DraftId} · {d.BeneficiaryName} · {d.AmountDisplay} ({d.ExecutionMode})",
            $"{d.BeneficiaryName} ({d.BeneficiaryIban})",
            $"{d.AmountDisplay} · {d.ExecutionMode}",
            false,
            d)).ToArray();

        _draftsList.SetItems(items);
    }

    public void Dispose()
    {
        _session.StateChanged -= OnStateChanged;
        _root.Dispose();
    }
}
