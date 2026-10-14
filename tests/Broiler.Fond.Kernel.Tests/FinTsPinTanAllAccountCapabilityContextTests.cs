using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanAllAccountCapabilityContextTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-all-account-capability-context-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();

        foreach (var vector in vectors)
        {
            var signature = Inputs(vector);
            var result = FinTsPinTanAllAccountCapabilityContext.Evaluate(signature);
            string name = vector.GetProperty("name").GetString()!;
            var expectedIssues = Flags<FinTsPinTanAllAccountCapabilityIssue>(vector, "issues");
            var expectedOpEvidence = Enum.Parse<FinTsPinTanOperationEvidence>(vector.GetProperty("operationEvidence").GetString()!);

            Verify(signature.Initialization.HasMatchingEvidence == vector.GetProperty("initializationMatches").GetBoolean(), "Initialization requirements integration: " + name);
            Verify(result.Issues == expectedIssues, $"Expected capability issues for {name}: got {result.Issues}, expected {expectedIssues}");
            Verify(result.HasMatchingEvidence == (expectedIssues == FinTsPinTanAllAccountCapabilityIssue.None), $"Matching evidence flag for {name}: {result.HasMatchingEvidence}");
            Verify(result.OperationEvidence == expectedOpEvidence, $"Operation evidence for {name}: got {result.OperationEvidence}, expected {expectedOpEvidence}");
            Verify(ReferenceEquals(result.Signature, signature), $"Signature provenance preserved for {name}");

            if (result.HasMatchingEvidence)
            {
                Verify(result.MatchingAdvertisement is not null, $"Matching advertisement exposed for {name}");
                Verify(ReferenceEquals(result.MatchingAdvertisement, result.Advertisement), $"Matching advertisement matches advertisement for {name}");
                Verify(ReferenceEquals(result.ReadParameters, signature.Initialization.ReadParameters), $"Read parameters provenance preserved for {name}");
            }
            else
            {
                Verify(result.MatchingAdvertisement is null, $"Non-matching context withholds matching advertisement for {name}");
            }
        }

        Verify(vectors.Length == 27, "All twenty-seven independent PIN/TAN all-account capability context vectors verified.");

        // Legacy requirements without read parameters
        var baseSignature = Inputs(vectors[0]);
        var baseInit = baseSignature.Initialization;
        var legacyRequirements = FinTsPinTanInitializationRequirementsEvidence.Evaluate(
            baseInit.Procedures.Initialization.Binding,
            baseInit.Procedures.Procedures,
            baseInit.Procedures.Permissions,
            baseInit.PinTan);
        var legacySignature = FinTsPinTanReadSignatureContext.Evaluate(
            legacyRequirements,
            baseSignature.Request,
            baseSignature.Header,
            baseSignature.Selection,
            baseSignature.ControlReference);
        var legacyContext = FinTsPinTanAllAccountCapabilityContext.Evaluate(legacySignature);
        Verify(legacyContext.Issues.HasFlag(FinTsPinTanAllAccountCapabilityIssue.MissingReadParameters) &&
               legacyContext.Issues.HasFlag(FinTsPinTanAllAccountCapabilityIssue.SignatureNeedsReview) &&
               legacyContext.ReadParameters is null &&
               legacyContext.MatchingAdvertisement is null,
               "Legacy requirements without read parameters yield MissingReadParameters.");

        // Credential comparison integration with AllAccount capability context
        var validContext = FinTsPinTanAllAccountCapabilityContext.Evaluate(baseSignature);
        Verify(validContext.HasMatchingEvidence, "Base vector context has matching evidence.");

        byte[] validPin = "12345"u8.ToArray();
        byte[] shortPin = "123"u8.ToArray();
        byte[] longPin = "1234567890123456"u8.ToArray();
        byte[] invalidPin = "12\034"u8.ToArray();
        try
        {
            var pinIssue = FinTsPinTanReadCredentialComparison.Compare(validContext, FinTsCredentialKind.Pin, validPin);
            Verify(pinIssue == FinTsPinTanReadCredentialIssue.None, "Valid PIN matches all-account credential requirements.");

            var shortIssue = FinTsPinTanReadCredentialComparison.Compare(validContext, FinTsCredentialKind.Pin, shortPin);
            Verify(shortIssue == FinTsPinTanReadCredentialIssue.PinTooShort, "Short PIN flagged as PinTooShort.");

            var longIssue = FinTsPinTanReadCredentialComparison.Compare(validContext, FinTsCredentialKind.Pin, longPin);
            Verify(longIssue == FinTsPinTanReadCredentialIssue.PinTooLong, "Long PIN flagged as PinTooLong.");

            var invalidIssue = FinTsPinTanReadCredentialComparison.Compare(validContext, FinTsCredentialKind.Pin, invalidPin);
            Verify(invalidIssue == FinTsPinTanReadCredentialIssue.InvalidText, "Invalid control char flagged as InvalidText.");

            var reviewIssue = FinTsPinTanReadCredentialComparison.Compare(legacyContext, FinTsCredentialKind.Pin, validPin);
            Verify(reviewIssue == FinTsPinTanReadCredentialIssue.ContextNeedsReview, "Unresolved context returns ContextNeedsReview.");

            using var cancelSrc = new CancellationTokenSource();
            cancelSrc.Cancel();
            try
            {
                FinTsPinTanReadCredentialComparison.Compare(validContext, FinTsCredentialKind.Pin, validPin, cancelSrc.Token);
                Verify(false, "Cancelled credential comparison must throw.");
            }
            catch (OperationCanceledException)
            {
                Verify(true, "Cancelled credential comparison throws OperationCanceledException.");
            }

            try
            {
                FinTsPinTanReadCredentialComparison.Compare((FinTsPinTanAllAccountCapabilityContext)null!, FinTsCredentialKind.Pin, validPin);
                Verify(false, "Null all-account context in Compare must throw.");
            }
            catch (ArgumentNullException)
            {
                Verify(true, "Null all-account context in Compare throws ArgumentNullException.");
            }

            try
            {
                FinTsPinTanReadCredentialComparison.Compare(validContext, (FinTsCredentialKind)99, validPin);
                Verify(false, "Invalid credential kind must throw.");
            }
            catch (ArgumentOutOfRangeException)
            {
                Verify(true, "Invalid credential kind throws ArgumentOutOfRangeException.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(validPin);
            CryptographicOperations.ZeroMemory(shortPin);
            CryptographicOperations.ZeroMemory(longPin);
            CryptographicOperations.ZeroMemory(invalidPin);
        }

        // Null argument and cancellation validation for Evaluate
        try
        {
            FinTsPinTanAllAccountCapabilityContext.Evaluate(null!);
            Verify(false, "Null signature in Evaluate must throw.");
        }
        catch (ArgumentNullException)
        {
            Verify(true, "Null signature in Evaluate throws ArgumentNullException.");
        }

        using (var cancelSrc = new CancellationTokenSource())
        {
            cancelSrc.Cancel();
            try
            {
                FinTsPinTanAllAccountCapabilityContext.Evaluate(baseSignature, cancelSrc.Token);
                Verify(false, "Cancelled Evaluate must throw.");
            }
            catch (OperationCanceledException)
            {
                Verify(true, "Cancelled Evaluate throws OperationCanceledException.");
            }
        }

        // Reflection check: no secret credential fields or raw sensitive types exposed
        var type = typeof(FinTsPinTanAllAccountCapabilityContext);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        foreach (var field in fields)
        {
            Verify(!field.FieldType.Name.Contains("Pin", StringComparison.OrdinalIgnoreCase) || field.FieldType == typeof(FinTsPinTanReadSignatureContext) || field.FieldType == typeof(FinTsPinTanAllAccountCapabilityIssue) || field.FieldType == typeof(FinTsPinTanOperationEvidence),
                $"No credential secrets exposed on field {field.Name}");
        }

        Console.WriteLine($"FinTS PIN/TAN all-account capability context verification passed ({vectors.Length} independent vectors, {count} checks).");
    }

    private static T Flags<T>(JsonElement vector, string property) where T : struct, Enum =>
        (T)Enum.ToObject(typeof(T), vector.GetProperty(property).EnumerateArray().Aggregate(0,
            (flags, item) => flags | Convert.ToInt32(Enum.Parse<T>(item.GetString()!))));

    private static FinTsPinTanReadSignatureContext Inputs(JsonElement vector, string controlReference = "NEXT-REF")
    {
        var (binding, tan, permissions, hipins) = FinTsPinTanInitializationRequirementsTests.Inputs(vector.GetProperty("initialization"));
        var reads = FinTsReadParameterSet.Parse(hipins.Source);
        var initialization = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(binding, tan, permissions, hipins, reads);
        int expectedBank = vector.GetProperty("expectedBankMessageNumber").GetInt32();
        var request = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!)), expectedBank);
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(vector.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var selection = binding.Request.SignatureEvidence.Request.Selection;
        return FinTsPinTanReadSignatureContext.Evaluate(initialization, request, header, selection, controlReference);
    }
}
