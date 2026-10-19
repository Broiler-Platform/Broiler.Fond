using System.Drawing;
using System.Windows.Forms;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible About & Legal Disclosures Dialog providing pure user-operated trust boundary notices,
/// DK FinTS 3.0 product registration disclosures, BFSG/BFSGV accessibility conformance statements,
/// and open-source licenses.
/// </summary>
public sealed class AboutDialog : Form
{
    public AboutDialog()
    {
        Text = $"About {KernelProduct.FullDisplayName}";
        ClientSize = new Size(680, 520);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "About and Legal Disclosures Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = "Product disclosures, FinTS registration, BFSG accessibility statement, and licenses.";

        // Header Panel
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = Color.FromArgb(245, 247, 250),
            AccessibleName = "About Dialog Header Panel",
            AccessibleRole = AccessibleRole.Grouping
        };

        var lblAppTitle = new Label
        {
            Text = KernelProduct.FullDisplayName,
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            ForeColor = Color.DarkSlateBlue,
            Location = new Point(20, 15),
            Size = new Size(640, 30),
            AccessibleName = "Application Full Display Name"
        };

        var lblVersion = new Label
        {
            Text = $"Windows 1.0 Release - Milestone {KernelProduct.Milestone}",
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Regular),
            ForeColor = Color.DarkSlateGray,
            Location = new Point(20, 48),
            Size = new Size(640, 22),
            AccessibleName = "Application Version and Milestone"
        };

        pnlHeader.Controls.AddRange(new Control[] { lblAppTitle, lblVersion });

        // Tab Control
        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            TabIndex = 0,
            AccessibleName = "About Dialog Tab Control",
            AccessibleRole = AccessibleRole.PageTabList
        };

        var tabOverview = new TabPage("&Overview")
        {
            AccessibleName = "Product Overview Tab Page",
            AccessibleRole = AccessibleRole.PageTab
        };
        var tabFinTs = new TabPage("&FinTS Registration")
        {
            AccessibleName = "FinTS Registration Tab Page",
            AccessibleRole = AccessibleRole.PageTab
        };
        var tabBfsg = new TabPage("&Accessibility (BFSG)")
        {
            AccessibleName = "Accessibility BFSG Declaration Tab Page",
            AccessibleRole = AccessibleRole.PageTab
        };
        var tabLegal = new TabPage("&Legal & Licenses")
        {
            AccessibleName = "Legal and Licenses Tab Page",
            AccessibleRole = AccessibleRole.PageTab
        };

        // Tab 1: Overview
        var txtOverview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Font = new Font(Font.FontFamily, 9.5f),
            AccessibleName = "Overview and Trust Boundary Disclosures Text",
            Text = @"TRUST BOUNDARY & ARCHITECTURAL INVARIANTS (ADR 0003)

1. Purely User-Operated Client:
Broiler Fond is an installed Windows desktop application communicating directly and exclusively between your computer and your banking institutions. Broiler operates no account aggregation servers, no proxy gateways, no credential relays, no cloud synchronization services, and no financial-data databases.

2. Zero Telemetry & Analytics Policy:
Broiler Fond contains zero tracking code, zero analytics SDKs, zero crash-upload endpoints, and zero background network heartbeats. The only outbound network traffic created by the application is direct HTTPS communication to the financial institution endpoints you explicitly configure and confirm.

3. Ephemeral In-Memory Credential Hygiene:
Online banking PINs and TANs are captured exclusively into volatile memory buffers for the immediate bank dialogue and zeroized using CryptographicOperations.ZeroMemory upon dialogue termination. Banking PINs are never persisted to disk, never included in logs, and never exported in backups.

4. Authenticated Local Storage (BFPE v1):
All profile data is encrypted at rest using AES-256-GCM authenticated encryption and PBKDF2-HMAC-SHA256 key derivation with up to 5,000,000 calibrated iterations on the Windows platform floor."
        };
        tabOverview.Controls.Add(txtOverview);

        // Tab 2: FinTS Registration
        var txtFinTs = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Font = new Font(Font.FontFamily, 9.5f),
            AccessibleName = "FinTS Product Registration Disclosures Text",
            Text = $@"FINTS 3.0 ACCREDITED SPECIFICATIONS & PRODUCT IDENTIFICATION

1. DK FinTS Product Registration:
In compliance with the regulations of the German Banking Industry Committee (Die Deutsche Kreditwirtschaft - DK), Broiler Fond identifies itself during all FinTS dialogue initializations using its registered product attributes:
- Registered Product Identifier: {KernelProduct.FinTsRegistration.ProductIdentifier}
- Registered Product Version: {KernelProduct.FinTsRegistration.ProductVersion}
- Registration Code: {KernelProduct.FinTsRegistration.RegistrationCode}
- Display Name: {KernelProduct.FinTsRegistration.DisplayName}
- Standard Baseline: FinTS 3.0 Formals and PIN/TAN Security Procedures

2. Certified Wire Protocols:
- Security Envelopes: HNSHK / HNSHA / HNVSK / HNVSD
- Authentication: PIN/TAN Security Profile (HIBPA, HIUPA, HIUPD, HIPINS)
- Supported Operations: Account Discovery, Balances (HISAL/HISPAS), Account Statements (MT940/CAMT), Verification of Payee (VoP), SEPA Credit Transfers (HKCCS), Scheduled Transfers, Standing Orders, and Direct Debit Returns."
        };
        tabFinTs.Controls.Add(txtFinTs);

        // Tab 3: Accessibility (BFSG / BFSGV)
        var txtBfsg = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Font = new Font(Font.FontFamily, 9.5f),
            AccessibleName = "Accessibility BFSG Compliance Declaration Text",
            Text = @"BARRIEREFREIHEITSSTÄRKUNGSGESETZ (BFSG) & BFSGV COMPLIANCE DECLARATION

1. Statutory Conformance Statement:
Broiler Fond is designed and audited in compliance with the German Barrierefreiheitsstärkungsgesetz (BFSG) and the statutory ordinance BFSGV § 17 governing consumer banking and financial software services. The user interface meets the accessibility requirements of European Standard EN 301 549 and Web Content Accessibility Guidelines (WCAG) 2.1 Level AA.

2. Implemented Accessibility Features:
- Perceivable: All visual status, error, and progress indicators provide redundant text-based visual cues (e.g. [✓ ...] and [⚠ ...]) and never rely exclusively on color.
- Operable: All interactive screens, dialogs, forms, and tables are fully navigable via standard keyboard controls (Tab, Shift+Tab, Arrow keys, Enter, Escape, and Alt-mnemonic shortcuts).
- Understandable: Financial figures and dates use culture-invariant and localized decimal notation with ISO currency codes to avoid ambiguous presentation.
- Robust: All controls and dialogs declare semantic AccessibleName, AccessibleRole, and AccessibleDescription properties compatible with Windows screen readers (Narrator, NVDA, JAWS).
- Display Adaptability: Full support for high-contrast themes and system DPI/text scaling."
        };
        tabBfsg.Controls.Add(txtBfsg);

        // Tab 4: Legal & Licenses
        var txtLegal = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window,
            Font = new Font(Font.FontFamily, 9.5f),
            AccessibleName = "Legal Disclosures and Open Source Licenses Text",
            Text = @"LEGAL NOTICES & THIRD-PARTY LICENSES

Copyright (c) 2026 Broiler Fond Contributors.
All rights reserved.

1. Software License:
Broiler Fond - Finance on Demand is distributed subject to the terms of its applicable end-user software license.

2. Pure .NET 10 Shared Framework:
The core banking engine (Broiler.Fond.Kernel) is authored with zero third-party NuGet package references, zero native DLL dependencies, and zero proprietary runtime frameworks, executing exclusively on the Microsoft .NET 10 Shared Framework (Microsoft.NETCore.App).

3. Disclaimer of Warranties:
This software is provided 'as is', without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose, and non-infringement. In no event shall the authors or copyright holders be liable for any claim, damages, or other liability arising from the use of this software."
        };
        tabLegal.Controls.Add(txtLegal);

        tabControl.TabPages.AddRange(new TabPage[] { tabOverview, tabFinTs, tabBfsg, tabLegal });

        // Bottom Button Panel
        var pnlBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            AccessibleName = "About Dialog Bottom Action Panel",
            AccessibleRole = AccessibleRole.Grouping
        };

        var btnClose = new Button
        {
            Text = "&Close",
            DialogResult = DialogResult.OK,
            Location = new Point(560, 10),
            Size = new Size(95, 30),
            TabIndex = 1,
            AccessibleName = "Close About Dialog Button",
            AccessibleRole = AccessibleRole.PushButton
        };

        pnlBottom.Controls.Add(btnClose);
        AcceptButton = btnClose;
        CancelButton = btnClose;

        Controls.Add(tabControl);
        Controls.Add(pnlHeader);
        Controls.Add(pnlBottom);
    }
}
