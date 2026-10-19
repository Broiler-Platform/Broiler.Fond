using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Domain.Transactions;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible transaction split dialog enforcing the strict balance invariant:
/// sum of split amounts must exactly equal the parent transaction amount.
/// </summary>
public sealed class TransactionSplitDialog : Form
{
    private readonly HostSession _session;
    private readonly TransactionViewModel _transaction;
    private readonly List<TransactionSplitItem> _splits = [];

    private readonly Label _lblParentDetails;
    private readonly Label _lblParentAmount;
    private readonly ListView _lstSplits;
    private readonly TextBox _txtSplitAmount;
    private readonly TextBox _txtSplitCategory;
    private readonly TextBox _txtSplitNote;
    private readonly Button _btnAddSplit;
    private readonly Button _btnRemoveSplit;
    private readonly Label _lblTotalAllocated;
    private readonly Label _lblRemainingBalance;
    private readonly Label _lblValidationStatus;
    private readonly Button _btnSave;
    private readonly Button _btnCancel;

    public IReadOnlyList<TransactionSplitItem> Splits => _splits.AsReadOnly();

    public TransactionSplitDialog(HostSession session, TransactionViewModel transaction)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(transaction);

        _session = session;
        _transaction = transaction;

        Text = "Split Transaction";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(580, 520);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleName = "Split Transaction Dialog";
        AccessibleDescription = "Allocate transaction amount across multiple categories while maintaining exact balance invariance.";

        // Parent transaction summary
        var grpParent = new GroupBox
        {
            Text = "Parent Transaction",
            Location = new Point(15, 12),
            Size = new Size(550, 75)
        };

        _lblParentDetails = new Label
        {
            Text = $"Date: {transaction.DateDisplay} | Counterparty: {transaction.CounterpartyDisplay}",
            Location = new Point(12, 22),
            Size = new Size(520, 20),
            AccessibleName = "Parent Transaction Details Label"
        };

        _lblParentAmount = new Label
        {
            Text = $"Total Amount: {transaction.AmountDisplay} {transaction.Currency}",
            Location = new Point(12, 45),
            Size = new Size(520, 22),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            AccessibleName = "Parent Transaction Total Amount Label"
        };

        grpParent.Controls.Add(_lblParentDetails);
        grpParent.Controls.Add(_lblParentAmount);

        // Split items list
        _lstSplits = new ListView
        {
            Location = new Point(15, 95),
            Size = new Size(550, 160),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            AccessibleName = "Transaction Split Lines List"
        };
        _lstSplits.Columns.Add("#", 35);
        _lstSplits.Columns.Add("Amount", 110);
        _lstSplits.Columns.Add("Category", 180);
        _lstSplits.Columns.Add("Note", 200);

        // Add line entry box
        var grpAdd = new GroupBox
        {
            Text = "Add Split Line",
            Location = new Point(15, 265),
            Size = new Size(550, 100)
        };

        var lblAmt = new Label { Text = "Amount:", Location = new Point(12, 26), Size = new Size(60, 20) };
        _txtSplitAmount = new TextBox
        {
            Location = new Point(75, 23),
            Size = new Size(100, 23),
            AccessibleName = "Split Line Amount Input"
        };

        var lblCat = new Label { Text = "Category:", Location = new Point(190, 26), Size = new Size(65, 20) };
        _txtSplitCategory = new TextBox
        {
            Location = new Point(260, 23),
            Size = new Size(160, 23),
            AccessibleName = "Split Line Category Input"
        };

        var lblNote = new Label { Text = "Note:", Location = new Point(12, 60), Size = new Size(60, 20) };
        _txtSplitNote = new TextBox
        {
            Location = new Point(75, 57),
            Size = new Size(345, 23),
            AccessibleName = "Split Line Note Input"
        };

        _btnAddSplit = new Button
        {
            Text = "&Add Line",
            Location = new Point(435, 22),
            Size = new Size(100, 27),
            AccessibleName = "Add Split Line Button"
        };
        _btnAddSplit.Click += BtnAddSplit_Click;

        _btnRemoveSplit = new Button
        {
            Text = "&Remove Selected",
            Location = new Point(435, 56),
            Size = new Size(100, 27),
            Enabled = false,
            AccessibleName = "Remove Selected Split Line Button"
        };
        _btnRemoveSplit.Click += BtnRemoveSplit_Click;
        _lstSplits.SelectedIndexChanged += (s, e) =>
        {
            _btnRemoveSplit.Enabled = _lstSplits.SelectedIndices.Count > 0;
        };

        grpAdd.Controls.Add(lblAmt);
        grpAdd.Controls.Add(_txtSplitAmount);
        grpAdd.Controls.Add(lblCat);
        grpAdd.Controls.Add(_txtSplitCategory);
        grpAdd.Controls.Add(lblNote);
        grpAdd.Controls.Add(_txtSplitNote);
        grpAdd.Controls.Add(_btnAddSplit);
        grpAdd.Controls.Add(_btnRemoveSplit);

        // Balance invariant status panel
        var grpStatus = new GroupBox
        {
            Text = "Balance Invariant Status",
            Location = new Point(15, 375),
            Size = new Size(550, 80)
        };

        _lblTotalAllocated = new Label
        {
            Text = "Allocated: 0.00",
            Location = new Point(12, 22),
            Size = new Size(240, 20),
            AccessibleName = "Allocated Amount Label"
        };

        _lblRemainingBalance = new Label
        {
            Text = "Remaining: 0.00",
            Location = new Point(260, 22),
            Size = new Size(270, 20),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            AccessibleName = "Remaining Balance Label"
        };

        _lblValidationStatus = new Label
        {
            Text = "[⚠ Remaining balance must be zero]",
            Location = new Point(12, 48),
            Size = new Size(520, 22),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            AccessibleName = "Split Validation Status Label"
        };

        grpStatus.Controls.Add(_lblTotalAllocated);
        grpStatus.Controls.Add(_lblRemainingBalance);
        grpStatus.Controls.Add(_lblValidationStatus);

        // Action buttons
        _btnSave = new Button
        {
            Text = "&Save Splits",
            Location = new Point(340, 470),
            Size = new Size(110, 30),
            Enabled = false,
            DialogResult = DialogResult.OK,
            AccessibleName = "Save Transaction Splits Button"
        };
        _btnSave.Click += BtnSave_Click;

        _btnCancel = new Button
        {
            Text = "&Cancel",
            Location = new Point(458, 470),
            Size = new Size(105, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel Button"
        };

        Controls.Add(grpParent);
        Controls.Add(_lstSplits);
        Controls.Add(grpAdd);
        Controls.Add(grpStatus);
        Controls.Add(_btnSave);
        Controls.Add(_btnCancel);

        AcceptButton = _btnAddSplit;
        CancelButton = _btnCancel;

        // Initialize with remaining balance set to parent transaction amount
        UpdateBalanceInvariantStatus();
    }

    private void BtnAddSplit_Click(object? sender, EventArgs e)
    {
        string amtText = _txtSplitAmount.Text.Trim();
        if (!decimal.TryParse(amtText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) &&
            !decimal.TryParse(amtText, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
        {
            MessageBox.Show("Please enter a valid numeric split amount.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtSplitAmount.Focus();
            return;
        }

        if (amount <= 0m)
        {
            MessageBox.Show("Split line amount must be greater than zero.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtSplitAmount.Focus();
            return;
        }

        string category = _txtSplitCategory.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            MessageBox.Show("Please enter a category for this split line.", "Category Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtSplitCategory.Focus();
            return;
        }

        string? note = string.IsNullOrWhiteSpace(_txtSplitNote.Text) ? null : _txtSplitNote.Text.Trim();

        int nextIndex = _splits.Count + 1;
        var item = new TransactionSplitItem(nextIndex, amount, category, note);
        _splits.Add(item);

        _txtSplitAmount.Clear();
        _txtSplitCategory.Clear();
        _txtSplitNote.Clear();

        RefreshSplitsList();
        UpdateBalanceInvariantStatus();
        _txtSplitAmount.Focus();
    }

    private void BtnRemoveSplit_Click(object? sender, EventArgs e)
    {
        if (_lstSplits.SelectedIndices.Count > 0)
        {
            int idx = _lstSplits.SelectedIndices[0];
            _splits.RemoveAt(idx);

            // Re-index remaining
            for (int i = 0; i < _splits.Count; i++)
            {
                var s = _splits[i];
                _splits[i] = new TransactionSplitItem(i + 1, s.Amount, s.Category, s.Note, s.Tags);
            }

            RefreshSplitsList();
            UpdateBalanceInvariantStatus();
        }
    }

    private void RefreshSplitsList()
    {
        _lstSplits.Items.Clear();
        foreach (var s in _splits)
        {
            var lvi = new ListViewItem(s.Index.ToString(CultureInfo.InvariantCulture));
            lvi.SubItems.Add(s.Amount.ToString("N2", CultureInfo.InvariantCulture) + " " + _transaction.Currency);
            lvi.SubItems.Add(s.Category);
            lvi.SubItems.Add(s.Note ?? string.Empty);
            _lstSplits.Items.Add(lvi);
        }
    }

    private void UpdateBalanceInvariantStatus()
    {
        decimal targetAmount = _transaction.Amount;
        decimal allocated = 0m;
        foreach (var s in _splits)
        {
            allocated += s.Amount;
        }

        decimal remaining = targetAmount - allocated;

        _lblTotalAllocated.Text = $"Allocated: {allocated.ToString("N2", CultureInfo.InvariantCulture)} {_transaction.Currency}";
        _lblRemainingBalance.Text = $"Remaining: {remaining.ToString("N2", CultureInfo.InvariantCulture)} {_transaction.Currency}";

        if (remaining == 0m && _splits.Count >= 2)
        {
            _lblValidationStatus.Text = "[✓ Balance Exact Match — Ready to Save]";
            _lblValidationStatus.ForeColor = Color.DarkGreen;
            _btnSave.Enabled = true;
        }
        else if (remaining < 0m)
        {
            _lblValidationStatus.Text = $"[⚠ Overallocated by {Math.Abs(remaining).ToString("N2", CultureInfo.InvariantCulture)} {_transaction.Currency}]";
            _lblValidationStatus.ForeColor = Color.DarkRed;
            _btnSave.Enabled = false;
        }
        else
        {
            _lblValidationStatus.Text = _splits.Count < 2
                ? $"[⚠ Must create at least 2 split lines; remaining: {remaining.ToString("N2", CultureInfo.InvariantCulture)} {_transaction.Currency}]"
                : $"[⚠ Unallocated balance remaining: {remaining.ToString("N2", CultureInfo.InvariantCulture)} {_transaction.Currency}]";
            _lblValidationStatus.ForeColor = Color.DarkOrange;
            _btnSave.Enabled = false;
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            _session.SaveTransactionSplits(_transaction.TransactionId, _splits);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save transaction splits: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Helper method for headless verification of balance allocation and save enablement.
    /// </summary>
    public void AddSplitForTesting(decimal amount, string category, string? note = null)
    {
        int nextIndex = _splits.Count + 1;
        _splits.Add(new TransactionSplitItem(nextIndex, amount, category, note));
        RefreshSplitsList();
        UpdateBalanceInvariantStatus();
    }
}
