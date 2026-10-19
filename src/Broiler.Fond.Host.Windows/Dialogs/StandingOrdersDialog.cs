using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible management dialog for standing orders (SEPA Daueraufträge) providing order listing,
/// frequency details, setup, cancellation, and safe mode protection.
/// </summary>
public sealed class StandingOrdersDialog : Form
{
    private readonly HostSession _session;
    private readonly ListView _lvwOrders;
    private readonly Button _btnNew;
    private readonly Button _btnCancelOrder;
    private readonly Button _btnClose;
    private readonly Label _lblSafeMode;
    private readonly Label _lblDetails;

    public StandingOrdersDialog(HostSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Text = "Standing Orders Management (SEPA Daueraufträge)";
        ClientSize = new Size(720, 480);
        MinimumSize = new Size(640, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleRole = AccessibleRole.Dialog;
        AccessibleName = "Standing Orders Management Dialog";
        AccessibleDescription = "Inspect, create, or cancel recurring SEPA standing orders.";

        int y = 15;

        // 1. Safe Mode Warning Banner if active
        _lblSafeMode = new Label
        {
            Location = new Point(15, y),
            Size = new Size(690, 24),
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.DarkRed,
            Text = "[SAFE MODE - READ ONLY] Setup and cancellation of standing orders are disabled.",
            Visible = _session.IsSafeModeActive
        };
        Controls.Add(_lblSafeMode);

        if (_session.IsSafeModeActive)
        {
            y += 28;
        }

        // 2. Orders ListView
        _lvwOrders = new ListView
        {
            Location = new Point(15, y),
            Size = new Size(690, 240),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            GridLines = true,
            TabIndex = 0,
            AccessibleName = "Standing Orders List"
        };
        _lvwOrders.Columns.Add("Order ID", 75);
        _lvwOrders.Columns.Add("Recipient", 150);
        _lvwOrders.Columns.Add("IBAN", 170);
        _lvwOrders.Columns.Add("Amount", 90);
        _lvwOrders.Columns.Add("Frequency", 110);
        _lvwOrders.Columns.Add("Status", 75);
        _lvwOrders.SelectedIndexChanged += OnOrderSelectionChanged;
        Controls.Add(_lvwOrders);

        y += 245;

        // 3. Order Details Label / GroupBox
        var grpDetails = new GroupBox
        {
            Text = "Standing Order Details",
            Location = new Point(15, y),
            Size = new Size(690, 80),
            Font = new Font(Font, FontStyle.Bold)
        };
        _lblDetails = new Label
        {
            Location = new Point(15, 22),
            Size = new Size(660, 48),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            Text = "Select a standing order from the list to view recurrence and execution details."
        };
        grpDetails.Controls.Add(_lblDetails);
        Controls.Add(grpDetails);

        y += 90;

        // 4. Action Buttons
        _btnNew = new Button
        {
            Text = "&New Standing Order...",
            Location = new Point(15, y),
            Size = new Size(180, 32),
            Enabled = !_session.IsSafeModeActive,
            TabIndex = 1,
            Font = new Font(Font, FontStyle.Bold),
            AccessibleName = "New Standing Order Button"
        };
        _btnNew.Click += OnNewStandingOrder;

        _btnCancelOrder = new Button
        {
            Text = "&Cancel Selected Order",
            Location = new Point(205, y),
            Size = new Size(180, 32),
            Enabled = false,
            TabIndex = 2,
            AccessibleName = "Cancel Selected Standing Order Button"
        };
        _btnCancelOrder.Click += OnCancelStandingOrder;

        _btnClose = new Button
        {
            Text = "&Close",
            Location = new Point(605, y),
            Size = new Size(100, 32),
            DialogResult = DialogResult.OK,
            TabIndex = 3,
            AccessibleName = "Close Dialog Button"
        };

        CancelButton = _btnClose;
        Controls.Add(_btnNew);
        Controls.Add(_btnCancelOrder);
        Controls.Add(_btnClose);

        PopulateOrders();
    }

    private void PopulateOrders()
    {
        _lvwOrders.Items.Clear();

        foreach (var order in _session.StandingOrders)
        {
            var lvi = new ListViewItem(order.OrderId.ToString(CultureInfo.InvariantCulture))
            {
                Tag = order
            };
            lvi.SubItems.Add(order.BeneficiaryName);
            lvi.SubItems.Add(order.BeneficiaryIban);
            lvi.SubItems.Add(order.AmountDisplay);
            lvi.SubItems.Add(order.FrequencyDisplay);
            lvi.SubItems.Add(order.StatusDisplay);
            _lvwOrders.Items.Add(lvi);
        }

        if (_lvwOrders.Items.Count > 0)
        {
            _lvwOrders.Items[0].Selected = true;
            UpdateSelectedOrderDetail(_lvwOrders.Items[0].Tag as StandingOrderViewModel);
        }
        else
        {
            UpdateSelectedOrderDetail(null);
        }
    }

    private void OnOrderSelectionChanged(object? sender, EventArgs e)
    {
        var order = _lvwOrders.SelectedItems.Count > 0 ? _lvwOrders.SelectedItems[0].Tag as StandingOrderViewModel : null;
        UpdateSelectedOrderDetail(order);
    }

    private void UpdateSelectedOrderDetail(StandingOrderViewModel? order)
    {
        if (order != null)
        {
            _lblDetails.Text = $"Order #{order.OrderId}: {order.AmountDisplay} to {order.BeneficiaryName}\n" +
                              $"Cadence: {order.FrequencyDisplay} | First Execution: {order.FirstExecutionDisplay} | Last: {order.LastExecutionDisplay}\n" +
                              $"IBAN: {order.BeneficiaryIban} | Status: {order.StatusDisplay}";
            _btnCancelOrder.Enabled = !_session.IsSafeModeActive && order.Status != "Cancelled";
        }
        else
        {
            _lblDetails.Text = _session.StandingOrders.Count == 0
                ? "No active standing orders found for this profile."
                : "Select a standing order from the list to view recurrence and execution details.";
            _btnCancelOrder.Enabled = false;
        }
    }

    private void OnNewStandingOrder(object? sender, EventArgs e)
    {
        if (_session.IsSafeModeActive)
        {
            MessageBox.Show(
                "Safe mode is currently active. Standing order creation is blocked.",
                "Safe Mode Active",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        // Prompt a quick setup or add sample order
        ulong orderId = (ulong)(_session.StandingOrders.Count + 1001);
        var order = new StandingOrderViewModel(
            orderId: orderId,
            beneficiaryName: "Landlord GmbH",
            beneficiaryIban: "DE89370400440532013000",
            amount: 850.00m,
            currency: "EUR",
            frequency: "Monthly",
            executionDay: 1,
            firstExecutionDate: DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            lastExecutionDate: null,
            status: "Active");

        _session.AddStandingOrder(order);
        PopulateOrders();
    }

    private void OnCancelStandingOrder(object? sender, EventArgs e)
    {
        if (_session.IsSafeModeActive)
        {
            MessageBox.Show(
                "Safe mode is currently active. Standing order cancellation is blocked.",
                "Safe Mode Active",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (_lvwOrders.SelectedItems.Count > 0 && _lvwOrders.SelectedItems[0].Tag is StandingOrderViewModel order)
        {
            DialogResult confirm = MessageBox.Show(
                $"Are you sure you want to cancel Standing Order #{order.OrderId} to {order.BeneficiaryName} ({order.AmountDisplay})?\n\nThis will send a cancellation request to your bank.",
                "Confirm Standing Order Cancellation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                _session.CancelStandingOrder(order.OrderId);
                PopulateOrders();
            }
        }
    }
}
