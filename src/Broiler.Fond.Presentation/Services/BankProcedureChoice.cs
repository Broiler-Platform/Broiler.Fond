using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Presentation.Services;

public sealed record BankProcedureOption(string Id, string Name, bool CanSelect, string Explanation,
    int ProfileVersion, int SecurityFunction, int TanSegmentVersion);
public sealed record BankProcedureChoiceRequest(string BankName, string Endpoint, IReadOnlyList<BankProcedureOption> Options);
public interface IBankProcedureChoice
{
    Task<string?> ChooseBankProcedureAsync(BankProcedureChoiceRequest request, CancellationToken cancellationToken = default);
}

internal static class BankProcedureChoices
{
    internal static bool Matches(FinTsBankParameterObservation observation, FinTsPinTanProcedureContext identified, BankProcedureOption option)
    {
        if (option.ProfileVersion == 1) return true; // The production factory separately requires returned one-step permission.
        var original = observation.Tan.Advertisements.Single(a => a.Source.Version == option.TanSegmentVersion).Procedures.Single(p => p.SecurityFunction == option.SecurityFunction);
        var current = identified.Parameters.Advertisements.Single(a => a.Source.Version == option.TanSegmentVersion).Procedures.Single(p => p.SecurityFunction == option.SecurityFunction);
        return original.Elements.Count == current.Elements.Count && original.Elements.Zip(current.Elements).All(pair =>
            pair.First.CopyValueBytes().AsSpan().SequenceEqual(pair.Second.CopyValueBytes()));
    }
    internal static IReadOnlyList<BankProcedureOption> Create(FinTsBankParameterObservation observation)
    {
        var options = new List<BankProcedureOption>();
        foreach (var advertisement in observation.Tan.Advertisements)
        {
            bool usable = advertisement.MaximumOrders >= 1 && advertisement.MinimumSignatures <= 1;
            int version = advertisement.Source.Version;
            if (advertisement.OneStepReportedAllowed)
                options.Add(new($"{version}:999", "One-step PIN/TAN", usable,
                    "Bank-advertised one-step procedure. Login-specific permission must still be checked.", 1, 999, version));
            foreach (var procedure in advertisement.Procedures)
            {
                // Media requiring selection must wait for an actual user-scoped media inventory; never invent a device name.
                bool media = procedure.MediumNameRequirement != "0";
                bool supportedTextTan = !media && procedure.ProcessVariant == "2" && !procedure.IsDecoupled &&
                    !procedure.HhdResponseRequired && procedure.SmsAccountRequirement != "2" &&
                    procedure.MaximumTanLength is > 0 && procedure.InputFormat is "1" or "2";
                string name = System.Text.Encoding.Latin1.GetString(procedure.Name.CopyValueBytes());
                name = new string(name.Where(c => !char.IsControl(c)).Take(80).ToArray());
                options.Add(new($"{version}:{procedure.SecurityFunction}", $"{name} ({procedure.SecurityFunction})", usable && supportedTextTan,
                    media ? "Requires a bank-reported TAN medium; media discovery/selection is not supported yet." :
                    !supportedTextTan ? "Requires decoupled, visual/HHD, or other unsupported approval parameters; only process-2 text-TAN without medium selection is supported." :
                    "Login-specific permission will be checked. Signed text-TAN approval is supported when the bank requests a challenge.", 2, procedure.SecurityFunction, version));
            }
        }
        return options.AsReadOnly();
    }
}
