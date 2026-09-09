namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsCorrelationState { Ready, AwaitingResponse, Stopped }
public enum FinTsCorrelationStatus
{
    RequestRecorded, Matched, NoPendingRequest, RequestAlreadyPending, Stopped,
    RequestMessageMismatch, RequestDialogMismatch, UnexpectedRequestReference, UnsupportedSecurityWrapper,
    MissingMessageReference, BankMessageMismatch, RequestReferenceMismatch, DialogMismatch,
    InvalidAssignedDialog, UnknownSegmentReference, ConflictingResponse,
}

/// <summary>
/// Synchronized, in-memory mechanical correlation for one locally supplied request
/// at a time. A match is not authentication or banking success. This sends nothing.
/// </summary>
public sealed class FinTsDialogueCorrelation
{
    private readonly object _gate = new();
    private FinTsMessageFrame? _pending;
    private byte[]? _dialogId;
    private int _nextClientNumber = 1;
    private int _nextBankNumber = 1;
    private FinTsCorrelationState _state;

    public FinTsCorrelationState State { get { lock (_gate) { return _state; } } }

    public FinTsCorrelationStatus BeginRequest(FinTsMessageFrame request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_state == FinTsCorrelationState.Stopped) { return FinTsCorrelationStatus.Stopped; }
            if (_pending is not null) { return FinTsCorrelationStatus.RequestAlreadyPending; }
            var segments = request.Syntax.Segments;
            if (segments.Any(s => s.Code is "HNVSK" or "HNVSD")) { return FinTsCorrelationStatus.UnsupportedSecurityWrapper; }
            if (request.MessageNumber != _nextClientNumber) { return FinTsCorrelationStatus.RequestMessageMismatch; }
            var fields = segments[0].Fields;
            if (fields.Count == 5 && !(fields[4].Elements.Count == 1 && fields[4].Elements[0].IsEmpty)) { return FinTsCorrelationStatus.UnexpectedRequestReference; }
            byte[] dialog = fields[2].Elements[0].CopyValueBytes();
            if (!dialog.AsSpan().SequenceEqual(_dialogId is null ? "0"u8 : _dialogId)) { return FinTsCorrelationStatus.RequestDialogMismatch; }
            _pending = request;
            _state = FinTsCorrelationState.AwaitingResponse;
            return FinTsCorrelationStatus.RequestRecorded;
        }
    }

    public FinTsCorrelationStatus AcceptResponse(FinTsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            if (_state == FinTsCorrelationState.Stopped) { return FinTsCorrelationStatus.Stopped; }
            if (_pending is null) { return FinTsCorrelationStatus.NoPendingRequest; }
            // A rejected candidate never consumes the pending request or establishes a dialog ID.
            var frame = response.Frame;
            if (frame.MessageNumber != _nextBankNumber) { return FinTsCorrelationStatus.BankMessageMismatch; }
            var fields = frame.Syntax.Segments[0].Fields;
            if (fields.Count != 5 || fields[4].Elements.Count != 2) { return FinTsCorrelationStatus.MissingMessageReference; }
            if (FinTsSyntax.Number(fields[4].Elements[1], 4, allowZero: false) != _pending.MessageNumber) { return FinTsCorrelationStatus.RequestReferenceMismatch; }
            byte[] dialog = fields[2].Elements[0].CopyValueBytes();
            byte[] referenceDialog = fields[4].Elements[0].CopyValueBytes();
            if (!dialog.AsSpan().SequenceEqual(referenceDialog) || _dialogId is not null && !dialog.AsSpan().SequenceEqual(_dialogId))
            {
                return FinTsCorrelationStatus.DialogMismatch;
            }

            if (_dialogId is null && (dialog.AsSpan().SequenceEqual("0"u8) || dialog.AsSpan().SequenceEqual("unbekannt"u8)))
            {
                return FinTsCorrelationStatus.InvalidAssignedDialog;
            }

            HashSet<int> requestNumbers = _pending.Syntax.Segments.Select(s => s.Number).ToHashSet();
            foreach (FinTsSegment segment in response.BodySegments)
            {
                if (segment.Reference is int reference && !requestNumbers.Contains(reference)) { return FinTsCorrelationStatus.UnknownSegmentReference; }
            }

            if (response.HasConflictingClasses) { return FinTsCorrelationStatus.ConflictingResponse; }
            _dialogId ??= dialog;
            _nextClientNumber++;
            _nextBankNumber++;
            _pending = null;
            _state = _nextClientNumber > 9999 || _nextBankNumber > 9999 ? FinTsCorrelationState.Stopped : FinTsCorrelationState.Ready;
            return FinTsCorrelationStatus.Matched;
        }
    }

    /// <summary>Terminal local cancellation/timeout/transport-abandonment; no implicit retry.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _pending = null;
            _dialogId = null;
            _state = FinTsCorrelationState.Stopped;
        }
    }
}
