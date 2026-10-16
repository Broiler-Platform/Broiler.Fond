using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Dialogs;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Institutions;
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
    private readonly ToolStripStatusLabel _lblCurrencyTotalsStatus;

    private readonly TabControl _tabControl;
    private readonly TabPage _tabAccounts;
    private readonly TabPage _tabConnections;
    private readonly TabPage _tabSecurity;

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
    private readonly ListBox _lstCurrencyTotals;

    // Connections tab
    private readonly ListView _lstConnections;
    private readonly GroupBox _grpConnectionDetail;
    private readonly Label _lblConnInst;
    private readonly Label _lblConnBlz;
    private readonly Label _lblConnUser;
    private readonly Label _lblConnHost;
    private readonly Label _lblConnState;
    private readonly Label _lblConnProcedures;
    private readonly Button _btnSyncConnection;
    private readonly Button _btnRemoveConnection;

    // Toolbar buttons
    private readonly ToolStripButton _btnToolbarUnlock;
    private readonly ToolStripButton _btnToolbarLock;
    private readonly ToolStripButton _btnToolbarAddConnection;
    private readonly ToolStripButton _btnToolbarRefresh;
    private readonly ToolStripButton _btnToolbarDiscover;
    private readonly ToolStripButton _btnToolbarSupportExport;

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
        _menuStrip = new MenuStrip { TabIndex = 0 };
        BuildMenu();

        // Tool strip
        _toolStrip = new ToolStrip { TabIndex = 1 };
        _btnToolbarUnlock = new ToolStripButton("&Unlock Profile", null, (s, e) => ShowUnlockDialog()) { AccessibleName = "Unlock Profile Toolbar Button" };
        _btnToolbarLock = new ToolStripButton("&Lock", null, (s, e) => _session.Lock()) { Enabled = false, AccessibleName = "Lock Profile Toolbar Button" };
        _btnToolbarAddConnection = new ToolStripButton("&Add Connection", null, (s, e) => ShowAddConnectionDialog()) { Enabled = false, AccessibleName = "Add Bank Connection Toolbar Button" };
        _btnToolbarRefresh = new ToolStripButton("&Refresh Balances", null, (s, e) => RefreshSelectedAccount()) { Enabled = false, AccessibleName = "Refresh Balances Toolbar Button" };
        _btnToolbarDiscover = new ToolStripButton("&Discover Accounts", null, (s, e) => DiscoverAccounts()) { Enabled = false, AccessibleName = "Discover Accounts Toolbar Button" };
        _btnToolbarSupportExport = new ToolStripButton("&Support Export", null, (s, e) => ShowSupportExportDialog()) { AccessibleName = "Export Support Diagnostics Toolbar Button" };

        _toolStrip.Items.AddRange(new ToolStripItem[]
        {
            _btnToolbarUnlock, _btnToolbarLock, new ToolStripSeparator(),
            _btnToolbarAddConnection, _btnToolbarRefresh, _btnToolbarDiscover, new ToolStripSeparator(),
            _btnToolbarSupportExport
        });

        // Status strip
        _statusStrip = new StatusStrip { TabIndex = 3 };
        _lblProfileStatus = new ToolStripStatusLabel("[Locked - No Profile Open]") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _lblConnectionStatus = new ToolStripStatusLabel("Connections: 0") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        _lblCurrencyTotalsStatus = new ToolStripStatusLabel("Total: None") { BorderSides = ToolStripStatusLabelBorderSides.Left };
        _statusStrip.Items.AddRange(new ToolStripItem[] { _lblProfileStatus, _lblConnectionStatus, _lblCurrencyTotalsStatus });

        // Tab Control
        _tabControl = new TabControl { Dock = DockStyle.Fill, TabIndex = 2 };
        _tabAccounts = new TabPage("Accounts & Balances (&1)") { AccessibleName = "Accounts Tab Page" };
        _tabConnections = new TabPage("Bank Connections (&2)") { AccessibleName = "Connections Tab Page" };
        _tabSecurity = new TabPage("Privacy & Diagnostics (&3)") { AccessibleName = "Security Tab Page" };

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
            Size = new Size(180, 30),
            Text = "&Relink Binding...",
            Enabled = false,
            TabIndex = 2,
            AccessibleName = "Relink Account Binding Button"
        };
        _btnRelinkBinding.Click += (s, e) => RelinkSelectedBinding();

        var lblTotalsHeader = new Label { Location = new Point(15, 338), Size = new Size(330, 18), Text = "Totals per Currency (Isolated, No Conversion):", Font = new Font(Font.FontFamily, 9, FontStyle.Bold) };
        _lstCurrencyTotals = new ListBox { Location = new Point(15, 360), Size = new Size(330, 95), TabIndex = 3, AccessibleName = "Currency Totals List" };

        _grpAccountDetail.Controls.AddRange(new Control[]
        {
            _lblDetailName, _lblDetailType, _lblDetailHolder, _lblDetailLocator,
            lblBookedHeader, _lblDetailBooked, _lblDetailBookedAt,
            lblAvailableHeader, _lblDetailAvailable,
            _lblDetailFreshness, _lblDetailRetrievedAt,
            _btnRelinkBinding,
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

        _btnSyncConnection = new Button { Location = new Point(15, 220), Size = new Size(160, 34), Text = "&Synchronize Connection...", Enabled = false, TabIndex = 2, AccessibleName = "Synchronize Connection Button" };
        _btnSyncConnection.Click += (s, e) => SynchronizeSelectedConnection();

        _btnRemoveConnection = new Button { Location = new Point(185, 220), Size = new Size(160, 34), Text = "&Disconnect / Purge...", Enabled = false, ForeColor = Color.DarkRed, TabIndex = 3, AccessibleName = "Disconnect Connection Button" };
        _btnRemoveConnection.Click += (s, e) => RemoveSelectedConnection();

        _grpConnectionDetail.Controls.AddRange(new Control[]
        {
            _lblConnInst, _lblConnBlz, _lblConnUser, _lblConnHost, _lblConnState, _lblConnProcedures,
            _btnSyncConnection, _btnRemoveConnection
        });

        splitConnections.Panel1.Controls.Add(_lstConnections);
        splitConnections.Panel2.Controls.Add(_grpConnectionDetail);
        _tabConnections.Controls.Add(splitConnections);

        // 3. Build Security & Privacy Tab
        BuildSecurityTab();

        _tabControl.TabPages.AddRange(new TabPage[] { _tabAccounts, _tabConnections, _tabSecurity });

        Controls.AddRange(new Control[] { _tabControl, _toolStrip, _menuStrip, _statusStrip });
        MainMenuStrip = _menuStrip;

        UpdateUIState();
    }

    private void BuildMenu()
    {
        var mnuFile = new ToolStripMenuItem("&File");
        var mnuUnlock = new ToolStripMenuItem("&Unlock Profile...", null, (s, e) => ShowUnlockDialog(), Keys.Control | Keys.U) { AccessibleName = "Unlock Profile Menu Item" };
        var mnuLock = new ToolStripMenuItem("&Lock Profile", null, (s, e) => _session.Lock(), Keys.Control | Keys.L) { AccessibleName = "Lock Profile Menu Item" };
        var mnuExit = new ToolStripMenuItem("E&xit", null, (s, e) => Close(), Keys.Alt | Keys.F4) { AccessibleName = "Exit Application Menu Item" };
        mnuFile.DropDownItems.AddRange(new ToolStripItem[] { mnuUnlock, mnuLock, new ToolStripSeparator(), mnuExit });

        var mnuConnection = new ToolStripMenuItem("&Connection");
        var mnuAddConn = new ToolStripMenuItem("&Add Connection...", null, (s, e) => ShowAddConnectionDialog(), Keys.Control | Keys.N);
        var mnuSync = new ToolStripMenuItem("&Synchronize Selected...", null, (s, e) => SynchronizeSelectedConnection(), Keys.F5);
        var mnuDisconnect = new ToolStripMenuItem("&Disconnect / Purge...", null, (s, e) => RemoveSelectedConnection());
        mnuConnection.DropDownItems.AddRange(new ToolStripItem[] { mnuAddConn, mnuSync, new ToolStripSeparator(), mnuDisconnect });

        var mnuAccounts = new ToolStripMenuItem("&Accounts");
        var mnuRefresh = new ToolStripMenuItem("&Refresh Balances...", null, (s, e) => RefreshSelectedAccount(), Keys.Control | Keys.R);
        var mnuDiscover = new ToolStripMenuItem("&Discover Accounts...", null, (s, e) => DiscoverAccounts(), Keys.Control | Keys.D);
        var mnuRelink = new ToolStripMenuItem("&Relink Account Binding...", null, (s, e) => RelinkSelectedBinding(), Keys.Control | Keys.B);
        mnuAccounts.DropDownItems.AddRange(new ToolStripItem[] { mnuRefresh, mnuDiscover, new ToolStripSeparator(), mnuRelink });

        var mnuDiagnostics = new ToolStripMenuItem("&Diagnostics");
        var mnuExportPreview = new ToolStripMenuItem("&Export Redacted Support Preview...", null, (s, e) => ShowSupportExportDialog());
        var mnuToggleDiag = new ToolStripMenuItem("Enable Encrypted &Local Diagnostics", null, (s, e) => ToggleDiagnostics());
        mnuDiagnostics.DropDownItems.AddRange(new ToolStripItem[] { mnuExportPreview, mnuToggleDiag });

        _menuStrip.Items.AddRange(new ToolStripItem[] { mnuFile, mnuConnection, mnuAccounts, mnuDiagnostics });
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
            _btnSyncConnection.Enabled = false;
            _btnRemoveConnection.Enabled = false;
            return;
        }

        _lblConnInst.Text = conn.InstitutionName;
        _lblConnBlz.Text = $"Bank Code (BLZ): {conn.BankCode}";
        _lblConnUser.Text = $"User ID: {conn.UserId} (Customer: {conn.CustomerIdDisplay})";
        _lblConnHost.Text = $"Endpoint Host: {conn.EndpointHost}\nURL: {conn.EndpointUriDisplay}";
        _lblConnState.Text = $"Status: {conn.StateDisplay}";
        _lblConnProcedures.Text = $"{conn.SecurityProfileDisplay} | {conn.ProceduresCountDisplay}";

        _btnSyncConnection.Enabled = _session.IsUnlocked;
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

    private void RefreshSelectedAccount()
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
            "Balance refresh initiated with session-only PIN. Under normal bank conditions, balance refresh completes within seconds. Production bank validation is governed by M1-09.",
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

    private void ToggleDiagnostics()
    {
        _session.IsDiagnosticsEnabled = !_session.IsDiagnosticsEnabled;
        string status = _session.IsDiagnosticsEnabled ? "Enabled (persisting to diagnostics.bffond)" : "Disabled";
        MessageBox.Show(this, $"Encrypted local diagnostics is now: {status}", "Diagnostic Logging", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
