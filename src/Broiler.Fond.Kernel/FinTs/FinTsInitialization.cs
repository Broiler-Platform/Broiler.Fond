using System.Globalization;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsCustomerSystemStatus { NotRequired = 0, Required = 1 }
public enum FinTsDialogueLanguage { Standard = 0, German = 1, English = 2, French = 3 }

/// <summary>Untrusted HKIDN-2 fields. No authenticated identity or security-profile selection.</summary>
public sealed class FinTsInitializationIdentification
{
    public const string AnonymousCustomerId = "9999999999";
    private FinTsInitializationIdentification(FinTsSegment source)
    {
        Source = source;
        if (source.Fields.Count != 4 || source.Fields[0].Elements.Count != 2) { throw InitializationFields.Invalid(); }
        Country = InitializationFields.Text(source.Fields[0].Elements[0], 3);
        if (Country.Length != 3 || Country.Any(c => c is < '0' or > '9')) { throw InitializationFields.Invalid(); }
        Institution = InitializationFields.Text(source.Fields[0].Elements[1], 30);
        CustomerId = InitializationFields.Scalar(source.Fields[1], 30);
        SystemId = InitializationFields.Scalar(source.Fields[2], 30);
        SystemStatus = InitializationFields.Scalar(source.Fields[3], 1) switch
        { "0" => FinTsCustomerSystemStatus.NotRequired, "1" => FinTsCustomerSystemStatus.Required, _ => throw InitializationFields.Invalid() };
        if (IsAnonymous && (SystemId != "0" || SystemStatus != FinTsCustomerSystemStatus.NotRequired)) { throw InitializationFields.Invalid(); }
    }
    public FinTsSegment Source { get; }
    public string Country { get; }
    public string Institution { get; }
    public string CustomerId { get; }
    public string SystemId { get; }
    public FinTsCustomerSystemStatus SystemStatus { get; }
    /// <summary>The prescribed anonymous wire marker only; grants no capability.</summary>
    public bool IsAnonymous => CustomerId == AnonymousCustomerId;
    public static FinTsInitializationIdentification Parse(FinTsSegment source, CancellationToken cancellationToken = default)
    {
        InitializationFields.Header(source, "HKIDN", 2, cancellationToken);
        return new(source);
    }
}

/// <summary>Untrusted HKVVB-3 fields. Product text is not evidence of registration.</summary>
public sealed class FinTsInitializationPreparation
{
    private FinTsInitializationPreparation(FinTsSegment source)
    {
        Source = source;
        if (source.Fields.Count != 5) { throw InitializationFields.Invalid(); }
        BankParameterVersion = InitializationFields.Number(source.Fields[0]);
        UserParameterVersion = InitializationFields.Number(source.Fields[1]);
        Language = InitializationFields.Scalar(source.Fields[2], 3) switch
        {
            "0" => FinTsDialogueLanguage.Standard,
            "1" => FinTsDialogueLanguage.German,
            "2" => FinTsDialogueLanguage.English,
            "3" => FinTsDialogueLanguage.French,
            _ => throw InitializationFields.Invalid(),
        };
        ProductIdentifier = InitializationFields.Scalar(source.Fields[3], 25);
        ProductVersion = InitializationFields.Scalar(source.Fields[4], 5);
    }
    public FinTsSegment Source { get; }
    public int BankParameterVersion { get; }
    public int UserParameterVersion { get; }
    public FinTsDialogueLanguage Language { get; }
    public string ProductIdentifier { get; }
    public string ProductVersion { get; }
    public static FinTsInitializationPreparation Parse(FinTsSegment source, CancellationToken cancellationToken = default)
    {
        InitializationFields.Header(source, "HKVVB", 3, cancellationToken);
        return new(source);
    }
}

/// <summary>Restricted unsigned initialization observation, never an authenticated session.</summary>
public sealed class FinTsUnsignedInitializationRequest
{
    private FinTsUnsignedInitializationRequest(FinTsMessageFrame frame, FinTsInitializationIdentification identification, FinTsInitializationPreparation preparation)
    { Frame = frame; Identification = identification; Preparation = preparation; }
    public FinTsMessageFrame Frame { get; }
    public FinTsInitializationIdentification Identification { get; }
    public FinTsInitializationPreparation Preparation { get; }
    public static FinTsUnsignedInitializationRequest Parse(FinTsMessageFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame); cancellationToken.ThrowIfCancellationRequested();
        var segments = frame.Syntax.Segments;
        if (segments.Any(s => s.Code is "HNVSK" or "HNVSD" or "HNSHK" or "HNSHA")) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedSecurityWrapper); }
        if (segments.Count != 4 || frame.MessageNumber != 1 || segments[0].Fields[2].Elements[0].HeaderText() != "0" ||
            segments[0].Fields.Count == 5 && !ParameterFields.Empty(segments[0].Fields[4])) { throw InitializationFields.Invalid(); }
        return new(frame, FinTsInitializationIdentification.Parse(segments[1], cancellationToken), FinTsInitializationPreparation.Parse(segments[2], cancellationToken));
    }
}

/// <summary>Immutable caller input. Encoding validates fields; no product or institution identity is inferred.</summary>
public sealed class FinTsInitializationInput
{
    public FinTsInitializationInput(string country, string institution, string customerId, string systemId, FinTsCustomerSystemStatus systemStatus,
        int bankParameterVersion, int userParameterVersion, FinTsDialogueLanguage language, string productIdentifier, string productVersion)
    {
        ArgumentNullException.ThrowIfNull(country); ArgumentNullException.ThrowIfNull(institution);
        ArgumentNullException.ThrowIfNull(customerId); ArgumentNullException.ThrowIfNull(systemId);
        ArgumentNullException.ThrowIfNull(productIdentifier); ArgumentNullException.ThrowIfNull(productVersion);
        Country = country; Institution = institution; CustomerId = customerId; SystemId = systemId; SystemStatus = systemStatus;
        BankParameterVersion = bankParameterVersion; UserParameterVersion = userParameterVersion; Language = language;
        ProductIdentifier = productIdentifier; ProductVersion = productVersion;
    }
    public string Country { get; }
    public string Institution { get; }
    public string CustomerId { get; }
    public string SystemId { get; }
    public FinTsCustomerSystemStatus SystemStatus { get; }
    public int BankParameterVersion { get; }
    public int UserParameterVersion { get; }
    public FinTsDialogueLanguage Language { get; }
    public string ProductIdentifier { get; }
    public string ProductVersion { get; }
}

/// <summary>Local unsigned initialization encoding only. No credentials, registration, synchronization, authentication or sending.</summary>
public static class FinTsUnsignedInitializationWriter
{
    public static byte[] EncodeAnonymous(string country, string institution, string productIdentifier, string productVersion,
        int bankParameterVersion = 0, int userParameterVersion = 0, FinTsDialogueLanguage language = FinTsDialogueLanguage.Standard,
        CancellationToken cancellationToken = default) => Encode(new(country, institution, FinTsInitializationIdentification.AnonymousCustomerId, "0",
            FinTsCustomerSystemStatus.NotRequired, bankParameterVersion, userParameterVersion, language, productIdentifier, productVersion), cancellationToken);

    public static byte[] Encode(FinTsInitializationInput input, CancellationToken cancellationToken = default)
    {
        var body = CreateBody(input, cancellationToken);
        byte[] wire = FinTsUnsignedWireEncoding.Frame("0", 1, body, 4);
        _ = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(wire, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return wire;
    }

    internal static StringBuilder CreateBody(FinTsInitializationInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
        if (input.BankParameterVersion is < 0 or > 999 || input.UserParameterVersion is < 0 or > 999 ||
            input.SystemStatus is not (FinTsCustomerSystemStatus.NotRequired or FinTsCustomerSystemStatus.Required) ||
            input.Language is < FinTsDialogueLanguage.Standard or > FinTsDialogueLanguage.French) { throw InitializationFields.Invalid(); }
        string Text(string value, int length) => FinTsUnsignedWireEncoding.Text(value, length, FinTsSyntaxError.InvalidInitialization);
        string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        return new StringBuilder("HKIDN:2:2+").Append(Text(input.Country, 3)).Append(':').Append(Text(input.Institution, 30))
            .Append('+').Append(Text(input.CustomerId, 30)).Append('+').Append(Text(input.SystemId, 30)).Append('+').Append(Number((int)input.SystemStatus)).Append('\'')
            .Append("HKVVB:3:3+").Append(Number(input.BankParameterVersion)).Append('+').Append(Number(input.UserParameterVersion))
            .Append('+').Append(Number((int)input.Language)).Append('+').Append(Text(input.ProductIdentifier, 25)).Append('+').Append(Text(input.ProductVersion, 5)).Append('\'');
    }
}

internal static class InitializationFields
{
    internal static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidInitialization);
    internal static void Header(FinTsSegment source, string code, int version, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source); token.ThrowIfCancellationRequested();
        if (source.Code != code || source.Reference is not null) { throw Invalid(); }
        if (source.Version != version) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedInitializationVersion); }
    }
    internal static string Text(FinTsDataElement element, int maximum)
    {
        if (element.IsBinary) { throw Invalid(); }
        string text = element.HeaderText();
        if (text.Length == 0 || text.Length > maximum || text[0] == ' ' || text[^1] == ' ' ||
            text.Any(c => c < 32 || c is >= (char)127 and <= (char)160)) { throw Invalid(); }
        return text;
    }
    internal static string Scalar(FinTsField field, int maximum)
    {
        if (field.Elements.Count != 1) { throw Invalid(); }
        return Text(field.Elements[0], maximum);
    }
    internal static int Number(FinTsField field)
    {
        string value = Scalar(field, 3);
        if (value.Any(c => c is < '0' or > '9') || value.Length > 1 && value[0] == '0') { throw Invalid(); }
        return int.Parse(value, CultureInfo.InvariantCulture);
    }
}
