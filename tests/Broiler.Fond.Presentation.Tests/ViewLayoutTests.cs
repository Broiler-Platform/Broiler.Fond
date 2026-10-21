using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;
using Broiler.Graphics.Geometry;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Toolbar.Standard;

namespace Broiler.Fond.Presentation.Tests;

internal static class ViewLayoutTests
{
    public static void Run(Action<bool, string> check)
    {
        using var session = new HostSession();
        using var shell = new BroilerMainWindow(session);
        shell.TabView.SelectTab("connections");
        shell.Connections.ConnectionsList.SetItems([
            new UiListItem("first", "First bank connection"),
            new UiListItem("second", "Second bank connection")
        ]);

        foreach (BSize size in new[] { new BSize(1200, 800), new BSize(640, 600), new BSize(320, 600) })
        {
            shell.Window.Measure(size);
            shell.Window.Arrange(new BRect(15, 25, size.Width, size.Height));
            var connections = shell.Connections;
            Verify("Connections in main window", connections.Root, connections.Toolbar, connections.ConnectionsList);
            check(connections.Root.Bounds.Top >= shell.TopBar.Bounds.Bottom - 0.5,
                "Connections view stays below the main window toolbar.");
            check(connections.ConnectionsList.ContentBounds.Top >= connections.Toolbar.Bounds.Bottom - 0.5,
                "First connection row starts below all connection toolbar controls.");
            check(connections.ConnectionsList.ContentBounds.Height >= connections.ConnectionsList.EffectiveItemHeight,
                "The full first connection row fits in the list viewport.");

            VerifyStandalone("Accounts", shell.Accounts.Root, shell.Accounts.Toolbar, shell.Accounts.AccountsList, size);
            VerifyStandalone("Transactions", shell.Transactions.Root, shell.Transactions.Toolbar, shell.Transactions.TransactionsList, size);
        }

        void VerifyStandalone(string name, StandardPanel root, StandardToolbar toolbar, StandardListView list, BSize size)
        {
            root.Measure(size);
            root.Arrange(new BRect(15, 25, size.Width, size.Height));
            Verify(name, root, toolbar, list);
        }

        void Verify(string name, StandardPanel root, StandardToolbar toolbar, StandardListView list)
        {
            check(list.Bounds.Top >= toolbar.Bounds.Bottom - 0.5, $"{name}: list starts below the complete toolbar, including wrapped rows.");
            check(list.Bounds.Bottom <= root.Bounds.Bottom + 0.5, $"{name}: list stays within its view.");
            check(list.Bounds.Height > 0, $"{name}: list retains a visible viewport.");
            foreach (var button in toolbar.Children)
                check(button.Bounds.Bottom <= list.Bounds.Top + 0.5, $"{name}: toolbar controls do not cover the first list row.");
        }
    }
}
