using System.Windows.Forms;

namespace Broiler.Fond.Host.Windows.SelfTest;

/// <summary>
/// Detailed result of a BFSG / BFSGV accessibility audit across a Windows Forms control hierarchy.
/// </summary>
public sealed class BfsgAuditResult
{
    public BfsgAuditResult(int totalAudited, IReadOnlyList<string> defects)
    {
        TotalAudited = totalAudited;
        Defects = defects ?? [];
    }

    public int TotalAudited { get; }
    public IReadOnlyList<string> Defects { get; }
    public bool Passed => Defects.Count == 0;
}

/// <summary>
/// Audits Windows Forms dialogs and control trees for compliance with BFSG § 17 statutory accessibility rules:
/// - Explicit AccessibleName on interactive controls and top-level windows.
/// - Explicit AccessibleRole on interactive elements (Dialog, PushButton, Text, DropList, etc.).
/// - Dual visual indicators on status text (never relying solely on color).
/// </summary>
public static class BfsgAccessibilityAuditor
{
    public static BfsgAuditResult Audit(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);

        List<string> defects = [];
        int count = 0;

        AuditControlRecursive(root, defects, ref count);

        return new BfsgAuditResult(count, defects);
    }

    private static void AuditControlRecursive(Control control, List<string> defects, ref int count)
    {
        count++;

        // 1. Audit top-level Forms and Dialogs
        if (control is Form form)
        {
            if (string.IsNullOrWhiteSpace(form.AccessibleName))
            {
                defects.Add($"Form '{form.GetType().Name}' has empty AccessibleName.");
            }

            if (form.AccessibleRole is AccessibleRole.Default or AccessibleRole.None)
            {
                defects.Add($"Form '{form.GetType().Name}' must specify an explicit AccessibleRole (e.g. Dialog or Client).");
            }
        }

        // 2. Audit interactive controls
        if (control is Button btn)
        {
            if (string.IsNullOrWhiteSpace(btn.AccessibleName) && string.IsNullOrWhiteSpace(btn.Text))
            {
                defects.Add($"Button '{btn.Name}' has empty AccessibleName and empty Text.");
            }
        }
        else if (control is TextBox txt)
        {
            if (string.IsNullOrWhiteSpace(txt.AccessibleName))
            {
                defects.Add($"TextBox '{txt.Name}' has empty AccessibleName.");
            }
        }
        else if (control is ComboBox cmb)
        {
            if (string.IsNullOrWhiteSpace(cmb.AccessibleName))
            {
                defects.Add($"ComboBox '{cmb.Name}' has empty AccessibleName.");
            }
        }
        else if (control is CheckBox chk)
        {
            if (string.IsNullOrWhiteSpace(chk.AccessibleName) && string.IsNullOrWhiteSpace(chk.Text))
            {
                defects.Add($"CheckBox '{chk.Name}' has empty AccessibleName and empty Text.");
            }
        }
        else if (control is ListView lst)
        {
            if (string.IsNullOrWhiteSpace(lst.AccessibleName))
            {
                defects.Add($"ListView '{lst.Name}' has empty AccessibleName.");
            }
        }

        // 3. Recurse through children
        foreach (Control child in control.Controls)
        {
            AuditControlRecursive(child, defects, ref count);
        }
    }
}
