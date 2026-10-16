# Broiler Fond - Finance on Demand

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

Broiler Fond - Finance on Demand is a planned privacy-first, multibank
homebanking application. It is a purely user-operated client: the installed
application connects directly to accounts that its user owns or is authorized
to use. Broiler does not receive credentials, account data, transaction data,
or payment instructions.

> **Milestone 1 in progress (ADRs 0003–0093).**
> - **Domain & identity:** Manual HTTPS institution endpoint validation with confirmation/quarantine ([ADR 0003](docs/adr/0003-user-operated-boundary.md)), shared 64-bit `LocalIdentityLedger` with atomic batch commits and startup validation ([ADR 0006](docs/adr/0006-local-identity-allocation.md)), lossless `AccountSourceLocator` and `AccountRediscoveryPlanner` ([ADR 0007](docs/adr/0007-account-source-rediscovery.md)), exact ISO 4217 `Money` and `AccountValueProjection` with freshness warnings and unmixed currency totals ([ADR 0008](docs/adr/0008-account-values-and-exact-money.md)), ambiguous-rediscovery account relinking with provenance-preserving candidate pruning via `ExecutePrivacyPurge` ([ADR 0090](docs/adr/0090-ambiguous-rediscovery-account-relinking.md)), and user-reviewed relinking UI with deterministic live previews ([ADR 0093](docs/adr/0093-user-reviewed-account-relinking-ui.md)).
> - **Encrypted profile storage, lifecycle, disconnect & diagnostics (`M1-01`, `M1-02`, `M1-08`):** Authenticated-encrypted `BFPE` v1 envelope ([ADR 0004](docs/adr/0004-profile-envelope-v1.md)), ZIP/XML snapshot protocol (`bootstrap-v1.xsd`, [ADR 0005](docs/adr/0005-profile-snapshots-v1.md)), full M1 financial store schemas (`store-v1.xsd`), ancestry lineage, encrypted purge-intent schemas (`purge-intent-v1.xsd`), bounded hostile-input ZIP preflight validation, supported-floor PBKDF2-HMAC-SHA256 calibration (`600,000..5,000,000` iterations, [ADR 0087](docs/adr/0087-profile-storage-schemas-preflight-and-calibration.md)), production `IProfileStore` / `ProfileStore` lifecycle with single-writer directory locking, seekable authenticating frame stream (`SeekableAuthenticatedProfileStream`), atomic generation promotion, branched prior-generation recovery, privacy purge, passphrase rotation, whole-profile erasure, backup/restore, and ID-remapping import/merge ([ADR 0088](docs/adr/0088-production-profile-store-lifecycle.md)), plus connection removal / cached-data purge (`ConnectionRemovalPlanner`, `IProfileStore.RemoveConnection`) and opt-in encrypted local diagnostic persistence (`EncryptedLocalDiagnosticStore`, `ExportReviewedSupportPreview`, [ADR 0089](docs/adr/0089-connection-removal-and-encrypted-diagnostics.md)).
> - **FinTS 3.0 protocol, sessions, execution pipelines & SCA continuation:** Bounded FinTS 3.0 byte syntax/framing, typed `HIRMG`/`HIRMS` correlation, `HIBPA`/`HIUPA`/`HIUPD`/`HIPINS` parameter and `HITANS`/`HITAN` v6–v7 challenge schemas, PIN/TAN security headers/trailers/envelopes (`HNSHK`, `HNSHA`, `HNVSK`, `HNVSD`), zeroized `SessionCredentialBuffer`, single- and multi-page read/discovery/initialization/synchronization/closing attempts, multi-stage session coordinators (`FinTsPinTanReadSessionAttempt`, `FinTsPinTanSynchronizationSessionAttempt`), connection state recovery (`FinTsSynchronizationRecoveryApplication`), capability activation (`FinTsCapabilityActivation`), bounded HTTPS transport (`FinTsHttpsTransport`), read store ingestion (`FinTsReadIngestionApplication`), end-to-end execution pipelines (`FinTsReadExecutionPipeline`, `FinTsSynchronizationExecutionPipeline`), FinTS product registration (`FinTsProductRegistration`, `KernelProduct.FinTsRegistration`), and interactive/decoupled SCA challenge continuation (`FinTsScaExecutionCoordinator`; [ADRs 0009](docs/adr/0009-synthetic-workflows-and-diagnostics.md)–[0086](docs/adr/0086-fints-synchronization-execution-pipeline.md), [ADR 0091](docs/adr/0091-fints-product-registration-and-sca-continuation.md)).
> - **Windows host UI & support workflows (`M1-06`, `M1-07`, `M1-08`, `ADR 0092`, `ADR 0093`):** Accessible desktop UI (`Broiler.Fond.Host.Windows`, `MainForm`), headless self-test (`HostSelfTest`) meeting smoke test invariants, profile passphrase lock/unlock, manual endpoint entry with independent HTTPS confirmation dialog disclosing phishing risks (`EndpointConfirmationDialog`), session PIN/TAN prompts with volatile buffer zeroization (`CredentialPromptDialog`, `ScaChallengeDialog`), user-reviewed account relinking dialog (`AccountRelinkingDialog`), truthful balance presentation with freshness indicators and strict multi-currency isolation, connection disconnect and irreversible privacy purge dialog (`ConnectionRemovalDialog`), and SHA-256 preview digest bound redacted support export dialog (`SupportExportDialog`) with zero background telemetry.

## Product direction

- Germany and EUR/SEPA first.
- Windows is the first host. A platform-independent .NET 10+ kernel grows with
  each milestone; Linux, macOS, and mobile hosts follow later.
- The kernel is developed in this repository and has no third-party runtime or
  platform dependency. It uses only the standard .NET runtime libraries.
- FinTS 3.0 as the first live connector; any later protocol must preserve direct
  user-device-to-bank operation behind the same connector boundary.
- Institutions are configured manually in the first milestones.
- Local data is stored as multiple versioned XML documents in a compressed,
  authenticated-encrypted stream using only .NET 10+ runtime APIs.
- No telemetry, analytics, or automatic crash upload is collected.
- Bank-advertised capabilities decide what the application offers. A feature is
  never assumed merely because another institution supports it.
- Read-only account access comes before any money movement.
- Credentials, TANs, financial data, and ambiguous payment outcomes are treated
  as security-critical data and states.

Milestone 1 is planned as a technical alpha that can create access to existing
accounts and list their current values. Transaction detail, reliable multibank
sync, SEPA transfers, recurring payments, and personal-finance features follow
in separately gated milestones.

## Engineering foundation and current development

The [Milestone 0 foundation](docs/milestone-0.md) remains strictly enforced as [Milestone 1](docs/milestone-1.md) progresses:

- `Broiler.Fond.Kernel` targets platform-neutral `net10.0` and contains pure domain models, manual endpoint validation, identity allocation and startup checks, account rediscovery, exact money and balance projections, bounded structured diagnostics and opt-in encrypted local diagnostic persistence (`EncryptedLocalDiagnosticStore`), production encrypted profile storage (`IProfileStore`, `ProfileStore`, `ConnectionRemovalPlanner`, `SeekableAuthenticatedProfileStream`, `ProfileEnvelopeCodec`, `StoreArchiveCodec`, `ZipPreflight`, `SupportedFloorKdfCalibration`), and the complete FinTS 3.0 PIN/TAN read and synchronization protocol stack (wire framing, parameter/challenge schemas, security headers/trailers/envelopes, session coordinators, capability activation, bounded HTTPS transport, store ingestion, and end-to-end execution pipelines).
- `Broiler.Fond.Host.Windows` targets `net10.0-windows` implementing the accessible read-only banking desktop UI with dual-mode startup (headless automated self-test during CI smoke runs and interactive `MainForm` when launched by the user).
- `Broiler.Fond.Kernel.Tests` is a self-contained BCL-only executable verification runner exercising 64 deterministic FinTS wire/session fixture suites, 5 independent storage envelope vectors, full XML schema/ZIP preflight/KDF calibration checks, 127 production `ProfileStore` lifecycle checks, 65 connection removal and encrypted diagnostic persistence checks, 57 account relinking checks, and concurrency/boundary suites; no external test framework or package feed is required.
- Repository build rules and `eng/Verify-KernelBoundary.ps1` reject third-party packages, platform UI, native/COM, unsafe code, and out-of-kernel project dependencies in the kernel.
- GitHub Actions verifies the full solution and smoke-runs the inert host on Windows, then independently builds the kernel and its checks on Linux. CI does not publish artifacts; the manual dispatch-only Publish workflow builds the host self-contained and framework-dependent for `win-x64` and drafts an unpublished pre-release (tag `fond-v<version>`, from `BroilerFondVersion`).

The [M1 progress and acceptance backlog](docs/milestone-1.md) and [roadmap](docs/roadmap.md) record completed work and remaining alpha exit criteria:

- **Storage format, lifecycle & disconnect/diagnostics (`M1-01`, `M1-02`, `M1-08`):** Implemented in the kernel ([ADRs 0004](docs/adr/0004-profile-envelope-v1.md), [0005](docs/adr/0005-profile-snapshots-v1.md), [0087](docs/adr/0087-profile-storage-schemas-preflight-and-calibration.md), [0088](docs/adr/0088-production-profile-store-lifecycle.md), [0089](docs/adr/0089-connection-removal-and-encrypted-diagnostics.md)): encrypted `BFPE` v1 envelope, `bootstrap-v1.xsd`, `store-v1.xsd`, `purge-intent-v1.xsd`, bounded ZIP preflight validation, PBKDF2 calibration (`600,000..5,000,000` iterations), exclusive directory locking, seekable frame authentication, atomic generation promotion, branched prior-generation recovery, privacy purge, passphrase rotation, whole-profile erasure, backup/restore, ID-remapping import/merge, connection removal with cached-data purge (`ConnectionRemovalPlanner`, `IProfileStore.RemoveConnection`), opt-in encrypted local diagnostic persistence (`EncryptedLocalDiagnosticStore`), and SHA-256 preview-bound local support export. Storage format human review and cross-host Linux execution evidence are complete and recorded ([docs/storage-format-review.md](docs/storage-format-review.md)).
- **Identity, rediscovery & relinking (`M1-03`, `M1-07`):** `LocalIdentityLedger` ([ADR 0006](docs/adr/0006-local-identity-allocation.md)), `AccountRediscoveryPlanner` ([ADR 0007](docs/adr/0007-account-source-rediscovery.md)), `AccountValueProjection` ([ADR 0008](docs/adr/0008-account-values-and-exact-money.md)), `FinTsReadIngestionApplication` ([ADR 0084](docs/adr/0084-fints-read-ingestion-and-store-binding.md)), durable `ProfileStore` snapshot commits ([ADR 0088](docs/adr/0088-production-profile-store-lifecycle.md)), deterministic account relinking with privacy purge (`AccountRelinkingPlanner`, `IProfileStore.RelinkAccountBinding`, [ADR 0090](docs/adr/0090-ambiguous-rediscovery-account-relinking.md)), and user-reviewed relinking dialog (`AccountRelinkingDialog`, [ADR 0093](docs/adr/0093-user-reviewed-account-relinking-ui.md)) with truthful balance presentation ([ADR 0092](docs/adr/0092-windows-host-ui-and-support-workflows.md)) are implemented in the kernel and Windows host.
- **FinTS read & synchronization slice (`M1-04`, `M1-05`, `M1-06`, `M1-08`):** End-to-end read and synchronization pipelines ([ADRs 0085](docs/adr/0085-fints-read-execution-pipeline.md), [0086](docs/adr/0086-fints-synchronization-execution-pipeline.md)) over bounded HTTPS transport ([ADR 0083](docs/adr/0083-fints-transport-and-capability-activation.md)), FinTS product registration (`FinTsProductRegistration`), interactive/decoupled SCA challenge continuation (`FinTsScaExecutionCoordinator`, [ADR 0091](docs/adr/0091-fints-product-registration-and-sca-continuation.md)), and Windows host setup/dialogs/support workflows ([ADR 0092](docs/adr/0092-windows-host-ui-and-support-workflows.md)) are implemented. Controlled live bank acceptance (`M1-09`) and human release sign-off (`M1-10`) remain pending.

Prerequisite: a compatible .NET 10 SDK. Run the same full check used by CI:

```powershell
./eng/Invoke-CI.ps1 -Scope Full
```

The platform-neutral subset can be checked with:

```powershell
./eng/Invoke-CI.ps1 -Scope Kernel
```

## Documentation

- [Product specification and roadmap](docs/roadmap.md)
- [Milestone 0 definition and inventory](docs/milestone-0.md)
- [Milestone 1 progress and acceptance backlog](docs/milestone-1.md)
- [Milestone 2 progress and acceptance backlog](docs/milestone-2.md)
- [Storage format review and cross-host evidence](docs/storage-format-review.md)
- [Architecture decisions](docs/adr/README.md)
- [Compatibility evidence](docs/compatibility/README.md)
- [Security policy](SECURITY.md)
- [Human review record](HUMAN_REVIEW.md)
- [Prerelease checklist](docs/release/prerelease-checklist.md)

Major implementation decisions are recorded as ADRs under `docs/adr/`.

## License

Broiler Fond - Finance on Demand is licensed under the
[Apache License 2.0](LICENSE).
