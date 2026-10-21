using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Services;
using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Panel;
using Broiler.UI.ScrollView.Standard;

namespace Broiler.Fond.Presentation.Tests;

internal static class DialogLayoutTests
{
    public static void Run(Action<bool, string> check)
    {
        string longText = string.Join(" ", Enumerable.Repeat("Bank advisory requiring careful review before authorization.", 12));
        Verify(new BroilerAboutDialog(), d => d.Dialog);
        Verify(new BroilerAccountSetupDialog(new AccountSetupRequest([new AccountConnectionOption(2, "Example Bank")])), d => d.Dialog);
        Verify(new BroilerBankProcedureDialog(new("Example Bank", "https://bank.example/fints", [new("7:900", "Bank method", true, longText, 2, 900, 7)])), d => d.Dialog);
        Verify(new BroilerAccountRelinkingDialog(), d => d.Dialog);
        Verify(new BroilerAlertDialog("Alert", longText), d => d.Dialog);
        Verify(new BroilerAlertDialog("Bank Refresh Partially Completed", string.Join("\n", Enumerable.Repeat(
            "Account: Failed. Bank data was received but not saved. Check profile storage access before retrying.", 20)) +
            "\nSupport reference: 0123456789abcdef"), d => d.Dialog);
        Verify(new BroilerCashFlowForecastDialog(), d => d.Dialog);
        Verify(new BroilerCategorizationRulesDialog(), d => d.Dialog);
        Verify(new BroilerConfirmationDialog("Confirm", longText), d => d.Dialog);
        Verify(new BroilerConnectionRemovalDialog(longText), d => d.Dialog);
        Verify(new BroilerCredentialPromptDialog(new CredentialPromptRequest(
            "Bank", longText, "User", true, "TAN procedure")), d => d.Dialog);
        Verify(new BroilerCsvExportDialog(), d => d.Dialog);
        Verify(new BroilerDirectDebitReturnDialog(longText), d => d.Dialog);
        Verify(new BroilerEndpointConfirmationDialog("https://bank.example.com/fints"), d => d.Dialog);
        var institution = new BroilerInstitutionSetupDialog();
        institution.ErrorLabel.Text = longText;
        Verify(institution, d => d.Dialog);
        Verify(new BroilerPaymentInitiationDialog(), d => d.Dialog);
        Verify(new BroilerPaymentReviewDialog(new PaymentReviewRequest(
            1, longText, "DE89370400440532013000", 100, "EUR", longText, "Standard", "Test", "Match", longText)), d => d.Dialog);
        Verify(new BroilerProfileUnlockDialog("Profile"), d => d.Dialog);
        Verify(new BroilerRestoreBackupDialog(initialPath: "backup.bffond"), d => d.Dialog);
        Verify(new BroilerScaChallengeDialog("photoTAN", longText, true), d => d.Dialog);
        Verify(new BroilerStandingOrdersDialog(), d => d.Dialog);
        Verify(new BroilerSupportExportDialog(longText), d => d.Dialog);
        Verify(new BroilerTransactionSplitDialog(), d => d.Dialog);

        void Verify<T>(T wrapper, Func<T, StandardDialog> getDialog) where T : IDisposable
        {
            using (wrapper)
            {
                StandardDialog dialog = getDialog(wrapper);
                BRect placement = DialogLayout.GetPlacement(dialog, new BSize(1920, 1080));
                BRect smallPlacement = DialogLayout.GetPlacement(dialog, new BSize(240, 160));
                check(smallPlacement.Width <= 240 && smallPlacement.Height <= 160,
                    $"{dialog.Title}: default placement fits a small owner.");
                foreach (BSize size in new[] { placement.Size, dialog.PreferredSize, new BSize(320, 200), new BSize(240, 160) })
                {
                    dialog.Measure(size);
                    dialog.Arrange(new BRect(0, 0, size.Width, size.Height));
                    check(dialog.Children.Single() is StandardScrollView, $"{dialog.Title}: content supports overflow.");
                    var scroll = (StandardScrollView)dialog.Children.Single();
                    Inspect(scroll.Children.Single());
                    check(scroll.ExtentSize.Height >= scroll.Children.Single().DesiredSize.Height - 0.5,
                        $"{dialog.Title}: scroll extent contains all content.");
                    scroll.ScrollToEnd();
                    dialog.Arrange(new BRect(0, 0, size.Width, size.Height));
                    check(scroll.Children.Single().Bounds.Bottom <= scroll.Bounds.Bottom + 0.5,
                        $"{dialog.Title}: final actions can be scrolled into view.");
                    scroll.ScrollToStart();
                }

                void Inspect(UiElement element)
                {
                    if (element is UiPanel)
                    {
                        var children = element.Children.Where(c => c.Visibility != UiVisibility.Collapsed).ToArray();
                        foreach (UiElement child in children)
                        {
                            check(child.Bounds.Left >= element.Bounds.Left - 0.5 &&
                                child.Bounds.Right <= element.Bounds.Right + 0.5 &&
                                child.Bounds.Top >= element.Bounds.Top - 0.5 &&
                                child.Bounds.Bottom <= element.Bounds.Bottom + 0.5,
                                $"{dialog.Title}: {child.GetType().Name} stays inside its content panel.");
                        }
                        for (int i = 0; i < children.Length; i++)
                            for (int j = i + 1; j < children.Length; j++)
                            {
                                BRect overlap = children[i].Bounds.Intersect(children[j].Bounds);
                                check(overlap.Width <= 0.5 || overlap.Height <= 0.5,
                                    $"{dialog.Title}: sibling controls do not overlap.");
                            }
                    }
                    foreach (UiElement child in element.Children)
                        Inspect(child);
                }
            }
        }
    }
}
