# Architecture decision records

Architecture decision records (ADRs) capture decisions that constrain the
product across milestones. An accepted ADR records intent; it does not prove
that a feature has been implemented or security-reviewed.

## Status values

- `Proposed`: under discussion and not binding.
- `Accepted`: the current architectural rule.
- `Superseded`: replaced by a later ADR, which must be linked.
- `Rejected`: considered but not adopted.

Changes to an accepted decision require a new ADR. Do not silently rewrite the
history or weaken a boundary in implementation.

## Index

| ADR | Status | Decision |
| --- | --- | --- |
| [0001](0001-kernel-boundary.md) | Accepted | Platform-independent, BCL-only .NET kernel |
| [0002](0002-portable-profile-container.md) | Accepted | XML documents in a compressed, authenticated-encrypted profile stream |
| [0003](0003-user-operated-boundary.md) | Accepted | Purely user-operated client with no telemetry or Broiler financial-data backend |
| [0004](0004-profile-envelope-v1.md) | Proposed | Exact v1 encrypted-envelope framing, KDF bounds, and independent vectors |
| [0005](0005-profile-snapshots-v1.md) | Proposed | Bootstrap XML/ZIP contract and generation/recovery/purge protocol candidate |
| [0006](0006-local-identity-allocation.md) | Accepted | In-memory store-wide identity batches, exhaustion and startup validation |
| [0007](0007-account-source-rediscovery.md) | Accepted | Lossless source locators and exact rediscovery with quarantined candidates |
| [0008](0008-account-values-and-exact-money.md) | Accepted | Exact money, immutable balance provenance, freshness and per-currency totals |
| [0009](0009-synthetic-workflows-and-diagnostics.md) | Accepted | Test-only read workflows and opt-in structured in-memory diagnostics/previews |
| [0010](0010-fints-byte-syntax-and-framing.md) | Accepted | Bounded FinTS 3.0 byte syntax, opaque outer framing and independent synthetic vectors |
| [0011](0011-fints-responses-and-dialogue-correlation.md) | Accepted | Typed HIRMG/HIRMS replies and single-pending-request in-memory correlation |
| [0012](0012-fints-parameter-evidence.md) | Accepted | Bounded HIBPA/HIUPA/HIUPD evidence and explicit permission-policy distinctions |
| [0013](0013-read-capability-evidence.md) | Accepted | Read-operation parameter schemas and conservative explicit-version capability evidence |
| [0014](0014-pin-tan-envelope-and-parameters.md) | Accepted | Bounded PIN/TAN response-envelope and HIPINS parameter evidence |
| [0015](0015-tan-procedure-and-challenge-schemas.md) | Accepted | HITANS 6/7 procedure and HITAN 6/7 challenge schemas with untrusted evidence preservation |
| [0016](0016-permitted-procedures-and-challenge-context.md) | Accepted | HIRMS 3920 reports and pure request-bound challenge comparison |
| [0017](0017-in-memory-sca-continuation.md) | Accepted | One in-memory SCA attempt with bounded queries, monotonic timeout and per-instance replay rejection |
| [0018](0018-account-discovery-and-balance-schemas.md) | Accepted | Credential-free HKSPA/HISPA-1 and HKSAL/HISAL-6/7/8 schemas preserving untrusted account and amount evidence |
| [0019](0019-read-request-response-context.md) | Accepted | Pure single-account read context comparison with scoped status and bounded partial-page provenance |
| [0020](0020-in-memory-read-refresh-attempt.md) | Accepted | One in-memory read attempt with explicit continuation, bounded lifetime/pages and per-instance response consumption |
| [0021](0021-scoped-read-unavailability.md) | Accepted | Scoped 3010 unavailable observations, distinct empty/missing/data shapes and terminal local handling |
| [0022](0022-all-account-discovery-evidence.md) | Accepted | Bounded all-account discovery comparison preserving unknown, conflicting, duplicate and unmatched account evidence |
| [0023](0023-in-memory-all-account-discovery-attempt.md) | Accepted | One local all-account discovery attempt with one-time evidence handoff, timeout, cancellation and terminal reference cleanup |
| [0024](0024-unsigned-read-request-encoding.md) | Accepted | Typed unsigned HKSPA-1 and HKSAL-6/7/8 encoding with strict text preservation, escaped bytes and exact frame lengths |
| [0025](0025-unsigned-dialogue-initialization.md) | Accepted | Restricted HKIDN-2/HKVVB-3 initialization schemas and unsigned encoding with explicit anonymous identity and caller-supplied product fields |
| [0026](0026-initialization-response-scope.md) | Accepted | Pure initialization response binding with exact bank/user/customer scope, explicit missing parameters and untrusted version observations |
| [0027](0027-in-memory-initialization-attempt.md) | Accepted | One local initialization attempt with pinned user context, one-time evidence handoff, timeout, cancellation and terminal cleanup |
| [0028](0028-synchronization-schemas.md) | Accepted | HKSYN-3/HISYN-4 schemas and unsigned synchronization encoding with exact counters and preserved unresolved response shapes |
| [0029](0029-synchronization-response-context.md) | Accepted | Pure synchronization mode/profile and response comparison with explicit prior-dialogue context and required close/reinitialize disposition |
| [0030](0030-dialogue-end-schemas-and-encoding.md) | Accepted | HKEND-1 schemas, unsigned encoding and pure request-bound closure/abort reply evidence |
| [0031](0031-in-memory-synchronization-and-closing.md) | Accepted | One local synchronization-and-closing attempt with explicit close recording, one-time stage evidence, a shared deadline and terminal cleanup |
| [0032](0032-pin-tan-signature-header-schemas.md) | Accepted | Restricted credential-free HNSHK-4 PIN/TAN header schemas with exact identity/reference observations and profile/code rules |
| [0033](0033-pin-tan-signature-request-context.md) | Accepted | Pure initialization/synchronization signature-header comparison with explicit identity, system and sourced HITANS/3920 context |
| [0034](0034-pin-tan-signature-header-encoding.md) | Accepted | Typed credential-free HNSHK-4 encoding with exact text/numbers, canonical omissions and precision validation |
| [0035](0035-session-credential-buffer-ownership.md) | Accepted | Bounded local PIN/TAN byte ownership with input clearing, one-time TAN copy-out, access deadlines and owned-buffer zeroization |
| [0036](0036-pin-tan-signature-trailer-encoding.md) | Accepted | Local HNSHA-2 encoding with explicit signature context, TAN placement restrictions, bounded secret output and failure cleanup |
| [0037](0037-pin-tan-request-assembly.md) | Accepted | Bounded plain PIN/TAN initialization/synchronization assembly with exact framing, renumbering and whole-message secret cleanup |
| [0038](0038-pin-tan-request-envelope-assembly.md) | Accepted | Plaintext PIN/TAN request envelopes with bound metadata, exact binary payloads and cleanup across all output boundaries |
| [0039](0039-pin-tan-assembled-request-reference-binding.md) | Accepted | Credential-free candidate segment maps and source-preserving response-reference comparison for assembled PIN/TAN requests |
| [0040](0040-pin-tan-initialization-response-semantics.md) | Accepted | Assembled initialization status and parameter/envelope identity comparison with exact response provenance and untrusted version observations |
| [0041](0041-pin-tan-synchronization-response-semantics.md) | Accepted | Assembled synchronization report/status/envelope checks with exact provenance, recovery bounds and mandatory close/reinitialize next step |
| [0042](0042-pin-tan-initialization-attempt-lifecycle.md) | Accepted | One assembled initialization candidate, absolute deadline, one-time evidence handoff and terminal metadata cleanup |
| [0043](0043-pin-tan-synchronization-attempt-lifecycle.md) | Accepted | One assembled synchronization candidate and recovery context, bounded one-time closing/reinitialization handoff and terminal cleanup |
| [0044](0044-pin-tan-dialogue-end-context-encoding.md) | Accepted | Synchronization-bound PIN/TAN closing context and PIN-only envelope encoding with bounded secret staging and failed-output cleanup |
| [0045](0045-pin-tan-closing-response-binding.md) | Accepted | Assembled closing candidate/reference maps, exact envelope identity checks and distinct closure/abort/review observations |
| [0046](0046-pin-tan-closing-attempt-lifecycle.md) | Accepted | One bounded closing candidate with one-time closure/abort/review handoff, reinitialization requirement and terminal cleanup |
| [0047](0047-assembled-initialization-procedure-integration.md) | Accepted | Exact-source assembled initialization HITANS/3920 integration and explicit one-time combined procedure handoff without activation |
| [0048](0048-assembled-initialization-hipins-requirements.md) | Accepted | Exact-source HIPINS requirements with nullable bounds, qualified TAN flags and one-time combined initialization handoff |
| [0049](0049-first-read-pin-tan-signature-context.md) | Accepted | First-read detached signature context reusing complete returned initialization evidence with exact dialogue, counter, identity and procedure scope |
| [0050](0050-initialization-read-capability-integration.md) | Accepted | Explicit initialization read-schema integration and first-read single-account permission, capability and request-option checks |
| [0051](0051-first-read-credential-requirement-comparison.md) | Accepted | Scalar first-read PIN/TAN byte comparison against reported bounds and formats, preserving caller ownership and unresolved requirements |
| [0052](0052-first-read-pin-only-trailer-encoding.md) | Accepted | First-read PIN-only HNSHA encoding with actual owned-byte validation, bounded staging and expiry/cancellation/output cleanup |
| [0053](0053-first-read-pin-only-request-assembly.md) | Accepted | Plain first-read PIN-only request assembly with bound segment roles, exact framing and whole-message secret cleanup |
| [0054](0054-first-read-pin-only-request-envelope.md) | Accepted | First-read PIN-only envelopes with exact binary payloads, immutable candidate metadata and final-output secret cleanup |
| [0055](0055-first-read-response-reference-binding.md) | Accepted | Assembled first-read response references bound to actual request roles, dialogue, counters and profile with exact source retention |
| [0056](0056-assembled-first-read-response-semantics.md) | Accepted | Exact-source assembled read semantics with account/version/currency and envelope identity checks, scoped unavailable/partial observations |
| [0057](0057-assembled-first-read-attempt.md) | Accepted | Bounded assembled first-read attempt with one candidate/deadline, one-time response handoff and terminal metadata cleanup |
| [0058](0058-assembled-balance-continuation-context.md) | Accepted | Second-page balance context preserving partial-read provenance, exact token/account/options and sequential counters |
| [0059](0059-balance-continuation-pin-tan-signature-context.md) | Accepted | Continuation PIN/TAN signature context with exact identity, unchanged procedure selection, explicit control and qualified requirement provenance |
| [0060](0060-continuation-pin-only-trailer-encoding.md) | Accepted | Continuation PIN-only trailer encoding with actual owned-PIN requirement validation, bounded staging and failed-output cleanup |
| [0061](0061-continuation-pin-only-request-assembly.md) | Accepted | Continuation PIN-only plain request assembly with exact message-3 framing, bound segment roles and whole-message failure cleanup |
| [0062](0062-continuation-pin-only-request-envelope.md) | Accepted | Continuation PIN-only envelopes with bound candidate metadata, exact binary payload and final-output cleanup |
| [0063](0063-continuation-response-reference-binding.md) | Accepted | Continuation-response binding against exact message-3 metadata and actual segment roles, preserving source/status observations |
| [0064](0064-continuation-response-semantics.md) | Accepted | Exact-source continuation-response semantics with inherited account/version/currency scope, envelope identity and separate page observations |
| [0065](0065-assembled-continuation-attempt.md) | Accepted | Bounded assembled continuation attempt with one candidate/deadline, one-time response handoff and terminal metadata cleanup |
| [0066](0066-later-page-balance-continuation-context.md) | Accepted | Later-page balance continuation context enabling continuation beyond page two with 128-page limit and sequential counter checks |
| [0067](0067-assembled-balance-refresh-attempt.md) | Accepted | Multi-page PIN/TAN balance refresh attempt coordinating page one and later-page continuation with page budget, counter monotonicity and balance aggregation |
| [0068](0068-assembled-all-account-capability-context.md) | Accepted | Pure all-account PIN/TAN capability, permission and parameter evaluation with credential comparison support |
| [0069](0069-all-account-request-envelope.md) | Accepted | All-account PIN-only request binding, plain request assembly, trailer encoding and plaintext envelopes |
| [0070](0070-all-account-response-semantics.md) | Accepted | All-account response reference binding, envelope identity comparison, scoped status verification, and account matching |
| [0071](0071-assembled-all-account-discovery-attempt.md) | Accepted | Bounded assembled all-account discovery attempt with one candidate/deadline, one-time evidence handoff and terminal metadata cleanup |
| [0072](0072-assembled-read-session-attempt.md) | Accepted | Bounded assembled PIN/TAN read session attempt coordinator across initialization, operation (all-account or balance refresh), and closing stages |
| [0073](0073-assembled-synchronization-session-attempt.md) | Accepted | Bounded assembled PIN/TAN synchronization session attempt coordinator across synchronization and closing stages |
| [0074](0074-synchronization-session-clock-boundaries.md) | Accepted | Absolute synchronization session deadlines, fail-closed conversion and reentrant terminal-state preservation |
| [0075](0075-session-terminal-evidence-ownership.md) | Accepted | Terminal session reference cleanup, stable caller collection snapshots and preserved evidence handoffs |
| [0076](0076-read-session-evidence-provenance.md) | Accepted | Exact accepted initialization and predecessor evidence for read-session operation, continuation and closing candidates |
| [0077](0077-read-session-operation-scope.md) | Accepted | One operation per read session, HKSAL balance scope and distinct page-limit outcomes with bounded accounting |
| [0078](0078-read-session-diagnostic-lifecycle.md) | Accepted | Resolved retry diagnostics, current response evidence details and truncation preserved through dialogue closing |
| [0079](0079-read-session-initialization-handoffs.md) | Accepted | Complete caller-owned initialization handoffs for all overloads with exact evidence identity and qualification precedence |
| [0080](0080-read-session-response-accounting.md) | Accepted | Read-session response accounting across review and terminal outcomes, sequential message counters, and invariance rules |
| [0081](0081-synchronization-session-diagnostic-lifecycle.md) | Accepted | Synchronization-session counter tracking, diagnostic lifecycle, dialogue pinning, and result handoffs |
| [0082](0082-synchronization-recovery-application.md) | Accepted | Synchronization recovery application and bank connection operational state transitions |
| [0083](0083-fints-transport-and-capability-activation.md) | Accepted | Bounded FinTS HTTPS transport, parameter/capability activation, and secret-safe network boundary |
| [0084](0084-fints-read-ingestion-and-store-binding.md) | Accepted | FinTS read data ingestion, account rediscovery, and store binding application |
| [0085](0085-fints-read-execution-pipeline.md) | Accepted | FinTS read execution pipeline and session coordinator coordinating transport, pagination, and store ingestion |
| [0086](0086-fints-synchronization-execution-pipeline.md) | Accepted | FinTS synchronization execution pipeline coordinating transport, synchronization attempt state machine, and recovery application |
| [0087](0087-profile-storage-schemas-preflight-and-calibration.md) | Accepted | Profile storage schemas, ancestry metadata, purge-intent schemas, bounded ZIP preflight, and supported-floor KDF calibration |
| [0088](0088-production-profile-store-lifecycle.md) | Accepted | Production profile store lifecycle (`IProfileStore`, `ProfileStore`), seekable frame authentication, branched prior-generation recovery, privacy purge, passphrase rotation, and ID-remapping import/merge |
| [0089](0089-connection-removal-and-encrypted-diagnostics.md) | Accepted | Connection removal and cached-data purge (`ConnectionRemovalPlanner`, `IProfileStore.RemoveConnection`), opt-in encrypted local diagnostic persistence (`EncryptedLocalDiagnosticStore`), and preview-bound support export |
| [0090](0090-ambiguous-rediscovery-account-relinking.md) | Accepted | Ambiguous-rediscovery account relinking and provenance-preserving pruning (`AccountRelinkingPlanner`, `IProfileStore.RelinkAccountBinding`) |
| [0091](0091-fints-product-registration-and-sca-continuation.md) | Accepted | FinTS product registration and interactive/decoupled SCA challenge continuation (`FinTsProductRegistration`, `FinTsScaExecutionCoordinator`) |
| [0092](0092-windows-host-ui-and-support-workflows.md) | Accepted | Windows host UI architecture, truthful balance presentation, accessible dialogs, and redacted support workflows (`Broiler.Fond.Host.Windows`, `HostSelfTest`, `MainForm`) |
| [0093](0093-user-reviewed-account-relinking-ui.md) | Accepted | User-reviewed ambiguous-rediscovery account relinking UI (`AccountRelinkingDialog`, `MainForm`, `HostSelfTest`) |
| [0094](0094-immutable-transaction-domain-models.md) | Accepted | Immutable transaction domain models, verbatim bank facts, independent user annotation revisions, and pending-to-booked links |
| [0095](0095-statement-parameters-and-capability-negotiation.md) | Accepted | Statement parameter schemas (`HIKAZS`, `HICAZS`), capability evidence matching, and multi-format preference negotiation |
| [0096](0096-statement-wire-parsers-mt940-and-camt.md) | Accepted | Statement wire parsers for SWIFT MT940 and ISO 20022 CAMT XML (`camt.052`, `camt.053`) |
| [0097](0097-statement-pagination-range-queries-and-overlap-sync.md) | Accepted | Statement pagination, bounded range queries, continuation coordinator, and overlap synchronization planner (`HKKAZ`, `HKCAZ`, `StatementOverlapSyncPlanner`, `StatementContinuationCoordinator`) |
| [0098](0098-transaction-ledger-storage-extension.md) | Accepted | Transaction ledger storage extension, transactions-v1.xsd schema, deterministic XML serialization, backward-compatible container migration, and ID remapping (`StoreArchiveCodec`, `transactions-v1.xsd`, `StoreTransactionsDocument`) |
| [0099](0099-windows-host-transaction-views-and-csv-export.md) | Accepted | Windows host UI transaction views, high-performance search (< 300 ms p95 with 100k records), and privacy-conscious RFC 4180 CSV export (`TransactionViewModel`, `MainForm`, `CsvExportDialog`, `TransactionSearchEngine`, `TransactionCsvExporter`) |
| [0100](0100-multibank-connection-orchestration-and-concurrency.md) | Accepted | Multibank connection orchestration, concurrency boundaries, transient failure backoff, and permission-aware foreground refresh (`InstitutionConcurrencyCoordinator`, `ConnectionBackoffTracker`, `ForegroundSessionRefreshCoordinator`, `MultibankSyncCoordinator`) |
| [0101](0101-connector-compatibility-profiles-and-incompatibility-ledger.md) | Accepted | Versioned connector compatibility profiles, bank workaround overrides, and incompatibility ledger (`ConnectorCompatibilityProfile`, `ConnectorCompatibilityRegistry`, `CompatibilityWorkaroundOverride`) |
| [0102](0102-decoupled-sca-and-visual-challenge-continuation.md) | Accepted | Decoupled SCA, polling loop boundaries, and visual challenge parsers (`VisualChallengeParser`, `VisualChallengePayload`, `DecoupledPollingCoordinator`) |
| [0103](0103-offline-statement-file-import.md) | Accepted | Offline fallback statement file import engine, format auto-detection, wire parser reuse, and atomic store commitment (`StatementFileDetector`, `StatementFileImporter`, `StatementImportOptions`, `StatementImportResult`) |
| [0104](0104-backup-migration-and-fork-detection-hardening.md) | Accepted | Backup migration, lineage fork detection, and connection reauthentication hardening (`LineageForkDetector`, `LineageForkReport`, `ForkConflict`) |
| [0105](0105-windows-host-multibank-management-and-support-export.md) | Accepted | Windows host multibank management, freshness, and support export (`ConnectionViewModel`, `AccountViewModel`, `MainForm`, `HostSelfTest`) |
| [0106](0106-payment-domain-draft-revisions-and-sepa-validation.md) | Accepted | Payment domain models, draft revisions, and SEPA validation (`SepaIbanValidator`, `PaymentDraft`, `PaymentDraftRevision`, `SepaPaymentDraftValidator`) |
| [0107](0107-verification-of-payee-vop-workflow.md) | Accepted | Verification of Payee (VoP) workflow, domain states, and FinTS integration (`VerificationOfPayeeStatus`, `VerificationOfPayeeResult`, `FinTsVopRequestWriter`, `FinTsVopResponseParser`) |
| [0108](0108-durable-payment-state-machine-and-attempt-allocation.md) | Accepted | Durable payment state machine, pre-send commit guarantees, immutable attempt allocation, and duplicate risk guard (`PaymentState`, `PaymentStateMachine`, `PaymentAttempt`, `PaymentStateTransition`, `PaymentDuplicateGuard`) |
| [0109](0109-sepa-credit-transfer-fints-codec-and-sca.md) | Accepted | Single SEPA Credit Transfer FinTS wire codecs, ISO 20022 pain.001 generation, SCA continuation, submission receipts, and failure injection (`SepaPainDocumentWriter`, `FinTsSepaTransferRequestWriter`, `FinTsSepaTransferResponseParser`, `PaymentSubmissionReceipt`, `SepaTransferExecutionCoordinator`) |
| [0110](0110-payment-storage-extension-and-audit-history.md) | Accepted | Payment storage extension, payments-v1.xsd schema, deterministic XML serialization, backward-compatible container migration, and audit history (`StoreArchiveCodec`, `payments-v1.xsd`, `StorePaymentsDocument`) |
| [0111](0111-windows-host-payment-initiation-and-safe-mode.md) | Accepted | Windows host payment initiation, review screen, reconciliation, and read-only safe mode (`PaymentInitiationDialog`, `PaymentReviewDialog`, `PaymentViewModel`, `HostSession.IsSafeModeActive`, `MainForm`) |
| [0112](0112-own-account-and-instant-sepa-transfers.md) | Accepted | Own-account transfers and SEPA instant credit transfers (`PaymentExecutionMode`, `InstantPaymentCapability`, `InstantPaymentDowngradeException`, `OwnAccountTransferValidator`, `FinTsInstantPaymentAdvertisement`, `BuildHkipbSegment`) |
| [0113](0113-scheduled-sepa-credit-transfers.md) | Accepted | Scheduled SEPA credit transfers and bank order lifecycle (`ScheduledPaymentCapability`, `ScheduledOrder`, `ScheduledOrderStatus`, `ScheduledPaymentValidator`, `FinTsScheduledTransferRequestWriter`) |
| [0114](0114-standing-orders-management.md) | Accepted | Standing orders management (`StandingOrderFrequency`, `StandingOrderStatus`, `StandingOrder`, `StandingOrderCapability`, `StandingOrderScheduleCalculator`, `StandingOrderValidator`, `FinTsStandingOrderRequestWriter`, `FinTsStandingOrderResponseParser`) |
| [0115](0115-direct-debit-return-and-mandate-revocation.md) | Accepted | Direct-debit return writes and mandate revocation (`DirectDebitReturnReason`, `DirectDebitReturnStatus`, `DirectDebitReturnRequest`, `DirectDebitStatutoryDeadline`, `DirectDebitReturnCapability`, `DirectDebitReturnValidator`, `FinTsDirectDebitReturnRequestWriter`, `FinTsDirectDebitReturnResponseParser`) |
| [0116](0116-beneficiary-templates-and-address-book.md) | Accepted | Beneficiary templates and address book in profile store (`BeneficiaryTemplate`, `BeneficiaryTemplateValidator`, `templates-v1.xsd`, `StoreBeneficiaryTemplateEntry`, `StoreTemplatesDocument`, `StoreArchiveCodec`) |
| [0117](0117-windows-host-everyday-payments.md) | Accepted | Windows host everyday payments, standing orders, direct-debit returns and address book (`PaymentInitiationDialog`, `StandingOrdersDialog`, `DirectDebitReturnDialog`, `HostSession`, `HostSelfTest`) |
| [0118](0118-deterministic-categorization-and-rules-engine.md) | Accepted | Deterministic categorization engine, reversible rules, and merchant normalization (`Category`, `CategorizationRule`, `CategorizationEngine`, `MerchantNormalizer`, `CategorizationPreview`) |
| [0119](0119-transaction-splits-and-tagging.md) | Accepted | Transaction splits, strict balance invariant, and multi-tagging in profile store (`TransactionSplitItem`, `TransactionSplitValidator`, `AnnotationRevision`, `transactions-v1.xsd`, `StoreArchiveCodec`) |
| [0120](0120-budgets-and-spending-targets.md) | Accepted | Monthly budgets, spending targets, split-aware category rollups, and rollover calculations (`BudgetTarget`, `CategoryBudgetProgress`, `BudgetRollupEngine`) |
| [0121](0121-recurring-obligations-and-cash-flow-forecasts.md) | Accepted | Recurring obligation detection, cash-flow projections, low-balance alerts, and non-conflation invariants (`RecurringCadence`, `RecurringObligation`, `RecurringCadenceDetector`, `ForecastItem`, `CashFlowForecastEngine`) |
| [0122](0122-net-worth-history-and-extended-read-only-products.md) | Accepted | Multi-currency net-worth history, assets vs liabilities breakdown, and extended read-only product presentation (`AccountProductCategory`, `ExtendedAccountProduct`, `NetWorthSnapshot`, `NetWorthHistoryCalculator`) |
| [0123](0123-windows-host-financial-insights-and-budgets.md) | Accepted | Windows host financial insights, budget dashboard, rule management, cash-flow forecast, and transaction splits (`MainForm`, `HostSession`, `CategorizationRulesDialog`, `TransactionSplitDialog`, `CashFlowForecastDialog`) |
| [0124](0124-fints-3-freeze-and-conformance-hardening.md) | Accepted | FinTS 3.0 protocol freeze, conformance harness, and error hardening (`FinTs30CapabilityLock`, `FinTs30ConformanceHarness`, `UnsupportedSegmentVersionException`) |
| [0125](0125-storage-migration-and-rollback-integrity.md) | Accepted | Storage migration guarantees, downgrade protection, and rollback integrity (`StoreMigrationEngine`, `StoreDowngradeGuard`, `StoreDowngradeException`, `LineageRollbackGuard`, `RollbackDetectedException`) |
| [0126](0126-security-controls-and-incident-response.md) | Accepted | Security controls, zero-telemetry invariants, and memory credential hygiene (`SecurityHygieneValidator`, `FinTsSessionCredential`, `ExportReviewedSupportPreview`) |
| [0127](0127-accessibility-and-localization-hardening.md) | Accepted | Accessibility (BFSG/BFSGV) and German/English localization hardening (`BfsgAccessibilityAuditor`, `LocalizationCatalog`, `ApplicationLanguage`) |
| [0128](0128-windows-packaging-signing-and-release-lifecycle.md) | Accepted | Windows 1.0 packaging manifest, artifact verification, and release lifecycle (`ReleaseArtifactVerifier`, `WindowsPackageVariant`, `PackageChecksumManifest`) |
| [0129](0129-windows-host-1-0-hardening.md) | Accepted | Windows host 1.0 hardening, shell integration, legal disclosures, and headless verification (`AboutDialog`, `RestoreBackupDialog`, `HostSession.ExportBackup`, `HostSession.RestoreFromBackup`, `HostSelfTest`) |
| [0130](0130-cross-platform-host-seam-and-platform-qualification.md) | Accepted | Cross-platform host seam, platform qualification engine, and host-neutral contracts (`HostPlatformQualification`, `IHostPlatformSeam`, `IHostNotificationChannel`, `IHostCredentialPrompt`, `IHostClipboardService`) |
| [0131](0131-cross-host-storage-portability-and-filesystem-semantics.md) | Accepted | Cross-host storage portability, deterministic archive serialization, and filesystem concurrency semantics (`CrossHostStoragePortability`, `ProfileStore`) |
| [0132](0132-trimming-and-native-aot-compatibility.md) | Accepted | Trimming, linker analysis, and Native AOT compatibility verification (`TrimmingAndAotCompatibilityValidator`, `Broiler.Fond.Kernel.csproj`) |
| [0133](0133-mobile-lifecycle-suspension-and-device-security.md) | Accepted | Mobile lifecycle suspension, transient memory hygiene, and device threat model (`MobileLifecycleCoordinator`, `HostLifecycleState`) |
| [0134](0134-cross-host-distribution-pipeline-and-multi-platform-ci.md) | Accepted | Cross-host distribution pipeline, multi-platform packaging specifications, and CI verification (`MultiPlatformPackageSpec`, `MultiPlatformReleaseVerifier`) |
| [0135](0135-fints-4-1-protocol-architecture-and-version-negotiation.md) | Accepted | FinTS 4.1 protocol architecture, version enumeration, and negotiation framework (`FinTsProtocolVersion`, `FinTsProtocolNegotiator`, `FinTsNegotiatedProtocol`) |
| [0136](0136-fints-4-1-xml-envelope-and-syntax-codec.md) | Accepted | FinTS 4.1 XML message envelope, framing, and secure syntax codec (`FinTs41XmlMessage`, `FinTs41MessageCodec`, `FinTs41Segment`) |
| [0137](0137-fints-4-1-security-profile-and-pin-tan-continuation.md) | Accepted | FinTS 4.1 PIN/TAN security procedures, signature envelopes, and SCA challenge continuation (`FinTs41SecurityHeader`, `FinTs41SecurityTrailer`, `FinTs41ScaChallengeParser`) |
| [0138](0138-fints-4-1-account-operations-and-iso-20022-integration.md) | Accepted | FinTS 4.1 account discovery, balance queries, native ISO 20022 CAMT statements, and PAIN SEPA transfers (`FinTs41AccountOperations`, `FinTs41DiscoveredAccount`, `FinTs41BalanceInfo`) |
| [0139](0139-multi-version-conformance-harness-and-incompatibility-ledger.md) | Accepted | Multi-version FinTS protocol coordinator, dual-protocol conformance test harness, and incompatibility ledger (`FinTsMultiVersionCoordinator`, `FinTsMultiVersionConformanceTests`) |
| [0140](0140-professional-roles-and-four-eyes-approval-workflow.md) | Accepted | Professional roles, multi-user permissions, and four-eyes approval workflow (`ProfessionalRole`, `ProfessionalUser`, `FourEyesApprovalEngine`, `CorporateAuditLog`) |
| [0141](0141-ebics-protocol-architecture-and-order-types.md) | Accepted | EBICS protocol architecture, versioning, order types, and BCL cryptographic signatures (`EbicsVersion`, `EbicsHostConfig`, `EbicsOrderType`, `EbicsSigner`, `EbicsMessageCodec`) |
| [0142](0142-iso-20022-batch-processing-and-granular-reconciliation.md) | Accepted | ISO 20022 batch processing, bulk payments, control sums, and granular reconciliation (`BulkPaymentBatch`, `Iso20022BatchXmlCodec`, `BatchReconciliationEngine`) |
| [0143](0143-distributed-electronic-signatures-veu-lifecycle.md) | Accepted | Distributed Electronic Signatures (VEU / EDS), signature classes, and multi-actor order lifecycle (`VeuSignatureClass`, `VeuOrderState`, `VeuOrder`, `EbicsVeuCoordinator`) |
| [0144](0144-accounting-exports-and-datev-interoperability.md) | Accepted | Accounting exports, DATEV CSV interoperability, enterprise audit manifests, and key recovery policies (`DatevExportGenerator`, `DatevBookingRecord`, `EnterpriseAuditExporter`) |







