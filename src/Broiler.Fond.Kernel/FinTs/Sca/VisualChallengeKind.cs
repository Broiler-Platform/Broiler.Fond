namespace Broiler.Fond.Kernel.FinTs.Sca;

/// <summary>
/// Categorization of visual and optical TAN challenge payloads in FinTS 3.0.
/// </summary>
public enum VisualChallengeKind
{
    /// <summary>No visual challenge payload is present.</summary>
    None,

    /// <summary>Informational text-only challenge (e.g. SMS TAN text or mobileTAN notice).</summary>
    PlainText,

    /// <summary>Optical flicker code (chipTAN optisch / HHD 1.3 / HHD 1.4 alternating visual bars).</summary>
    FlickerCode,

    /// <summary>Matrix code or photoTAN rendered as a PNG, JPEG, or GIF image (binary or base64 data URI).</summary>
    MatrixCodeImage,

    /// <summary>Raw 2D matrix or photoTAN binary payload without image framing.</summary>
    MatrixCodeRaw,

    /// <summary>QR-Code text payload (e.g. EPC GiroCode or authentication URL).</summary>
    QrCodeText,
}
