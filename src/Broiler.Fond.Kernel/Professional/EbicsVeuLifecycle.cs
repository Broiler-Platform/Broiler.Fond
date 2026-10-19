using System.Text;
using System.Xml;
using Broiler.Fond.Kernel.Domain;

namespace Broiler.Fond.Kernel.Professional;

/// <summary>
/// EBICS VEU (Verteilte Elektronische Unterschrift / Distributed Electronic Signature) signature classes.
/// </summary>
public enum VeuSignatureClass
{
    /// <summary>Transport signature (T) — authenticates transmission only, no payment authorization</summary>
    Transport = 'T',

    /// <summary>Single authorization signature (E / Einzelunterschrift) — unilateral authorization</summary>
    SingleAuthorization = 'E',

    /// <summary>First authorization signature (A / Erstunterschrift) — requires distinct second signature</summary>
    FirstSignature = 'A',

    /// <summary>Second authorization signature (B / Zweitunterschrift) — completes dual authorization</summary>
    SecondSignature = 'B'
}

public static class VeuSignatureClassExtensions
{
    public static string ToCode(this VeuSignatureClass sigClass) => ((char)sigClass).ToString();

    public static VeuSignatureClass ParseVeuClass(string code) => code?.Trim().ToUpperInvariant() switch
    {
        "T" => VeuSignatureClass.Transport,
        "E" => VeuSignatureClass.SingleAuthorization,
        "A" => VeuSignatureClass.FirstSignature,
        "B" => VeuSignatureClass.SecondSignature,
        _ => throw new ArgumentException($"Unknown or unsupported VEU signature class: '{code}'", nameof(code))
    };
}

/// <summary>
/// Lifecycle state of a Distributed Electronic Signature (VEU) order.
/// </summary>
public enum VeuOrderState
{
    /// <summary>Uploaded with transport signature, pending initial authorization</summary>
    StagedWithTransportSignature = 0,

    /// <summary>Awaiting first signature (A)</summary>
    PendingFirstSignature = 1,

    /// <summary>Received first signature (A), awaiting distinct second signature (B)</summary>
    PendingSecondSignature = 2,

    /// <summary>Quorum met (both A+B, or single E)</summary>
    CompletelySigned = 3,

    /// <summary>Released and executed by the banking institution</summary>
    Released = 4,

    /// <summary>Terminal rejection by an authorized signatory</summary>
    Rejected = 5,

    /// <summary>Terminal revocation by the submitter or administrator</summary>
    Cancelled = 6
}

/// <summary>
/// Required signature scheme for a corporate payment order.
/// </summary>
public enum VeuSignatureScheme
{
    /// <summary>Requires dual authorization (First Signature A + distinct Second Signature B)</summary>
    DualSignatureAB = 0,

    /// <summary>Allows single executive authorization (Signature E)</summary>
    SingleSignatureE = 1
}

/// <summary>
/// EBICS VEU protocol order types for querying and signing pending orders.
/// </summary>
public enum VeuOrderType
{
    /// <summary>Query overview list of pending VEU orders requiring signatures</summary>
    HVZ = 0,

    /// <summary>Download order details and payload digest for a specific pending order</summary>
    HVD = 1,

    /// <summary>Submit partial electronic signature (e.g. A signature)</summary>
    HVE = 2,

    /// <summary>Submit final release electronic signature (e.g. B or E signature)</summary>
    HVS = 3,

    /// <summary>Download transaction status log / signature history for an order</summary>
    HVT = 4
}

public static class VeuOrderTypeExtensions
{
    public static string ToOrderCode(this VeuOrderType orderType) => orderType switch
    {
        VeuOrderType.HVZ => "HVZ",
        VeuOrderType.HVD => "HVD",
        VeuOrderType.HVE => "HVE",
        VeuOrderType.HVS => "HVS",
        VeuOrderType.HVT => "HVT",
        _ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, null)
    };

    public static VeuOrderType ParseVeuOrderType(string code) => code?.Trim().ToUpperInvariant() switch
    {
        "HVZ" => VeuOrderType.HVZ,
        "HVD" => VeuOrderType.HVD,
        "HVE" => VeuOrderType.HVE,
        "HVS" => VeuOrderType.HVS,
        "HVT" => VeuOrderType.HVT,
        _ => throw new ArgumentException($"Unknown or unsupported VEU order type: '{code}'", nameof(code))
    };
}

/// <summary>
/// An immutable digital signature record applied to a VEU order.
/// </summary>
public sealed record VeuSignature(
    string UserId,
    VeuSignatureClass SignatureClass,
    string SignedDigest,
    string SignatureValueBase64,
    DateTimeOffset TimestampUtc);

/// <summary>
/// An immutable VEU corporate order undergoing asynchronous, multi-actor authorization.
/// </summary>
public sealed record VeuOrder
{
    public string OrderId { get; }
    public string OrderType { get; }
    public string PartnerId { get; }
    public Money OrderAmount { get; }
    public string SubmitterUserId { get; }
    public DateTimeOffset SubmissionTimeUtc { get; }
    public VeuOrderState CurrentState { get; init; }
    public VeuSignatureScheme RequiredSignatureScheme { get; }
    public IReadOnlyList<VeuSignature> Signatures { get; init; }

    public bool IsTerminal => CurrentState is VeuOrderState.CompletelySigned or VeuOrderState.Released or VeuOrderState.Rejected or VeuOrderState.Cancelled;

    public VeuOrder(
        string orderId,
        string orderType,
        string partnerId,
        Money orderAmount,
        string submitterUserId,
        DateTimeOffset submissionTimeUtc,
        VeuOrderState currentState,
        VeuSignatureScheme requiredSignatureScheme,
        IEnumerable<VeuSignature>? signatures = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderType);
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerId);
        ArgumentNullException.ThrowIfNull(orderAmount);
        ArgumentException.ThrowIfNullOrWhiteSpace(submitterUserId);

        OrderId = orderId.Trim();
        OrderType = orderType.Trim().ToUpperInvariant();
        PartnerId = partnerId.Trim();
        OrderAmount = orderAmount;
        SubmitterUserId = submitterUserId.Trim();
        SubmissionTimeUtc = submissionTimeUtc;
        CurrentState = currentState;
        RequiredSignatureScheme = requiredSignatureScheme;
        Signatures = signatures?.ToArray() ?? Array.Empty<VeuSignature>();
    }
}

/// <summary>
/// Coordinator enforcing the EBICS VEU distributed signature lifecycle, distinct signatories invariant,
/// and terminal state protection.
/// </summary>
public sealed class EbicsVeuCoordinator
{
    /// <summary>
    /// Creates a newly staged VEU order awaiting electronic signatures.
    /// </summary>
    public VeuOrder StageOrder(
        string orderId,
        string orderType,
        string partnerId,
        Money orderAmount,
        ProfessionalUser submitter,
        VeuSignatureScheme signatureScheme,
        string transportSignatureBase64,
        string orderDigest,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(submitter);
        ArgumentException.ThrowIfNullOrWhiteSpace(transportSignatureBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderDigest);

        if (!submitter.IsActive)
        {
            throw new InvalidOperationException($"Submitter '{submitter.UserId}' is deactivated.");
        }

        if (submitter.Role is not (ProfessionalRole.Operator or ProfessionalRole.Administrator))
        {
            throw new InvalidOperationException($"User '{submitter.UserId}' with role '{submitter.Role}' cannot stage VEU orders.");
        }

        var transportSig = new VeuSignature(
            submitter.UserId,
            VeuSignatureClass.Transport,
            orderDigest,
            transportSignatureBase64,
            timestampUtc);

        return new VeuOrder(
            orderId,
            orderType,
            partnerId,
            orderAmount,
            submitter.UserId,
            timestampUtc,
            VeuOrderState.PendingFirstSignature,
            signatureScheme,
            [transportSig]);
    }

    /// <summary>
    /// Applies an electronic signature (A, B, or E) to a pending VEU order.
    /// Enforces:
    /// - Non-terminal state.
    /// - Distinct signatories rule: same user cannot sign both A and B.
    /// - Quorum progression.
    /// </summary>
    public VeuOrder ApplySignature(
        VeuOrder order,
        ProfessionalUser signatory,
        VeuSignatureClass signatureClass,
        string orderDigest,
        string signatureValueBase64,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(signatory);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(signatureValueBase64);

        if (order.IsTerminal)
        {
            throw new InvalidOperationException($"Cannot apply signature to terminal VEU order '{order.OrderId}' (state: {order.CurrentState}).");
        }

        if (!signatory.IsActive)
        {
            throw new InvalidOperationException($"Signatory '{signatory.UserId}' is deactivated.");
        }

        if (signatory.Role is not (ProfessionalRole.Approver or ProfessionalRole.Administrator))
        {
            throw new InvalidOperationException($"User '{signatory.UserId}' with role '{signatory.Role}' is not authorized to sign VEU orders.");
        }

        if (signatureClass == VeuSignatureClass.Transport)
        {
            throw new InvalidOperationException("Transport signatures (T) cannot be applied as authorization signatures.");
        }

        // Single Authorization (E)
        if (signatureClass == VeuSignatureClass.SingleAuthorization)
        {
            if (order.RequiredSignatureScheme != VeuSignatureScheme.SingleSignatureE)
            {
                throw new InvalidOperationException($"Order '{order.OrderId}' requires dual authorization (A+B) and cannot be released with single authorization (E).");
            }

            var singleSig = new VeuSignature(signatory.UserId, VeuSignatureClass.SingleAuthorization, orderDigest, signatureValueBase64, timestampUtc);
            var updatedSignatures = new List<VeuSignature>(order.Signatures) { singleSig };

            return order with
            {
                CurrentState = VeuOrderState.CompletelySigned,
                Signatures = updatedSignatures
            };
        }

        // Dual authorization: First signature (A)
        if (signatureClass == VeuSignatureClass.FirstSignature)
        {
            if (order.CurrentState != VeuOrderState.PendingFirstSignature && order.CurrentState != VeuOrderState.StagedWithTransportSignature)
            {
                throw new InvalidOperationException($"Order '{order.OrderId}' is not in PendingFirstSignature state (current: {order.CurrentState}).");
            }

            var firstSig = new VeuSignature(signatory.UserId, VeuSignatureClass.FirstSignature, orderDigest, signatureValueBase64, timestampUtc);
            var updatedSignatures = new List<VeuSignature>(order.Signatures) { firstSig };

            return order with
            {
                CurrentState = VeuOrderState.PendingSecondSignature,
                Signatures = updatedSignatures
            };
        }

        // Dual authorization: Second signature (B)
        if (signatureClass == VeuSignatureClass.SecondSignature)
        {
            if (order.CurrentState != VeuOrderState.PendingSecondSignature)
            {
                throw new InvalidOperationException($"Order '{order.OrderId}' is not in PendingSecondSignature state (current: {order.CurrentState}). First signature (A) must be applied before second signature (B).");
            }

            // Strict distinct principal enforcement: first signatory cannot provide second signature
            var firstSignature = order.Signatures.FirstOrDefault(s => s.SignatureClass == VeuSignatureClass.FirstSignature);
            if (firstSignature != null && string.Equals(firstSignature.UserId, signatory.UserId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The same signatory ('{signatory.UserId}') cannot provide both the first and second signature for VEU order '{order.OrderId}' under the four-eyes principle.");
            }

            var secondSig = new VeuSignature(signatory.UserId, VeuSignatureClass.SecondSignature, orderDigest, signatureValueBase64, timestampUtc);
            var updatedSignatures = new List<VeuSignature>(order.Signatures) { secondSig };

            return order with
            {
                CurrentState = VeuOrderState.CompletelySigned,
                Signatures = updatedSignatures
            };
        }

        throw new ArgumentOutOfRangeException(nameof(signatureClass), signatureClass, "Unhandled VEU signature class.");
    }

    /// <summary>
    /// Explicitly rejects a pending VEU order, transitioning to terminal Rejected state.
    /// </summary>
    public VeuOrder RejectOrder(
        VeuOrder order,
        ProfessionalUser actor,
        string reason,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (order.IsTerminal)
        {
            throw new InvalidOperationException($"Cannot reject terminal VEU order '{order.OrderId}' (state: {order.CurrentState}).");
        }

        if (!actor.IsActive)
        {
            throw new InvalidOperationException($"Actor '{actor.UserId}' is deactivated.");
        }

        if (actor.Role is not (ProfessionalRole.Approver or ProfessionalRole.Administrator))
        {
            throw new InvalidOperationException($"User '{actor.UserId}' with role '{actor.Role}' cannot reject VEU orders.");
        }

        return order with
        {
            CurrentState = VeuOrderState.Rejected
        };
    }

    /// <summary>
    /// Cancels a pending VEU order by the submitter or an administrator.
    /// </summary>
    public VeuOrder CancelOrder(
        VeuOrder order,
        ProfessionalUser actor,
        string reason,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (order.IsTerminal)
        {
            throw new InvalidOperationException($"Cannot cancel terminal VEU order '{order.OrderId}' (state: {order.CurrentState}).");
        }

        bool isSubmitter = string.Equals(actor.UserId, order.SubmitterUserId, StringComparison.OrdinalIgnoreCase);
        bool isAdmin = actor.Role == ProfessionalRole.Administrator;

        if (!isSubmitter && !isAdmin)
        {
            throw new InvalidOperationException($"User '{actor.UserId}' is not authorized to cancel VEU order '{order.OrderId}'. Only submitter or Administrator may cancel.");
        }

        return order with
        {
            CurrentState = VeuOrderState.Cancelled
        };
    }

    /// <summary>
    /// Confirms that a completely signed order was successfully released and executed by the bank.
    /// </summary>
    public VeuOrder ConfirmRelease(VeuOrder order, DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.CurrentState != VeuOrderState.CompletelySigned)
        {
            throw new InvalidOperationException($"Cannot confirm release for order '{order.OrderId}' in state '{order.CurrentState}'. Order must be CompletelySigned.");
        }

        return order with
        {
            CurrentState = VeuOrderState.Released
        };
    }

    /// <summary>
    /// Builds an EBICS XML request envelope to query the list of pending VEU orders (HVZ).
    /// </summary>
    public string BuildHvzQueryRequest(EbicsHostConfig hostConfig, DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(hostConfig);

        string ns = EbicsMessageCodec.GetNamespaceUri(hostConfig.Version);
        var settings = new XmlWriterSettings { Encoding = Encoding.UTF8, OmitXmlDeclaration = false, Indent = false };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("ebicsRequest", ns);
            writer.WriteAttributeString("version", hostConfig.Version == EbicsVersion.H005 ? "H005" : "H004");
            writer.WriteAttributeString("revision", "1");

            writer.WriteStartElement("header");
            writer.WriteAttributeString("authenticate", "true");

            writer.WriteStartElement("staticHeader");
            writer.WriteElementString("HostID", hostConfig.HostId);
            writer.WriteElementString("PartnerID", hostConfig.PartnerId);
            writer.WriteElementString("UserID", hostConfig.UserId);

            writer.WriteStartElement("OrderDetails");
            writer.WriteElementString("OrderType", "HVZ");
            writer.WriteElementString("OrderAttribute", "DZHNN");
            writer.WriteEndElement(); // OrderDetails

            writer.WriteEndElement(); // staticHeader

            writer.WriteStartElement("mutableHeader");
            writer.WriteElementString("Timestamp", timestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WriteEndElement(); // mutableHeader

            writer.WriteEndElement(); // header

            writer.WriteStartElement("body");
            writer.WriteEndElement(); // body

            writer.WriteEndElement(); // ebicsRequest
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }
}
