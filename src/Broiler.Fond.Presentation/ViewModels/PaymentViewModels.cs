using System.Globalization;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Mvvm;

namespace Broiler.Fond.Presentation.ViewModels;

/// <summary>
/// Presentation view model for a local SEPA payment draft and its active revision.
/// </summary>
public sealed class PaymentDraftViewModel : ObservableObject
{
    public PaymentDraftViewModel(
        StorePaymentDraftEntry draft,
        StorePaymentDraftRevisionEntry revision,
        string sourceAccountName)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(revision);

        DraftId = draft.Id;
        CurrentRevisionId = draft.CurrentRevisionId;
        SourceAccountId = draft.SourceAccountId;
        SourceAccountBindingId = draft.SourceAccountBindingId;
        SourceAccountIncarnationId = draft.SourceAccountIncarnationId;
        SourceAccountName = sourceAccountName ?? $"Account #{draft.SourceAccountId}";

        RevisionNumber = revision.RevisionNumber;
        BeneficiaryName = revision.BeneficiaryName;
        BeneficiaryIban = revision.BeneficiaryIban;
        BeneficiaryBic = revision.BeneficiaryBic;
        Amount = revision.Amount;
        Currency = revision.Currency;
        RemittanceUnstructured = revision.RemittanceUnstructured;
        RemittanceCreditorReference = revision.RemittanceCreditorReference;
        ExecutionMode = revision.ExecutionMode;
        ExecutionDate = revision.ExecutionDate;

        CreatedAt = draft.CreatedAt;
        UpdatedAt = draft.UpdatedAt;
    }

    public ulong DraftId { get; }
    public ulong CurrentRevisionId { get; }
    public ulong SourceAccountId { get; }
    public ulong SourceAccountBindingId { get; }
    public ulong SourceAccountIncarnationId { get; }
    public string SourceAccountName { get; }

    public int RevisionNumber { get; }
    public string BeneficiaryName { get; }
    public string BeneficiaryIban { get; }
    public string? BeneficiaryBic { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public string? RemittanceUnstructured { get; }
    public string? RemittanceCreditorReference { get; }
    public string ExecutionMode { get; }
    public DateOnly ExecutionDate { get; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }

    public string AmountDisplay => $"{Amount.ToString("N2", CultureInfo.CurrentCulture)} {Currency}";
    public string BeneficiaryDisplay => $"{BeneficiaryName} ({BeneficiaryIban})";
    public string BeneficiaryBicDisplay => BeneficiaryBic ?? "[None]";
    public string RemittanceDisplay => !string.IsNullOrWhiteSpace(RemittanceCreditorReference)
        ? $"Creditor Ref: {RemittanceCreditorReference}"
        : (!string.IsNullOrWhiteSpace(RemittanceUnstructured) ? RemittanceUnstructured : "[None]");
    public string ExecutionDateDisplay => ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string UpdatedAtDisplay => UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}

/// <summary>
/// Presentation view model for an immutable payment attempt with truthful state badges and VoP outcome.
/// </summary>
public sealed class PaymentAttemptViewModel : ObservableObject
{
    public PaymentAttemptViewModel(
        StorePaymentAttemptEntry attempt,
        PaymentDraftViewModel? draft,
        IReadOnlyList<StorePaymentStateTransitionEntry> transitions)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        AttemptId = attempt.Id;
        DraftId = attempt.DraftId;
        DraftRevisionId = attempt.DraftRevisionId;
        SourceAccountId = attempt.SourceAccountId;
        SourceAccountBindingId = attempt.SourceAccountBindingId;
        SourceAccountIncarnationId = attempt.SourceAccountIncarnationId;
        EndToEndId = attempt.EndToEndId;
        State = attempt.State;
        BankReference = attempt.BankReference;
        ResponseCode = attempt.ResponseCode;
        StatusDescription = attempt.StatusDescription;
        CreatedAt = attempt.CreatedAt;
        UpdatedAt = attempt.UpdatedAt;

        Draft = draft;
        VoPResult = attempt.VoPResult;

        Transitions = (transitions ?? Array.Empty<StorePaymentStateTransitionEntry>())
            .Where(t => t.AttemptId == attempt.Id)
            .OrderBy(t => t.Timestamp)
            .Select(t => new PaymentStateTransitionViewModel(t))
            .ToList()
            .AsReadOnly();
    }

    public ulong AttemptId { get; }
    public ulong DraftId { get; }
    public ulong DraftRevisionId { get; }
    public ulong SourceAccountId { get; }
    public ulong SourceAccountBindingId { get; }
    public ulong SourceAccountIncarnationId { get; }
    public string EndToEndId { get; }
    public string State { get; }
    public string? BankReference { get; }
    public string? ResponseCode { get; }
    public string? StatusDescription { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }

    public PaymentDraftViewModel? Draft { get; }
    public StorePaymentVoPResultEntry? VoPResult { get; }
    public IReadOnlyList<PaymentStateTransitionViewModel> Transitions { get; }

    public string BeneficiaryName => Draft?.BeneficiaryName ?? "[Unknown Payee]";
    public string BeneficiaryIban => Draft?.BeneficiaryIban ?? "[Unknown IBAN]";
    public decimal Amount => Draft?.Amount ?? 0m;
    public string Currency => Draft?.Currency ?? "EUR";
    public string AmountDisplay => Draft?.AmountDisplay ?? "0.00 EUR";

    public bool IsUnknown => string.Equals(State, nameof(PaymentState.Unknown), StringComparison.OrdinalIgnoreCase);
    public bool IsAccepted => string.Equals(State, nameof(PaymentState.Accepted), StringComparison.OrdinalIgnoreCase);
    public bool IsBankPending => string.Equals(State, nameof(PaymentState.BankPending), StringComparison.OrdinalIgnoreCase);
    public bool IsRejected => string.Equals(State, nameof(PaymentState.Rejected), StringComparison.OrdinalIgnoreCase);
    public bool IsReconciled => string.Equals(State, nameof(PaymentState.Reconciled), StringComparison.OrdinalIgnoreCase);
    public bool IsCancelled => string.Equals(State, nameof(PaymentState.AuthorizationCancelled), StringComparison.OrdinalIgnoreCase);

    public bool CanReconcile => IsAccepted || IsBankPending || IsUnknown;
    public bool CanRetry => IsRejected || IsCancelled;

    public string StateDisplay => State switch
    {
        nameof(PaymentState.Draft) => "[Draft]",
        nameof(PaymentState.Validated) => "[✓ Validated]",
        nameof(PaymentState.PayeeCheckPending) => "[⏳ VoP Pending]",
        nameof(PaymentState.PayeeMatch) => "[✓ VoP Match]",
        nameof(PaymentState.PayeeCloseMatch) => "[~ VoP Close Match]",
        nameof(PaymentState.PayeeNoMatch) => "[⚠ VoP Mismatch]",
        nameof(PaymentState.PayeeCheckUnavailable) => "[? VoP Unavailable]",
        nameof(PaymentState.PayeeCheckNotApplicable) => "[- VoP N/A]",
        nameof(PaymentState.AwaitingAuthorization) => "[🔑 Awaiting SCA]",
        nameof(PaymentState.Submitting) => "[📤 Submitting]",
        nameof(PaymentState.Accepted) => "[✓ Accepted by Bank]",
        nameof(PaymentState.BankPending) => "[⏳ Bank Processing]",
        nameof(PaymentState.Rejected) => "[✗ Bank Rejected]",
        nameof(PaymentState.Unknown) => "[⚠ Unknown Outcome - Reconcile]",
        nameof(PaymentState.Reconciled) => "[✓ Reconciled with Booking]",
        nameof(PaymentState.AuthorizationCancelled) => "[- Cancelled by User]",
        _ => $"[{State}]"
    };

    public string VoPStatusDisplay => VoPResult is null
        ? "[- Not Performed]"
        : VoPResult.Status switch
        {
            nameof(VerificationOfPayeeStatus.Match) => "[✓ Match]",
            nameof(VerificationOfPayeeStatus.CloseMatch) => $"[~ Close Match: {VoPResult.MatchedName ?? "Suggested Name"}]",
            nameof(VerificationOfPayeeStatus.NoMatch) => "[⚠ Mismatch Warning]",
            nameof(VerificationOfPayeeStatus.Unavailable) => "[? Verification Unavailable]",
            nameof(VerificationOfPayeeStatus.NotApplicable) => "[- Not Applicable]",
            _ => $"[{VoPResult.Status}]"
        };

    public string BankReferenceDisplay => BankReference ?? "[None]";
    public string ResponseCodeDisplay => ResponseCode ?? "[None]";
    public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string UpdatedAtDisplay => UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
}

/// <summary>
/// Presentation view model for an append-only payment state transition audit record.
/// </summary>
public sealed class PaymentStateTransitionViewModel : ObservableObject
{
    public PaymentStateTransitionViewModel(StorePaymentStateTransitionEntry transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        Id = transition.Id;
        AttemptId = transition.AttemptId;
        FromState = transition.FromState;
        ToState = transition.ToState;
        Timestamp = transition.Timestamp;
        Reason = transition.Reason;
        BankCode = transition.BankCode;
    }

    public ulong Id { get; }
    public ulong AttemptId { get; }
    public string FromState { get; }
    public string ToState { get; }
    public DateTimeOffset Timestamp { get; }
    public string Reason { get; }
    public string? BankCode { get; }

    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);
    public string TransitionDisplay => $"{FromState} -> {ToState}";
    public string DetailsDisplay => string.IsNullOrWhiteSpace(BankCode) ? Reason : $"{Reason} (Code: {BankCode})";
}

/// <summary>
/// Presentation view model for a saved beneficiary template in the profile address book.
/// </summary>
public sealed class BeneficiaryTemplateViewModel : ObservableObject
{
    public BeneficiaryTemplateViewModel(StoreBeneficiaryTemplateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Id = entry.Id;
        Label = entry.Label;
        BeneficiaryName = entry.BeneficiaryName;
        BeneficiaryIban = entry.BeneficiaryIban;
        BeneficiaryBic = entry.BeneficiaryBic;
        DefaultRemittanceUnstructured = entry.DefaultRemittanceUnstructured;
        DefaultRemittanceCreditorReference = entry.DefaultRemittanceCreditorReference;
        DefaultAmount = entry.DefaultAmount;
        DefaultCurrency = entry.DefaultCurrency ?? "EUR";
        DefaultExecutionMode = entry.DefaultExecutionMode ?? "StandardCreditTransfer";
        IsSoftDeleted = entry.IsSoftDeleted;
        CreatedAt = entry.CreatedAt;
        UpdatedAt = entry.UpdatedAt;
    }

    public ulong Id { get; }
    public string Label { get; }
    public string BeneficiaryName { get; }
    public string BeneficiaryIban { get; }
    public string Iban => BeneficiaryIban;
    public string? BeneficiaryBic { get; }
    public string? DefaultRemittanceUnstructured { get; }
    public string? DefaultRemittance => DefaultRemittanceUnstructured;
    public string? DefaultRemittanceCreditorReference { get; }
    public decimal? DefaultAmount { get; }
    public string DefaultCurrency { get; }
    public string DefaultExecutionMode { get; }
    public bool IsSoftDeleted { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }

    public string DisplayName => $"{Label} — {BeneficiaryName} ({BeneficiaryIban})";
    public string DefaultAmountDisplay => DefaultAmount.HasValue
        ? $"{DefaultAmount.Value.ToString("N2", CultureInfo.CurrentCulture)} {DefaultCurrency}"
        : "[None]";
    public string ExecutionModeDisplay => DefaultExecutionMode;
}

/// <summary>
/// Presentation view model for a standing order (SEPA Dauerauftrag).
/// </summary>
public sealed class StandingOrderViewModel : ObservableObject
{
    public StandingOrderViewModel(
        ulong orderId,
        string beneficiaryName,
        string beneficiaryIban,
        decimal amount,
        string currency,
        string frequency,
        int executionDay,
        DateOnly firstExecutionDate,
        DateOnly? lastExecutionDate,
        string status)
    {
        OrderId = orderId;
        BeneficiaryName = beneficiaryName;
        BeneficiaryIban = beneficiaryIban;
        Amount = amount;
        Currency = currency;
        Frequency = frequency;
        ExecutionDay = executionDay;
        FirstExecutionDate = firstExecutionDate;
        LastExecutionDate = lastExecutionDate;
        Status = status;
    }

    public ulong OrderId { get; }
    public string BeneficiaryName { get; }
    public string BeneficiaryIban { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public string Frequency { get; }
    public int ExecutionDay { get; }
    public DateOnly FirstExecutionDate { get; }
    public DateOnly? LastExecutionDate { get; }
    public string Status { get; }
    public string CadenceDisplay => FrequencyDisplay;
    public DateOnly NextExecutionDate => FirstExecutionDate;

    public string AmountDisplay => $"{Amount.ToString("N2", CultureInfo.CurrentCulture)} {Currency}";
    public string FrequencyDisplay => $"{Frequency} (Day {ExecutionDay})";
    public string FirstExecutionDisplay => FirstExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string LastExecutionDisplay => LastExecutionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "[Indefinite / Bis auf Weiteres]";
    public string StatusDisplay => $"[{Status}]";
}

/// <summary>
/// Presentation view model for evaluating an eligible direct-debit transaction for return.
/// </summary>
public sealed class DirectDebitReturnViewModel : ObservableObject
{
    public DirectDebitReturnViewModel(
        ulong transactionId,
        string creditorName,
        string creditorIban,
        string? creditorId,
        string? mandateId,
        decimal amount,
        string currency,
        DateOnly bookingDate,
        DateOnly referenceDate)
    {
        TransactionId = transactionId;
        CreditorName = creditorName;
        CreditorIban = creditorIban;
        CreditorId = creditorId;
        MandateId = mandateId;
        Amount = Math.Abs(amount);
        Currency = currency;
        BookingDate = bookingDate;

        int days = referenceDate.DayNumber - bookingDate.DayNumber;
        DaysSinceBooking = Math.Max(0, days);
        IsWithin8Weeks = DaysSinceBooking <= 56;
        IsWithin13Months = DaysSinceBooking <= 396;
    }

    public ulong TransactionId { get; }
    public string CreditorName { get; }
    public string CreditorIban { get; }
    public string? CreditorId { get; }
    public string? MandateId { get; }
    public string? MandateReference => MandateId;
    public decimal Amount { get; }
    public string Currency { get; }
    public DateOnly BookingDate { get; }
    public DateOnly EightWeekDeadline => BookingDate.AddDays(56);
    public int DaysSinceBooking { get; }
    public bool IsWithin8Weeks { get; }
    public bool IsWithinEightWeeks => IsWithin8Weeks;
    public bool IsWithin13Months { get; }

    public string AmountDisplay => $"{Amount.ToString("N2", CultureInfo.CurrentCulture)} {Currency}";
    public string BookingDateDisplay => BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
