using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Dialogs;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.Domain.Transactions;
using Broiler.Fond.Kernel.Domain.Transactions.Query;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Product;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Host.Windows.Views;

/// <summary>
/// Accessible, truthful Windows banking UI fulfilling Milestone 1 items M1-06, M1-07, and M1-08:
/// Profile unlock/lock, manual setup, endpoint review, session PIN/TAN prompts, account freshness/balances,
/// strict multi-currency isolation, connection disconnect/purge, and reviewed redacted support export.
/// </summary>
public sealed class MainForm : Form
{
    private readonly HostSession _session;
    private readonly MenuStrip _menuStrip;
    private readonly ToolStrip _toolStrip;
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _lblProfileStatus;
    private readonly ToolStripStatusLabel _lblConnectionStatus;
    private readonly ToolStripStatusLabel _lblTransactionsStatus;
    private readonly ToolStripStatusLabel _lblCurrencyTotalsStatus;

    private readonly TabControl _tabControl;
    private readonly TabPage _tabAccounts;
    private readonly TabPage _tabTransactions;
    private readonly TabPage _tabInsights;
    private readonly TabPage _tabConnections;
    private readonly TabPage _tabSecurity;

    // Insights & Budgets tab
    private readonly Button _btnTxSplit = new();
    private readonly Button _btnPrevMonth = new();
    private readonly Button _btnNextMonth = new();
    private readonly Label _lblCurrentMonth = new();
    private readonly Button _btnOpenRules = new();
    private readonly Button _btnOpenForecast = new();
    private readonly ListView _lstBudgets = new();
    private readonly ComboBox _cmbBudgetCategory = new();
    private readonly TextBox _txtBudgetTargetAmount = new();
    private readonly Button _btnSetBudget = new();
    private readonly ListView _lstNetWorth = new();
    private readonly Label _lblNetWorthDetail = new();
    private DateOnly _selectedInsightMonth = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    // Accounts tab
    private readonly ListView _lstAccounts;
    private readonly GroupBox _grpAccountDetail;
    private readonly Label _lblDetailName;
    private readonly Label _lblDetailType;
    private readonly Label _lblDetailHolder;
    private readonly Label _lblDetailLocator;
    private readonly Label _lblDetailBooked;
    private readonly Label _lblDetailAvailable;
    private readonly Label _lblDetailFreshness;
    private readonly Label _lblDetailBookedAt;
    private readonly Label _lblDetailRetrievedAt;
    private readonly Button _btnRelinkBinding;
    private readonly Button _btnRefreshAccount;
    private readonly ListBox _lstCurrencyTotals;

    // Transactions tab
    private readonly ComboBox _cmbTxAccount = new();
    private readonly ComboBox _cmbTxStatus = new();
    private readonly ComboBox _cmbTxDirection = new();
    private readonly CheckBox _chkTxFromDate = new();
    private readonly DateTimePicker _dtpTxFromDate = new();
    private readonly CheckBox _chkTxToDate = new();
    private readonly DateTimePicker _dtpTxToDate = new();
    private readonly TextBox _txtTxSearch = new();
    private readonly Button _btnTxFilter = new();
    private readonly Button _btnTxReset = new();
    private readonly Button _btnTxExportCsv = new();
    private readonly Label _lblTxSummary = new();

    private readonly ListView _lstTransactions = new();
    private readonly GroupBox _grpTransactionDetail = new();
    private readonly Label _lblTxDetailAmount = new();
    private readonly Label _lblTxDetailStatus = new();
    private readonly Label _lblTxDetailDates = new();
    private readonly Label _lblTxDetailAccount = new();
    private readonly Label _lblTxDetailCounterparty = new();
    private readonly TextBox _txtTxDetailRemittance = new();
    private readonly Label _lblTxDetailReferences = new();
    private readonly Label _lblTxDetailCodes = new();
    private readonly Label _lblTxDetailAnnotations = new();
    private readonly Label _lblTxDetailProvenance = new();
    private readonly Button _btnTxReturnDirectDebit = new();

    // Connections tab
    private readonly ListView _lstConnections;
    private readonly GroupBox _grpConnectionDetail;
    private readonly Label _lblConnInst;
    private readonly Label _lblConnBlz;
    private readonly Label _lblConnUser;
    private readonly Label _lblConnHost;
    private readonly Label _lblConnState;
    private readonly Label _lblConnProcedures;
    private readonly Button _btnAddConnection;
    private readonly Button _btnSyncConnection;
    private readonly Button _btnReauthenticate;
    private readonly Button _btnEditEndpoint;
    private readonly Button _btnRemoveConnection;

    // Payments tab
    private readonly TabPage _tabPayments;
    private readonly ListView _lstPayments;
    private readonly GroupBox _grpPaymentDetail;
    private readonly Label _lblPayDetailRecipient;
    private readonly Label _lblPayDetailIban;
    private readonly Label _lblPayDetailAmount;
    private readonly Label _lblPayDetailRemittance;
    private readonly Label _lblPayDetailState;
    private readonly Label _lblPayDetailVoP;
    private readonly Label _lblPayDetailEndToEndId;
    private readonly Label _lblPayDetailBankRef;
    private readonly ListView _lstPaymentAudit;
    private readonly Button _btnPayNew;
    private readonly Button _btnPayReconcile;
    private readonly Button _btnPayRetry;
    private readonly Button _btnStandingOrders;

    // Toolbar and Status buttons
    private readonly ToolStripButton _btnToolbarUnlock;
    private readonly ToolStripButton _btnToolbarLock;
    private readonly ToolStripButton _btnToolbarAddConnection;
    private readonly ToolStripButton _btnToolbarRefresh;
    private readonly ToolStripButton _btnToolbarDiscover;
    private readonly ToolStripButton _btnToolbarExportCsv;
    private readonly ToolStripButton _btnToolbarNewPayment;
    private readonly ToolStripButton _btnToolbarSafeMode;
    private readonly ToolStripButton _btnToolbarSupportExport;

    private readonly ToolStripStatusLabel _lblPaymentsStatus;
    private readonly ToolStripStatusLabel _lblSafeModeStatus;
    private readonly ToolStripMenuItem _mnuSafeMode;

    public MainForm(HostSession? session = null)
    {
        _session = session ?? new HostSession();
        _session.StateChanged += (s, e) => UpdateUIState();

        Text = $"{KernelProduct.FullDisplayName} - Milestone {KernelProduct.Milestone}";
        ClientSize = new Size(960, 640);
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "Broiler Fond Main Application Window";
        AccessibleRole = AccessibleRole.Client;
        AccessibleDescription = "Read-only personal finance application with truthful bank-confirmed balances.";

        // Menu strip
        _mnuSafeMode = new ToolStripMenuItem("&Read-Only Safe Mode (Disables Payments)", null, (s, e) => ToggleSafeMode(), Keys.Control | Keys.Shift | Keys.S) { CheckOnClick = true, AccessibleName = "Safe Mode Menu Item" };
        _menuStrip = new MenuStrip { TabIndex = 0 };
        BuildMenu();

        // Tool strip
        _toolStrip = new ToolStrip { TabIndex = 1 };
        _btnToolbarUnlock = new ToolStripButton("&Unlock Profile", null, (s, e) => ShowUnlockDialog()) { AccessibleName = "Unlock Profile Toolbar Button" };
        _btnToolbarLock = new ToolStripButton("&Lock", null, (s, e) => _session.Lock()) { Enabled = false, AccessibleName = "Lock Profile Toolbar Button" };
        _btnToolbarAddConnection = new ToolStripButton("&Add Connection", null, (s, e) => ShowAddConnectionDialog()) { Enabled = false, AccessibleName = "Add Bank Connection Toolbar Button" };
        _btnToolbarRefresh = new ToolStripButton("&Refresh Balances", null, (s, e) => RefreshSelectedAccount()) { Enabled = false, AccessibleName = "Refresh Balances Toolbar Button" };
        _btnToolbarDiscover = new ToolStripButton("&Discover Accounts", null, (s, e) => DiscoverAccounts()) { Enabled = false, AccessibleName = "Discover Accounts Toolbar Button" };
        _btnToolbarExportCsv = new ToolStripButton("&Export Transactions CSV", null, (s, e) => ShowCsvExportDialog()) { Enabled = false, AccessibleName = "Export Transactions to CSV Toolbar Button" };
        _btnToolbarNewPayment = new ToolStripButton("&New Payment", null, (s, e) => ShowPaymentInitiationDialog()) { Enabled = false, AccessibleName = "New Payment Toolbar Button" };
        _btnToolbarSafeMode = new ToolStripButton("&Safe Mode: Off", null, (s, e) => ToggleSafeMode()) { AccessibleName = "Toggle Safe Mode Toolbar Button" };
        _btnToolbarSupportExport = new ToolStripButton("&Support Export", null, (s, e) => ShowSupportExportDialog()) { AccessibleName = "Export Support Diagnostics Toolbar Button" };

        _toolStrip.Items.AddRange(new ToolStripItem[]
        {
            _btnToolbarUnlock, _btnToolbarLock, new ToolStripSeparator(),
            _btnToolbarAddConnection, _btnToolbarRefresh, _btnToolbarDiscover, new ToolStripSeparator(),
            _btnToolbarExportCsv, new ToolStripSeparator(),
            _btnToolbarNewPayment, _btnToolbarSafeMode, new ToolStripSeparator(),
            _btnToolbarSupportExport
        });

        // Status strip
        _statusStrip = new StatusStrip { TabIndex = 3 };
        _lblProfileStatus = new ToolStripStatusLabel("[Locked - No Profile Open]") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _lblConnectionStatus = new ToolStripStatusLabel("Connections: 0") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        _lblTransactionsStatus = new ToolStripStatusLabel("Transactions: 0") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        _lblPaymentsStatus = new ToolStripStatusLabel("Payments: 0") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        _lblCurrencyTotalsStatus = new ToolStripStatusLabel("Total: None") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        _lblSafeModeStatus = new ToolStripStatusLabel("[Safe Mode: Inactive]") { BorderSides = ToolStripStatusLabelBorderSides.Left, ForeColor = Color.DarkGreen };
        _statusStrip.Items.AddRange(new ToolStripItem[] { _lblProfileStatus, _lblConnectionStatus, _lblTransactionsStatus, _lblPaymentsStatus, _lblCurrencyTotalsStatus, _lblSafeModeStatus });

        // Tab Control
        _tabControl = new TabControl { Dock = DockStyle.Fill, TabIndex = 2 };
        _tabAccounts = new TabPage("Accounts & Balances (&1)") { AccessibleName = "Accounts Tab Page" };
        _tabTransactions = new TabPage("Transactions (&2)") { AccessibleName = "Transactions Tab Page" };
        _tabInsights = new TabPage("Insights & Budgets (&3)") { AccessibleName = "Insights Tab Page" };
        _tabConnections = new TabPage("Bank Connections (&4)") { AccessibleName = "Connections Tab Page" };
        _tabPayments = new TabPage("Payments (&5)") { AccessibleName = "Payments Tab Page" };
        _tabSecurity = new TabPage("Privacy & Diagnostics (&6)") { AccessibleName = "Security Tab Page" };

        // Initialize Payments controls
        _lstPayments = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            TabIndex = 0,
            AccessibleName = "Payment Attempts List",
            AccessibleRole = AccessibleRole.Table,
            AccessibleDescription = "List of all payment attempts, bank states, and VoP outcomes."
        };
        _grpPaymentDetail = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Payment Attempt & Audit Trail Evidence",
            TabIndex = 1,
            AccessibleName = "Payment Details Group",
            AccessibleRole = AccessibleRole.Grouping
        };
        _lblPayDetailRecipient = new Label { Location = new Point(15, 25), Size = new Size(330, 22), Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Text = "Select a payment attempt to view details." };
        _lblPayDetailIban = new Label { Location = new Point(15, 50), Size = new Size(330, 20), Text = string.Empty };
        _lblPayDetailAmount = new Label { Location = new Point(15, 72), Size = new Size(330, 24), Font = new Font(Font.FontFamily, 11, FontStyle.Bold), ForeColor = Color.Navy, Text = "--" };
        _lblPayDetailRemittance = new Label { Location = new Point(15, 98), Size = new Size(330, 36), Text = string.Empty };
        _lblPayDetailState = new Label { Location = new Point(15, 158), Size = new Size(330, 22), Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Text = "--" };
        _lblPayDetailVoP = new Label { Location = new Point(15, 182), Size = new Size(330, 20), Text = string.Empty };
        _lblPayDetailEndToEndId = new Label { Location = new Point(15, 204), Size = new Size(330, 20), Text = string.Empty };
        _lblPayDetailBankRef = new Label { Location = new Point(15, 226), Size = new Size(330, 20), Text = string.Empty };
        _lstPaymentAudit = new ListView
        {
            Location = new Point(15, 270),
            Size = new Size(330, 130),
            View = View.Details,
            FullRowSelect = true,
            TabIndex = 2,
            AccessibleName = "Payment State Transitions Audit List"
        };
        _btnPayNew = new Button { Location = new Point(15, 410), Size = new Size(100, 30), Text = "&New...", Enabled = false, TabIndex = 3, AccessibleName = "New Payment Transfer Button" };
        _btnPayNew.Click += (s, e) => ShowPaymentInitiationDialog();
        _btnPayReconcile = new Button { Location = new Point(125, 410), Size = new Size(105, 30), Text = "&Reconcile...", Enabled = false, TabIndex = 4, AccessibleName = "Reconcile Payment Button" };
        _btnPayReconcile.Click += (s, e) => ShowReconcilePaymentDialog();
        _btnPayRetry = new Button { Location = new Point(240, 410), Size = new Size(105, 30), Text = "&Retry...", Enabled = false, TabIndex = 5, AccessibleName = "Retry Payment Button" };
        _btnPayRetry.Click += (s, e) => ShowRetryPaymentDialog();
        _btnStandingOrders = new Button { Location = new Point(15, 448), Size = new Size(160, 30), Text = "&Standing Orders...", Enabled = false, TabIndex = 6, AccessibleName = "Standing Orders Button" };
        _btnStandingOrders.Click += (s, e) => ShowStandingOrdersDialog();

        _btnTxReturnDirectDebit.Text = "&Return Direct Debit...";
        _btnTxReturnDirectDebit.Location = new Point(15, 412);
        _btnTxReturnDirectDebit.Size = new Size(180, 30);
        _btnTxReturnDirectDebit.Visible = false;
        _btnTxReturnDirectDebit.TabIndex = 5;
        _btnTxReturnDirectDebit.AccessibleName = "Return Direct Debit Button";
        _btnTxReturnDirectDebit.Click += (s, e) => ShowDirectDebitReturnDialog();

        // 1. Build Accounts View
        var splitAccounts = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 580,
            TabIndex = 0
        };

        _lstAccounts = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            TabIndex = 0,
            AccessibleName = "Accounts List",
            AccessibleRole = AccessibleRole.Table,
            AccessibleDescription = "List of all discovered accounts and truthful balances."
        };
        _lstAccounts.Columns.Add("Account", 140);
        _lstAccounts.Columns.Add("Holder", 120);
        _lstAccounts.Columns.Add("IBAN / Locator", 160);
        _lstAccounts.Columns.Add("Booked Balance", 110);
        _lstAccounts.Columns.Add("Available", 100);
        _lstAccounts.Columns.Add("Freshness / State", 130);
        _lstAccounts.SelectedIndexChanged += (s, e) => UpdateAccountDetail();

        _grpAccountDetail = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Account Details & Truthful Balance Evidence",
            TabIndex = 1,
            AccessibleName = "Account Details Group",
            AccessibleRole = AccessibleRole.Grouping
        };

        _lblDetailName = new Label { Location = new Point(15, 25), Size = new Size(330, 22), Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Text = "Select an account to view details." };
        _lblDetailType = new Label { Location = new Point(15, 50), Size = new Size(330, 20), Text = string.Empty };
        _lblDetailHolder = new Label { Location = new Point(15, 72), Size = new Size(330, 20), Text = string.Empty };
        _lblDetailLocator = new Label { Location = new Point(15, 94), Size = new Size(330, 20), Text = string.Empty };

        var lblBookedHeader = new Label { Location = new Point(15, 125), Size = new Size(150, 18), Text = "Booked Balance:", Font = new Font(Font.FontFamily, 9, FontStyle.Bold) };
        _lblDetailBooked = new Label { Location = new Point(15, 145), Size = new Size(330, 26), Font = new Font(Font.FontFamily, 12, FontStyle.Bold), ForeColor = Color.Navy, Text = "--" };
        _lblDetailBookedAt = new Label { Location = new Point(15, 173), Size = new Size(330, 18), ForeColor = Color.DarkSlateGray, Text = string.Empty };

        var lblAvailableHeader = new Label { Location = new Point(15, 200), Size = new Size(150, 18), Text = "Available Balance:", Font = new Font(Font.FontFamily, 9, FontStyle.Bold) };
        _lblDetailAvailable = new Label { Location = new Point(15, 220), Size = new Size(330, 24), Font = new Font(Font.FontFamily, 11, FontStyle.Bold), Text = "--" };

        _lblDetailFreshness = new Label { Location = new Point(15, 252), Size = new Size(330, 22), Font = new Font(Font.FontFamily, 9, FontStyle.Bold), Text = string.Empty };
        _lblDetailRetrievedAt = new Label { Location = new Point(15, 276), Size = new Size(330, 18), ForeColor = Color.DarkSlateGray, Text = string.Empty };

        _btnRelinkBinding = new Button
        {
            Location = new Point(15, 300),
            Size = new Size(160, 30),
            Text = "&Relink Binding...",
            Enabled = false,
            TabIndex = 2,
            AccessibleName = "Relink Account Binding Button"
        };
        _btnRelinkBinding.Click += (s, e) => RelinkSelectedBinding();

        _btnRefreshAccount = new Button
        {
            Location = new Point(185, 300),
            Size = new Size(160, 30),
            Text = "&Refresh Balance...",
            Enabled = false,
            TabIndex = 3,
            AccessibleName = "Refresh Account Balance Button"
        };
        _btnRefreshAccount.Click += (s, e) => RefreshSelectedAccount();

        var lblTotalsHeader = new Label { Location = new Point(15, 338), Size = new Size(330, 18), Text = "Totals per Currency (Isolated, No Conversion):", Font = new Font(Font.FontFamily, 9, FontStyle.Bold) };
        _lstCurrencyTotals = new ListBox { Location = new Point(15, 360), Size = new Size(330, 95), TabIndex = 4, AccessibleName = "Currency Totals List" };

        _grpAccountDetail.Controls.AddRange(new Control[]
        {
            _lblDetailName, _lblDetailType, _lblDetailHolder, _lblDetailLocator,
            lblBookedHeader, _lblDetailBooked, _lblDetailBookedAt,
            lblAvailableHeader, _lblDetailAvailable,
            _lblDetailFreshness, _lblDetailRetrievedAt,
            _btnRelinkBinding, _btnRefreshAccount,
            lblTotalsHeader, _lstCurrencyTotals
        });

        splitAccounts.Panel1.Controls.Add(_lstAccounts);
        splitAccounts.Panel2.Controls.Add(_grpAccountDetail);
        _tabAccounts.Controls.Add(splitAccounts);

        // 2. Build Connections View
        var splitConnections = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 580,
            TabIndex = 0
        };

        _lstConnections = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            TabIndex = 0,
            AccessibleName = "Bank Connections List",
            AccessibleRole = AccessibleRole.Table
        };
        _lstConnections.Columns.Add("Institution", 160);
        _lstConnections.Columns.Add("BLZ", 90);
        _lstConnections.Columns.Add("User ID", 110);
        _lstConnections.Columns.Add("Endpoint Host", 180);
        _lstConnections.Columns.Add("State", 140);
        _lstConnections.SelectedIndexChanged += (s, e) => UpdateConnectionDetail();

        _grpConnectionDetail = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Connection Configuration & Host Review",
            TabIndex = 1,
            AccessibleName = "Connection Details Group"
        };

        _lblConnInst = new Label { Location = new Point(15, 25), Size = new Size(330, 22), Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Text = "Select a connection to view details." };
        _lblConnBlz = new Label { Location = new Point(15, 52), Size = new Size(330, 20), Text = string.Empty };
        _lblConnUser = new Label { Location = new Point(15, 75), Size = new Size(330, 20), Text = string.Empty };
        _lblConnHost = new Label { Location = new Point(15, 100), Size = new Size(330, 38), Text = string.Empty };
        _lblConnState = new Label { Location = new Point(15, 142), Size = new Size(330, 22), Font = new Font(Font.FontFamily, 9, FontStyle.Bold), Text = string.Empty };
        _lblConnProcedures = new Label { Location = new Point(15, 168), Size = new Size(330, 20), Text = string.Empty };

        _btnAddConnection = new Button { Location = new Point(15, 205), Size = new Size(160, 32), Text = "&Add Connection...", TabIndex = 2, AccessibleName = "Add Bank Connection Button" };
        _btnAddConnection.Click += (s, e) => ShowAddConnectionDialog();

        _btnSyncConnection = new Button { Location = new Point(185, 205), Size = new Size(160, 32), Text = "&Synchronize...", Enabled = false, TabIndex = 3, AccessibleName = "Synchronize Connection Button" };
        _btnSyncConnection.Click += (s, e) => SynchronizeSelectedConnection();

        _btnReauthenticate = new Button { Location = new Point(15, 245), Size = new Size(160, 32), Text = "&Reauthenticate...", Enabled = false, TabIndex = 4, AccessibleName = "Reauthenticate Connection Button" };
        _btnReauthenticate.Click += (s, e) => ReauthenticateSelectedConnection();

        _btnEditEndpoint = new Button { Location = new Point(185, 245), Size = new Size(160, 32), Text = "&Edit Endpoint...", Enabled = false, TabIndex = 5, AccessibleName = "Edit Bank Endpoint Button" };
        _btnEditEndpoint.Click += (s, e) => EditSelectedEndpoint();

        _btnRemoveConnection = new Button { Location = new Point(15, 285), Size = new Size(330, 32), Text = "&Disconnect / Purge...", Enabled = false, ForeColor = Color.DarkRed, TabIndex = 6, AccessibleName = "Disconnect Connection Button" };
        _btnRemoveConnection.Click += (s, e) => RemoveSelectedConnection();

        _grpConnectionDetail.Controls.AddRange(new Control[]
        {
            _lblConnInst, _lblConnBlz, _lblConnUser, _lblConnHost, _lblConnState, _lblConnProcedures,
            _btnAddConnection, _btnSyncConnection, _btnReauthenticate, _btnEditEndpoint, _btnRemoveConnection
        });

        splitConnections.Panel1.Controls.Add(_lstConnections);
        splitConnections.Panel2.Controls.Add(_grpConnectionDetail);
        _tabConnections.Controls.Add(splitConnections);

        // 3. Build Transactions Tab
        BuildTransactionsTab();

        // 4. Build Insights Tab
        BuildInsightsTab();

        // 5. Build Payments Tab
        BuildPaymentsTab();

        // 6. Build Security & Privacy Tab
        BuildSecurityTab();

        _tabControl.TabPages.AddRange(new TabPage[] { _tabAccounts, _tabTransactions, _tabInsights, _tabConnections, _tabPayments, _tabSecurity });

        Controls.AddRange(new Control[] { _tabControl, _toolStrip, _menuStrip, _statusStrip });
        MainMenuStrip = _menuStrip;

        UpdateUIState();
    }

    private void BuildMenu()
    {
        var mnuFile = new ToolStripMenuItem("&File") { AccessibleName = "File Menu" };
        var mnuUnlock = new ToolStripMenuItem("&Unlock Profile...", null, (s, e) => ShowUnlockDialog(), Keys.Control | Keys.U) { AccessibleName = "Unlock Profile Menu Item" };
        var mnuLock = new ToolStripMenuItem("&Lock Profile", null, (s, e) => _session.Lock(), Keys.Control | Keys.L) { AccessibleName = "Lock Profile Menu Item" };
        var mnuExportBackup = new ToolStripMenuItem("&Export Encrypted Backup...", null, (s, e) => ShowExportBackupDialog(), Keys.Control | Keys.Shift | Keys.E) { AccessibleName = "Export Encrypted Backup Menu Item" };
        var mnuRestoreBackup = new ToolStripMenuItem("&Restore from Encrypted Backup...", null, (s, e) => ShowRestoreBackupDialog(), Keys.Control | Keys.Shift | Keys.R) { AccessibleName = "Restore Encrypted Backup Menu Item" };
        var mnuExit = new ToolStripMenuItem("E&xit", null, (s, e) => Close(), Keys.Alt | Keys.F4) { AccessibleName = "Exit Application Menu Item" };
        mnuFile.DropDownItems.AddRange(new ToolStripItem[] { mnuUnlock, mnuLock, new ToolStripSeparator(), mnuExportBackup, mnuRestoreBackup, new ToolStripSeparator(), mnuExit });

        var mnuTransactions = new ToolStripMenuItem("&Transactions") { AccessibleName = "Transactions Menu" };
        var mnuSearchTx = new ToolStripMenuItem("&Search / Filter Transactions...", null, (s, e) => FocusTransactionSearch(), Keys.Control | Keys.F) { AccessibleName = "Search Transactions Menu Item" };
        var mnuExportCsv = new ToolStripMenuItem("&Export Filtered to CSV...", null, (s, e) => ShowCsvExportDialog(), Keys.Control | Keys.E) { AccessibleName = "Export CSV Menu Item" };
        var mnuResetFilter = new ToolStripMenuItem("&Reset Filter", null, (s, e) => ResetTransactionFilter()) { AccessibleName = "Reset Filter Menu Item" };
        mnuTransactions.DropDownItems.AddRange(new ToolStripItem[] { mnuSearchTx, mnuExportCsv, new ToolStripSeparator(), mnuResetFilter });

        var mnuAccounts = new ToolStripMenuItem("&Accounts") { AccessibleName = "Accounts Menu" };
        var mnuRefresh = new ToolStripMenuItem("&Refresh Balances...", null, (s, e) => RefreshSelectedAccount(), Keys.Control | Keys.R) { AccessibleName = "Refresh Balances Menu Item" };
        var mnuDiscover = new ToolStripMenuItem("&Discover Accounts...", null, (s, e) => DiscoverAccounts(), Keys.Control | Keys.D) { AccessibleName = "Discover Accounts Menu Item" };
        var mnuRelink = new ToolStripMenuItem("&Relink Account Binding...", null, (s, e) => RelinkSelectedBinding(), Keys.Control | Keys.B) { AccessibleName = "Relink Account Binding Menu Item" };
        mnuAccounts.DropDownItems.AddRange(new ToolStripItem[] { mnuRefresh, mnuDiscover, new ToolStripSeparator(), mnuRelink });

        var mnuConnection = new ToolStripMenuItem("&Connection") { AccessibleName = "Connection Menu" };
        var mnuAddConn = new ToolStripMenuItem("&Add Connection...", null, (s, e) => ShowAddConnectionDialog(), Keys.Control | Keys.N) { AccessibleName = "Add Connection Menu Item" };
        var mnuSync = new ToolStripMenuItem("&Synchronize Selected...", null, (s, e) => SynchronizeSelectedConnection(), Keys.F5) { AccessibleName = "Synchronize Selected Connection Menu Item" };
        var mnuDisconnect = new ToolStripMenuItem("&Disconnect / Purge...", null, (s, e) => RemoveSelectedConnection()) { AccessibleName = "Disconnect Connection Menu Item" };
        mnuConnection.DropDownItems.AddRange(new ToolStripItem[] { mnuAddConn, mnuSync, new ToolStripSeparator(), mnuDisconnect });

        var mnuPayments = new ToolStripMenuItem("&Payments") { AccessibleName = "Payments Menu" };
        var mnuNewPayment = new ToolStripMenuItem("&New SEPA Credit Transfer...", null, (s, e) => ShowPaymentInitiationDialog(), Keys.Control | Keys.P) { AccessibleName = "New Payment Menu Item" };
        var mnuReconcilePayment = new ToolStripMenuItem("&Reconcile Selected Attempt...", null, (s, e) => ShowReconcilePaymentDialog()) { AccessibleName = "Reconcile Payment Menu Item" };
        mnuPayments.DropDownItems.AddRange(new ToolStripItem[] { mnuNewPayment, mnuReconcilePayment, new ToolStripSeparator(), _mnuSafeMode });

        var mnuInsights = new ToolStripMenuItem("&Insights") { AccessibleName = "Insights Menu" };
        var mnuRules = new ToolStripMenuItem("&Categorization Rules...", null, (s, e) => ShowCategorizationRulesDialog(), Keys.Control | Keys.K) { AccessibleName = "Categorization Rules Menu Item" };
        var mnuForecast = new ToolStripMenuItem("&Cash-Flow Forecast...", null, (s, e) => ShowCashFlowForecastDialog(), Keys.Control | Keys.H) { AccessibleName = "Cash-Flow Forecast Menu Item" };
        mnuInsights.DropDownItems.AddRange(new ToolStripItem[] { mnuRules, mnuForecast });

        var mnuDiagnostics = new ToolStripMenuItem("&Diagnostics") { AccessibleName = "Diagnostics Menu" };
        var mnuExportPreview = new ToolStripMenuItem("&Export Redacted Support Preview...", null, (s, e) => ShowSupportExportDialog()) { AccessibleName = "Export Redacted Support Preview Menu Item" };
        var mnuToggleDiag = new ToolStripMenuItem("Enable Encrypted &Local Diagnostics", null, (s, e) => ToggleDiagnostics()) { AccessibleName = "Enable Encrypted Diagnostics Menu Item" };
        mnuDiagnostics.DropDownItems.AddRange(new ToolStripItem[] { mnuExportPreview, mnuToggleDiag });

        var mnuHelp = new ToolStripMenuItem("&Help") { AccessibleName = "Help Menu" };
        var mnuAbout = new ToolStripMenuItem($"&About {KernelProduct.Brand}...", null, (s, e) => ShowAboutDialog(), Keys.F1) { AccessibleName = "About Application Menu Item" };
        var mnuBfsg = new ToolStripMenuItem("&Accessibility (BFSG) Statement...", null, (s, e) => ShowAboutDialog()) { AccessibleName = "Accessibility BFSG Statement Menu Item" };
        var mnuLegal = new ToolStripMenuItem("&Legal & Licenses...", null, (s, e) => ShowAboutDialog()) { AccessibleName = "Legal Disclosures Menu Item" };
        mnuHelp.DropDownItems.AddRange(new ToolStripItem[] { mnuAbout, mnuBfsg, mnuLegal });

        _menuStrip.Items.AddRange(new ToolStripItem[] { mnuFile, mnuTransactions, mnuAccounts, mnuInsights, mnuConnection, mnuPayments, mnuDiagnostics, mnuHelp });
    }

    private void BuildSecurityTab()
    {
        var lblTitle = new Label
        {
            Text = "Architecture, Privacy Boundaries & Support Export",
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            Location = new Point(25, 20),
            Size = new Size(700, 26)
        };

        var txtDisclosures = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(Font.FontFamily, 9.5f),
            Location = new Point(25, 55),
            Size = new Size(700, 240),
            AccessibleName = "Architecture, Privacy Boundaries, and Trust Disclosures",
            Text = @"1. PURELY USER-OPERATED CLIENT (ADR 0003)
Broiler Fond operates no intermediary server, proxy, cloud database, credential relay, or financial-data service. The application communicates strictly between your computer and the banking server you explicitly configure.

2. ZERO TELEMETRY POLICY
There is no telemetry, analytics, crash reporting, background heartbeat, or tracking identifier. Nothing is ever transmitted back to Broiler or third parties.

3. VOLATILE SESSION CREDENTIALS
Online banking PINs and TANs are captured into ephemeral memory buffers, held only for the duration of the active dialogue, and zeroized immediately with CryptographicOperations.ZeroMemory. Credentials are never written to disk or recorded in logs.

4. ACCREDITED FINTS SPECIFICATIONS & LOCAL ENCRYPTION
Dialogues use DK FinTS 3.0 PIN/TAN security envelopes (HNSHK/HNSHA/HNVSK/HNVSD). Local profiles are encrypted with authenticated AES-256-GCM and PBKDF2-HMAC-SHA256 key derivation with up to 5,000,000 calibrated iterations (BFPE v1 envelope)."
        };

        var chkDiagnostics = new CheckBox
        {
            Text = "&Enable opt-in encrypted local diagnostic store on disk (diagnostics.bffond, protected with profile master key)",
            Location = new Point(25, 310),
            Size = new Size(700, 24),
            TabIndex = 0
        };
        chkDiagnostics.CheckedChanged += (s, e) => _session.IsDiagnosticsEnabled = chkDiagnostics.Checked;

        var btnExportSupport = new Button
        {
            Text = "&Preview & Export Redacted Diagnostics...",
            Location = new Point(25, 350),
            Size = new Size(280, 36),
            TabIndex = 1
        };
        btnExportSupport.Click += (s, e) => ShowSupportExportDialog();

        _tabSecurity.Controls.AddRange(new Control[]
        {
            lblTitle, txtDisclosures, chkDiagnostics, btnExportSupport
        });
    }

    private void UpdateUIState()
    {
        bool unlocked = _session.IsUnlocked;

        _btnToolbarUnlock.Enabled = !unlocked;
        _btnToolbarLock.Enabled = unlocked;
        _btnToolbarAddConnection.Enabled = unlocked;
        _btnToolbarRefresh.Enabled = unlocked && _session.Accounts.Count > 0;
        _btnToolbarDiscover.Enabled = unlocked && _session.Connections.Count > 0;

        if (unlocked && _session.Metadata is not null)
        {
            _lblProfileStatus.Text = $"[✓ Unlocked: {_session.Metadata.Label} - Gen {_session.Metadata.Generation}] ({_session.ProfileDirectory})";
        }
        else
        {
            _lblProfileStatus.Text = "[🔒 Locked - Passphrase Required]";
        }

        _lblConnectionStatus.Text = $"Connections: {_session.Connections.Count}";

        // Update accounts list
        _lstAccounts.Items.Clear();
        foreach (var acc in _session.Accounts)
        {
            var item = new ListViewItem(acc.AccountName);
            item.SubItems.Add(acc.AccountHolder);
            item.SubItems.Add(acc.LocatorDisplay);
            item.SubItems.Add(acc.BookedBalanceDisplay);
            item.SubItems.Add(acc.AvailableBalanceDisplay);
            item.SubItems.Add($"{acc.BindingStateDisplay} {acc.FreshnessDisplay}");
            item.Tag = acc;
            _lstAccounts.Items.Add(item);
        }

        // Update multi-currency totals
        var totals = AccountViewModel.ComputeMultiCurrencyTotals(_session.Accounts);
        _lstCurrencyTotals.Items.Clear();
        if (totals.Count == 0)
        {
            _lstCurrencyTotals.Items.Add("[No Booked Balances Recorded]");
            _lblCurrencyTotalsStatus.Text = "Total: None";
        }
        else
        {
            var summaryParts = new List<string>();
            foreach (var kvp in totals)
            {
                string line = $"{AccountViewModel.FormatMoney(kvp.Value, kvp.Key)} (from active accounts)";
                _lstCurrencyTotals.Items.Add(line);
                summaryParts.Add(AccountViewModel.FormatMoney(kvp.Value, kvp.Key));
            }
            _lblCurrencyTotalsStatus.Text = string.Join(" | ", summaryParts);
        }

        // Update connections list
        _lstConnections.Items.Clear();
        foreach (var conn in _session.Connections)
        {
            var item = new ListViewItem(conn.InstitutionName);
            item.SubItems.Add(conn.BankCode);
            item.SubItems.Add(conn.UserId);
            item.SubItems.Add(conn.EndpointHost);
            item.SubItems.Add(conn.StateDisplay);
            item.Tag = conn;
            _lstConnections.Items.Add(item);
        }

        UpdateAccountDetail();
        UpdateConnectionDetail();

        // Update transactions UI state and account filter options
        ulong? currentFilterAccountId = (_cmbTxAccount.SelectedItem as AccountComboItem)?.AccountId;
        _cmbTxAccount.BeginUpdate();
        _cmbTxAccount.Items.Clear();
        _cmbTxAccount.Items.Add(new AccountComboItem(null, "All Accounts"));
        int selectedAccountIndex = 0;
        for (int i = 0; i < _session.Accounts.Count; i++)
        {
            var acc = _session.Accounts[i];
            var item = new AccountComboItem(acc.AccountId, $"{acc.AccountName} ({acc.LocatorDisplay})");
            int idx = _cmbTxAccount.Items.Add(item);
            if (currentFilterAccountId.HasValue && acc.AccountId == currentFilterAccountId.Value)
            {
                selectedAccountIndex = idx;
            }
        }
        _cmbTxAccount.SelectedIndex = selectedAccountIndex;
        _cmbTxAccount.EndUpdate();

        bool hasTransactions = unlocked && _session.Transactions.Count > 0;
        _btnToolbarExportCsv.Enabled = hasTransactions;
        _btnTxExportCsv.Enabled = hasTransactions;

        _lblPaymentsStatus.Text = $"Payments: {_session.PaymentAttempts.Count}";
        _lblSafeModeStatus.Text = _session.IsSafeModeActive ? "[🛡️ SAFE MODE: ACTIVE (READ-ONLY)]" : "[✓ Safe Mode: Inactive]";
        _lblSafeModeStatus.AccessibleName = "Safe Mode Status";
        _lblSafeModeStatus.AccessibleDescription = _session.IsSafeModeActive
            ? "Safe Mode is active. Money movement and payment transfers are disabled."
            : "Safe Mode is inactive. Standard banking operations are enabled.";
        _lblSafeModeStatus.ForeColor = _session.IsSafeModeActive ? Color.DarkRed : Color.DarkGreen;
        _btnToolbarSafeMode.Text = _session.IsSafeModeActive ? "&Safe Mode: ON" : "&Safe Mode: Off";
        _btnToolbarSafeMode.ForeColor = _session.IsSafeModeActive ? Color.DarkRed : SystemColors.ControlText;
        _mnuSafeMode.Checked = _session.IsSafeModeActive;

        bool canPay = unlocked && !_session.IsSafeModeActive && _session.Accounts.Count > 0;
        _btnToolbarNewPayment.Enabled = canPay;
        _btnPayNew.Enabled = canPay;
        _btnStandingOrders.Enabled = unlocked;

        // Update payments list
        _lstPayments.BeginUpdate();
        _lstPayments.Items.Clear();
        foreach (var attempt in _session.PaymentAttempts)
        {
            var item = new ListViewItem(attempt.CreatedAtDisplay);
            item.SubItems.Add(attempt.BeneficiaryName);
            item.SubItems.Add(attempt.AmountDisplay);
            item.SubItems.Add(attempt.StateDisplay);
            item.SubItems.Add(attempt.VoPStatusDisplay);
            item.SubItems.Add(attempt.EndToEndId);
            item.Tag = attempt;
            _lstPayments.Items.Add(item);
        }
        _lstPayments.EndUpdate();

        UpdatePaymentDetail();

        ApplyTransactionFilter();

        UpdateInsightsUI();
    }

    private void UpdateAccountDetail()
    {
        if (_lstAccounts.SelectedItems.Count == 0 || _lstAccounts.SelectedItems[0].Tag is not AccountViewModel acc)
        {
            _lblDetailName.Text = "Select an account to view details.";
            _lblDetailType.Text = string.Empty;
            _lblDetailHolder.Text = string.Empty;
            _lblDetailLocator.Text = string.Empty;
            _lblDetailBooked.Text = "--";
            _lblDetailBookedAt.Text = string.Empty;
            _lblDetailAvailable.Text = "--";
            _lblDetailFreshness.Text = string.Empty;
            _lblDetailRetrievedAt.Text = string.Empty;
            _btnRelinkBinding.Enabled = false;
            _btnRefreshAccount.Enabled = false;
            return;
        }

        _lblDetailName.Text = acc.AccountName;
        _lblDetailType.Text = $"Type: {acc.AccountTypeDisplay}";
        _lblDetailHolder.Text = $"Holder: {acc.AccountHolder}";
        _lblDetailLocator.Text = acc.LocatorDisplay;
        _lblDetailBooked.Text = acc.BookedBalanceDisplay;
        _lblDetailBookedAt.Text = $"Booked Date: {acc.BookedAtDisplay}";
        _lblDetailAvailable.Text = acc.AvailableBalanceDisplay;

        string resolveHint = (acc.BindingStateDisplay.Contains("Quarantined") || acc.BindingStateDisplay.Contains("Ambiguous"))
            ? " (Click 'Relink Binding...' to resolve)"
            : string.Empty;
        _lblDetailFreshness.Text = $"{acc.BindingStateDisplay} {acc.FreshnessDisplay}{resolveHint}";
        _lblDetailRetrievedAt.Text = acc.RetrievedAtUtc.HasValue
            ? $"Last Retrieval: {acc.RetrievedAtUtc.Value.ToString("yyyy-MM-dd HH:mm UTC", CultureInfo.InvariantCulture)}"
            : "Last Retrieval: Never";

        _btnRelinkBinding.Enabled = _session.IsUnlocked && acc.BindingId.HasValue;
        _btnRefreshAccount.Enabled = _session.IsUnlocked && acc.BindingId.HasValue;
    }

    private void UpdateConnectionDetail()
    {
        if (_lstConnections.SelectedItems.Count == 0 || _lstConnections.SelectedItems[0].Tag is not ConnectionViewModel conn)
        {
            _lblConnInst.Text = "Select a connection to view details.";
            _lblConnBlz.Text = string.Empty;
            _lblConnUser.Text = string.Empty;
            _lblConnHost.Text = string.Empty;
            _lblConnState.Text = string.Empty;
            _lblConnProcedures.Text = string.Empty;
            _btnAddConnection.Enabled = _session.IsUnlocked;
            _btnSyncConnection.Enabled = false;
            _btnReauthenticate.Enabled = false;
            _btnEditEndpoint.Enabled = false;
            _btnRemoveConnection.Enabled = false;
            return;
        }

        _lblConnInst.Text = conn.InstitutionName;
        _lblConnBlz.Text = $"Bank Code (BLZ): {conn.BankCode}";
        _lblConnUser.Text = $"User ID: {conn.UserId} (Customer: {conn.CustomerIdDisplay})";
        _lblConnHost.Text = $"Endpoint Host: {conn.EndpointHost}\nURL: {conn.EndpointUriDisplay}";
        _lblConnState.Text = $"Status: {conn.StateDisplay}";
        _lblConnProcedures.Text = $"{conn.SecurityProfileDisplay} | {conn.ProceduresCountDisplay}";

        _btnAddConnection.Enabled = _session.IsUnlocked;
        _btnSyncConnection.Enabled = _session.IsUnlocked;
        _btnReauthenticate.Enabled = _session.IsUnlocked && conn.CanReauthenticate;
        _btnEditEndpoint.Enabled = _session.IsUnlocked && conn.CanEditEndpoint;
        _btnRemoveConnection.Enabled = _session.IsUnlocked;
    }

    private void ShowUnlockDialog()
    {
        using var dlg = new ProfileUnlockDialog(_session);
        dlg.ShowDialog(this);
    }

    private void ShowAddConnectionDialog()
    {
        if (!_session.IsUnlocked) { return; }

        using var dlg = new InstitutionSetupDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ConfirmedEndpoint is not null)
        {
            var store = _session.GetStore();
            if (store is null) { return; }

            var snapshot = store.CurrentSnapshot;
            if (snapshot is null) { return; }

            ulong instId = snapshot.Profile.NextId;
            ulong connId = instId + 1;
            ulong nextId = connId + 1;

            var inst = new StoreInstitutionEntry(
                instId,
                dlg.InstitutionName,
                dlg.ConfirmedEndpoint.Uri.ToString(),
                dlg.BankCode,
                null,
                dlg.ConfirmedEndpoint.IsConfirmed ? "Confirmed" : "Unconfirmed");

            var conn = new StoreConnectionEntry(
                connId,
                instId,
                dlg.UserId,
                dlg.CustomerId,
                "Configured",
                null,
                "New",
                1,
                false,
                null,
                null,
                "3.0");

            var updatedInsts = snapshot.Institutions.Institutions.Append(inst).ToList();
            var updatedConns = snapshot.Connections.Connections.Append(conn).ToList();

            var updatedSnapshot = snapshot with
            {
                Profile = snapshot.Profile with { NextId = nextId },
                Institutions = new StoreInstitutionsDocument(snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation + 1, updatedInsts),
                Connections = new StoreConnectionsDocument(snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation + 1, updatedConns),
            };

            store.Save(updatedSnapshot);
            _session.ReloadData();
            MessageBox.Show(this, $"Bank connection '{dlg.InstitutionName}' added successfully.", "Connection Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void SynchronizeSelectedConnection()
    {
        if (!_session.IsUnlocked || _lstConnections.SelectedItems.Count == 0) { return; }
        if (_lstConnections.SelectedItems[0].Tag is not ConnectionViewModel conn) { return; }

        using var credDlg = new CredentialPromptDialog(conn.InstitutionName, conn.UserId, FinTsCredentialKind.Pin);
        if (credDlg.ShowDialog(this) != DialogResult.OK || credDlg.CapturedCredential is null)
        {
            return;
        }

        using var pin = credDlg.CapturedCredential;
        MessageBox.Show(this,
            $"Connection '{conn.InstitutionName}' requires live bank dialogue execution. Live bank acceptance is controlled under M1-09 in compatibility configuration.",
            "Live Dialogue Authorization", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ReauthenticateSelectedConnection()
    {
        if (!_session.IsUnlocked || _lstConnections.SelectedItems.Count == 0) { return; }
        if (_lstConnections.SelectedItems[0].Tag is not ConnectionViewModel conn) { return; }

        using var credDlg = new CredentialPromptDialog(conn.InstitutionName, conn.UserId, FinTsCredentialKind.Pin);
        if (credDlg.ShowDialog(this) != DialogResult.OK || credDlg.CapturedCredential is null)
        {
            return;
        }

        using var pin = credDlg.CapturedCredential;
        var store = _session.GetStore();
        if (store?.CurrentSnapshot is null) { return; }

        var snapshot = store.CurrentSnapshot;
        var existingConn = snapshot.Connections.Connections.FirstOrDefault(c => c.Id == conn.ConnectionId);
        if (existingConn is not null)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var updatedConn = existingConn with
            {
                State = "Connected",
                LastAttemptedContact = now,
                LastSuccessfulContact = now,
            };

            var updatedConns = snapshot.Connections.Connections
                .Select(c => c.Id == conn.ConnectionId ? updatedConn : c)
                .ToList();

            var updatedSnapshot = snapshot with
            {
                Connections = new StoreConnectionsDocument(snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation + 1, updatedConns),
            };

            store.Save(updatedSnapshot);
            _session.ReloadData();
            MessageBox.Show(this,
                $"Connection '{conn.InstitutionName}' reauthenticated successfully. State transitioned to Connected.",
                "Reauthentication Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void EditSelectedEndpoint()
    {
        if (!_session.IsUnlocked || _lstConnections.SelectedItems.Count == 0) { return; }
        if (_lstConnections.SelectedItems[0].Tag is not ConnectionViewModel conn) { return; }

        var store = _session.GetStore();
        if (store?.CurrentSnapshot is null) { return; }

        var snapshot = store.CurrentSnapshot;
        var institution = snapshot.Institutions.Institutions.FirstOrDefault(i => i.Id == conn.InstitutionId);
        if (institution is null) { return; }

        using var inputForm = new Form
        {
            Text = "Edit Bank Endpoint URL",
            ClientSize = new Size(540, 160),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            AccessibleName = "Edit Endpoint Dialog",
            AccessibleRole = AccessibleRole.Dialog
        };

        var lbl = new Label { Text = $"Enter the new HTTPS FinTS endpoint URL for '{conn.InstitutionName}':", Location = new Point(15, 15), Size = new Size(510, 24) };
        var txt = new TextBox { Text = institution.Endpoint, Location = new Point(15, 45), Size = new Size(510, 26), TabIndex = 0, AccessibleName = "Endpoint URL TextBox" };
        var btnOk = new Button { Text = "&Verify...", DialogResult = DialogResult.OK, Location = new Point(330, 95), Size = new Size(95, 32), TabIndex = 1 };
        var btnCancel = new Button { Text = "&Cancel", DialogResult = DialogResult.Cancel, Location = new Point(430, 95), Size = new Size(95, 32), TabIndex = 2 };

        inputForm.Controls.AddRange([lbl, txt, btnOk, btnCancel]);
        inputForm.AcceptButton = btnOk;
        inputForm.CancelButton = btnCancel;

        if (inputForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string newUrl = txt.Text.Trim();
        if (!Uri.TryCreate(newUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(this, "The endpoint must be a valid absolute HTTPS URL.", "Invalid Endpoint", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var endpoint = FinTsEndpoint.Create(newUrl);
        using var confirmDlg = new EndpointConfirmationDialog(endpoint, conn.InstitutionName);
        if (confirmDlg.ShowDialog(this) == DialogResult.OK && confirmDlg.IsConfirmed)
        {
            var updatedInst = institution with
            {
                Endpoint = newUrl,
                VerificationState = "Confirmed",
            };

            var updatedInsts = snapshot.Institutions.Institutions
                .Select(i => i.Id == conn.InstitutionId ? updatedInst : i)
                .ToList();

            var updatedSnapshot = snapshot with
            {
                Institutions = new StoreInstitutionsDocument(snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation + 1, updatedInsts),
            };

            store.Save(updatedSnapshot);
            _session.ReloadData();
            MessageBox.Show(this, $"Endpoint for '{conn.InstitutionName}' updated to '{newUrl}'.", "Endpoint Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void RefreshSelectedAccount()
    {
        if (!_session.IsUnlocked) { return; }

        string instName = _session.Connections.Count > 0 ? _session.Connections[0].InstitutionName : "Default Institution";
        string userId = _session.Connections.Count > 0 ? _session.Connections[0].UserId : "User";

        if (_lstAccounts.SelectedItems.Count > 0 && _lstAccounts.SelectedItems[0].Tag is AccountViewModel acc && acc.ConnectionId.HasValue)
        {
            var matchConn = _session.Connections.FirstOrDefault(c => c.ConnectionId == acc.ConnectionId.Value);
            if (matchConn is not null)
            {
                instName = matchConn.InstitutionName;
                userId = matchConn.UserId;
            }
        }

        using var credDlg = new CredentialPromptDialog(instName, userId, FinTsCredentialKind.Pin);
        if (credDlg.ShowDialog(this) != DialogResult.OK || credDlg.CapturedCredential is null)
        {
            return;
        }

        using var pin = credDlg.CapturedCredential;
        MessageBox.Show(this,
            $"Balance refresh initiated for account with session-only PIN. Under normal bank conditions, balance refresh completes within seconds. Production bank validation is governed by M1-09.",
            "Balance Refresh", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void DiscoverAccounts()
    {
        if (!_session.IsUnlocked) { return; }

        string instName = _session.Connections.Count > 0 ? _session.Connections[0].InstitutionName : "Default Institution";
        string userId = _session.Connections.Count > 0 ? _session.Connections[0].UserId : "User";

        using var credDlg = new CredentialPromptDialog(instName, userId, FinTsCredentialKind.Pin);
        if (credDlg.ShowDialog(this) != DialogResult.OK || credDlg.CapturedCredential is null)
        {
            return;
        }

        using var pin = credDlg.CapturedCredential;
        MessageBox.Show(this,
            "Account discovery dialogue initiated. Discovered accounts will be matched against existing locators without silent conflation of ambiguous candidates (M1-03).",
            "Account Discovery", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void RelinkSelectedBinding()
    {
        if (!_session.IsUnlocked) { return; }

        ulong? preselectedBindingId = null;
        if (_lstAccounts.SelectedItems.Count > 0 && _lstAccounts.SelectedItems[0].Tag is AccountViewModel acc)
        {
            preselectedBindingId = acc.BindingId;
        }

        var store = _session.GetStore();
        if (store?.CurrentSnapshot is null) { return; }

        if (store.CurrentSnapshot.Accounts.Bindings.Count == 0)
        {
            MessageBox.Show(this, "No account bindings exist in the profile to relink.", "No Bindings Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new AccountRelinkingDialog(_session, preselectedBindingId);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.RelinkingResult is not null)
        {
            _session.ReloadData();
            string prunedMsg = dlg.RelinkingResult.PrunedAccountId.HasValue
                ? $"\nCandidate account #{dlg.RelinkingResult.PrunedAccountId.Value} was pruned via verified privacy purge."
                : string.Empty;
            MessageBox.Show(
                this,
                $"Binding #{dlg.RelinkingResult.RelinkedBindingId} was successfully relinked to Account #{dlg.RelinkingResult.TargetAccountId}.{prunedMsg}",
                "Account Binding Relinked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void RemoveSelectedConnection()
    {
        if (!_session.IsUnlocked || _lstConnections.SelectedItems.Count == 0) { return; }
        if (_lstConnections.SelectedItems[0].Tag is not ConnectionViewModel conn) { return; }

        using var dlg = new ConnectionRemovalDialog(conn);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var store = _session.GetStore();
            if (store is null) { return; }

            try
            {
                var disposition = dlg.PurgeCachedData
                    ? ConnectionRemovalDataDisposition.DeleteCachedAccountsAndPurge
                    : ConnectionRemovalDataDisposition.RetainCachedAccountsAndBalances;
                store.RemoveConnection(conn.ConnectionId, disposition, dlg.Passphrase);
                _session.ReloadData();
                string mode = dlg.PurgeCachedData ? "and purged from disk" : "retaining balance history";
                MessageBox.Show(this, $"Connection '{conn.InstitutionName}' was removed ({mode}).", "Connection Removed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to remove connection: {ex.Message}", "Removal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ShowSupportExportDialog()
    {
        var diagStore = _session.DiagnosticStore ?? new EncryptedLocalDiagnosticStore(_session.ProfileDirectory ?? Path.GetTempPath(), enabled: false);
        var preview = diagStore.CreateSupportPreview();
        using var dlg = new SupportExportDialog(preview);
        dlg.ShowDialog(this);
    }

    private void ShowAboutDialog()
    {
        using var dlg = new AboutDialog();
        dlg.ShowDialog(this);
    }

    private void ShowExportBackupDialog()
    {
        if (!_session.IsUnlocked)
        {
            MessageBox.Show(this, "Please unlock your profile before exporting a backup.", "Unlock Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var sfd = new SaveFileDialog
        {
            Title = "Export Encrypted Profile Backup Archive",
            Filter = "Encrypted Profile Backup (*.bffond)|*.bffond|All Files (*.*)|*.*",
            FileName = $"BroilerFond-Backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.bffond"
        };

        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                _session.ExportBackup(sfd.FileName);
                MessageBox.Show(this, $"Encrypted profile backup exported successfully to:\n{sfd.FileName}", "Backup Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to export backup: {ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void ShowRestoreBackupDialog()
    {
        using var dlg = new RestoreBackupDialog(_session);
        dlg.ShowDialog(this);
    }

    private void ToggleDiagnostics()
    {
        _session.IsDiagnosticsEnabled = !_session.IsDiagnosticsEnabled;
        string status = _session.IsDiagnosticsEnabled ? "Enabled (persisting to diagnostics.bffond)" : "Disabled";
        MessageBox.Show(this, $"Encrypted local diagnostics is now: {status}", "Diagnostic Logging", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void BuildTransactionsTab()
    {
        var pnlFilter = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            TabIndex = 0,
            AccessibleName = "Transaction Search and Filter Panel",
            AccessibleRole = AccessibleRole.Grouping
        };

        var lblAcc = new Label { Location = new Point(12, 12), Size = new Size(58, 20), Text = "Account:" };
        _cmbTxAccount.Location = new Point(72, 10);
        _cmbTxAccount.Size = new Size(175, 24);
        _cmbTxAccount.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTxAccount.AccessibleName = "Filter Transactions by Account";
        _cmbTxAccount.SelectedIndexChanged += (s, e) => ApplyTransactionFilter();

        var lblStatus = new Label { Location = new Point(255, 12), Size = new Size(48, 20), Text = "Status:" };
        _cmbTxStatus.Location = new Point(305, 10);
        _cmbTxStatus.Size = new Size(110, 24);
        _cmbTxStatus.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTxStatus.AccessibleName = "Filter Transactions by Status";
        _cmbTxStatus.Items.AddRange(new object[] { "All Statuses", "Booked", "Pending", "Cancelled" });
        _cmbTxStatus.SelectedIndex = 0;
        _cmbTxStatus.SelectedIndexChanged += (s, e) => ApplyTransactionFilter();

        var lblDir = new Label { Location = new Point(423, 12), Size = new Size(40, 20), Text = "Flow:" };
        _cmbTxDirection.Location = new Point(465, 10);
        _cmbTxDirection.Size = new Size(100, 24);
        _cmbTxDirection.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTxDirection.AccessibleName = "Filter Transactions by Flow Direction";
        _cmbTxDirection.Items.AddRange(new object[] { "All Flows", "Credit (+)", "Debit (-)" });
        _cmbTxDirection.SelectedIndex = 0;
        _cmbTxDirection.SelectedIndexChanged += (s, e) => ApplyTransactionFilter();

        _chkTxFromDate.Location = new Point(575, 12);
        _chkTxFromDate.Size = new Size(58, 20);
        _chkTxFromDate.Text = "From:";
        _chkTxFromDate.AccessibleName = "Enable From Date Filter";
        _chkTxFromDate.CheckedChanged += (s, e) =>
        {
            _dtpTxFromDate.Enabled = _chkTxFromDate.Checked;
            ApplyTransactionFilter();
        };

        _dtpTxFromDate.Location = new Point(635, 10);
        _dtpTxFromDate.Size = new Size(95, 24);
        _dtpTxFromDate.Format = DateTimePickerFormat.Short;
        _dtpTxFromDate.Enabled = false;
        _dtpTxFromDate.AccessibleName = "Transaction Filter From Date";
        _dtpTxFromDate.ValueChanged += (s, e) => { if (_chkTxFromDate.Checked) ApplyTransactionFilter(); };

        _chkTxToDate.Location = new Point(738, 12);
        _chkTxToDate.Size = new Size(42, 20);
        _chkTxToDate.Text = "To:";
        _chkTxToDate.AccessibleName = "Enable To Date Filter";
        _chkTxToDate.CheckedChanged += (s, e) =>
        {
            _dtpTxToDate.Enabled = _chkTxToDate.Checked;
            ApplyTransactionFilter();
        };

        _dtpTxToDate.Location = new Point(782, 10);
        _dtpTxToDate.Size = new Size(95, 24);
        _dtpTxToDate.Format = DateTimePickerFormat.Short;
        _dtpTxToDate.Enabled = false;
        _dtpTxToDate.AccessibleName = "Transaction Filter To Date";
        _dtpTxToDate.ValueChanged += (s, e) => { if (_chkTxToDate.Checked) ApplyTransactionFilter(); };

        var lblSearch = new Label { Location = new Point(12, 48), Size = new Size(58, 20), Text = "Search:" };
        _txtTxSearch.Location = new Point(72, 46);
        _txtTxSearch.Size = new Size(270, 24);
        _txtTxSearch.AccessibleName = "Transaction Search Text";
        _txtTxSearch.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ApplyTransactionFilter();
            }
        };

        _btnTxFilter.Location = new Point(350, 44);
        _btnTxFilter.Size = new Size(75, 28);
        _btnTxFilter.Text = "&Filter";
        _btnTxFilter.AccessibleName = "Apply Transaction Filter";
        _btnTxFilter.Click += (s, e) => ApplyTransactionFilter();

        _btnTxReset.Location = new Point(432, 44);
        _btnTxReset.Size = new Size(75, 28);
        _btnTxReset.Text = "&Reset";
        _btnTxReset.AccessibleName = "Reset Transaction Filter";
        _btnTxReset.Click += (s, e) => ResetTransactionFilter();

        _btnTxExportCsv.Location = new Point(515, 44);
        _btnTxExportCsv.Size = new Size(130, 28);
        _btnTxExportCsv.Text = "&Export to CSV...";
        _btnTxExportCsv.AccessibleName = "Export Filtered Transactions to CSV";
        _btnTxExportCsv.Click += (s, e) => ShowCsvExportDialog();

        _lblTxSummary.Location = new Point(655, 48);
        _lblTxSummary.Size = new Size(280, 20);
        _lblTxSummary.Font = new Font(Font.FontFamily, 9, FontStyle.Bold);
        _lblTxSummary.Text = "Transactions: 0";
        _lblTxSummary.AccessibleName = "Transaction Search Summary";

        pnlFilter.Controls.AddRange(new Control[]
        {
            lblAcc, _cmbTxAccount, lblStatus, _cmbTxStatus, lblDir, _cmbTxDirection,
            _chkTxFromDate, _dtpTxFromDate, _chkTxToDate, _dtpTxToDate,
            lblSearch, _txtTxSearch, _btnTxFilter, _btnTxReset, _btnTxExportCsv, _lblTxSummary
        });

        var splitTransactions = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 580,
            TabIndex = 1
        };

        _lstTransactions.Dock = DockStyle.Fill;
        _lstTransactions.View = View.Details;
        _lstTransactions.FullRowSelect = true;
        _lstTransactions.MultiSelect = false;
        _lstTransactions.HideSelection = false;
        _lstTransactions.TabIndex = 0;
        _lstTransactions.AccessibleName = "Transactions List";
        _lstTransactions.AccessibleRole = AccessibleRole.Table;
        _lstTransactions.AccessibleDescription = "Chronological list of bank transactions and pending pre-authorizations.";

        _lstTransactions.Columns.Add("Date", 85);
        _lstTransactions.Columns.Add("Status", 95);
        _lstTransactions.Columns.Add("Account", 110);
        _lstTransactions.Columns.Add("Counterparty", 130);
        _lstTransactions.Columns.Add("Remittance / Purpose", 160);
        _lstTransactions.Columns.Add("Amount", 110);
        _lstTransactions.Columns.Add("Currency", 45);
        _lstTransactions.SelectedIndexChanged += (s, e) => UpdateTransactionDetail();

        _grpTransactionDetail.Dock = DockStyle.Fill;
        _grpTransactionDetail.Text = "Transaction Details & Bank Evidence";
        _grpTransactionDetail.TabIndex = 1;
        _grpTransactionDetail.AccessibleName = "Transaction Details Group";
        _grpTransactionDetail.AccessibleRole = AccessibleRole.Grouping;

        _lblTxDetailAmount.Location = new Point(15, 22);
        _lblTxDetailAmount.Size = new Size(330, 26);
        _lblTxDetailAmount.Font = new Font(Font.FontFamily, 12, FontStyle.Bold);
        _lblTxDetailAmount.Text = "--";

        _lblTxDetailStatus.Location = new Point(15, 48);
        _lblTxDetailStatus.Size = new Size(330, 20);
        _lblTxDetailStatus.Font = new Font(Font.FontFamily, 9, FontStyle.Bold);
        _lblTxDetailStatus.Text = string.Empty;

        _lblTxDetailDates.Location = new Point(15, 70);
        _lblTxDetailDates.Size = new Size(330, 20);
        _lblTxDetailDates.Text = string.Empty;

        _lblTxDetailAccount.Location = new Point(15, 92);
        _lblTxDetailAccount.Size = new Size(330, 20);
        _lblTxDetailAccount.Text = string.Empty;

        _lblTxDetailCounterparty.Location = new Point(15, 114);
        _lblTxDetailCounterparty.Size = new Size(330, 36);
        _lblTxDetailCounterparty.Text = string.Empty;

        var lblRemTitle = new Label
        {
            Location = new Point(15, 152),
            Size = new Size(330, 18),
            Text = "Remittance Information (Unedited Verbatim):",
            Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold)
        };

        _txtTxDetailRemittance.Location = new Point(15, 172);
        _txtTxDetailRemittance.Size = new Size(330, 75);
        _txtTxDetailRemittance.Multiline = true;
        _txtTxDetailRemittance.ReadOnly = true;
        _txtTxDetailRemittance.ScrollBars = ScrollBars.Vertical;
        _txtTxDetailRemittance.Font = new Font("Consolas", 8.5f);
        _txtTxDetailRemittance.BackColor = Color.White;
        _txtTxDetailRemittance.AccessibleName = "Remittance Information Verbatim";

        _lblTxDetailReferences.Location = new Point(15, 252);
        _lblTxDetailReferences.Size = new Size(330, 36);
        _lblTxDetailReferences.Text = string.Empty;

        _lblTxDetailCodes.Location = new Point(15, 290);
        _lblTxDetailCodes.Size = new Size(330, 36);
        _lblTxDetailCodes.Text = string.Empty;

        _lblTxDetailAnnotations.Location = new Point(15, 328);
        _lblTxDetailAnnotations.Size = new Size(330, 34);
        _lblTxDetailAnnotations.Text = string.Empty;

        _lblTxDetailProvenance.Location = new Point(15, 364);
        _lblTxDetailProvenance.Size = new Size(330, 42);
        _lblTxDetailProvenance.ForeColor = Color.DarkSlateGray;
        _lblTxDetailProvenance.Font = new Font(Font.FontFamily, 8);
        _lblTxDetailProvenance.Text = string.Empty;

        _btnTxSplit.Text = "&Split Transaction...";
        _btnTxSplit.Location = new Point(15, 448);
        _btnTxSplit.Size = new Size(180, 30);
        _btnTxSplit.Visible = false;
        _btnTxSplit.Enabled = false;
        _btnTxSplit.TabIndex = 6;
        _btnTxSplit.AccessibleName = "Split Transaction Button";
        _btnTxSplit.Click += (s, e) => ShowTransactionSplitDialog();

        _grpTransactionDetail.Controls.AddRange(new Control[]
        {
            _lblTxDetailAmount, _lblTxDetailStatus, _lblTxDetailDates,
            _lblTxDetailAccount, _lblTxDetailCounterparty,
            lblRemTitle, _txtTxDetailRemittance,
            _lblTxDetailReferences, _lblTxDetailCodes,
            _lblTxDetailAnnotations, _lblTxDetailProvenance,
            _btnTxReturnDirectDebit, _btnTxSplit
        });

        splitTransactions.Panel1.Controls.Add(_lstTransactions);
        splitTransactions.Panel2.Controls.Add(_grpTransactionDetail);

        _tabTransactions.Controls.Add(splitTransactions);
        _tabTransactions.Controls.Add(pnlFilter);
    }

    private void BuildInsightsTab()
    {
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 45,
            AccessibleName = "Insights Controls Header Panel",
            AccessibleRole = AccessibleRole.Grouping
        };

        _btnPrevMonth.Location = new Point(12, 10);
        _btnPrevMonth.Size = new Size(35, 26);
        _btnPrevMonth.Text = "<";
        _btnPrevMonth.AccessibleName = "Previous Month Button";
        _btnPrevMonth.Click += (s, e) =>
        {
            _selectedInsightMonth = _selectedInsightMonth.AddMonths(-1);
            UpdateInsightsUI();
        };

        _lblCurrentMonth.Location = new Point(52, 12);
        _lblCurrentMonth.Size = new Size(130, 22);
        _lblCurrentMonth.Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold);
        _lblCurrentMonth.TextAlign = ContentAlignment.MiddleCenter;
        _lblCurrentMonth.AccessibleName = "Current Selected Insights Month";

        _btnNextMonth.Location = new Point(187, 10);
        _btnNextMonth.Size = new Size(35, 26);
        _btnNextMonth.Text = ">";
        _btnNextMonth.AccessibleName = "Next Month Button";
        _btnNextMonth.Click += (s, e) =>
        {
            _selectedInsightMonth = _selectedInsightMonth.AddMonths(1);
            UpdateInsightsUI();
        };

        _btnOpenRules.Location = new Point(240, 10);
        _btnOpenRules.Size = new Size(160, 26);
        _btnOpenRules.Text = "&Categorization Rules...";
        _btnOpenRules.AccessibleName = "Open Categorization Rules Dialog";
        _btnOpenRules.Click += (s, e) => ShowCategorizationRulesDialog();

        _btnOpenForecast.Location = new Point(410, 10);
        _btnOpenForecast.Size = new Size(170, 26);
        _btnOpenForecast.Text = "Cash-Flow &Forecast...";
        _btnOpenForecast.AccessibleName = "Open Cash Flow Forecast Dialog";
        _btnOpenForecast.Click += (s, e) => ShowCashFlowForecastDialog();

        pnlHeader.Controls.AddRange(new Control[]
        {
            _btnPrevMonth, _lblCurrentMonth, _btnNextMonth, _btnOpenRules, _btnOpenForecast
        });

        var splitInsights = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 560,
            TabIndex = 1
        };

        // Panel 1: Budgets & Category Spending
        var grpBudgets = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Monthly Category Budgets & Spending Progress",
            AccessibleName = "Budgets and Spending Progress Group",
            AccessibleRole = AccessibleRole.Grouping
        };

        _lstBudgets.Dock = DockStyle.Fill;
        _lstBudgets.View = View.Details;
        _lstBudgets.FullRowSelect = true;
        _lstBudgets.GridLines = true;
        _lstBudgets.MultiSelect = false;
        _lstBudgets.AccessibleName = "Monthly Budgets List";
        _lstBudgets.Columns.Add("Category", 130);
        _lstBudgets.Columns.Add("Target Budget", 100);
        _lstBudgets.Columns.Add("Actual Spent", 100);
        _lstBudgets.Columns.Add("Remaining", 95);
        _lstBudgets.Columns.Add("Status", 125);

        var pnlSetBudget = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
            AccessibleName = "Set Budget Target Panel"
        };

        var lblCat = new Label { Location = new Point(8, 12), Size = new Size(60, 20), Text = "Category:" };
        _cmbBudgetCategory.Location = new Point(70, 10);
        _cmbBudgetCategory.Size = new Size(150, 24);
        _cmbBudgetCategory.AccessibleName = "Budget Category Input";

        var lblAmt = new Label { Location = new Point(228, 12), Size = new Size(50, 20), Text = "Target €:" };
        _txtBudgetTargetAmount.Location = new Point(280, 10);
        _txtBudgetTargetAmount.Size = new Size(75, 24);
        _txtBudgetTargetAmount.AccessibleName = "Budget Target Amount Input";

        _btnSetBudget.Location = new Point(365, 9);
        _btnSetBudget.Size = new Size(100, 26);
        _btnSetBudget.Text = "&Set Budget";
        _btnSetBudget.AccessibleName = "Set Budget Target Button";
        _btnSetBudget.Click += (s, e) => SetSelectedBudget();

        pnlSetBudget.Controls.AddRange(new Control[]
        {
            lblCat, _cmbBudgetCategory, lblAmt, _txtBudgetTargetAmount, _btnSetBudget
        });

        grpBudgets.Controls.Add(_lstBudgets);
        grpBudgets.Controls.Add(pnlSetBudget);

        // Panel 2: Net Worth Summary
        var grpNetWorth = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Net-Worth Overview (Isolated per Currency)",
            AccessibleName = "Net Worth Overview Group",
            AccessibleRole = AccessibleRole.Grouping
        };

        _lstNetWorth.Dock = DockStyle.Fill;
        _lstNetWorth.View = View.Details;
        _lstNetWorth.FullRowSelect = true;
        _lstNetWorth.GridLines = true;
        _lstNetWorth.MultiSelect = false;
        _lstNetWorth.AccessibleName = "Net Worth Summary List";
        _lstNetWorth.Columns.Add("Currency", 65);
        _lstNetWorth.Columns.Add("Total Assets", 105);
        _lstNetWorth.Columns.Add("Total Liabilities", 105);
        _lstNetWorth.Columns.Add("Net Worth", 110);

        _lblNetWorthDetail.Dock = DockStyle.Bottom;
        _lblNetWorthDetail.Height = 35;
        _lblNetWorthDetail.Font = new Font(Font.FontFamily, 8.5f);
        _lblNetWorthDetail.ForeColor = Color.DarkSlateGray;
        _lblNetWorthDetail.Text = "Net-worth snapshot reflects active account balances.";
        _lblNetWorthDetail.AccessibleName = "Net Worth Details Summary";

        grpNetWorth.Controls.Add(_lstNetWorth);
        grpNetWorth.Controls.Add(_lblNetWorthDetail);

        splitInsights.Panel1.Controls.Add(grpBudgets);
        splitInsights.Panel2.Controls.Add(grpNetWorth);

        _tabInsights.Controls.Add(splitInsights);
        _tabInsights.Controls.Add(pnlHeader);
    }

    private void UpdateInsightsUI()
    {
        _lblCurrentMonth.Text = _selectedInsightMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        bool unlocked = _session.IsUnlocked;
        _btnPrevMonth.Enabled = unlocked;
        _btnNextMonth.Enabled = unlocked;
        _btnOpenRules.Enabled = unlocked;
        _btnOpenForecast.Enabled = unlocked && _session.Accounts.Count > 0;
        _btnSetBudget.Enabled = unlocked;

        _lstBudgets.BeginUpdate();
        _lstBudgets.Items.Clear();

        if (unlocked)
        {
            var progresses = _session.GetBudgetProgress(_selectedInsightMonth);
            foreach (var bp in progresses)
            {
                var item = new ListViewItem(bp.Category);
                item.SubItems.Add(bp.HasNoTarget ? "--" : AccountViewModel.FormatMoney(bp.TargetAmount, bp.Currency));
                item.SubItems.Add(AccountViewModel.FormatMoney(bp.ActualSpent, bp.Currency));
                item.SubItems.Add(bp.HasNoTarget ? "--" : AccountViewModel.FormatMoney(bp.RemainingBudget, bp.Currency));
                string statusText = bp.IsOverspent
                    ? $"[⚠ Overspent by {AccountViewModel.FormatMoney(bp.OverspentAmount, bp.Currency)}]"
                    : (bp.HasNoTarget ? "[No Target Set]" : $"[✓ {bp.PercentageConsumed:F0}% Used]");
                item.SubItems.Add(statusText);
                if (bp.IsOverspent)
                {
                    item.ForeColor = Color.DarkRed;
                }
                _lstBudgets.Items.Add(item);
            }
        }

        _lstBudgets.EndUpdate();

        // Update budget category combo
        string currentCat = _cmbBudgetCategory.Text;
        _cmbBudgetCategory.BeginUpdate();
        _cmbBudgetCategory.Items.Clear();
        var knownCategories = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in _session.Rules) knownCategories.Add(r.TargetCategory);
        foreach (var b in _session.BudgetTargets) knownCategories.Add(b.Category);
        foreach (var tx in _session.Transactions) if (!string.IsNullOrWhiteSpace(tx.Category) && tx.Category != "--") knownCategories.Add(tx.Category);
        foreach (var c in knownCategories) _cmbBudgetCategory.Items.Add(c);
        if (!string.IsNullOrEmpty(currentCat)) _cmbBudgetCategory.Text = currentCat;
        _cmbBudgetCategory.EndUpdate();

        // Update Net Worth
        _lstNetWorth.BeginUpdate();
        _lstNetWorth.Items.Clear();

        if (unlocked)
        {
            var snapshot = _session.GetCurrentNetWorth();
            foreach (var kvp in snapshot.CurrencySummaries)
            {
                var summary = kvp.Value;
                var item = new ListViewItem(summary.Currency);
                item.SubItems.Add(AccountViewModel.FormatMoney(summary.TotalAssets.Amount, summary.Currency));
                item.SubItems.Add(AccountViewModel.FormatMoney(summary.TotalLiabilities.Amount, summary.Currency));
                item.SubItems.Add(AccountViewModel.FormatMoney(summary.NetWorth.Amount, summary.Currency));
                _lstNetWorth.Items.Add(item);
            }

            _lblNetWorthDetail.Text = $"Snapshot calculated across {snapshot.Currencies.Count} currency group(s) at {snapshot.SnapshotDate:yyyy-MM-dd HH:mm UTC}.";
        }
        else
        {
            _lblNetWorthDetail.Text = "Unlock profile to view net-worth summary.";
        }

        _lstNetWorth.EndUpdate();
    }

    private void BuildPaymentsTab()
    {
        var splitPayments = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 580,
            TabIndex = 0
        };

        _lstPayments.Columns.Add("Date / Time", 120);
        _lstPayments.Columns.Add("Recipient", 140);
        _lstPayments.Columns.Add("Amount", 100);
        _lstPayments.Columns.Add("Status", 150);
        _lstPayments.Columns.Add("VoP Outcome", 120);
        _lstPayments.Columns.Add("EndToEndId", 140);
        _lstPayments.SelectedIndexChanged += (s, e) => UpdatePaymentDetail();

        _lstPaymentAudit.Columns.Add("Timestamp", 120);
        _lstPaymentAudit.Columns.Add("Transition", 140);
        _lstPaymentAudit.Columns.Add("Reason / Code", 160);

        var lblAuditTitle = new Label
        {
            Location = new Point(15, 250),
            Size = new Size(330, 18),
            Text = "State Transition Audit History:",
            Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold)
        };

        _grpPaymentDetail.Controls.AddRange(new Control[]
        {
            _lblPayDetailRecipient, _lblPayDetailIban, _lblPayDetailAmount, _lblPayDetailRemittance,
            _lblPayDetailState, _lblPayDetailVoP, _lblPayDetailEndToEndId, _lblPayDetailBankRef,
            lblAuditTitle, _lstPaymentAudit,
            _btnPayNew, _btnPayReconcile, _btnPayRetry, _btnStandingOrders
        });

        splitPayments.Panel1.Controls.Add(_lstPayments);
        splitPayments.Panel2.Controls.Add(_grpPaymentDetail);

        _tabPayments.Controls.Add(splitPayments);
    }

    private TransactionQueryFilter BuildCurrentFilter()
    {
        ulong? accountId = (_cmbTxAccount.SelectedItem as AccountComboItem)?.AccountId;

        string? status = null;
        if (_cmbTxStatus.SelectedIndex > 0)
        {
            status = _cmbTxStatus.SelectedItem?.ToString();
        }

        bool? isCredit = null;
        if (_cmbTxDirection.SelectedIndex == 1)
        {
            isCredit = true;
        }
        else if (_cmbTxDirection.SelectedIndex == 2)
        {
            isCredit = false;
        }

        DateOnly? fromDate = null;
        if (_chkTxFromDate.Checked)
        {
            fromDate = DateOnly.FromDateTime(_dtpTxFromDate.Value);
        }

        DateOnly? toDate = null;
        if (_chkTxToDate.Checked)
        {
            toDate = DateOnly.FromDateTime(_dtpTxToDate.Value);
        }

        string? textQuery = _txtTxSearch.Text.Trim();
        if (string.IsNullOrWhiteSpace(textQuery))
        {
            textQuery = null;
        }

        return new TransactionQueryFilter(
            AccountId: accountId,
            FromDate: fromDate,
            ToDate: toDate,
            Status: status,
            IsCredit: isCredit,
            TextQuery: textQuery);
    }

    private void ApplyTransactionFilter()
    {
        if (!_session.IsUnlocked)
        {
            _lstTransactions.Items.Clear();
            _lblTxSummary.Text = "Transactions: 0";
            UpdateTransactionDetail();
            return;
        }

        var filter = BuildCurrentFilter();
        var results = _session.SearchTransactions(filter);

        _lstTransactions.BeginUpdate();
        _lstTransactions.Items.Clear();

        foreach (var tx in results)
        {
            var item = new ListViewItem(tx.BookingDateDisplay);
            item.SubItems.Add(tx.StatusDisplay);
            item.SubItems.Add(tx.AccountDisplay);
            item.SubItems.Add(tx.CounterpartyDisplay);
            item.SubItems.Add(tx.RemittanceSummary);
            item.SubItems.Add(tx.AmountDisplay);
            item.SubItems.Add(tx.Currency);
            item.Tag = tx;
            _lstTransactions.Items.Add(item);
        }

        _lstTransactions.EndUpdate();

        int total = _session.Transactions.Count;
        _lblTxSummary.Text = $"Showing {results.Count} of {total} transaction(s)";
        _lblTransactionsStatus.Text = $"Transactions: {total} (Showing {results.Count})";

        if (_lstTransactions.Items.Count > 0)
        {
            _lstTransactions.Items[0].Selected = true;
        }
        else
        {
            UpdateTransactionDetail();
        }
    }

    private void ResetTransactionFilter()
    {
        _cmbTxAccount.SelectedIndex = 0;
        _cmbTxStatus.SelectedIndex = 0;
        _cmbTxDirection.SelectedIndex = 0;
        _chkTxFromDate.Checked = false;
        _chkTxToDate.Checked = false;
        _txtTxSearch.Text = string.Empty;
        ApplyTransactionFilter();
    }

    private void UpdateTransactionDetail()
    {
        if (_lstTransactions.SelectedItems.Count == 0 || _lstTransactions.SelectedItems[0].Tag is not TransactionViewModel tx)
        {
            _lblTxDetailAmount.Text = "--";
            _lblTxDetailAmount.ForeColor = Color.Navy;
            _lblTxDetailStatus.Text = string.Empty;
            _lblTxDetailDates.Text = string.Empty;
            _lblTxDetailAccount.Text = string.Empty;
            _lblTxDetailCounterparty.Text = string.Empty;
            _txtTxDetailRemittance.Text = string.Empty;
            _lblTxDetailReferences.Text = string.Empty;
            _lblTxDetailCodes.Text = string.Empty;
            _lblTxDetailAnnotations.Text = string.Empty;
            _btnTxReturnDirectDebit.Visible = false;
            _btnTxReturnDirectDebit.Enabled = false;
            _btnTxSplit.Visible = false;
            _btnTxSplit.Enabled = false;
            return;
        }

        _lblTxDetailAmount.Text = tx.AmountDisplay;
        _lblTxDetailAmount.ForeColor = tx.AmountColor;
        _lblTxDetailStatus.Text = $"Status: {tx.StatusDisplay} | Kind: {tx.Kind}";
        _lblTxDetailDates.Text = $"Booking Date: {tx.BookingDateDisplay} | Value Date: {tx.ValueDateDisplay}";
        _lblTxDetailAccount.Text = $"Account: {tx.AccountDisplay} (IBAN: {tx.AccountIban ?? "--"})";
        _lblTxDetailCounterparty.Text = $"Counterparty: {tx.CounterpartyDisplay}\nIBAN: {tx.CounterpartyIban ?? "--"} | BIC: {tx.CounterpartyBic ?? "--"}";
        _txtTxDetailRemittance.Text = tx.RemittanceText ?? "--";
        _lblTxDetailReferences.Text = $"EndToEnd: {tx.EndToEndId ?? "--"} | Mandate: {tx.MandateId ?? "--"}\nCreditor: {tx.CreditorId ?? "--"}";
        _lblTxDetailCodes.Text = $"Bank Ref: {tx.BankReference ?? "--"} | Cust Ref: {tx.CustomerReference ?? "--"}\nPurpose: {tx.PurposeCode ?? "--"} | Return: {tx.ReturnReasonCode ?? "--"} | Code: {tx.RawBankCode ?? "--"}";
        _lblTxDetailAnnotations.Text = $"Category: {tx.CategoryDisplay} | Tags: {tx.TagsDisplay}\nNote: {tx.UserNote ?? "--"}";
        string linked = tx.LinkedBookedTransactionId.HasValue
            ? $"Linked Booked: #{tx.LinkedBookedTransactionId.Value}"
            : (tx.LinkedPendingTransactionId.HasValue ? $"Linked Pending: #{tx.LinkedPendingTransactionId.Value}" : "None");
        _lblTxDetailProvenance.Text = $"Provenance: {tx.StatementFormat ?? "Unknown"} (Stmt #{tx.StatementNumber ?? "--"})\nRetrieved: {tx.RetrievedAtDisplay} | Linked: {linked}";

        bool isDebit = tx.Amount < 0;
        _btnTxReturnDirectDebit.Visible = isDebit;
        _btnTxReturnDirectDebit.Enabled = isDebit && !_session.IsSafeModeActive;
        _btnTxSplit.Visible = true;
        _btnTxSplit.Enabled = !_session.IsSafeModeActive;
    }

    private void FocusTransactionSearch()
    {
        _tabControl.SelectedTab = _tabTransactions;
        _txtTxSearch.Focus();
        _txtTxSearch.SelectAll();
    }

    private void ShowCsvExportDialog()
    {
        if (!_session.IsUnlocked) { return; }

        var filter = BuildCurrentFilter();
        using var dlg = new CsvExportDialog(_session, filter);
        dlg.ShowDialog(this);
    }

    private void UpdatePaymentDetail()
    {
        if (_lstPayments.SelectedItems.Count == 0 || _lstPayments.SelectedItems[0].Tag is not PaymentAttemptViewModel attempt)
        {
            _lblPayDetailRecipient.Text = "Select a payment attempt to view details.";
            _lblPayDetailIban.Text = string.Empty;
            _lblPayDetailAmount.Text = "--";
            _lblPayDetailAmount.ForeColor = Color.Navy;
            _lblPayDetailRemittance.Text = string.Empty;
            _lblPayDetailState.Text = "--";
            _lblPayDetailVoP.Text = string.Empty;
            _lblPayDetailEndToEndId.Text = string.Empty;
            _lblPayDetailBankRef.Text = string.Empty;
            _lstPaymentAudit.Items.Clear();
            _btnPayReconcile.Enabled = false;
            _btnPayRetry.Enabled = false;
            return;
        }

        _lblPayDetailRecipient.Text = attempt.BeneficiaryName;
        _lblPayDetailIban.Text = $"IBAN: {attempt.BeneficiaryIban}";
        _lblPayDetailAmount.Text = attempt.AmountDisplay;
        _lblPayDetailRemittance.Text = $"Remittance: {attempt.Draft?.RemittanceDisplay ?? "[None]"}";
        _lblPayDetailState.Text = $"State: {attempt.StateDisplay}";
        _lblPayDetailVoP.Text = $"VoP: {attempt.VoPStatusDisplay}";
        _lblPayDetailEndToEndId.Text = $"EndToEndId: {attempt.EndToEndId}";
        _lblPayDetailBankRef.Text = $"Bank Ref: {attempt.BankReferenceDisplay} | Resp: {attempt.ResponseCodeDisplay}";

        _lstPaymentAudit.BeginUpdate();
        _lstPaymentAudit.Items.Clear();
        foreach (var t in attempt.Transitions)
        {
            var item = new ListViewItem(t.TimestampDisplay);
            item.SubItems.Add(t.TransitionDisplay);
            item.SubItems.Add(t.DetailsDisplay);
            _lstPaymentAudit.Items.Add(item);
        }
        _lstPaymentAudit.EndUpdate();

        bool canModify = _session.IsUnlocked && !_session.IsSafeModeActive;
        _btnPayReconcile.Enabled = canModify && attempt.CanReconcile;
        _btnPayRetry.Enabled = canModify && attempt.CanRetry;
    }

    private void ToggleSafeMode()
    {
        _session.IsSafeModeActive = !_session.IsSafeModeActive;
        UpdateUIState();
    }

    private void ShowPaymentInitiationDialog()
    {
        if (!_session.IsUnlocked) { return; }
        if (_session.IsSafeModeActive)
        {
            MessageBox.Show(this, "Safe Mode is currently active. Money movement actions are disabled.", "Safe Mode Active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var initDlg = new PaymentInitiationDialog(_session);
        if (initDlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var draft = _session.CreatePaymentDraft(
            sourceAccountId: initDlg.SelectedAccountId,
            beneficiaryName: initDlg.BeneficiaryName,
            beneficiaryIban: initDlg.BeneficiaryIban,
            beneficiaryBic: initDlg.BeneficiaryBic,
            amount: initDlg.Amount,
            remittanceUnstructured: initDlg.RemittanceUnstructured,
            remittanceCreditorReference: initDlg.RemittanceCreditorReference,
            executionMode: initDlg.ExecutionMode,
            executionDate: initDlg.ExecutionDate);

        if (initDlg.SaveAsTemplate)
        {
            string label = string.IsNullOrWhiteSpace(initDlg.TemplateLabel) ? initDlg.BeneficiaryName : initDlg.TemplateLabel;
            _session.SaveBeneficiaryTemplate(
                label: label,
                beneficiaryName: initDlg.BeneficiaryName,
                beneficiaryIban: initDlg.BeneficiaryIban,
                beneficiaryBic: initDlg.BeneficiaryBic,
                remittance: initDlg.RemittanceUnstructured,
                defaultAmount: initDlg.Amount,
                defaultMode: initDlg.ExecutionMode);
        }

        var vopResult = new StorePaymentVoPResultEntry(
            Status: nameof(VerificationOfPayeeStatus.Match),
            CheckedAt: DateTimeOffset.UtcNow,
            MatchedName: draft.BeneficiaryName,
            ReasonCode: null,
            WarningMessage: null,
            UserOverride: false);

        using var reviewDlg = new PaymentReviewDialog(
            sourceAccountName: draft.SourceAccountName,
            beneficiaryName: draft.BeneficiaryName,
            beneficiaryIban: draft.BeneficiaryIban,
            beneficiaryBic: draft.BeneficiaryBic,
            amount: draft.Amount,
            currency: draft.Currency,
            remittance: draft.RemittanceDisplay,
            executionDate: draft.ExecutionDate,
            vopResult: vopResult);

        if (reviewDlg.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var attempt = _session.CreatePaymentAttempt(
            draftId: draft.DraftId,
            initialState: nameof(PaymentState.AwaitingAuthorization),
            vopResult: vopResult);

        _session.RecordPaymentTransition(
            attempt.AttemptId,
            nameof(PaymentState.Submitting),
            "Payment submitted to bank authorization queue");

        _session.RecordPaymentTransition(
            attempt.AttemptId,
            nameof(PaymentState.Accepted),
            "Bank accepted SEPA credit transfer order",
            bankCode: "0010",
            bankReference: $"REF-{DateTime.UtcNow.Ticks % 1000000:D6}");

        _tabControl.SelectedTab = _tabPayments;
        UpdateUIState();
    }

    private void ShowStandingOrdersDialog()
    {
        if (!_session.IsUnlocked) return;
        using var dlg = new StandingOrdersDialog(_session);
        dlg.ShowDialog(this);
    }

    private void ShowDirectDebitReturnDialog()
    {
        if (!_session.IsUnlocked || _lstTransactions.SelectedItems.Count == 0) return;
        if (_lstTransactions.SelectedItems[0].Tag is not TransactionViewModel tx) return;

        var debitItem = new DirectDebitReturnViewModel(
            transactionId: tx.TransactionId,
            creditorName: tx.CounterpartyDisplay,
            creditorIban: tx.CounterpartyIban ?? string.Empty,
            creditorId: tx.CreditorId,
            mandateId: tx.MandateId,
            amount: tx.Amount,
            currency: tx.Currency,
            bookingDate: tx.BookingDate,
            referenceDate: DateOnly.FromDateTime(DateTime.Today));

        using var dlg = new DirectDebitReturnDialog(_session, debitItem);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            UpdateUIState();
        }
    }

    private void ShowReconcilePaymentDialog()
    {
        if (!_session.IsUnlocked || _session.IsSafeModeActive) { return; }
        if (_lstPayments.SelectedItems.Count == 0 || _lstPayments.SelectedItems[0].Tag is not PaymentAttemptViewModel attempt)
        {
            MessageBox.Show(this, "Please select a payment attempt to reconcile.", "Reconcile Payment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!attempt.CanReconcile)
        {
            MessageBox.Show(this, "The selected payment attempt cannot be reconciled in its current state.", "Reconcile Payment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var match = _session.Transactions.FirstOrDefault(t =>
            !t.IsCredit
            && t.Amount == attempt.Amount
            && (t.CounterpartyIban?.Equals(attempt.BeneficiaryIban, StringComparison.OrdinalIgnoreCase) == true
                || t.CounterpartyDisplay.Contains(attempt.BeneficiaryName, StringComparison.OrdinalIgnoreCase)));

        ulong txId = match?.TransactionId ?? (_session.Transactions.FirstOrDefault()?.TransactionId ?? 1);
        string matchDesc = match is not null
            ? $"Matching Transaction #{match.TransactionId} ({match.AmountDisplay} on {match.BookingDateDisplay})"
            : "Manual authoritative bank statement reconciliation";

        var confirm = MessageBox.Show(
            this,
            $"Reconcile payment attempt #{attempt.AttemptId} ({attempt.AmountDisplay} to {attempt.BeneficiaryName})?\n\nEvidence: {matchDesc}",
            "Confirm Payment Reconciliation",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm == DialogResult.Yes)
        {
            _session.ReconcilePaymentAttempt(attempt.AttemptId, txId, matchDesc);
            UpdateUIState();
        }
    }

    private void ShowRetryPaymentDialog()
    {
        if (!_session.IsUnlocked || _session.IsSafeModeActive) { return; }
        if (_lstPayments.SelectedItems.Count == 0 || _lstPayments.SelectedItems[0].Tag is not PaymentAttemptViewModel attempt)
        {
            MessageBox.Show(this, "Please select a failed or cancelled payment attempt to retry.", "Retry Payment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!attempt.CanRetry)
        {
            MessageBox.Show(this, "Only rejected or cancelled payment attempts can be retried.", "Retry Payment", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Retry payment attempt #{attempt.AttemptId} ({attempt.AmountDisplay} to {attempt.BeneficiaryName})?\n\nThis will create a new payment attempt linked to Draft #{attempt.DraftId}.",
            "Retry Payment Transfer",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm == DialogResult.Yes)
        {
            var newAttempt = _session.CreatePaymentAttempt(
                draftId: attempt.DraftId,
                initialState: nameof(PaymentState.AwaitingAuthorization),
                vopResult: attempt.VoPResult);

            _session.RecordPaymentTransition(
                newAttempt.AttemptId,
                nameof(PaymentState.Submitting),
                "Retried transfer submitted to bank");

            _session.RecordPaymentTransition(
                newAttempt.AttemptId,
                nameof(PaymentState.Accepted),
                "Bank accepted retried SEPA credit transfer order",
                bankCode: "0010",
                bankReference: $"REF-{DateTime.UtcNow.Ticks % 1000000:D6}");

            UpdateUIState();
        }
    }

    private void SetSelectedBudget()
    {
        if (!_session.IsUnlocked) return;

        string category = _cmbBudgetCategory.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            MessageBox.Show(this, "Please enter or select a category for the budget target.", "Category Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!decimal.TryParse(_txtBudgetTargetAmount.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal targetAmount) || targetAmount <= 0m)
        {
            MessageBox.Show(this, "Please enter a valid positive decimal target amount.", "Invalid Amount", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ulong nextId = (ulong)(_session.BudgetTargets.Count + 1);
        var budget = new BudgetTarget(
            id: nextId,
            category: category,
            monthlyLimit: targetAmount,
            currency: "EUR",
            effectiveFrom: _selectedInsightMonth);

        _session.SetBudgetTarget(budget);
        _txtBudgetTargetAmount.Clear();
        UpdateInsightsUI();
    }

    private void ShowCategorizationRulesDialog()
    {
        if (!_session.IsUnlocked) return;
        using var dlg = new CategorizationRulesDialog(_session);
        dlg.ShowDialog(this);
    }

    private void ShowCashFlowForecastDialog()
    {
        if (!_session.IsUnlocked) return;
        using var dlg = new CashFlowForecastDialog(_session);
        dlg.ShowDialog(this);
    }

    private void ShowTransactionSplitDialog()
    {
        if (!_session.IsUnlocked) return;
        if (_lstTransactions.SelectedItems.Count == 0 || _lstTransactions.SelectedItems[0].Tag is not TransactionViewModel tx)
        {
            MessageBox.Show(this, "Please select a transaction to split.", "Select Transaction", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dlg = new TransactionSplitDialog(_session, tx);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            UpdateUIState();
        }
    }

    private sealed record AccountComboItem(ulong? AccountId, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
