using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

public sealed class FinTsPermittedProcedureReport
{
    internal FinTsPermittedProcedureReport(FinTsReplySegment segment, FinTsReply reply, List<int> functions)
    {
        Segment = segment;
        Reply = reply;
        SecurityFunctions = functions.AsReadOnly();
    }
    public FinTsReplySegment Segment { get; }
    public FinTsReply Reply { get; }
    public ReadOnlyCollection<int> SecurityFunctions { get; }
    public bool HasDuplicates => SecurityFunctions.Distinct().Count() != SecurityFunctions.Count;
}

/// <summary>HIRMS 3920 reports only. An aborted dialogue can still report procedures; this grants no permission.</summary>
public sealed class FinTsPermittedProcedureSet
{
    public const int MaximumReports = 128;
    private FinTsPermittedProcedureSet(FinTsResponse source, List<FinTsPermittedProcedureReport> reports)
    {
        Source = source;
        Reports = reports.AsReadOnly();
    }
    public FinTsResponse Source { get; }
    public ReadOnlyCollection<FinTsPermittedProcedureReport> Reports { get; }
    public bool IsAmbiguous => Reports.Count > 1 || Reports.Any(r => r.HasDuplicates);

    public static FinTsPermittedProcedureSet Parse(FinTsResponse source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<FinTsPermittedProcedureReport> reports = [];
        foreach (var segment in source.ReplySegments)
        {
            foreach (var reply in segment.Replies.Where(r => r.Code == "3920"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reports.Count == MaximumReports) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
                if (segment.IsMessageLevel || !reply.ElementReference.IsEmpty) { throw Invalid(); }
                List<int> functions = [];
                bool omitted = false;
                foreach (var parameter in reply.Parameters)
                {
                    if (parameter.IsEmpty) { omitted = true; continue; }
                    string value = parameter.HeaderText();
                    if (omitted || value.Length != 3 || value.Any(c => c is < '0' or > '9')) { throw Invalid(); }
                    int function = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (function is < 900 or > 999 or 998) { throw Invalid(); }
                    functions.Add(function);
                }
                if (functions.Count == 0) { throw Invalid(); }
                reports.Add(new(segment, reply, functions));
            }
        }
        return new(source, reports);
    }
    private static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidTanContext);
}
