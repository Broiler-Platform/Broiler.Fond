namespace Broiler.Fond.Kernel.Domain;

/// <summary>
/// Carries an exact decimal amount and its explicit currency. Validation and
/// currency metadata are deliberately deferred beyond Milestone 0.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency);
