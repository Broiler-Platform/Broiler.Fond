using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Single-use process-2 continuation of an actual, explicitly selected synchronization HKTAN-4 request.</summary>
public sealed class FinTsSignedTextTanRequest
{
    public const int MaximumEncodedLength = 3072;
    private readonly long _started;
    private readonly TimeProvider _clock;
    private int _used;
    private FinTsSignedTextTanRequest(FinTsPinTanRequestBinding origin, FinTsTanChallengeEvidence challenge, FinTsPinTanSignatureHeader header, TimeProvider clock)
    { Origin = origin; Challenge = challenge; Header = header; _clock = clock; _started = clock.GetTimestamp(); }
    public FinTsPinTanRequestBinding Origin { get; }
    public FinTsTanChallengeEvidence Challenge { get; }
    public FinTsPinTanSignatureHeader Header { get; }
    public string DialogueId => Challenge.Response.Source.Frame.Syntax.Segments[0].Fields[2].Elements[0].HeaderText();
    public int MessageNumber => 2;
    public int ExpectedBankMessageNumber => 2;
    public bool IsCurrent
    {
        get
        {
            try { long now = _clock.GetTimestamp(); var elapsed = _clock.GetElapsedTime(_started, now); return now >= _started && elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromMinutes(2); }
            catch (Exception) { return false; }
        }
    }

    internal static FinTsSignedTextTanRequest Create(FinTsPinTanRequestBinding origin, FinTsResponse response,
        FinTsPinTanSignatureHeader header, TimeProvider clock, CancellationToken ct)
    {
        var selected = origin.SignatureEvidence.MatchingProcedure;
        if (selected is null || selected.ProcessVariant != "2" || selected.IsDecoupled || selected.MediumNameRequirement != "0" ||
            selected.HhdResponseRequired || selected.SmsAccountRequirement == "2" || selected.MaximumTanLength is null or 0 || selected.InputFormat is not ("1" or "2") ||
            !origin.Segments.Any(s => s.Role == FinTsPinTanRequestSegmentRole.TanOperation) || !FinTsPinTanResponseBinding.Evaluate(origin, response, ct).HasMatchingReferences)
            throw new FinTsCandidateException(FinTsCandidateFailure.UnsupportedSca);
        var parameters = FinTsParameterSet.Parse(response, ct);
        var procedures = FinTsTanParameterSet.Parse(parameters, ct);
        var permission = FinTsPermittedProcedureSet.Parse(response, ct);
        var returned = procedures.Advertisements.Where(a => a.Source.Version == selected.SegmentVersion).SelectMany(a => a.Procedures)
            .SingleOrDefault(p => p.SecurityFunction == selected.SecurityFunction);
        if (returned is null || returned.Elements.Count != selected.Elements.Count || returned.Elements.Zip(selected.Elements).Any(p =>
            !p.First.CopyValueBytes().AsSpan().SequenceEqual(p.Second.CopyValueBytes())))
            throw new FinTsCandidateException(FinTsCandidateFailure.UnqualifiedEvidence);
        var originalHeader = origin.SignatureEvidence.Header;
        var envelope = response.PinTanEnvelope!;
        var identity = envelope.SecurityHeader.Fields[6].Elements;
        if (identity[0].HeaderText() != originalHeader.CountryCode || identity[1].HeaderText() != originalHeader.InstitutionId || identity[2].HeaderText() != originalHeader.UserId ||
            parameters.User is { } user && user.UserId.HeaderText() != originalHeader.UserId ||
            parameters.Bank is { } bank && (bank.Source.Fields[1].Elements[0].HeaderText() != originalHeader.CountryCode || bank.Source.Fields[1].Elements[1].HeaderText() != originalHeader.InstitutionId) ||
            header.ProfileVersion != 2 || header.SecurityFunction != originalHeader.SecurityFunction || header.CountryCode != originalHeader.CountryCode ||
            header.InstitutionId != originalHeader.InstitutionId || header.UserId != originalHeader.UserId || header.SystemId != originalHeader.SystemId ||
            header.SecurityReferenceNumber != 2 || header.ControlReference == originalHeader.ControlReference)
            throw new FinTsCandidateException(FinTsCandidateFailure.IdentityMismatch);
        var evidence = FinTsTanChallengeEvidence.Evaluate(FinTsTanRequestContext.ForSynchronization(origin, ct), procedures, returned, permission, FinTsTanChallengeSet.Parse(response, ct), ct);
        var challenge = evidence.Challenge;
        string text = challenge?.Text?.HeaderText() ?? "";
        if (evidence.Issues != 0 || evidence.Observation != FinTsTanOutcomeObservation.ChallengeReported || challenge is null ||
            challenge.OrderReference is null || challenge.HasNoReferencePlaceholder || challenge.HhdData is { IsEmpty: false } || text.Length == 0 || text.Contains('<') || text.Contains('>'))
            throw new FinTsCandidateException(FinTsCandidateFailure.UnsupportedSca);
        if (clock.TimestampFrequency <= 0) throw new FinTsCandidateException(FinTsCandidateFailure.UnqualifiedEvidence);
        return new(origin, evidence, header, clock);
    }

    public FinTsPinTanRequestWriteResult TryEncode(FinTsSessionCredential pin, FinTsSessionCredential tan, Span<byte> destination,
        out int written, CancellationToken cancellationToken = default)
    {
        written = 0;
        ArgumentNullException.ThrowIfNull(pin); ArgumentNullException.ThrowIfNull(tan);
        if (destination.Length < MaximumEncodedLength) return FinTsPinTanRequestWriteResult.DestinationTooSmall;
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent || Interlocked.CompareExchange(ref _used, 1, 0) != 0) return FinTsPinTanRequestWriteResult.ContextNeedsReview;
        Span<byte> body = stackalloc byte[2048]; body.Clear();
        Span<byte> wire = stackalloc byte[MaximumEncodedLength]; wire.Clear();
        try
        {
            var publicBody = new StringBuilder();
            FinTsPinTanRequestWriter.AppendPublicSegment(publicBody, Header.Source, 2);
            publicBody.Append("HKTAN:3:").Append(Challenge.Procedure.SegmentVersion.ToString(CultureInfo.InvariantCulture)).Append("+2++++")
                .Append(FinTsUnsignedWireEncoding.Text(Challenge.Challenge!.OrderReference!.HeaderText(), 35, FinTsSyntaxError.InvalidTanContext)).Append("+N'");
            int length = Encoding.Latin1.GetBytes(publicBody.ToString(), body);
            var result = FinTsPinTanSignatureTrailerWriter.TryEncodeTextTan(this, pin, tan, body[length..], out int trailerLength, cancellationToken);
            if (result != FinTsSignatureTrailerWriteResult.Written) return result == FinTsSignatureTrailerWriteResult.CredentialUnavailable
                ? FinTsPinTanRequestWriteResult.CredentialUnavailable : FinTsPinTanRequestWriteResult.CredentialRequirementsNeedReview;
            length += trailerLength;
            string dialogue = FinTsUnsignedWireEncoding.Text(DialogueId, 30, FinTsSyntaxError.InvalidTanContext);
            string framing = $"HNHBK:1:3+000000000000+300+{dialogue}+2'";
            int position = Encoding.Latin1.GetBytes(FinTsPinTanRequestEnvelopeWriter.PublicPrefix(Header, framing), wire);
            position += Encoding.Latin1.GetBytes(length.ToString(CultureInfo.InvariantCulture), wire[position..]);
            wire[position++] = (byte)'@'; body[..length].CopyTo(wire[position..]); position += length; wire[position++] = (byte)'\'';
            position += Encoding.Latin1.GetBytes("HNHBS:5:1+2'", wire[position..]);
            int size = position; for (int i = 21; i >= 10; i--) { wire[i] = (byte)('0' + size % 10); size /= 10; }
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrent || pin.GetSnapshot().State != FinTsCredentialState.Available) return FinTsPinTanRequestWriteResult.CredentialUnavailable;
            wire[..position].CopyTo(destination); written = position; return FinTsPinTanRequestWriteResult.Written;
        }
        catch (OperationCanceledException) { pin.Cancel(); tan.Cancel(); throw; }
        finally { CryptographicOperations.ZeroMemory(body); CryptographicOperations.ZeroMemory(wire); }
    }
}
