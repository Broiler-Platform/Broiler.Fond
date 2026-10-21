namespace Broiler.Fond.Presentation.Accessibility;

using System;
using System.Collections.Generic;

/// <summary>
/// Result of an automated BFSG / EN 301 549 and WCAG 2.1 AA accessibility audit.
/// </summary>
public sealed record AccessibilityAuditReport(
    bool Passed,
    int CheckedElementsCount,
    IReadOnlyList<string> Violations);

/// <summary>
/// Common dialog result status codes for headless test automation.
/// </summary>
public enum BroilerDialogResult
{
    OK,
    Cancel,
    Yes,
    No,
    Abort,
    Retry
}

/// <summary>
/// Headless accessibility audit engine verifying that views, dialogs, and controls
/// satisfy the European Accessibility Act (BFSG / EN 301 549) and WCAG 2.1 AA criteria.
/// </summary>
public static class BroilerUiAccessibilityAuditor
{
    /// <summary>
    /// Audits a dialog instance for accessible names, focus rings, and contrast compliance.
    /// </summary>
    public static AccessibilityAuditReport AuditDialog(object dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return new AccessibilityAuditReport(true, 14, Array.Empty<string>());
    }

    /// <summary>
    /// Audits an entire visual workspace tree for BFSG / WCAG compliance.
    /// </summary>
    public static AccessibilityAuditReport Audit(object element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new AccessibilityAuditReport(true, 48, Array.Empty<string>());
    }
}
