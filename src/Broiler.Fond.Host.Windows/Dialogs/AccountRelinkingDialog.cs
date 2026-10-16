using System.Drawing;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible, truthful dialog enabling user-reviewed relinking of quarantined or candidate account bindings
/// to an existing logical account identity per M1-03 / M1-06 and ADR 0090.
/// Invariants:
/// 1. Relinking never rewrites historical balance snapshots; original binding provenance is permanently preserved.
/// 2. If the previous candidate account is an unreferenced temporary shell, it is pruned and superseded
///    generations containing its temporary ID are deleted via a verified privacy purge.
/// 3. Passphrase is required for generation commit / privacy purge.
/// </summary>
public sealed class AccountRelinkingDialog : Form
{
    private readonly StoreSnapshot _snapshot;
    private readonly IProfileStore _store;

    private readonly ComboBox _cboBindings;
    private readonly ComboBox _cboTargetAccounts;
    private readonly CheckBox _chkPruneOrphan;
    private readonly TextBox _txtPassphrase;
    private readonly TextBox _txtPreview;
    private readonly Button _btnRelink;
    private readonly Button _btnCancel;

    private AccountRelinkingResult? _currentPlan;

    public AccountRelinkingResult? RelinkingResult { get; private set; }

    public AccountRelinkingDialog(HostSession session, ulong? initialBindingId = null)
        : this(GetRequiredSnapshot(session), GetRequiredStore(session), initialBindingId)
    {
    }

    public AccountRelinkingDialog(StoreSnapshot snapshot, IProfileStore store, ulong? initialBindingId = null)
    {
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _store = store ?? throw new ArgumentNullException(nameof(store));

        Text = "Relink Account Binding - Review & Confirmation";
        ClientSize = new Size(680, 560);
        MinimumSize = new Size(600, 480);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "Account Binding Relinking Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = "Allows reviewed relinking of account bindings to logical accounts without silent conflation.";

        var lblHeader = new Label
        {
            Location = new Point(20, 15),
            Size = new Size(640, 42),
            Font = new Font(Font.FontFamily, 9f),
            ForeColor = Color.DarkSlateGray,
            Text = "Ambiguous or quarantined candidate bindings can be relinked to an existing logical account. All historical balance observations permanently retain their original binding identity (ADR 0090)."
        };

        // Source binding selection
        var lblBinding = new Label
        {
            Location = new Point(20, 65),
            Size = new Size(640, 20),
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            Text = "Source &Binding to Relink:"
        };

        _cboBindings = new ComboBox
        {
            Location = new Point(20, 88),
            Size = new Size(640, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            TabIndex = 0,
            AccessibleName = "Source Binding Selection",
            AccessibleRole = AccessibleRole.ComboBox
        };

        // Target account selection
        var lblTargetAccount = new Label
        {
            Location = new Point(20, 125),
            Size = new Size(640, 20),
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            Text = "Target &Logical Account:"
        };

        _cboTargetAccounts = new ComboBox
        {
            Location = new Point(20, 148),
            Size = new Size(640, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            TabIndex = 1,
            AccessibleName = "Target Account Selection",
            AccessibleRole = AccessibleRole.ComboBox
        };

        // Prune orphan checkbox
        _chkPruneOrphan = new CheckBox
        {
            Location = new Point(20, 185),
            Size = new Size(640, 24),
            Text = "&Prune candidate account & execute privacy purge if unreferenced (recommended for candidate shells)",
            Checked = true,
            TabIndex = 2,
            AccessibleName = "Prune Candidate Account Checkbox"
        };

        // Passphrase entry
        var lblPassphrase = new Label
        {
            Location = new Point(20, 218),
            Size = new Size(640, 20),
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            Text = "Profile &Passphrase (required for generation commit and privacy purge):"
        };

        _txtPassphrase = new TextBox
        {
            Location = new Point(20, 241),
            Size = new Size(340, 26),
            UseSystemPasswordChar = true,
            TabIndex = 3,
            AccessibleName = "Profile Passphrase Input",
            AccessibleRole = AccessibleRole.Text
        };

        // Plan preview group
        var grpPreview = new GroupBox
        {
            Location = new Point(20, 280),
            Size = new Size(640, 190),
            Text = "Relinking Plan Preview (Deterministic ADR 0090 Contract)",
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            AccessibleName = "Relinking Plan Preview Group",
            AccessibleRole = AccessibleRole.Grouping
        };

        _txtPreview = new TextBox
        {
            Location = new Point(15, 25),
            Size = new Size(610, 150),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(Font.FontFamily, 9f),
            BackColor = SystemColors.Window,
            TabIndex = 4,
            AccessibleName = "Plan Preview Details",
            AccessibleRole = AccessibleRole.Text
        };
        grpPreview.Controls.Add(_txtPreview);

        // Buttons
        _btnRelink = new Button
        {
            Location = new Point(410, 485),
            Size = new Size(130, 34),
            Text = "&Relink Binding",
            Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
            Enabled = false,
            TabIndex = 5,
            AccessibleName = "Relink Binding Button"
        };
        _btnRelink.Click += (s, e) => ExecuteRelink();

        _btnCancel = new Button
        {
            Location = new Point(550, 485),
            Size = new Size(110, 34),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            TabIndex = 6,
            AccessibleName = "Cancel Button"
        };

        AcceptButton = _btnRelink;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[]
        {
            lblHeader,
            lblBinding, _cboBindings,
            lblTargetAccount, _cboTargetAccounts,
            _chkPruneOrphan,
            lblPassphrase, _txtPassphrase,
            grpPreview,
            _btnRelink, _btnCancel
        });

        // Wire event handlers
        _cboBindings.SelectedIndexChanged += (s, e) => UpdatePlanPreview();
        _cboTargetAccounts.SelectedIndexChanged += (s, e) => UpdatePlanPreview();
        _chkPruneOrphan.CheckedChanged += (s, e) => UpdatePlanPreview();
        _txtPassphrase.TextChanged += (s, e) => UpdatePlanPreview();

        PopulateBindings(initialBindingId);
        PopulateTargetAccounts();
        UpdatePlanPreview();
    }

    private static StoreSnapshot GetRequiredSnapshot(HostSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        IProfileStore store = session.GetStore()
            ?? throw new InvalidOperationException("No unlocked profile store is available in this session.");
        return store.CurrentSnapshot
            ?? throw new InvalidOperationException("No valid snapshot is open in this profile store.");
    }

    private static IProfileStore GetRequiredStore(HostSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.GetStore()
            ?? throw new InvalidOperationException("No unlocked profile store is available in this session.");
    }

    private void PopulateBindings(ulong? initialBindingId)
    {
        _cboBindings.Items.Clear();

        var accountMap = _snapshot.Accounts.Accounts.ToDictionary(a => a.Id);
        int selectedIndex = 0;
        int currentIndex = 0;

        foreach (StoreBindingEntry binding in _snapshot.Accounts.Bindings)
        {
            string accName = accountMap.TryGetValue(binding.AccountId, out var acc)
                ? acc.DisplayName
                : $"Account #{binding.AccountId}";

            string locator = !string.IsNullOrWhiteSpace(binding.Iban)
                ? $"IBAN: {binding.Iban}"
                : (!string.IsNullOrWhiteSpace(binding.DomesticAccountNumber)
                    ? $"Acc: {binding.DomesticAccountNumber} (BLZ: {binding.DomesticBankCode})"
                    : "[No Locator]");

            string displayText = $"Binding #{binding.Id} [{binding.State}] - {locator} (Account #{binding.AccountId}: {accName})";
            _cboBindings.Items.Add(new BindingDisplayItem(binding, displayText));

            if (initialBindingId.HasValue && binding.Id == initialBindingId.Value)
            {
                selectedIndex = currentIndex;
            }
            else if (!initialBindingId.HasValue &&
                     (string.Equals(binding.State, "Quarantined", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(binding.State, "Ambiguous", StringComparison.OrdinalIgnoreCase)))
            {
                selectedIndex = currentIndex;
            }

            currentIndex++;
        }

        if (_cboBindings.Items.Count > 0)
        {
            _cboBindings.SelectedIndex = selectedIndex;
        }
    }

    private void PopulateTargetAccounts()
    {
        _cboTargetAccounts.Items.Clear();

        foreach (StoreAccountEntry account in _snapshot.Accounts.Accounts)
        {
            string displayText = $"Account #{account.Id}: {account.DisplayName} [{account.Currency}]";
            _cboTargetAccounts.Items.Add(new AccountDisplayItem(account, displayText));
        }

        if (_cboTargetAccounts.Items.Count > 0)
        {
            // Pick an account other than the currently bound account if available
            if (_cboBindings.SelectedItem is BindingDisplayItem bindingItem)
            {
                int preferredIndex = 0;
                for (int i = 0; i < _cboTargetAccounts.Items.Count; i++)
                {
                    if (_cboTargetAccounts.Items[i] is AccountDisplayItem accItem &&
                        accItem.Account.Id != bindingItem.Binding.AccountId)
                    {
                        preferredIndex = i;
                        break;
                    }
                }
                _cboTargetAccounts.SelectedIndex = preferredIndex;
            }
            else
            {
                _cboTargetAccounts.SelectedIndex = 0;
            }
        }
    }

    private void UpdatePlanPreview()
    {
        if (_cboBindings.SelectedItem is not BindingDisplayItem bindingItem ||
            _cboTargetAccounts.SelectedItem is not AccountDisplayItem accountItem)
        {
            _txtPreview.Text = "Please select both a source binding and a target account to preview relinking.";
            _btnRelink.Enabled = false;
            _currentPlan = null;
            return;
        }

        try
        {
            AccountRelinkingResult plan = AccountRelinkingPlanner.PlanRelink(
                _snapshot,
                bindingItem.Binding.Id,
                accountItem.Account.Id,
                _chkPruneOrphan.Checked);

            _currentPlan = plan;

            string pruningNote = plan.PrunedAccountId.HasValue
                ? $"Account #{plan.PrunedAccountId.Value} will be PRUNED (candidate shell with zero remaining bindings and no balances)."
                : "Previous account will be RETAINED (has historical balance observations or other active bindings).";

            string storageNote = plan.PrunedAccountId.HasValue
                ? "Verified Privacy Purge (superseded generation files containing the pruned ID will be durably deleted from disk)."
                : "Standard Atomic Generation Save (retains validated preceding generation).";

            _txtPreview.Text = string.Join(Environment.NewLine, new[]
            {
                "[✓ Relinking Plan Valid]",
                $"• Source Binding: #{bindingItem.Binding.Id} (Current State: {bindingItem.Binding.State})",
                $"• Target Account: #{accountItem.Account.Id} ({accountItem.Account.DisplayName}) [{accountItem.Account.Currency}]",
                $"• Target Incarnation: #{plan.TargetIncarnationId}",
                $"• State Transition: {bindingItem.Binding.State} ➔ Active",
                "• Balance Provenance: All historical balance snapshots permanently retain their original binding identity.",
                $"• Candidate Pruning: {pruningNote}",
                $"• Storage Action: {storageNote}",
            });

            _btnRelink.Enabled = !string.IsNullOrWhiteSpace(_txtPassphrase.Text);
        }
        catch (Exception ex)
        {
            _currentPlan = null;
            _txtPreview.Text = $"[⚠ Relinking Plan Invalid]{Environment.NewLine}{ex.Message}";
            _btnRelink.Enabled = false;
        }
    }

    private void ExecuteRelink()
    {
        if (_cboBindings.SelectedItem is not BindingDisplayItem bindingItem ||
            _cboTargetAccounts.SelectedItem is not AccountDisplayItem accountItem ||
            string.IsNullOrWhiteSpace(_txtPassphrase.Text))
        {
            return;
        }

        try
        {
            AccountRelinkingResult result = _store.RelinkAccountBinding(
                bindingItem.Binding.Id,
                accountItem.Account.Id,
                _txtPassphrase.Text,
                _chkPruneOrphan.Checked);

            RelinkingResult = result;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Failed to relink account binding:{Environment.NewLine}{ex.Message}",
                "Relinking Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private sealed record BindingDisplayItem(StoreBindingEntry Binding, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private sealed record AccountDisplayItem(StoreAccountEntry Account, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }
}
