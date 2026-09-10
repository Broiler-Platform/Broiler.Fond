using System.Reflection;
using System.Runtime.Versioning;
using Broiler.Fond.Kernel.Application;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Product;
using Broiler.Fond.Kernel.Tests.Storage;

namespace Broiler.Fond.Kernel.Tests;

internal static class Program
{
    private static readonly List<string> Failures = [];

    private static int Main()
    {
        Check(KernelProduct.Brand == "Broiler", "Brand constant differs from the product decision.");
        Check(KernelProduct.ProductName == "Fond - Finance on Demand", "Product-name constant differs from the product decision.");
        Check(KernelProduct.FullDisplayName == "Broiler Fond - Finance on Demand", "Full display name differs from the product decision.");
        Check(KernelProduct.Milestone == 1, "The kernel must declare the milestone currently in development.");
        InstitutionConfigurationTests.Run(Check);
        IdentityTests.Run(Check);
        AccountRediscoveryTests.Run(Check);
        AccountValueTests.Run(Check);
        SimulatorAndDiagnosticTests.Run(Check);
        FinTsSyntaxTests.Run(Check);
        FinTsResponseTests.Run(Check);
        FinTsParameterTests.Run(Check);
        FinTsReadCapabilityTests.Run(Check);
        FinTsPinTanTests.Run(Check);
        FinTsTanSchemaTests.Run(Check);
        FinTsTanContextTests.Run(Check);
        FinTsScaContinuationTests.Run(Check);
        FinTsReadDataTests.Run(Check);
        FinTsReadContextTests.Run(Check);
        FinTsReadRefreshTests.Run(Check);
        FinTsReadAvailabilityTests.Run(Check);
        FinTsAllAccountDiscoveryTests.Run(Check);
        FinTsAllDiscoveryAttemptTests.Run(Check);
        FinTsUnsignedReadRequestTests.Run(Check);
        FinTsInitializationTests.Run(Check);
        FinTsInitializationEvidenceTests.Run(Check);
        FinTsInitializationAttemptTests.Run(Check);
        FinTsSynchronizationTests.Run(Check);
        FinTsSynchronizationEvidenceTests.Run(Check);
        FinTsDialogueEndTests.Run(Check);
        FinTsSynchronizationAttemptTests.Run(Check);
        FinTsPinTanSignatureHeaderTests.Run(Check);
        FinTsPinTanSignatureEvidenceTests.Run(Check);
        FinTsPinTanSignatureHeaderWriterTests.Run(Check);
        FinTsSessionCredentialTests.Run(Check);
        FinTsPinTanSignatureTrailerTests.Run(Check);
        FinTsPinTanRequestWriterTests.Run(Check);
        FinTsPinTanRequestEnvelopeWriterTests.Run(Check);
        FinTsPinTanRequestBindingTests.Run(Check);
        FinTsPinTanInitializationEvidenceTests.Run(Check);
        FinTsPinTanSynchronizationEvidenceTests.Run(Check);
        FinTsPinTanInitializationAttemptTests.Run(Check);
        FinTsPinTanSynchronizationAttemptTests.Run(Check);
        FinTsPinTanDialogueEndTests.Run(Check);
        FinTsPinTanDialogueEndResponseTests.Run(Check);
        FinTsPinTanDialogueEndAttemptTests.Run(Check);
        FinTsPinTanInitializationProcedureTests.Run(Check);
        FinTsPinTanInitializationRequirementsTests.Run(Check);
        FinTsPinTanReadSignatureContextTests.Run(Check);
        FinTsPinTanReadCapabilityContextTests.Run(Check);
        FinTsPinTanReadCredentialComparisonTests.Run(Check);
        FinTsPinTanReadPinTrailerTests.Run(Check);
        FinTsPinTanReadRequestWriterTests.Run(Check);
        StorageFormatTests.Run(Check);

        Check(new AccountId(42) == new AccountId(42), "Typed local IDs must retain value equality.");
        Check(new Money(12.34m, "EUR") == new Money(12.34m, "EUR"), "Money must retain exact decimal value equality.");

        Assembly kernelAssembly = typeof(KernelProduct).Assembly;
        string? targetFramework = kernelAssembly
            .GetCustomAttribute<TargetFrameworkAttribute>()
            ?.FrameworkName;
        Check(targetFramework == ".NETCoreApp,Version=v10.0", "The kernel must target platform-neutral net10.0.");

        Type[] kernelImplementations = kernelAssembly
            .GetTypes()
            .Where(static type =>
                type is { IsClass: true, IsAbstract: false } &&
                typeof(IFondKernel).IsAssignableFrom(type))
            .ToArray();
        Check(kernelImplementations.Length == 0, "This M1 slice must not enable a concrete banking kernel implementation.");

        string[] forbiddenReferences = kernelAssembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .Where(static name => !IsRuntimeAssembly(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Check(forbiddenReferences.Length == 0, $"Kernel has non-runtime assembly references: {string.Join(", ", forbiddenReferences)}");

        if (Failures.Count == 0)
        {
            Console.WriteLine("Kernel boundary, M1 domain, simulator/diagnostics, FinTS syntax/responses/parameters, and storage format candidate verification passed.");
            return 0;
        }

        foreach (string failure in Failures)
        {
            Console.Error.WriteLine($"FAILED: {failure}");
        }

        return 1;
    }

    private static bool IsRuntimeAssembly(string name) =>
        name == "netstandard" ||
        name == "mscorlib" ||
        name.StartsWith("System", StringComparison.Ordinal) ||
        name.StartsWith("Microsoft.", StringComparison.Ordinal);

    private static void Check(bool condition, string failure)
    {
        if (!condition)
        {
            Failures.Add(failure);
        }
    }
}
