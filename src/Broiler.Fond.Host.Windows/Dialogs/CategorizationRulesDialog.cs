using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Domain.Insights;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible rule editor dialog with live preview and reversible application against profile transactions.
/// </summary>
public sealed class CategorizationRulesDialog : Form
{
    private readonly HostSession _session;
    private readonly ListView _lstRules;
    private readonly TextBox _txtCategory;
    private readonly TextBox _txtPattern;
    private readonly ComboBox _cmbMatchField;
    private readonly NumericUpDown _numPriority;
    private readonly Button _btnAddRule;
    private readonly Button _btnRemoveRule;
    private readonly Button _btnPreviewRule;
    private readonly Label _lblPreviewStatus;
    private readonly ListView _lstPreview;
    private readonly Button _btnApplyRules;
    private readonly Button _btnClose;

    public CategorizationRulesDialog(HostSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;

        Text = "Categorization Rules & Live Preview";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(680, 560);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleName = "Categorization Rules Dialog";
        AccessibleDescription = "Manage deterministic categorization rules and preview matches against transaction history.";

        // Rules list
        var grpList = new GroupBox
        {
            Text = "Active Categorization Rules",
            Location = new Point(15, 12),
            Size = new Size(650, 160)
        };

        _lstRules = new ListView
        {
            Location = new Point(12, 22),
            Size = new Size(625, 125),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            AccessibleName = "Categorization Rules List"
        };
        _lstRules.Columns.Add("Priority", 65);
        _lstRules.Columns.Add("Target Category", 160);
        _lstRules.Columns.Add("Match Field", 110);
        _lstRules.Columns.Add("Pattern / Criteria", 280);

        grpList.Controls.Add(_lstRules);

        // Add rule controls
        var grpAdd = new GroupBox
        {
            Text = "Add / Configure Categorization Rule",
            Location = new Point(15, 180),
            Size = new Size(650, 105)
        };

        var lblCat = new Label { Text = "Target Category:", Location = new Point(12, 26), Size = new Size(100, 20) };
        _txtCategory = new TextBox
        {
            Location = new Point(115, 23),
            Size = new Size(170, 23),
            AccessibleName = "Rule Target Category Input"
        };

        var lblField = new Label { Text = "Field:", Location = new Point(295, 26), Size = new Size(40, 20) };
        _cmbMatchField = new ComboBox
        {
            Location = new Point(340, 23),
            Size = new Size(120, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Rule Match Field Selector"
        };
        _cmbMatchField.Items.AddRange(["Counterparty", "Remittance", "IBAN", "Any"]);
        _cmbMatchField.SelectedIndex = 0;

        var lblPriority = new Label { Text = "Priority:", Location = new Point(475, 26), Size = new Size(50, 20) };
        _numPriority = new NumericUpDown
        {
            Location = new Point(530, 23),
            Size = new Size(100, 23),
            Minimum = 1,
            Maximum = 10000,
            Value = 100,
            AccessibleName = "Rule Priority Input"
        };

        var lblPat = new Label { Text = "Pattern / Keyword:", Location = new Point(12, 63), Size = new Size(100, 20) };
        _txtPattern = new TextBox
        {
            Location = new Point(115, 60),
            Size = new Size(345, 23),
            AccessibleName = "Rule Match Pattern Input"
        };

        _btnAddRule = new Button
        {
            Text = "&Add Rule",
            Location = new Point(475, 58),
            Size = new Size(75, 28),
            AccessibleName = "Add Rule Button"
        };
        _btnAddRule.Click += BtnAddRule_Click;

        _btnRemoveRule = new Button
        {
            Text = "&Remove",
            Location = new Point(555, 58),
            Size = new Size(75, 28),
            Enabled = false,
            AccessibleName = "Remove Selected Rule Button"
        };
        _btnRemoveRule.Click += BtnRemoveRule_Click;

        grpAdd.Controls.Add(lblCat);
        grpAdd.Controls.Add(_txtCategory);
        grpAdd.Controls.Add(lblField);
        grpAdd.Controls.Add(_cmbMatchField);
        grpAdd.Controls.Add(lblPriority);
        grpAdd.Controls.Add(_numPriority);
        grpAdd.Controls.Add(lblPat);
        grpAdd.Controls.Add(_txtPattern);
        grpAdd.Controls.Add(_btnAddRule);
        grpAdd.Controls.Add(_btnRemoveRule);

        // Live preview group
        var grpPreview = new GroupBox
        {
            Text = "Live Rule Preview (Deterministic Evaluation Against Profile Transactions)",
            Location = new Point(15, 295),
            Size = new Size(650, 215)
        };

        _btnPreviewRule = new Button
        {
            Text = "&Preview / Test Selected Rule",
            Location = new Point(12, 23),
            Size = new Size(190, 28),
            Enabled = false,
            AccessibleName = "Test Rule Live Preview Button"
        };
        _btnPreviewRule.Click += BtnPreviewRule_Click;
        _lstRules.SelectedIndexChanged += (s, e) =>
        {
            bool hasSelection = _lstRules.SelectedIndices.Count > 0;
            _btnRemoveRule.Enabled = hasSelection;
            _btnPreviewRule.Enabled = hasSelection;
        };

        _lblPreviewStatus = new Label
        {
            Text = "[Select a rule and click Preview to test matches]",
            Location = new Point(210, 28),
            Size = new Size(425, 20),
            Font = new Font("Segoe UI", 9F, FontStyle.Italic),
            AccessibleName = "Rule Preview Match Count Label"
        };

        _lstPreview = new ListView
        {
            Location = new Point(12, 58),
            Size = new Size(625, 145),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            AccessibleName = "Rule Preview Matches List"
        };
        _lstPreview.Columns.Add("Date", 85);
        _lstPreview.Columns.Add("Counterparty", 170);
        _lstPreview.Columns.Add("Amount", 95);
        _lstPreview.Columns.Add("Current Category", 130);
        _lstPreview.Columns.Add("Proposed Category", 135);

        grpPreview.Controls.Add(_btnPreviewRule);
        grpPreview.Controls.Add(_lblPreviewStatus);
        grpPreview.Controls.Add(_lstPreview);

        // Bottom action buttons
        _btnApplyRules = new Button
        {
            Text = "&Apply All Rules Now",
            Location = new Point(390, 520),
            Size = new Size(150, 30),
            AccessibleName = "Apply Rules to Transactions Button"
        };
        _btnApplyRules.Click += BtnApplyRules_Click;

        _btnClose = new Button
        {
            Text = "&Close",
            Location = new Point(555, 520),
            Size = new Size(110, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close Button"
        };

        Controls.Add(grpList);
        Controls.Add(grpAdd);
        Controls.Add(grpPreview);
        Controls.Add(_btnApplyRules);
        Controls.Add(_btnClose);

        CancelButton = _btnClose;

        RefreshRulesList();
    }

    private void RefreshRulesList()
    {
        _lstRules.Items.Clear();
        foreach (var rule in _session.Rules.OrderByDescending(r => r.Priority))
        {
            var lvi = new ListViewItem(rule.Priority.ToString(CultureInfo.InvariantCulture));
            lvi.SubItems.Add(rule.TargetCategory);
            lvi.SubItems.Add(GetFieldDisplay(rule));
            lvi.SubItems.Add(GetPatternDisplay(rule));
            lvi.Tag = rule;
            _lstRules.Items.Add(lvi);
        }
    }

    private static string GetFieldDisplay(CategorizationRule rule)
    {
        if (!string.IsNullOrEmpty(rule.Criteria.CounterpartyPattern)) return "Counterparty";
        if (!string.IsNullOrEmpty(rule.Criteria.RemittancePattern)) return "Remittance";
        if (!string.IsNullOrEmpty(rule.Criteria.CounterpartyIban)) return "IBAN";
        return "Custom";
    }

    private static string GetPatternDisplay(CategorizationRule rule)
    {
        if (!string.IsNullOrEmpty(rule.Criteria.CounterpartyPattern)) return rule.Criteria.CounterpartyPattern;
        if (!string.IsNullOrEmpty(rule.Criteria.RemittancePattern)) return rule.Criteria.RemittancePattern;
        if (!string.IsNullOrEmpty(rule.Criteria.CounterpartyIban)) return rule.Criteria.CounterpartyIban;
        return "*";
    }

    private void BtnAddRule_Click(object? sender, EventArgs e)
    {
        string cat = _txtCategory.Text.Trim();
        if (string.IsNullOrWhiteSpace(cat))
        {
            MessageBox.Show("Please enter a target category name.", "Category Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtCategory.Focus();
            return;
        }

        string pattern = _txtPattern.Text.Trim();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            MessageBox.Show("Please enter a match pattern or keyword.", "Pattern Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtPattern.Focus();
            return;
        }

        ulong nextId = (ulong)(_session.Rules.Count + 1);
        int priority = (int)_numPriority.Value;

        RuleCriteria criteria = _cmbMatchField.SelectedIndex switch
        {
            0 => new RuleCriteria(CounterpartyPattern: pattern),
            1 => new RuleCriteria(RemittancePattern: pattern),
            2 => new RuleCriteria(CounterpartyIban: pattern),
            _ => new RuleCriteria(CounterpartyPattern: pattern, RemittancePattern: pattern),
        };

        CategorizationRule rule = CategorizationRule.Create(nextId, $"{cat} Rule", cat, criteria, priority: priority);

        _session.AddRule(rule);
        _txtCategory.Clear();
        _txtPattern.Clear();
        RefreshRulesList();
    }

    private void BtnRemoveRule_Click(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndices.Count > 0 && _lstRules.SelectedItems[0].Tag is CategorizationRule rule)
        {
            _session.RemoveRule(rule.Id);
            RefreshRulesList();
            _lstPreview.Items.Clear();
            _lblPreviewStatus.Text = "[Select a rule and click Preview to test matches]";
        }
    }

    private void BtnPreviewRule_Click(object? sender, EventArgs e)
    {
        if (_lstRules.SelectedIndices.Count > 0 && _lstRules.SelectedItems[0].Tag is CategorizationRule rule)
        {
            var preview = _session.PreviewRule(rule);
            _lblPreviewStatus.Text = $"[✓ Matched {preview.MatchCount} of {preview.TotalEvaluated} transactions ({preview.MatchedTransactions.Count} preview items)]";
            _lblPreviewStatus.ForeColor = Color.DarkGreen;

            _lstPreview.Items.Clear();
            foreach (var match in preview.MatchedTransactions)
            {
                var lvi = new ListViewItem(match.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                lvi.SubItems.Add(match.CounterpartyName ?? "[Unknown]");
                lvi.SubItems.Add(match.Amount.ToString("N2", CultureInfo.InvariantCulture) + " " + match.Currency);
                lvi.SubItems.Add(match.CurrentCategory ?? "[Uncategorized]");
                lvi.SubItems.Add(rule.TargetCategory);
                _lstPreview.Items.Add(lvi);
            }
        }
    }

    private void BtnApplyRules_Click(object? sender, EventArgs e)
    {
        int appliedCount = _session.ApplyRules();
        MessageBox.Show($"Categorization rules evaluated. Applied category changes to {appliedCount} transactions.",
            "Rules Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Helper method for headless verification of live preview calculation.
    /// </summary>
    public int TriggerPreviewForTesting(CategorizationRule rule)
    {
        var preview = _session.PreviewRule(rule);
        _lblPreviewStatus.Text = $"Matched {preview.MatchCount}";
        return preview.MatchCount;
    }
}
