using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Domain.Insights;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible cash-flow forecast dialog presenting labeled future estimates and low-balance warnings.
/// Non-conflation invariant: all projected items are explicitly labeled [Estimate / Prognose].
/// </summary>
public sealed class CashFlowForecastDialog : Form
{
    private readonly HostSession _session;
    private readonly ComboBox _cmbAccount;
    private readonly ComboBox _cmbHorizon;
    private readonly TextBox _txtSafetyThreshold;
    private readonly Button _btnCalculate;
    private readonly Panel _pnlAlertBanner;
    private readonly Label _lblAlertWarning;
    private readonly ListView _lstTimeline;
    private readonly Label _lblSummary;
    private readonly Button _btnClose;

    public CashFlowForecastResult? CurrentForecast { get; private set; }

    public CashFlowForecastDialog(HostSession session, ulong? initialAccountId = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;

        Text = "Cash-Flow Forecast & Projections [Estimates Only]";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(740, 580);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleName = "Cash-Flow Forecast Dialog";
        AccessibleDescription = "View projected cash-flow timeline and low-balance warnings based on recurring obligations.";

        // Header controls
        var grpParams = new GroupBox
        {
            Text = "Forecast Parameters (Non-binding Projections)",
            Location = new Point(15, 12),
            Size = new Size(710, 80)
        };

        var lblAcc = new Label { Text = "Account:", Location = new Point(12, 26), Size = new Size(60, 20) };
        _cmbAccount = new ComboBox
        {
            Location = new Point(75, 23),
            Size = new Size(190, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Forecast Account Selector"
        };

        var lblHoriz = new Label { Text = "Horizon:", Location = new Point(280, 26), Size = new Size(55, 20) };
        _cmbHorizon = new ComboBox
        {
            Location = new Point(340, 23),
            Size = new Size(110, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Forecast Horizon Selector"
        };
        _cmbHorizon.Items.AddRange(["30 Days", "60 Days", "90 Days"]);
        _cmbHorizon.SelectedIndex = 0;

        var lblThresh = new Label { Text = "Safety Min:", Location = new Point(465, 26), Size = new Size(70, 20) };
        _txtSafetyThreshold = new TextBox
        {
            Location = new Point(540, 23),
            Size = new Size(75, 23),
            Text = "200.00",
            AccessibleName = "Safety Alert Threshold Input"
        };

        _btnCalculate = new Button
        {
            Text = "&Calculate",
            Location = new Point(625, 22),
            Size = new Size(75, 26),
            AccessibleName = "Calculate Forecast Button"
        };
        _btnCalculate.Click += BtnCalculate_Click;

        grpParams.Controls.Add(lblAcc);
        grpParams.Controls.Add(_cmbAccount);
        grpParams.Controls.Add(lblHoriz);
        grpParams.Controls.Add(_cmbHorizon);
        grpParams.Controls.Add(lblThresh);
        grpParams.Controls.Add(_txtSafetyThreshold);
        grpParams.Controls.Add(_btnCalculate);

        // Low-Balance Alert Banner
        _pnlAlertBanner = new Panel
        {
            Location = new Point(15, 100),
            Size = new Size(710, 36),
            BackColor = Color.MistyRose,
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false,
            AccessibleName = "Low Balance Warning Banner"
        };

        _lblAlertWarning = new Label
        {
            Location = new Point(8, 7),
            Size = new Size(690, 20),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.DarkRed,
            Text = "[⚠ Low-Balance Warning]"
        };
        _pnlAlertBanner.Controls.Add(_lblAlertWarning);

        // Timeline ListView
        _lstTimeline = new ListView
        {
            Location = new Point(15, 145),
            Size = new Size(710, 335),
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            AccessibleName = "Forecast Timeline List"
        };
        _lstTimeline.Columns.Add("Date", 85);
        _lstTimeline.Columns.Add("Classification", 140);
        _lstTimeline.Columns.Add("Expected Obligation / Item", 195);
        _lstTimeline.Columns.Add("Amount", 95);
        _lstTimeline.Columns.Add("Type", 65);
        _lstTimeline.Columns.Add("Projected Balance", 115);

        // Summary Label
        _lblSummary = new Label
        {
            Location = new Point(15, 490),
            Size = new Size(710, 30),
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Text = "Starting Balance: — | Min Projected: — | End Projected: —",
            AccessibleName = "Forecast Summary Metrics Label"
        };

        // Close button
        _btnClose = new Button
        {
            Text = "&Close",
            Location = new Point(615, 535),
            Size = new Size(110, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close Forecast Button"
        };

        Controls.Add(grpParams);
        Controls.Add(_pnlAlertBanner);
        Controls.Add(_lstTimeline);
        Controls.Add(_lblSummary);
        Controls.Add(_btnClose);

        CancelButton = _btnClose;

        PopulateAccounts(initialAccountId);
    }

    private void PopulateAccounts(ulong? initialAccountId)
    {
        _cmbAccount.Items.Clear();
        int selectedIndex = 0;
        int idx = 0;

        foreach (var acc in _session.Accounts)
        {
            _cmbAccount.Items.Add($"{acc.AccountName} (#{acc.AccountId})");
            if (initialAccountId.HasValue && acc.AccountId == initialAccountId.Value)
            {
                selectedIndex = idx;
            }
            idx++;
        }

        if (_cmbAccount.Items.Count > 0)
        {
            _cmbAccount.SelectedIndex = selectedIndex;
            CalculateCurrentForecast();
        }
    }

    private void BtnCalculate_Click(object? sender, EventArgs e)
    {
        CalculateCurrentForecast();
    }

    public void CalculateCurrentForecast()
    {
        if (_cmbAccount.SelectedIndex < 0 || _session.Accounts.Count == 0)
        {
            return;
        }

        var accountVm = _session.Accounts[_cmbAccount.SelectedIndex];
        int horizon = _cmbHorizon.SelectedIndex switch
        {
            1 => 60,
            2 => 90,
            _ => 30
        };

        if (!decimal.TryParse(_txtSafetyThreshold.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal threshold))
        {
            threshold = 200m;
        }

        CurrentForecast = _session.GenerateForecast(accountVm.AccountId, horizon, threshold);

        _lstTimeline.Items.Clear();
        decimal runningBalance = CurrentForecast.CurrentBookedBalance;
        foreach (var item in CurrentForecast.ForecastItems)
        {
            runningBalance += item.Amount;
            var lvi = new ListViewItem(item.ExpectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            lvi.SubItems.Add("[Estimate / Prognose]");
            lvi.SubItems.Add(item.CounterpartyName);
            lvi.SubItems.Add(Math.Abs(item.Amount).ToString("N2", CultureInfo.InvariantCulture) + " " + item.Currency);
            lvi.SubItems.Add(item.IsCredit ? "Income" : "Expense");
            lvi.SubItems.Add(runningBalance.ToString("N2", CultureInfo.InvariantCulture) + " " + item.Currency);

            if (runningBalance < threshold)
            {
                lvi.ForeColor = Color.DarkRed;
                lvi.Font = new Font(_lstTimeline.Font, FontStyle.Bold);
            }

            _lstTimeline.Items.Add(lvi);
        }

        decimal minBalance = CurrentForecast.DailyBalances.Count > 0
            ? CurrentForecast.DailyBalances.Min(d => d.ProjectedBalance)
            : CurrentForecast.CurrentBookedBalance;

        _lblSummary.Text = $"Starting Booked: {CurrentForecast.CurrentBookedBalance.ToString("N2", CultureInfo.InvariantCulture)} {CurrentForecast.Currency} | " +
                           $"Min Projected: {minBalance.ToString("N2", CultureInfo.InvariantCulture)} {CurrentForecast.Currency} | " +
                           $"End Projected: {CurrentForecast.ProjectedClosingBalance.ToString("N2", CultureInfo.InvariantCulture)} {CurrentForecast.Currency}";

        if (CurrentForecast.HasLowBalanceAlerts)
        {
            var firstAlert = CurrentForecast.LowBalanceAlerts[0];
            _pnlAlertBanner.Visible = true;
            _lblAlertWarning.Text = $"[⚠ LOW-BALANCE WARNING: Projected balance dips to {firstAlert.ProjectedBalance.ToString("N2", CultureInfo.InvariantCulture)} {CurrentForecast.Currency} on {firstAlert.Date:yyyy-MM-dd} (Shortfall: {firstAlert.Shortfall.ToString("N2", CultureInfo.InvariantCulture)})]";
        }
        else
        {
            _pnlAlertBanner.Visible = false;
        }
    }
}
