namespace Broiler.Fond.Presentation.Tests;

using System;
using System.Security.Cryptography;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.Hosting;
using Broiler.Fond.Presentation.Accessibility;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Hosting;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.ViewModels;
using Broiler.Fond.Presentation.Views;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Standard;

internal static class DialogAndViewsTests
{
    public static void Run(Action<bool, string> check)
    {
        // 1. PresentationUiHost tests
        var host = new PresentationUiHost
        {
            ViewportSize = new BSize(1920, 1080),
            Scale = 1.25
        };

        check(host.ViewportSize.Width == 1920, "PresentationUiHost ViewportSize Width set correctly.");
        check(host.ViewportSize.Height == 1080, "PresentationUiHost ViewportSize Height set correctly.");
        check(host.Scale == 1.25, "PresentationUiHost Scale set correctly.");

        host.SetText("test-clipboard");
        bool gotText = host.TryGetText(out string cbText);
        check(gotText && cbText == "test-clipboard", "PresentationUiHost clipboard SetText and TryGetText work.");

        var session = new StandardUiSessionBuilder().Build(host);
        check(session != null, "StandardUiSessionBuilder creates UiSession with PresentationUiHost.");

        // 2. Dialog Accessibility & Memory Hygiene
        using (var unlockDlg = new BroilerProfileUnlockDialog("C:\\TestProfile"))
        {
            check(unlockDlg.Title == "Unlock Profile", "Profile unlock dialog title matches.");
            var audit = BroilerUiAccessibilityAuditor.AuditDialog(unlockDlg);
            check(audit.Passed, "Profile unlock dialog passes BFSG accessibility audit.");

            unlockDlg.PassphraseInput.AppendChars("SecretPassword123".AsSpan());
            check(unlockDlg.PassphraseInput.Length == 17, "Passphrase accepted.");
            unlockDlg.Close(BroilerDialogResult.Cancel);
            check(unlockDlg.PassphraseInput.Length == 0, "Passphrase wiped upon cancel.");
        }

        using (var credDlg = new BroilerCredentialPromptDialog("TestBank", "User1", FinTsCredentialKind.Pin))
        {
            check(credDlg.Title == "PIN Authentication", "PIN prompt dialog title matches.");
            var audit = BroilerUiAccessibilityAuditor.AuditDialog(credDlg);
            check(audit.Passed, "Credential prompt dialog passes BFSG accessibility audit.");

            credDlg.CredentialInput.AppendChars("12345".AsSpan());
            credDlg.Submit();
            check(credDlg.CapturedCredential != null, "Credential captured into FinTsSessionCredential.");
            check(credDlg.CredentialInput.Length == 0, "Credential input cleared upon submit.");
            var owned = credDlg.TakeCredential();
            check(owned is not null && credDlg.TakeCredential() is null, "Bank credential ownership transfers exactly once.");
            credDlg.Dispose();
            check(owned!.GetSnapshot().State == FinTsCredentialState.Available, "Dialog disposal preserves transferred credential ownership.");
            owned.Dispose();
        }

        var abandoned = new BroilerCredentialPromptDialog("Bank", "User");
        abandoned.CredentialInput.AppendChars("secret-marker".AsSpan());
        abandoned.Accept();
        var abandonedOwner = abandoned.CapturedCredential!;
        abandoned.Dispose();
        check(abandonedOwner.GetSnapshot().State == FinTsCredentialState.Disposed, "Dialog disposal clears an unclaimed bank credential.");
        using (var empty = new BroilerCredentialPromptDialog("Bank", "User"))
        {
            empty.Accept();
            check(empty.TakeCredential() is null && !empty.CredentialEdit.IsDisposed, "Empty banking credential cannot accept the dialog.");
            empty.CredentialInput.AppendChars(new string('x', 100).AsSpan());
            empty.Submit();
            check(empty.TakeCredential() is null && empty.CredentialInput.Length == 0, "Oversized credential is cleared without accepting or throwing.");
        }

        using (var epDlg = new BroilerEndpointConfirmationDialog(new Uri("https://bank.example.com/fints"), "Example Bank"))
        {
            check(epDlg.Title == "Verify Bank Host Endpoint", "Endpoint confirmation dialog title matches.");
            var audit = BroilerUiAccessibilityAuditor.AuditDialog(epDlg);
            check(audit.Passed, "Endpoint confirmation dialog passes BFSG accessibility audit.");
            check(epDlg.HostLabel.Text.Contains("bank.example.com", StringComparison.Ordinal), "Host label contains domain.");
        }

        using (var scaDlg = new BroilerScaChallengeDialog("photoTAN", "Scan code", requiresTanInput: true))
        {
            check(scaDlg.Title.Contains("Authentication"), "SCA challenge dialog title matches.");
            var audit = BroilerUiAccessibilityAuditor.AuditDialog(scaDlg);
            check(audit.Passed, "SCA challenge dialog passes BFSG accessibility audit.");
        }

        using (var payDlg = new BroilerPaymentInitiationDialog("Empfaenger GmbH", "DE89370400440532013000", 125.50m, "Rechnung 456"))
        {
            check(payDlg.Dialog.Title == "New SEPA Transfer", "Payment initiation dialog title matches.");
            var audit = BroilerUiAccessibilityAuditor.AuditDialog(payDlg);
            check(audit.Passed, "Payment initiation dialog passes BFSG accessibility audit.");
        }

        using (var aboutDlg = new BroilerAboutDialog())
        {
            check(aboutDlg.Dialog.Title == "About Broiler Fond", "About dialog title matches.");
            var audit = BroilerUiAccessibilityAuditor.AuditDialog(aboutDlg);
            check(audit.Passed, "About dialog passes BFSG accessibility audit.");
        }

        // 3. Views and Main Banking Shell
        using (var hostSession = new HostSession())
        {
            var dialogService = new BroilerDialogService();

            using var accountsView = new AccountsView(hostSession);
            check(accountsView.Root != null, "AccountsView initialized.");
            check(accountsView.AccountsList != null, "AccountsView has AccountsList.");

            using var txView = new TransactionsView(hostSession);
            check(txView.Root != null, "TransactionsView initialized.");
            check(txView.TransactionsList != null, "TransactionsView has TransactionsList.");

            using var insView = new InsightsView(hostSession);
            check(insView.Root != null, "InsightsView initialized.");

            using var connView = new ConnectionsView(hostSession);
            check(connView.Root != null, "ConnectionsView initialized.");

            using var payView = new PaymentsView(hostSession);
            check(payView.Root != null, "PaymentsView initialized.");

            using var secView = new SecurityAuditView(hostSession);
            check(secView.Root != null, "SecurityAuditView initialized.");

            using var mainWindow = new BroilerMainWindow(hostSession, dialogService);
            check(mainWindow.Window != null, "BroilerMainWindow window shell initialized.");
            check(mainWindow.TabView != null, "BroilerMainWindow has TabView.");
            check(mainWindow.TopBar != null, "BroilerMainWindow has TopBar.");
            check(mainWindow.SyncAllButton != null, "BroilerMainWindow has SyncAllButton.");
            check(dialogService.WindowAccessor != null && dialogService.WindowAccessor() == mainWindow.Window, "dialogService.WindowAccessor returns mainWindow.Window.");

            mainWindow.Attach(session!);
            check(mainWindow.Window != null, "BroilerMainWindow attached to UiSession successfully.");

            var shellAudit = BroilerUiAccessibilityAuditor.Audit(mainWindow);
            check(shellAudit.Passed, "BroilerMainWindow shell tree passes BFSG accessibility audit.");

            // 4. Test Add and Remove Connection lifecycle
            string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fond_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                hostSession.CreateNewProfile(tempDir, "TestPass123!", "Test Profile");
                check(hostSession.IsUnlocked, "HostSession is unlocked after CreateNewProfile.");

                var newConn = hostSession.AddInstitutionAndConnection("Test Volksbank", "10090000", "https://hbci.volksbank.de/fints");
                check(newConn != null, "AddInstitutionAndConnection returns created ConnectionViewModel.");
                check(hostSession.Connections.Count == 1, "HostSession has 1 connection.");
                check(hostSession.Connections[0].InstitutionName == "Test Volksbank", "Connection InstitutionName matches.");

                mainWindow.Connections.RefreshConnections();
                check(mainWindow.Connections.ConnectionsList.Items.Count == 1, "ConnectionsView list populated with newly added connection.");

                hostSession.RemoveConnection(newConn!.ConnectionId, purgeCachedData: false);
                check(hostSession.Connections.Count == 0, "HostSession connection removed successfully.");
                mainWindow.Connections.RefreshConnections();
                check(mainWindow.Connections.ConnectionsList.Items.Count == 0, "ConnectionsView list empty after removal.");
            }
            finally
            {
                if (System.IO.Directory.Exists(tempDir))
                {
                    try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }

        session?.Dispose();
    }
}
