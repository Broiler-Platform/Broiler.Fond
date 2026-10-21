namespace Broiler.Fond.Presentation.Dialogs;

using Broiler.Graphics.Geometry;
using Broiler.UI;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.ScrollView;
using Broiler.UI.ScrollView.Standard;
using Broiler.UI.Window;

/// <summary>Shared flowing, scrollable layout for application dialogs.</summary>
internal static class DialogLayout
{
    public static void Attach(StandardDialog dialog, StandardPanel content)
    {
        var scroll = new StandardScrollView
        {
            Constraint = UiScrollConstraint.ConstrainWidth,
            HorizontalScrollBarVisibility = UiScrollBarVisibility.Hidden,
            PreferredSize = BSize.Empty,
            FocusWhenScrollable = true,
            AccessibleName = "Dialog content"
        };
        scroll.AddChild(content);
        dialog.AddChild(scroll);
    }

    public static Task<UiDialogResult> ShowModalAsync(StandardDialog dialog, UiWindow owner, BRect? placement)
    {
        return dialog.ShowModal(owner, placement ?? GetPlacement(dialog, owner.Bounds.Size));
    }

    internal static BRect GetPlacement(StandardDialog dialog, BSize ownerSize)
    {
        // Measure the complete flow, including wrapped text and actions, before
        // choosing the window height. Small owners retain a scrollable viewport.
        double width = Math.Min(dialog.PreferredSize.Width, ownerSize.Width);
        UiElement content = dialog.Children[0].Children[0];
        BSize desired = content.Measure(new BSize(Math.Max(0, width - dialog.Padding * 2), double.PositiveInfinity));
        double height = Math.Min(Math.Max(dialog.PreferredSize.Height,
            desired.Height + dialog.TitleBarHeight + dialog.Padding * 2), ownerSize.Height);
        return new BRect(
            Math.Max(0, (ownerSize.Width - width) / 2),
            Math.Max(0, (ownerSize.Height - height) / 2),
            width, height);
    }
}

/// <summary>Actions use a row when possible and wrap at narrower widths.</summary>
internal sealed class DialogActionPanel : UiPanel
{
    protected override BSize MeasureCore(BSize availableSize)
    {
        foreach (UiElement child in Children)
        {
            if (child.Visibility != UiVisibility.Collapsed)
                child.Measure(new BSize(availableSize.Width, double.PositiveInfinity));
        }
        return Flow(availableSize.Width, null);
    }

    protected override void ArrangeCore(BRect finalRect) => Flow(finalRect.Width, finalRect);

    private BSize Flow(double availableWidth, BRect? bounds)
    {
        double x = 0, y = 0, rowHeight = 0, width = 0;
        foreach (UiElement child in Children)
        {
            if (child.Visibility == UiVisibility.Collapsed)
            {
                if (bounds is not null)
                    child.Arrange(BRect.Empty);
                continue;
            }

            double childWidth = Math.Min(child.DesiredSize.Width, availableWidth);
            if (x > 0 && x + childWidth > availableWidth)
            {
                y += rowHeight + Spacing;
                x = 0;
                rowHeight = 0;
            }
            if (bounds is BRect rect)
                child.Arrange(new BRect(rect.Left + x, rect.Top + y, childWidth, child.DesiredSize.Height));
            width = Math.Max(width, x + childWidth);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            x += childWidth + Spacing;
        }
        return new BSize(width, y + rowHeight);
    }
}
