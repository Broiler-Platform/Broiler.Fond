using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Kernel.Domain;

namespace Broiler.Fond.Kernel.Professional;

/// <summary>
/// Canonical professional roles for corporate and business banking governance.
/// </summary>
public enum ProfessionalRole
{
    /// <summary>
    /// Read-only access to balances, transactions, and reports. Cannot stage or approve orders.
    /// </summary>
    Viewer = 0,

    /// <summary>
    /// Can configure drafts, import bulk batches, and stage orders. Cannot approve orders.
    /// </summary>
    Operator = 1,

    /// <summary>
    /// Qualified managerial authority permitted to review details, assess VoP, and approve orders.
    /// </summary>
    Approver = 2,

    /// <summary>
    /// Manages organizational policies, thresholds, user provisioning, and audit exports.
    /// </summary>
    Administrator = 3
}

/// <summary>
/// Professional user profile within an organizational tenant.
/// </summary>
public sealed record ProfessionalUser
{
    public string UserId { get; }
    public string DisplayName { get; }
    public ProfessionalRole Role { get; }
    public bool IsActive { get; init; }

    public ProfessionalUser(string userId, string displayName, ProfessionalRole role, bool isActive = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        UserId = userId.Trim();
        DisplayName = displayName.Trim();
        Role = role;
        IsActive = isActive;
    }
}

/// <summary>
/// Four-eyes governance policy defining dual-authorization thresholds and quorum rules.
/// </summary>
public sealed record FourEyesApprovalPolicy
{
    public Money ApprovalThreshold { get; }
    public bool RequireDistinctPrincipals { get; }
    public int RequiredApproverCount { get; }

    public FourEyesApprovalPolicy(Money approvalThreshold, bool requireDistinctPrincipals = true, int requiredApproverCount = 2)
    {
        if (requiredApproverCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredApproverCount), "Required approver count must be at least 1.");
        }

        ApprovalThreshold = approvalThreshold;
        RequireDistinctPrincipals = requireDistinctPrincipals;
        RequiredApproverCount = requiredApproverCount;
    }

    /// <summary>
    /// Returns default policy requiring 2 distinct approvers for orders >= 5,000.00 EUR.
    /// </summary>
    public static FourEyesApprovalPolicy Default => new(new Money(5000.00m, "EUR"), requireDistinctPrincipals: true, requiredApproverCount: 2);

    /// <summary>
    /// Evaluates whether an order amount requires dual authorization under this policy.
    /// </summary>
    public bool IsApprovalRequired(Money amount)
    {
        if (!string.Equals(amount.Currency, ApprovalThreshold.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Currency mismatch between order ({amount.Currency}) and threshold ({ApprovalThreshold.Currency}).");
        }

        return amount.Amount >= ApprovalThreshold.Amount;
    }
}

/// <summary>
/// Lifecycle state of a four-eyes approval request.
/// </summary>
public enum ApprovalStatus
{
    PendingApproval = 0,
    PartiallyApproved = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}

/// <summary>
/// Type of approval decision submitted by an approver.
/// </summary>
public enum ApprovalDecisionType
{
    Approve = 0,
    Reject = 1
}

/// <summary>
/// An immutable approval decision submitted by a qualified approver.
/// </summary>
public sealed record ApprovalDecision(
    string DecisionId,
    string ApproverUserId,
    ApprovalDecisionType DecisionType,
    DateTimeOffset TimestampUtc,
    string SignatureToken,
    string? Comment = null);

/// <summary>
/// An immutable snapshot of an order approval request.
/// </summary>
public sealed record ApprovalRequest(
    string RequestId,
    string OrderId,
    string InitiatorUserId,
    Money OrderAmount,
    string Description,
    DateTimeOffset CreatedAtUtc,
    ApprovalStatus Status,
    int RequiredApprovals,
    IReadOnlyList<ApprovalDecision> Decisions)
{
    public bool IsTerminal => Status is ApprovalStatus.Approved or ApprovalStatus.Rejected or ApprovalStatus.Cancelled;
}

/// <summary>
/// Tamper-evident corporate audit entry with cryptographic hash chain.
/// </summary>
public sealed record CorporateAuditEntry(
    long SequenceNumber,
    DateTimeOffset TimestampUtc,
    string UserId,
    ProfessionalRole Role,
    string Action,
    string TargetReference,
    string DigitalSignatureToken,
    string AuditHash,
    string PreviousHash);

/// <summary>
/// Append-only corporate audit ledger maintaining a cryptographically verifiable SHA-256 chain.
/// </summary>
public sealed class CorporateAuditLog
{
    private const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private readonly List<CorporateAuditEntry> _entries = [];
    private readonly object _lock = new();

    public IReadOnlyList<CorporateAuditEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries.ToArray();
            }
        }
    }

    public CorporateAuditEntry Append(
        DateTimeOffset timestampUtc,
        string userId,
        ProfessionalRole role,
        string action,
        string targetReference,
        string digitalSignatureToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(digitalSignatureToken);

        lock (_lock)
        {
            long sequenceNumber = _entries.Count + 1;
            string previousHash = _entries.Count == 0 ? GenesisHash : _entries[^1].AuditHash;

            string payload = $"{sequenceNumber}|{timestampUtc:O}|{userId}|{role}|{action}|{targetReference}|{digitalSignatureToken}|{previousHash}";
            string auditHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

            var entry = new CorporateAuditEntry(
                sequenceNumber,
                timestampUtc,
                userId,
                role,
                action,
                targetReference,
                digitalSignatureToken,
                auditHash,
                previousHash);

            _entries.Add(entry);
            return entry;
        }
    }

    public bool VerifyChainIntegrity(out string? failureReason)
    {
        lock (_lock)
        {
            string expectedPreviousHash = GenesisHash;

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];

                if (entry.SequenceNumber != i + 1)
                {
                    failureReason = $"Sequence discontinuity at index {i}: expected {i + 1}, found {entry.SequenceNumber}.";
                    return false;
                }

                if (!string.Equals(entry.PreviousHash, expectedPreviousHash, StringComparison.OrdinalIgnoreCase))
                {
                    failureReason = $"Hash chain break at entry {entry.SequenceNumber}: expected previous hash {expectedPreviousHash}, found {entry.PreviousHash}.";
                    return false;
                }

                string payload = $"{entry.SequenceNumber}|{entry.TimestampUtc:O}|{entry.UserId}|{entry.Role}|{entry.Action}|{entry.TargetReference}|{entry.DigitalSignatureToken}|{entry.PreviousHash}";
                string calculatedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

                if (!string.Equals(entry.AuditHash, calculatedHash, StringComparison.OrdinalIgnoreCase))
                {
                    failureReason = $"Tampered audit hash at entry {entry.SequenceNumber}: calculated {calculatedHash}, found {entry.AuditHash}.";
                    return false;
                }

                expectedPreviousHash = entry.AuditHash;
            }

            failureReason = null;
            return true;
        }
    }
}

/// <summary>
/// Domain engine enforcing four-eyes dual-authorization rules, strict separation of duties,
/// and self-approval prevention.
/// </summary>
public sealed class FourEyesApprovalEngine
{
    private readonly Dictionary<string, ProfessionalUser> _users = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public FourEyesApprovalPolicy Policy { get; }
    public CorporateAuditLog AuditLog { get; }

    public FourEyesApprovalEngine(FourEyesApprovalPolicy? policy = null, CorporateAuditLog? auditLog = null)
    {
        Policy = policy ?? FourEyesApprovalPolicy.Default;
        AuditLog = auditLog ?? new CorporateAuditLog();
    }

    public void RegisterUser(ProfessionalUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        lock (_lock)
        {
            _users[user.UserId] = user;
        }
    }

    public ProfessionalUser? GetUser(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        lock (_lock)
        {
            return _users.GetValueOrDefault(userId);
        }
    }

    /// <summary>
    /// Creates a new approval request for an order. If the order amount is below the policy threshold,
    /// it auto-qualifies with status Approved. Otherwise, it enters PendingApproval requiring dual signatures.
    /// </summary>
    public ApprovalRequest CreateRequest(
        string requestId,
        string orderId,
        ProfessionalUser initiator,
        Money amount,
        string description,
        DateTimeOffset timestampUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(initiator);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (!initiator.IsActive)
        {
            throw new InvalidOperationException($"User '{initiator.UserId}' is deactivated and cannot initiate orders.");
        }

        if (initiator.Role is not (ProfessionalRole.Operator or ProfessionalRole.Administrator))
        {
            throw new InvalidOperationException($"User '{initiator.UserId}' with role '{initiator.Role}' is not authorized to stage or initiate orders.");
        }

        bool approvalRequired = Policy.IsApprovalRequired(amount);
        ApprovalStatus initialStatus = approvalRequired ? ApprovalStatus.PendingApproval : ApprovalStatus.Approved;
        int requiredApprovals = approvalRequired ? Policy.RequiredApproverCount : 0;

        string initiationToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{orderId}|{amount.Amount}|{amount.Currency}|{initiator.UserId}|{timestampUtc:O}"))).ToLowerInvariant();

        AuditLog.Append(
            timestampUtc,
            initiator.UserId,
            initiator.Role,
            "InitiateOrder",
            orderId,
            initiationToken);

        return new ApprovalRequest(
            requestId,
            orderId,
            initiator.UserId,
            amount,
            description,
            timestampUtc,
            initialStatus,
            requiredApprovals,
            Array.Empty<ApprovalDecision>());
    }

    /// <summary>
    /// Submits an approval or rejection decision from a qualified Approver.
    /// Enforces:
    /// - Non-terminal request status.
    /// - Active Approver or Administrator role.
    /// - Strict prohibition of self-approval (initiator cannot approve).
    /// - Distinct approvers (same approver cannot sign twice).
    /// </summary>
    public ApprovalRequest SubmitDecision(
        ApprovalRequest request,
        ProfessionalUser approver,
        ApprovalDecisionType decisionType,
        string signatureToken,
        string? comment,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(approver);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureToken);

        if (request.IsTerminal)
        {
            throw new InvalidOperationException($"Cannot submit decision against terminal approval request '{request.RequestId}' (status: {request.Status}).");
        }

        if (!approver.IsActive)
        {
            throw new InvalidOperationException($"Approver '{approver.UserId}' is deactivated.");
        }

        if (approver.Role is not (ProfessionalRole.Approver or ProfessionalRole.Administrator))
        {
            throw new InvalidOperationException($"User '{approver.UserId}' with role '{approver.Role}' is not authorized to submit approval decisions.");
        }

        if (Policy.RequireDistinctPrincipals && string.Equals(approver.UserId, request.InitiatorUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Self-approval is strictly forbidden under the four-eyes principle (initiator '{request.InitiatorUserId}' attempted to approve).");
        }

        if (request.Decisions.Any(d => string.Equals(d.ApproverUserId, approver.UserId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Approver '{approver.UserId}' has already submitted a decision for request '{request.RequestId}'.");
        }

        string decisionId = $"DEC-{Guid.NewGuid():N}"[..16];
        var decision = new ApprovalDecision(
            decisionId,
            approver.UserId,
            decisionType,
            timestampUtc,
            signatureToken,
            comment);

        var updatedDecisions = new List<ApprovalDecision>(request.Decisions) { decision };

        if (decisionType == ApprovalDecisionType.Reject)
        {
            AuditLog.Append(
                timestampUtc,
                approver.UserId,
                approver.Role,
                "RejectOrder",
                request.OrderId,
                signatureToken);

            return request with
            {
                Status = ApprovalStatus.Rejected,
                Decisions = updatedDecisions
            };
        }

        // Decision is Approve
        int approvedCount = updatedDecisions.Count(d => d.DecisionType == ApprovalDecisionType.Approve);
        bool quorumMet = approvedCount >= request.RequiredApprovals;

        string auditAction = quorumMet ? "SecondApproval" : "FirstApproval";
        AuditLog.Append(
            timestampUtc,
            approver.UserId,
            approver.Role,
            auditAction,
            request.OrderId,
            signatureToken);

        ApprovalStatus nextStatus = quorumMet ? ApprovalStatus.Approved : ApprovalStatus.PartiallyApproved;

        return request with
        {
            Status = nextStatus,
            Decisions = updatedDecisions
        };
    }

    /// <summary>
    /// Cancels a pending request prior to terminal execution.
    /// Only the original initiator or an Administrator can cancel.
    /// </summary>
    public ApprovalRequest CancelRequest(
        ApprovalRequest request,
        ProfessionalUser actor,
        string reason,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (request.IsTerminal)
        {
            throw new InvalidOperationException($"Cannot cancel terminal approval request '{request.RequestId}' (status: {request.Status}).");
        }

        if (!actor.IsActive)
        {
            throw new InvalidOperationException($"Actor '{actor.UserId}' is deactivated.");
        }

        bool isInitiator = string.Equals(actor.UserId, request.InitiatorUserId, StringComparison.OrdinalIgnoreCase);
        bool isAdmin = actor.Role == ProfessionalRole.Administrator;

        if (!isInitiator && !isAdmin)
        {
            throw new InvalidOperationException($"User '{actor.UserId}' is not authorized to cancel request '{request.RequestId}'. Only the initiator or an Administrator may cancel.");
        }

        string cancelToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{request.RequestId}|CANCEL|{actor.UserId}|{reason}|{timestampUtc:O}"))).ToLowerInvariant();

        AuditLog.Append(
            timestampUtc,
            actor.UserId,
            actor.Role,
            "CancelOrder",
            request.OrderId,
            cancelToken);

        return request with
        {
            Status = ApprovalStatus.Cancelled
        };
    }
}
