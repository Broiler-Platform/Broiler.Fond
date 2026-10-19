# ADR 0132: Trimming, Linker Analysis & Native AOT Compatibility Verification

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0001](0001-repository-and-kernel-boundary.md), [ADR 0004](0004-profile-envelope-v1.md), [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md), [ADR 0130](0130-cross-platform-host-seam-and-platform-qualification.md), [ADR 0131](0131-cross-host-storage-portability-and-filesystem-semantics.md), [docs/milestone-8.md](../milestone-8.md#m8-03-trimming-linker-analysis--native-aot-compatibility-verification)

---

## 1. Context and Problem Statement

For Milestone 8 ([Additional Hosts](../roadmap.md#m8--additional-hosts)), Broiler Fond targets heterogeneous deployment targets, including Linux and macOS desktop applications and mobile runtimes (iOS and Android). On resource-constrained, sandboxed, or app-store-restricted platforms (especially iOS, which strictly prohibits JIT code generation and runtime code emission), Native AOT (Ahead-Of-Time compilation) and IL trimming are operational requirements.

To ensure that `Broiler.Fond.Kernel` can be published with full trimming and Native AOT without runtime breakage or silent data truncation:
1. **Zero Dynamic Code Generation:** The kernel must never require JIT code generation (`System.Reflection.Emit`, `DynamicMethod`, or runtime compiled delegates).
2. **Zero Unreferenced Code Reflection:** The kernel must eliminate runtime reflection serializers (`XmlSerializer`, dynamic JSON/binary formatters, or unreferenced type inspection) on all domain, FinTS protocol, and storage execution paths.
3. **Explicit Trimmability:** The kernel project model must declare `<IsTrimmable>true</IsTrimmable>` and pass compiler ILLink trim analysis with zero warnings.
4. **Automated Static Analysis Verification:** An automated audit must verify that no public kernel types declare `[RequiresDynamicCode]` or `[RequiresUnreferencedCode]`, and that all cryptographic and XML workflows execute under ahead-of-time constraints.

---

## 2. Decision and Architecture

### 2.1 Project Trimmability Configuration

In `src/Broiler.Fond.Kernel/Broiler.Fond.Kernel.csproj`:
- Enabled `<IsTrimmable>true</IsTrimmable>`.
- In `Directory.Build.targets`, verified that implicit SDK tooling (`Microsoft.NET.ILLink.Tasks`) is respected while strictly rejecting any explicit NuGet package references.

### 2.2 Non-Dynamic Serialization & Cryptography Architecture

- **XML Serialization:** All document codecs in `StoreArchiveCodec` (`profile.xml`, `institutions.xml`, `connections.xml`, `accounts.xml`, `balances.xml`, `ledger.xml`, `transactions.xml`, `payments.xml`, `templates.xml`, `purge-intent.xml`) are implemented directly with `System.Xml.XmlWriter` and `System.Xml.XmlReader` / `System.Xml.Linq.XDocument`. No `XmlSerializer` or dynamic assembly generation is utilized.
- **Cryptography:** Authenticated symmetric encryption (`AesGcm`), hash algorithms (`SHA256`), key derivation (`Rfc2898DeriveBytes.Pbkdf2`, `HKDF.DeriveKey`), and random number generation (`RandomNumberGenerator`) use direct static BCL invocations with fixed-time memory clearing (`CryptographicOperations.ZeroMemory`).
- **Domain Identity & Value Types:** All entities and value types (`AccountId`, `Money`, `Transaction`, `PaymentDraft`, `PaymentAttempt`) are immutable records or structs with explicit static factories, requiring zero runtime type metadata discovery.

### 2.3 Automated AOT Compatibility Validator (`TrimmingAndAotCompatibilityValidator`)

Implemented in `Broiler.Fond.Kernel.Product`:
- **`AssertZeroRequiresDynamicCode(Assembly assembly)`:** Inspects all exported and internal types in `Broiler.Fond.Kernel`, verifying that zero types, methods, or properties carry `RequiresDynamicCodeAttribute`.
- **`AssertXmlCodecsAotCompatible()`:** Verifies that all storage serialization and parsing operations execute without dynamic reflection.
- **`VerifyRuntimeFeatureCompatibility()`:** Verifies that the kernel does not rely on `RuntimeFeature.IsDynamicCodeCompiled` or `RuntimeFeature.IsDynamicCodeSupported` being true.

---

## 3. Consequences

### Positive
- Guarantees full compatibility with Native AOT compilation (`dotnet publish -r <rid> -c Release /p:PublishAot=true`) across Windows, Linux, and macOS.
- Enables native iOS compilation without encountering prohibited JIT exceptions.
- Reduces binary size and memory footprint through aggressive, safe linker trimming.

### Negative / Trade-offs
- Assembly-wide reflection for self-tests requires explicit `[UnconditionalSuppressMessage]` annotations to prevent false positives in trim analysis.
