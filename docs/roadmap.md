# Broiler Fond - Finance on Demand — Product Specification and Roadmap

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Full display name:** Broiler Fond - Finance on Demand
- **Allowed short name after first mention:** Broiler Fond
- **Technical name:** `Broiler.Fond`

**Status:** Draft for product and architecture review

**Last reviewed:** 2026-09-05

**Initial market:** Germany, consumer homebanking, EUR/SEPA

**Initial delivery:** Purely user-operated Windows application

This document is the working product specification and milestone roadmap. It is
deliberately more precise about correctness, authentication, and failure states
than a typical feature list because homebanking software can expose sensitive
data and initiate irreversible actions.

## Executive summary

Broiler Fond - Finance on Demand gives a user one trustworthy view of bank
accounts they already own or are authorized to use. The installed client talks
directly to each bank; no Broiler-operated service receives credentials,
financial data, or payment instructions. It starts with direct, read-only FinTS
access, then adds transaction detail and multibank reliability, and only then
enables payments with explicit review and bank-controlled strong customer
authentication.

The official FinTS site describes FinTS as the successor to HBCI, designed for
multibank access, and reports support by more than 2,000 credit institutions.
FinTS 3.0 is described as the established market version; FinTS 4.1 is the
latest specification. Those facts make FinTS 3.0 the pragmatic first connector,
but they do not mean every institution supports every business transaction,
segment version, account type, or TAN procedure.

In this document, **create access to accounts** means linking existing accounts.
It does not mean opening accounts, performing KYC, or becoming a bank. **Money
movement** means initiating a payment that the user's bank executes; the client
never holds customer funds.

## Product decisions for the first release

| Area | Initial decision | Reason |
| --- | --- | --- |
| User | Individual consumer with one or more German bank relationships | Keeps the first workflows and regulatory analysis bounded |
| Operating model | Purely user-operated client with direct device-to-bank communication | Broiler operates no aggregation, credential relay, account-information, payment-initiation, or financial-data backend |
| Host | Windows first; Linux, macOS, and mobile later | Ships a narrow first host while preserving a platform-independent core |
| Kernel | New in-repository `net10.0` kernel, grown per milestone, with no third-party runtime or platform dependency | Keeps the banking core auditable, portable, and controlled by the product |
| Connectivity | Direct FinTS 3.0 PIN/TAN connection | Established multibank starting point |
| Data | Multiple versioned XML documents in a compressed, authenticated-encrypted stream, implemented only with .NET 10+ runtime APIs | Provides portable local storage without a database engine or third-party package |
| Credentials | Ask for the banking PIN when needed; never persist PINs or TANs in the first releases | Avoids a platform-specific credential-vault dependency and keeps archives portable |
| Institution setup | Manual entry in the first milestones | Avoids premature directory licensing, freshness, and distribution assumptions |
| Scope order | Accounts and balances, then transactions, then payments | Correct read behavior must be proven before enabling writes |
| Payments | EUR SEPA credit transfers first | Common consumer use case with a bounded domain |
| Bank support | One flat, evidence-backed compatibility table that grows only when a row passes | Keeps the first support promise intentionally small and understandable |
| Telemetry | None | The application has no analytics, tracking, crash-upload, or background-reporting channel |
| Release authority | Named human release owner and second human security reviewer for every distributed prerelease and release | Makes security review an artifact-level gate rather than an informal milestone activity |
| Cloud | No Broiler-operated cloud or remote financial-data processing | Preserves the purely user-operated trust boundary |
| Exclusions | No browser scraping, custody, account opening, lending, trading, tax advice, or autonomous payments | These require different reliability, legal, and product models |

These are closed foundation decisions for this product. The exact local-client,
distribution, update, and user-initiated support-export flows still require
written legal review before an external prerelease; that review validates the
chosen model rather than reopening hosted AIS/PIS as an implementation option.
A Broiler-operated backend or licensed-partner relay would be a different
product and requires a separate specification and decision.

## Product principles

- **Read before write.** No payment code is enabled until read-only sync is
  stable across the verified compatibility table.
- **Capability-driven behavior.** Bank parameter data, user parameter data, and
  the active connection determine which actions are available.
- **Truthful states.** Missing is not zero, stale is not current, submitted is
  not settled, and an unknown payment outcome is not failure.
- **Explicit control.** Linking, credential entry, refresh requiring SCA,
  payment authorization, export, and deletion are deliberate user actions.
- **Exact money.** Amounts use decimal or fixed-point arithmetic plus an
  explicit ISO currency; floating-point money is forbidden.
- **Local means local.** Financial data and authentication material remain on
  the user's device except for direct communication with the selected bank and
  an explicit, previewed export initiated by the user.
- **No silent conflation.** External identifiers and hashes are evidence, not
  primary keys. Ambiguous observations remain separate and visible for review.
- **No telemetry.** Quality evidence comes from simulators, controlled test
  accounts, release fixtures, and human review—not installed-user reporting.
- **Safe interoperability.** Bank messages and imported files are untrusted
  inputs and are parsed with size, depth, encoding, and schema limits.
- **No ambiguous retries.** A write that may have reached a bank is reconciled
  or manually resolved, never automatically sent again.
- **Accessible operation.** Keyboard, screen-reader, high-contrast, text-scale,
  and non-color-only status behavior are release requirements.

## Users and primary jobs

### Initial user

A consumer who has one or several accounts at German institutions and wants a
private, consistent overview without using a provider-hosted financial-data
cloud.

### Later users

- A household using one local profile or deliberate encrypted export for shared
  reporting without a Broiler synchronization service or shared bank credentials.
- A power user who needs richer exports, rules, and forecasts.
- A sole trader or small business that needs EBICS, batches, roles, or approval
  workflows. This is a separate later track, not an extension of the consumer
  permission model by default.

### Core jobs to be done

1. Link a bank login securely and choose which discovered accounts to show.
2. See trustworthy balances, their type, currency, and freshness.
3. Inspect, search, filter, and export transaction details.
4. Move money safely and understand the bank-confirmed outcome.
5. Recognize recurring activity, plan cash flow, and manage budgets.
6. Export, back up, restore, or erase personal data under the user's control.

## Functional scope

### Connections and authentication

- Configure an institution manually in the first milestones: a user-visible
  name, FinTS HTTPS endpoint, and the BLZ/BIC or other identifiers required by
  that bank. The setup flow tells the user to copy the endpoint only from an
  authenticated bank channel or bank-issued documentation, displays the exact
  hostname separately from the friendly name, validates the URI, requires
  HTTPS, retains ordinary certificate and hostname checks, and asks the user to
  confirm the endpoint before first use. Any endpoint or hostname change is
  quarantined until the user repeats that verification and reauthenticates.
  BPD/UPD returned by the authenticated bank—not the manual label—becomes the
  authority for live capabilities. Manual entry still carries phishing and
  misdirection risk; HTTPS validation cannot prove that the user chose the
  intended institution, so this residual risk is disclosed rather than hidden.
- Maintain multiple institutions and multiple logins at one institution.
- Negotiate protocol and business-transaction versions instead of hard-coding
  one bank profile.
- Retrieve and persist bank parameter data and user parameter data with their
  versions and refresh rules.
- Capture user ID, customer ID where needed, PIN, selected TAN procedure, and
  TAN medium without conflating those concepts.
- Support challenge continuation rather than treating authentication as one
  request: manual TAN, decoupled/app approval, polling, and visual challenge
  procedures are separate handlers. For FinTS 3.0, the first implementation
  covers only the HKTAN segment version and process variant in the single M1
  compatibility row. Later rows add procedures one at a time with fixtures and
  live evidence.
- Request the banking PIN when required for a session. PINs and TANs are never
  persisted in the first releases and never enter the local archive.
- Represent connection states explicitly:
  `Unconfigured`, `Authenticating`, `ChallengeRequired`, `Connected`,
  `ReauthenticationRequired`, `TemporarilyUnavailable`, and `Disabled`.
- Handle changed credentials, locked access, cancelled challenges, endpoint
  changes, new bank parameters, and maintenance without deleting good cached
  data.
- Treat reauthentication as a normal connection state. Do not embed one fixed
  SCA-renewal interval in the domain; apply the bank response and the legal
  rules applicable to the selected connector and operating model.
- Show the last successful contact, last attempted contact, connector version,
  and a plain-language recovery action.
- Disconnect cleanly, clear in-memory credentials, remove connection
  configuration, and offer separate deletion of cached financial data.

### Accounts and values

- Discover all accounts returned for the authenticated user and let the user
  select which ones appear.
- Discover every account the bank returns, but assign support by account type.
  M1 fully validates only the payment/current account type in its one approved
  compatibility row. Savings, credit-card, credit/loan, and depot accounts may
  initially appear as an identity-only unsupported shell; their distinct
  details and semantics arrive only after separate milestone evidence. Manual
  cash accounts are an M6 feature.
- Show institution, product name, owner when supplied, masked IBAN or account
  identifier, currency, alias, and last successful refresh.
- Keep booked balance, available balance, credit line/overdraft, and other
  bank-supplied value types distinct. Never relabel one as another.
- Show an unavailable value as unavailable, never as `0.00`.
- Preserve the bank's balance timestamp and show a visible stale indicator.
- Display assets and liabilities separately.
- Aggregate only values in the same currency. Any future converted total must
  identify its exchange-rate source and timestamp and must not replace source
  values.
- Allow aliasing, ordering, grouping, favorites, hiding, and archiving without
  modifying bank-owned identifiers.
- Provide manual refresh, per-account progress, partial-result warnings, and an
  offline cached view.

### Transactions and details

- Retrieve both booked and pending transactions when exposed by the bank.
- Preserve booking date and value date as distinct date-only concepts.
- Normalize amount, currency, counterparty, IBAN/BIC, structured and
  unstructured remittance information, bank reference, end-to-end ID,
  transaction type, fees, mandate ID, and creditor ID where supplied.
- Preserve corrections, reversals, returned direct debits, and bank revisions
  rather than rewriting history invisibly.
- Link pending entries to booked entries only with unique evidence; ambiguity
  remains visible and cannot silently change totals.
- Continue multi-page responses, resume interrupted sync, overlap refresh
  windows, detect duplicates, and warn when the bank exposes only a limited
  history range.
- Search text, dates, amount ranges, direction, account, category, and status.
- Provide a detailed transaction view, tags, notes, user categories, and later
  split transactions.
- Mark a running balance as bank-supplied or locally calculated.
- Export selected data to CSV first; add versioned CAMT and MT940/MT942 import
  and export only where the semantics can be retained.

### Money movement

- Create a local payment draft without contacting the bank.
- Validate IBAN checksum, amount, currency, required remittance fields, source
  account capability, bank-advertised limits, and requested execution type.
- For covered EUR credit transfers, request the bank-provided Verification of
  Payee (VoP) result before authorization and represent pending, match, close
  match, no match, verification-unavailable, and bank warning states. Do not
  offer a general consumer opt-out. A mismatch warning does not itself block
  deliberate continuation unless an independent legal or bank control does.
- Require a review screen that presents the exact source account, recipient
  name and IBAN, amount, currency, execution type/date, remittance information,
  warnings, and VoP result before authorization.
- Continue through the bank-advertised SCA/TAN procedure. Broiler Fond never
  invents, weakens, or bypasses the bank's authorization flow.
- Support single SEPA credit transfers first, followed by own-account,
  instant, scheduled, and standing-order operations when advertised.
- Record a receipt with local attempt ID, timestamps, source, recipient, amount,
  connector response category, and non-secret bank references.
- Reconcile submitted orders with later bank order lists or account activity.
- Never claim settlement merely because a request was sent.

The payment state machine is part of the domain, not UI-only state. Draft edits
create explicit draft revisions; state changes are recorded in an append-only
history, and authorization/submission changes belong to one immutable
`PaymentAttempt`:

| State | Meaning | Allowed next state |
| --- | --- | --- |
| `Draft` | Editable local intent; nothing sent | `Validated` |
| `Validated` | Local and capability checks passed | `PayeeCheckPending`, or `PayeeCheckNotApplicable` only for an operation outside the applicable VoP scope |
| `PayeeCheckPending` | The bank has not returned a final VoP result | `PayeeMatch`, `PayeeCloseMatch`, `PayeeNoMatch`, `PayeeCheckUnavailable`, `Rejected` |
| `PayeeMatch` | Supplied name/account match | `AwaitingAuthorization` |
| `PayeeCloseMatch` | Bank supplied a close match and any corrected name | `AwaitingAuthorization`, `AuthorizationCancelled` |
| `PayeeNoMatch` | Bank supplied a mismatch warning | `AwaitingAuthorization`, `AuthorizationCancelled` |
| `PayeeCheckNotApplicable` | The operation is outside the applicable VoP scope, with the reason recorded | `AwaitingAuthorization` |
| `PayeeCheckUnavailable` | VoP could not produce a result; required bank warning is retained | `AwaitingAuthorization`, `AuthorizationCancelled`, `Rejected` |
| `AwaitingAuthorization` | Awaiting the bank's SCA/TAN flow | `Submitting`, `AuthorizationCancelled`, `Rejected` |
| `Submitting` | The write boundary may be crossed | `Accepted`, `BankPending`, `Rejected`, `Unknown` |
| `Accepted` | Bank accepted the order; settlement is not yet inferred | `BankPending`, `Reconciled` |
| `BankPending` | Bank reports ongoing processing | `Accepted`, `Rejected`, `Unknown`, `Reconciled` |
| `Unknown` | Connection failed after the order may have reached the bank | `Accepted`, `BankPending`, `Rejected`, `Reconciled` after explicit status/reconciliation evidence |
| `AuthorizationCancelled` | This attempt ended before submission | Terminal; a retry creates a new attempt from the draft |
| `Rejected` | Bank authoritatively rejected this attempt | Terminal; a retry creates a new attempt from the draft |
| `Reconciled` | Later authoritative order or booking evidence resolved the attempt | Terminal |

`Unknown` is durable and user-visible. Broiler Fond must not automatically
resubmit it. A user-requested retry creates a separate attempt and keeps the
earlier history visible.

### Personal finance and reporting

- User-defined and deterministic rule-based categories.
- Tags, notes, transaction splitting, and merchant normalization.
- Monthly budgets with category progress.
- Income, expense, balance, and cash-flow reports whose included transactions
  can be inspected.
- Recurring-payment and income detection with editable user confirmation.
- Upcoming-payment and low-balance alerts.
- Net-worth history without silently converting currencies.
- Forecasts based on known recurring items, always labeled as estimates.
- Manual cash/offline accounts kept visibly separate from bank-synced accounts.
- Encrypted backup, tested restore, interoperable export, and application-level
  erasure with explicit device, filesystem, and external-backup limitations.

### Mature and optional capabilities

- Electronic bank statements, bank mailbox messages, notices, and documents.
- Direct-debit mandate views and, in their separately gated milestones,
  return/revocation workflows where the account and bank explicitly advertise
  them.
- Credit-card transactions, loan schedules, and securities/depot read-only
  views.
- FinTS real-time notifications only where the institution exposes them to the
  user-operated client without a Broiler relay.
- Native Linux, macOS, and mobile hosts built on the same platform-independent
  kernel after the Windows host and storage format are proven. A host port does
  not imply cloud synchronization.
- A direct bank API may be researched only when that bank permits the retail
  user-operated client to connect without a Broiler or partner relay. Hosted
  PSD2/XS2A aggregation is outside this product roadmap.
- A separate professional edition with EBICS, CAMT/pain batch workflows,
  multiple users, roles, distributed approvals, and accounting exports.

## Protocol and connector strategy

| Interface | Role | Planned stage | Notes |
| --- | --- | --- | --- |
| FinTS 3.0 PIN/TAN | Primary direct bank connector | M1 onward | Use advertised segment versions, TAN procedures, BPD, and UPD; do not model all banks as identical |
| FinTS 4.1 | Additional FinTS connector/capabilities | M9 | Post-1.0 protocol expansion only where practical institution evidence justifies it |
| Direct retail bank API | Research only | Later, bank by bank | Eligible only if a user-operated client can connect directly; never routed through Broiler or a licensed-partner aggregation service |
| ISO 20022 CAMT and MT940/MT942 account-data files | Read-only import/export and interoperability | M3 onward | File support is not equivalent to a live bank connection |
| ISO 20022 pain payment-order files | Payment/batch interoperability | Professional track | Never treat importing a payment order as read-only data import |
| EBICS | Professional/business banking | Separate later track | Batch and distributed-approval needs differ from the consumer product |
| Browser scraping | None | Out of scope | Fragile, difficult to secure, and not a strategic connector |

Legacy HBCI protocol versions are not a new-development target. The FinTS site
states that editorial maintenance of HBCI 2.01/2.1 ended in 2005 and HBCI 2.2
ended in 2016. The term HBCI remains relevant within FinTS for specified
security procedures, but the product baseline is FinTS.

### Connector contract

Every live connector should expose the same typed operations:

- Discover or validate an institution endpoint.
- Open, continue, and close an authenticated dialogue/session.
- Return capabilities scoped to connection and account.
- Discover accounts and retrieve balance snapshots.
- Retrieve transactions using explicit ranges and continuation tokens.
- Validate, initiate, continue authorization for, and query a payment.
- List and manage scheduled orders where supported.
- Return structured bank messages and typed errors.

Connector errors must distinguish at least `UserActionRequired`,
`AuthenticationFailed`, `AuthorizationCancelled`, `Unsupported`,
`TemporarilyUnavailable`, `MalformedResponse`, `Rejected`, and
`UncertainWriteOutcome`. Institution-specific workarounds stay in versioned
connector compatibility profiles; they do not leak into the canonical domain.

## Architecture outline

The product is built around a new `Broiler.Fond.Kernel` targeting .NET 10 or
newer. The kernel is platform-independent, developed in this repository, and
has no `PackageReference`, native library, operating-system API, UI reference,
or third-party runtime dependency. Its production dependency graph ends at the
.NET shared framework. Test-only tooling must remain outside the shipped kernel
and cannot change that invariant.

```text
Windows host + Broiler.UI                 Later hosts
          |                         Linux | macOS | mobile
          +---------------+---------------+
                          |
               Broiler.Fond.Kernel (net10.0+)
          +---------------+----------------+
          |               |                |
   Application/domain   FinTS codec     Local store
   identity + rules     + dialogue      XML -> ZIP -> AEAD
          |               |                |
          +------- .NET runtime APIs only -+
```

The Windows host owns presentation, window lifecycle, and packaging. It calls a
host-neutral kernel API; the kernel never references Windows or `Broiler.UI`.
Future hosts reuse the same kernel and encrypted archive format rather than
forking financial behavior. Network access uses .NET networking APIs from the
kernel and always goes directly from the user's device to the chosen bank.

The kernel grows by complete vertical slices rather than attempting all FinTS
segments at once:

| Milestone | Kernel increment |
| --- | --- |
| M0 | Buildable `net10.0` boundary, domain-shaped contracts, Windows host seam, dependency guard, and CI; no banking behavior |
| M1 | FinTS 3.0 dialogue, BPD/UPD, one proven PIN/TAN path, store-wide allocator, account/binding/incarnation identity, account ambiguity, balances, and the encrypted profile store |
| M2 | Observation and transaction identity, durable replay evidence, booked/pending formats, range/continuation sync, corrections/revisions, search projections, and export |
| M3 | Additional proven FinTS variants, multibank scheduling, cross-profile merge/remap, explicit account relinking, CAMT/MT940 import, and compatibility evidence |
| M4 | SEPA credit-transfer validation, VoP, authorization continuation, submission, and reconciliation |
| M5 | Instant, scheduled, own-account, standing-order, and direct-debit return/revocation operations where advertised |
| M6 | Deterministic personal-finance projections and extended read-only products |
| M7 | Windows packaging, migration, recovery, support lifecycle, and 1.0 hardening |
| M8 | Linux, macOS, and mobile host qualification on the shared, unforked kernel and compatible archive contracts |
| M9 | Evidence-backed FinTS 4.1 protocol expansion after Windows 1.0 without weakening FinTS 3.0 support |

### Canonical domain model

| Entity/value | Purpose and invariant |
| --- | --- |
| `Institution` | Stable local identity plus user-entered endpoint metadata; not the source of live capabilities |
| `BankConnection` | One authentication context and its state; its PIN exists only for the active operation/session |
| `Account` | Stable user-facing local identity to which aliases, notes, and rules attach; it is never derived from an IBAN |
| `AccountIncarnation` | One bank-serviced account lifetime, separating closure/reopening or source-identifier reuse |
| `AccountBinding` | One connector/connection path to an account incarnation, with exact source locator and effective interval |
| `AccountCapability` | Operation and version currently advertised for a specific connection/account |
| `Money` | Exact amount plus mandatory currency |
| `BalanceSnapshot` | Balance type, money, bank timestamp, retrieval timestamp, source, and the original `AccountBindingId` and `AccountIncarnationId` |
| `SourceObservation` | Immutable occurrence received from a bank/file, retaining the original `AccountBindingId`, `AccountIncarnationId`, exact source location, and references even after relinking |
| `Transaction` | Stable local economic-record identity projected from one or more observations |
| `TransactionRevision` | Immutable bank-fact projection revision with explicit supersession and relationship evidence |
| `AnnotationRevision` | Immutable user-owned note, category, tag, or split revision, kept separate from bank facts and sync |
| `RelationshipDecision` | Append-only proposed/confirmed/rejected duplicate, correction, reconciliation, reversal, or relink decision |
| `SyncCheckpoint` | Range, continuation, completion, and gap state for an atomic sync run |
| `PaymentDraft` | Editable local intent that cannot itself be mistaken for a submitted order |
| `PaymentAttempt` | Immutable attempt and state transitions, including durable `Unknown` and the original source `AccountBindingId` and `AccountIncarnationId` |
| `AuthenticationChallenge` | Time-bounded, procedure-specific continuation with no stored TAN |
| `LocalDiagnostic` | Optional, bounded, encrypted on-device diagnostic record; never telemetry and never automatically transmitted |

Raw protocol payload retention is disabled in distributable builds. A user may
explicitly create a redacted support bundle, inspect its exact contents, and
choose where to save or send it. The application has no diagnostic-upload
endpoint.

### Portable local-store format

The local store is not a database. Its normative format is a versioned archive
implemented exclusively with .NET 10+ runtime APIs:

1. `System.Xml.XmlWriter` writes multiple UTF-8 XML documents such as
   `profile.xml`, `institutions.xml`, `connections.xml`, `accounts.xml`,
   partitioned transaction/observation documents, `payments.xml`, `rules.xml`,
   and `audit.xml`, followed by `manifest.xml`.
2. `System.IO.Compression.ZipArchive` compresses those entries. XML entry names,
   schemas, versions, counts, and uncompressed sizes are allow-listed. The final
   manifest records store lineage and a digest of the exact bytes of every
   non-manifest entry; it does not recursively digest itself. The outer AEAD
   authenticates the manifest and the rest of the compressed archive. Entry
   paths use only opaque local IDs, never account or user data.
3. A framed stream encrypts the compressed bytes with
   `System.Security.Cryptography.AesGcm` using a 256-bit key and 16-byte tags.
   Every frame has a unique 96-bit nonce for that key. Authenticated associated
   data binds the container/version header, frame ordinal, plaintext length,
   ordering, and final-frame marker so reordering, deletion, insertion, or
   truncation fails closed.
4. The profile master key is derived once per unlock from the user's passphrase
   and a stable 32-byte profile salt with the static
   `Rfc2898DeriveBytes.Pbkdf2` API and SHA-256. The clear but authenticated
   header records only format/algorithm identifiers, bounded KDF work factor,
   salts, framing parameters, and random nonce prefix—never user, bank, or
   account metadata. A valid Unicode passphrase is normalized to Unicode NFC
   (`NormalizationForm.FormC`) and then encoded with strict UTF-8 before the KDF;
   there is no trimming or case conversion. This exact rule is displayed when
   a passphrase is created and versioned with the container so all hosts derive
   identical bytes.
5. Every generation gets a new 32-byte random snapshot salt and separate
   content key from `HKDF.DeriveKey` with SHA-256 and a format-purpose label.
   Each frame nonce is the generation's four-byte random prefix followed by its
   monotonically increasing 64-bit frame sequence; a final authenticated frame
   and physical EOF are required. Key and plaintext buffers are cleared with
   `CryptographicOperations.ZeroMemory` where the runtime permits.

Compression always precedes encryption. No plaintext archive or XML temporary
file is written to disk. `AesGcm.IsSupported` is a host qualification gate. A
fixed-size framed envelope and a seekable authenticating reader allow
`ZipArchive` to work without decrypting the entire profile at once. Exact
binary framing, nonce construction, frame size, limits, and test vectors are
frozen in a versioned ADR before the first profile is created. Cross-host test
vectors include composed and decomposed Unicode passphrases, non-ASCII text,
empty and maximum-length fields, XML bytes, ZIP metadata, KDF output, every
authenticated frame, and the final file digest.

XML has one deterministic lexical contract: UTF-8 without a byte-order mark,
one fixed XML declaration, LF line endings, invariant-culture number/date
formats, ordinal schema-defined element and attribute order, and no indentation
or insignificant whitespace. Digests cover those exact serialized bytes, not a
later XML canonicalization transform. Readers accept only the encodings and
lexical forms declared by the schema/version; writers never depend on host
locale, newline convention, or ZIP timestamps for semantic identity.

The passphrase KDF is calibrated on the supported Windows floor with a release
minimum and unlock-time target; readers also enforce a maximum before doing KDF
work. PBKDF2 is CPU-hard rather than memory-hard because the .NET-only rule
excludes Argon2/scrypt. Weak passphrases therefore remain vulnerable to offline
guessing, and this limitation must appear in the prerelease security review.
Managed memory, paging, hibernation, and operating-system dumps can also retain
copies that `ZeroMemory` cannot reach. Prefer mutable bounded buffers, minimize
the unlocked lifetime, and disable automatic dump collection/upload; do not
claim perfect in-memory erasure.

XML readers set `DtdProcessing.Prohibit`, set `XmlResolver` to `null`, and apply
nonzero document/entity limits. Reads also cap entry count, individual and total
uncompressed bytes, nesting, strings, and compression ratio; entries are parsed
in memory/streams and are never extracted to paths supplied by an archive.
Documents validate against schemas embedded in the kernel; external schemas,
DTD/entity expansion, and `schemaLocation` are never honored. Unknown required
schema content fails closed. Unknown optional content is preserved only where
the version contract explicitly permits it.

Saving uses one writer and one monotonically increasing store generation. It
writes a `CreateNew` same-directory temporary candidate through the final
authenticated frame, flushes it durably, closes it, and reopens it to verify
every tag plus the XML manifest and identity invariants. Only a verified file is
promoted to its immutable generation name; the preceding valid generation is
retained only under the documented recovery-retention policy. Startup ignores
incomplete or invalid candidates and selects the highest fully authenticated
generation with consistent lineage. Even if a future host/filesystem cannot
promise atomic rename, validation prevents a partial target from displacing the
prior valid generation.

Privacy deletion and passphrase rotation use an explicit purge transaction
under the exclusive profile lock. Account/data deletion first commits and
verifies a replacement generation that omits the selected data; passphrase
rotation creates fresh profile and snapshot salts and a newly encrypted
generation. The client then closes all handles, deletes every superseded
current/prior generation and every recognized same-profile temporary candidate,
durably records the new state where the host permits, and verifies that none of
those application-managed files remains selectable or readable. Whole-profile
erasure deletes all validated generations and candidates. Purge targets come
only from verified lineage metadata and fixed filename rules, never an untrusted
archive path.
Copies outside the managed profile—especially user-created backups—must be
deleted separately by the user. SSD remapping, copy-on-write snapshots,
journaling, OS/device backups, and storage forensics prevent a guarantee of
physical byte erasure; device encryption and platform deletion controls remain
necessary, and the UI states this limitation.

A read-write session holds an exclusive profile lock using `FileStream` with
`FileShare.None`, plus an in-process single-writer guard; a second instance must
stop or open read-only. Network shares, cloud-synchronized directories, and
removable filesystems are unsupported initially and require separate fault
qualification before the support table can name them.

The first implementation rewrites a complete generation. XML is partitioned by
account and bounded time range so memory, search-index rebuild, and recovery can
be measured. Search indexes are derived and rebuildable; they never become an
identity authority. The performance gates are measured before increasing the
supported profile size. On the versioned 100,000-transaction Windows reference
profile, a verified snapshot save and a verified encrypted backup each complete
within 10 seconds at p95, and either operation adds no more than 256 MiB to the
idle unlocked working set. Release evidence records application-issued bytes
written both per final-generation byte and per changed logical byte. One
committed save may write at most one complete candidate (no more than 1.10 times
the final encrypted generation plus a fixed documented header allowance), and
one completed user operation may trigger at most one committed snapshot; burst
edits must be coalesced. Filesystem journal/device amplification is measured in
the reference profile when observable but is reported separately because the
runtime cannot control it.

An encrypted backup is a verified copy of this same portable format. Restore
requires the same profile passphrase and then asks again for bank PINs. A backup
protects availability; it is not passphrase recovery. Losing the passphrase
makes both the live archive and same-passphrase backups unrecoverable unless a
separately designed recovery mechanism exists. Because this purely local design
has no independent trusted anchor, an attacker able to replace all profile
generations with an older valid set can cause undetectable rollback; the UI must
show generation and last-save time, and the security documentation must state
this limitation.

### Collision-free local identity and source ambiguity

Regulation (EU) 2024/886 adds recipient-name/account Verification of Payee; it
does not create a globally unique transaction identifier. End-to-end IDs and
bank references may be supplied, omitted, reused, scoped, or not exposed by a
FinTS institution. Fond therefore guarantees unique identity inside one active
authenticated store lineage and collision-safe restore/import: a collision can
never silently lose, overwrite, or merge a record. It does not claim impossible
global certainty about indistinguishable source records or disconnected forks.

- One store-wide monotonically increasing unsigned 64-bit allocator creates
  every `LocalEntityId` in the same atomic commit as its entity. IDs are never
  derived from an IBAN, external reference, content hash, timestamp, or random
  GUID; they are not reused inside the active lineage, and exhaustion fails
  closed.
- Startup verifies uniqueness, `NextId > Max(Id)`, reference integrity, and
  acyclic revision graphs. Failure opens recovery/read-only mode and never
  repairs by discarding a record.
- Every received occurrence first receives an immutable `ObservationId`.
  External references are stored losslessly with account incarnation,
  connector and format version, issuer/scope, reference kind, and exact value.
  `UETR`, `EndToEndId`, `TxId`, `AcctSvcrRef`, `NtryRef`, FinTS references, and
  mandate/creditor IDs remain distinct evidence fields.
- A SHA-256 canonical fingerprint may find candidates, but equality always
  compares the full canonical tuple. A fingerprint or external reference is
  never a primary key and never merges records alone. Reused references create
  a visible `ReferenceCollision`; ambiguous candidates remain separate.
- Replay suppression requires durable evidence that the same persisted sync
  request/page/continuation was applied twice, or a unique bank-declared
  reference in its exact scope. Identical bytes, fields, dialogue/message
  numbers, or row ordinal alone are insufficient: absent durable replay evidence,
  retain another observation/candidate. One-to-one matching preserves
  multiplicity, so two indistinguishable rows in one response remain two.

Correction model:

- Bank observations are immutable. A uniquely evidenced change appends a
  `TransactionRevision` with `SupersedesRevisionId`; otherwise it creates a
  separate transaction plus `PossibleCorrectionOf`.
- Pending and booked entries remain auditable. Only unique compatible bank or
  locally owned payment-attempt evidence can confirm reconciliation. Otherwise
  both remain with `PossiblePendingSuccessor`. Ordinary absence means only
  `NotObservedInWindow`; `NoLongerReported` requires an explicit bank status or
  a complete authoritative snapshot whose declared coverage includes the item.
- Reversals, returned direct debits, refunds, split postings, and rebookings are
  separate linked events. The original is never silently deleted or negated.
- User notes, categories, tags, and splits use separate append-only
  `AnnotationRevision` records, never `TransactionRevision`, so bank
  synchronization cannot overwrite user-owned history.
- Every proposed or confirmed relationship records its evidence, algorithm
  version, time, and reversible decision. Ambiguity is visible and cannot
  silently change totals.
- Bank-supplied balances are never recalculated from ambiguous entries. A local
  report with an unresolved candidate group shows a provisional lower/upper
  range and the included/excluded observations instead of one unqualified total.

Portability and account relinking:

- `AccountId`, `AccountIncarnationId`, `AccountBindingId`, and connection identity
  are separate. Reauthentication with the exact same source locator retains the
  binding. A new connection, endpoint, connector, changed IBAN, or conflicting
  locator creates a quarantined candidate.
- The lossless source locator retains connector/version, local institution,
  IBAN, domestic account number and bank code, subaccount marker, currency, and
  every supplied bank-specific identifier. Normalized forms are indexes, not
  replacements for raw source identity.
- Cross-connection/connector relinking requires explicit user confirmation and
  shows old/new institution, identifiers, owner, currency, type, last activity,
  and conflicts. Name, balance, or transaction similarity alone can never
  relink. Ambiguous candidates stay separate.
- A relink changes only bindings. Local account identity, observations,
  annotations, history, and payment attempts remain stable; every decision can
  be undone without deleting observations.
- Every balance snapshot, source observation, and payment attempt permanently
  retains the `AccountBindingId` and `AccountIncarnationId` that were active at
  its origin. Relinking can change the current projection but never rewrites
  this provenance.
- A full restore into an empty profile preserves existing IDs but establishes a
  new writable allocation branch before creating new entities. Recovery to an
  older generation does the same. Import into a nonempty profile validates
  first, allocates all-new target IDs, rewrites references through a complete
  map, validates again, and atomically commits. Equal lineage/ID with different
  content is a fork conflict and is remapped, never treated as identity. Thus
  restored/forked counters may overlap outside one store, but cannot collide
  after a validated merge. PINs and TANs are never portable because they are
  never stored.

## Security, privacy, and compliance gates

### FinTS product registration and directory terms

- Since 1 August 2019, the German Banking Industry Committee requires FinTS
  access to identify a registered product during dialogue initialization.
- Apply at the start of M1. A FinTS library/kernel registration may be used only
  for internal tests. Broiler Fond's own application registration must be
  integrated before any external tester, distributable build, or user-facing
  live-bank dialogue. The official FAQ describes a normal processing time of
  10–15 working days, but the plan must tolerate delays.
- The official FinTS bank list is available only for registered products, is
  not guaranteed complete or current, and may not be redistributed as part of
  the software product. Do not commit or bundle that list.
- The first milestones do not use, bundle, or fetch an institution directory.
  Users enter the endpoint manually and the live bank's BPD/UPD controls feature
  enablement. A future directory remains a separate product/legal decision and
  is not a prerequisite for the early releases.

FinTS product registration is not the same thing as regulatory registration or
authorization under the ZAG.

### Regulatory decision gate

Before public testing, legal counsel must classify the exact purely local,
user-operated distribution and data flow:

- Under ZAG section 34, a commercial provider offering only an account
  information service generally requires BaFin registration and supporting
  governance, security, continuity, and liability-cover evidence.
- Payment initiation is a payment service under ZAG section 1 and generally
  falls under the section 10 authorization regime; section 49 adds duties for
  payment-initiation providers.
- Account-information access requires explicit user consent and purpose-bound
  data use under ZAG section 51.
- Strong customer authentication and dynamic linking requirements are reflected
  in ZAG section 55 and the applicable PSD2 RTS.
- The client has no Broiler-operated account aggregation, account-information
  service, payment-initiation service, credential relay, or bank-data backend.
  This document does not infer a legal exemption from that architecture;
  obtain a written opinion for its exact request, update, and support flows.
- As of 4 September 2026, PSD3 and the Payment Services Regulation remain in the
  legislative procedure and are not yet in force. PSD2, its applicable RTS,
  and the current ZAG remain the operative baseline. Track the final Official
  Journal texts, application dates, transposition deadlines, and transition
  provisions rather than freezing the design against PSD2 alone.
- A future regulated AISP/PISP or hosted model—including its DORA duties—would
  be a different product decision and is not an implementation path in this
  roadmap.
- Treat account and transaction data as personal data and apply GDPR-by-design
  controls throughout. Determine roles and lawful basis for distribution,
  updates, and any user-initiated support export. Purely personal or household
  processing performed locally by the user may fall under GDPR Article 2(2)(c),
  but that conclusion must be confirmed for the exact product. There is no
  telemetry, synchronization service, or hosted financial-data processing.
- Assess whether the German BFSG and BFSGV apply to the exact product/provider
  model. Where they do, record the applicable conformance standard and evidence
  for perceivable, operable, understandable, and robust authentication,
  security, and payment workflows, including the BFSGV's consumer-information
  language requirement. Do not treat a WCAG target alone as the legal analysis.
- Do not make non-payment-account aggregation depend on the proposed EU
  Financial Data Access framework until its legislation and transition rules
  are final.

No Broiler-operated cloud aggregation, account-information service,
payment-initiation service, credential relay, or remote financial-data
processing is part of this product. Legal review can block distribution but
cannot silently expand that boundary.

### Payment safety

- Verification of Payee is a first-class pre-authorization step, including
  close-match, no-match, unavailable, pending, and warning paths supplied by
  the bank. For covered EUR transfers, the service applies regardless of the
  initiation channel. Do not offer a consumer opt-out; Article 5c(6) of
  Regulation (EU) 2024/886 limits opt-out to non-consumers submitting multiple
  payment orders as a package. A mismatch warning must not by itself prevent
  authorization. Current FinTS specifications include VoP business
  transactions and response codes.
- Display the exact bank challenge and transaction-linked data without
  truncating or obscuring material differences.
- Store neither TANs nor reusable challenge responses.
- Serialize payment submission per connection unless protocol evidence proves
  that parallel submission is safe.
- Provide a user-visible local read-only safe mode and compile-time/release
  capability allow-list. A safety issue is handled by a signed update and
  advisory; no remote kill switch or remote-control channel exists.
- Require failure injection immediately before send, after send, before bank
  response, during SCA, and during reconciliation.

### Security and privacy requirements

- Encrypt and authenticate every local profile generation with the portable
  XML/ZIP/AEAD format above. Do not use a database or OS credential vault.
- Derive the profile master key from a user passphrase. Request banking PINs
  only when required and never persist PINs or TANs in the first releases.
- Never place PINs, TANs, complete account identifiers, payment descriptions,
  raw bank messages, or encryption keys in local logs, dumps, or support
  bundles.
- Apply ordinary TLS certificate and hostname validation with no user-facing
  insecure bypass or silent downgrade.
- Minimize secret lifetime in memory where the runtime permits and prevent
  secret-bearing UI fields from clipboard/history capture by default.
- Lock the application after configurable inactivity and define safe behavior
  for device loss, key loss, backup, restore, and account removal.
- Use signed builds and updates, an SBOM, dependency and secret scanning,
  reproducible provenance where practical, and the human release gate below.
- Implement no telemetry, analytics, tracking ID, crash uploader, automatic
  diagnostic upload, usage counter, or background reporting endpoint. Keep
  bounded diagnostics encrypted on-device; export is explicit and previewable.
- Enforce a documented network-egress allow-list. Apart from direct bank traffic,
  only a user-started signed-update download may connect outward; it carries no
  stable installation/profile identifier and never includes banking data.
- Implement GDPR-oriented minimization, purpose limitation, retention,
  interoperable export, and verified application-level purge. Do not describe
  deletion as guaranteed physical erasure across SSDs, filesystem snapshots,
  device backups, or user-created archive copies.
- Keep an authenticated local audit of payment state transitions without
  authentication secrets. It is tamper-evident within a verified encrypted
  generation and lineage, but it is not a forensic log against a person who
  holds the unlocked key and can rewrite history, nor against wholesale rollback
  to an older valid generation without an independent trusted anchor.

### Human prerelease and release gate

Every externally distributed prerelease and every stable release is blocked
until two named humans approve the exact candidate: a release owner and a
second security reviewer. Developer-only local builds are not releases.

The repository records the candidate commit, artifact hashes, reviewers, date,
decision, and evidence for the current threat model, storage/crypto vectors,
malformed-input and failure tests, secret scan, kernel dependency graph, SBOM,
signature verification, privacy/no-telemetry check, accessibility status, and
known limitations. Critical or high-severity security findings block release.
Payment-enabled candidates additionally require an independent penetration
test covering submission ambiguity, SCA, VoP, local persistence, and recovery.
Approval of an older commit or artifact never carries forward automatically.

## Milestone roadmap

### At a glance

| Milestone | User-visible outcome | Release posture |
| --- | --- | --- |
| M0 — Buildable foundation | Developers can build and verify the architecture skeleton; no banking function exists | Internal engineering baseline, not distributable |
| M1 — Create access to accounts and list values | Link existing accounts and see trustworthy balances | Technical alpha |
| M2 — Transactions and account details | Inspect, search, and export booked/pending activity | Read-only MVP |
| M3 — Reliable multibank read-only beta | Dependable sync across a declared compatibility table | Public beta candidate |
| M4 — Safe SEPA money movement | Initiate one standard transfer with VoP, SCA, and honest status | Controlled transactional alpha |
| M5 — Everyday payments | Own-account, instant, scheduled, standing-order, and direct-debit action workflows | Transactional beta |
| M6 — Insights and planning | Explainable budgets, reports, forecasts, and extended products | Feature-complete candidate |
| M7 — Windows 1.0 | Stable support, storage, security, compliance, and release promises | Windows 1.0 candidate |
| M8 — Additional hosts | Qualify Linux, macOS, and mobile on the shared kernel and compatible data contracts | Post-1.0 host releases |
| M9 — FinTS 4.1 protocol expansion | Add evidence-backed newer protocol capabilities without weakening established support | Post-1.0 protocol release |

Durations are indicative effort ranges for a cross-functional team of roughly
four to six people building the new FinTS kernel and BCL-only store in-house.
Live test-bank access and FinTS product registration can change the ranges
materially. Exit criteria, not dates, decide when a milestone is complete.

M0 establishes the build and policy boundary only. Threat-model completion,
product registration, the bank simulator, frozen storage details, and all live
bank behavior remain M1 work unless explicitly listed as documentation stubs in
M0.

### M0 — Buildable foundation

**Outcome:** A policy-enforced, platform-aware repository skeleton builds and
can be inspected before any banking, storage, credential, or network behavior
is implemented.

**Indicative effort:** 1–2 weeks.

Scope:

- Repository, solution, .NET 10 SDK selection/roll-forward policy, shared
  compiler/analyzer settings, deterministic build settings, formatting rules,
  and package-source policy.
- A `net10.0` `Broiler.Fond.Kernel` project containing only domain-shaped value
  types, identifiers, capability descriptors, and placeholder ports. It has no
  connector, persistence, cryptography, credential, scheduling, or business
  implementation.
- A Windows host shell that references the kernel but exposes no account or
  payment action and performs no network or profile-file access.
- BCL-only executable verification tests plus a build-time dependency guard that
  rejects package, native, platform, host, UI, unsafe, COM, and kernel project
  references.
- CI that restores, formats, builds, runs the boundary guard and skeleton tests,
  and smoke-runs the inert host on Windows, then independently builds/verifies
  the kernel on Linux.
- Initial ADR, security-policy, compatibility-register, milestone, and human
  prerelease-review templates. Their pending fields are explicit; M0 creates no
  fictional approval or support claim.
- No telemetry, analytics, crash upload, generated institution list, live-bank
  fixture, production secret, installer, published artifact, or external
  distribution.

Exit criteria:

- A clean .NET 10 SDK selected by the repository roll-forward policy can restore
  and build the solution warning-free; formatting verification, the skeleton
  executable tests, and dependency guard pass in local and CI-equivalent runs.
- The platform-neutral kernel builds and passes its boundary checks on both
  Windows and Linux; the initial host builds only on Windows.
- The shipped-project graph contains no `PackageReference`, native binary,
  platform API, host/UI reference, or third-party runtime dependency.
- Running the host produces only a clear milestone-zero message. It creates no
  profile, opens no socket, asks for no credential, and cannot list accounts or
  initiate money movement.
- Security and release-review templates remain visibly pending. M0 is an
  internal engineering baseline and is not an externally distributed
  prerelease.

Explicitly deferred: all FinTS/HBCI codecs and dialogue behavior, bank
registration use, institution connections, storage/container implementation,
cryptography, account discovery, balances, transactions, payments, SCA, support
claims, packaging, and release approval.

### M1 — Create access to accounts and list values

**Implementation status:** In progress. The manual endpoint validation,
confirmation, and quarantine domain model is implemented. Proposed storage
envelope/bootstrap specifications and independent test vectors now exist;
in-memory identity allocation, startup validation, lossless source locators and
exact rediscovery planning, exact money and account-value/freshness projections
are implemented. A test-only scripted read workflow and opt-in structured
in-memory diagnostic previews now cover synthetic outcomes and secret sentinels;
bounded FinTS byte syntax, element encoding and outer framing now have independent
synthetic vectors. Typed HIRMG/HIRMS responses and local dialogue correlation
are also implemented, together with bounded HIBPA/HIUPA/HIUPD evidence.
Read-operation parameter schemas and explicit-version capability evidence are
implemented, alongside credential-free account-discovery/balance schemas and
a bounded synthetic SCA attempt model. Pure single-account read context matching
and partial-page provenance checks are implemented, along with a bounded local
read-refresh attempt, scoped unavailable outcomes and bounded all-account
discovery evidence with a local one-response attempt lifecycle. Typed unsigned
discovery/balance request encoding has independent exact wire fixtures.
Restricted initialization identification/preparation schemas and unsigned
encoding also have independent fixtures; no authenticated session is established.
Pure initialization response binding checks dialogue/reference and parameter
identity scope, with missing data and unknown observations retained for review.
A bounded local initialization attempt now adds pinned request/user context,
one-time evidence handoff, timeout, cancellation and terminal reference cleanup.
Synchronization schemas and unsigned encoding now preserve system identifiers,
message numbers and exact signature references without applying recovery state.
Pure synchronization context comparison now checks mode/profile and prior-dialogue
scope, with matching observations requiring close and reinitialization.
Dialogue-end schemas and unsigned encoding now compare standard closing replies,
preserving reported closure, explicit abort and unresolved scope separately.
A bounded local synchronization-and-closing attempt now requires explicit closing,
hands each stage's evidence out once and enforces a shared absolute deadline.
Restricted PIN/TAN signature-header schemas now preserve identity, reference,
timestamp and filler observations with explicit profile/code constraints.
Pure signature-header context checks now bind initialization/synchronization
identity and explicit procedure origins, preserving missing and ambiguous evidence.
Typed signature-header encoding now emits independently verified bytes with
strict text preservation, canonical optional fields and explicit precision bounds.
A local credential ownership primitive now clears transferred input, consumes
TAN copy-out once and zeroes private storage on termination. Access expiry is
checked on calls; secure input and live session cleanup integration remain pending.
Local HNSHA-2 encoding now checks TAN placement and writes bounded caller-owned
output with temporary/failure-path cleanup and one-time TAN consumption.
Plain PIN/TAN initialization/synchronization assembly now preserves bound
fields, renumbers segments, computes exact framing and clears staged/failed
whole-message output. Plaintext request-envelope assembly now binds public
metadata and exact binary payloads with cleanup through final output handoff.
Immutable assembled-request metadata now compares response references against
actual segment roles without capturing credential output. Assembled initialization
now compares bound status and parameter/envelope identities with exact response
provenance. Assembled synchronization now checks unique report shape, bound status,
envelope identity and explicit recovery limits, retaining close/reinitialization as
the required next step. An assembled initialization attempt now pins one candidate,
enforces an absolute deadline, hands evidence out once and releases terminal
metadata. An assembled synchronization attempt now pins candidate/recovery context,
enforces a fixed deadline and returns scoped evidence once, always requiring
closing/reinitialization after a match. PIN/TAN closing context/encoding now binds
synchronization dialogue/counter and identity scope and emits PIN-only envelopes
with bounded staging and failed-output cleanup. Assembled closing response checks
now map actual segment roles and compare dialogue/counters/envelope identity,
distinguishing reported closure and abort from unresolved evidence. A bounded
closing attempt now owns one candidate and deadline, returns evidence once and
releases metadata on terminal outcomes. The synchronization handoff preserves exact
provenance and closure still requires fresh initialization. An explicit assembled
initialization path now integrates returned HITANS/3920 evidence for the pinned
selection with exact-source checks and one-time combined handoff. Combined HIPINS
integration now preserves nullable length bounds and checks reported TAN flags,
withholding qualification for ambiguous or unsupported requirements. First-read
signature context now reuses the complete returned initialization result with
dialogue/counter, identity and unchanged selection checks. An explicit initialization
path now integrates read advertisements; first-read single-account comparison
checks returned permissions, identity, signature requirements and request options.
First-read credential comparison now checks supplied byte spans against reported
bounds and TAN formats while preserving missing requirements and caller ownership.
PIN-only read trailer encoding now applies the checks to owned staging bytes with
bounded expiry/cancellation and failed-output cleanup. Plain first-read assembly
now binds segment numbering, preserves explicit request fields and computes exact
framing with whole-message secret cleanup. Read-envelope assembly, TAN/challenge
integration, all-account capabilities, procedure activation and broader session
coordination remain pending.
Broader request/session context,
security conformance and actual SCA qualification remain pending. Full storage
schemas, durable integration, calibration, format approval, Windows setup and
banking flows remain pending. See the [M1 acceptance backlog](milestone-1.md).

**Outcome:** A technical alpha can connect to existing accounts and show
trustworthy bank-supplied balances.

**Dependency:** M0's architecture, build, dependency-boundary, and governance
controls continue to pass. M1 intentionally supersedes M0's inert-host and
no-implementation exit conditions with the first banking vertical slice.

**Indicative effort:** 16–24 weeks.

Scope:

- Windows app shell, local profile, application lock, passphrase unlock, and the
  versioned XML/ZIP/framed-AES-GCM store.
- FinTS application registration for `Broiler Fond - Finance on Demand` started
  immediately and its ID integrated before any external tester or user-facing
  live-bank dialogue.
- Grow the M0 `Broiler.Fond.Kernel` vertical slice while retaining automated
  proof that its `net10.0` project/runtime graph contains no package, native,
  platform, host, or UI dependency.
- Scriptable, non-production FinTS simulator and redacted conformance fixtures.
- Manual institution name, required identifier, and validated HTTPS FinTS
  endpoint entry copied from an authenticated bank source. The exact hostname
  is confirmed independently of the friendly label; changes are quarantined
  until reconfirmation and reauthentication. No directory is bundled or
  contacted, and the residual risk of choosing a convincing wrong endpoint is
  disclosed.
- FinTS 3.0 dialogue initialization, BPD/UPD retrieval, protocol capability
  negotiation, PIN/TAN authentication, and the one challenge continuation path
  selected for the first verified compatibility row.
- Session-only banking credentials and explicit connection deletion.
- Account discovery and selection; the store-wide monotonic allocator; distinct
  account, incarnation, and binding identities; exact rediscovery; and no silent
  conflation of ambiguous accounts.
- Fully supported payment/current accounts for the first row; other returned
  account types use an explicit identity-only shell until separately certified.
- Account list and account detail summary.
- Booked/available balance types, currency, bank timestamp, retrieval timestamp,
  freshness, refresh, and actionable error states.
- Encrypted local diagnostics plus explicit previewable redacted support export;
  no network reporting path.

Exit criteria:

- On a clean install, a test user can reach the first balance within five
  minutes under normal bank conditions.
- The simulator covers success, invalid credentials, locked access, the one M1
  TAN/SCA continuation path, user cancellation, timeout, maintenance, changed
  parameters, malformed response, and partial result. Other challenge families
  are added with later compatibility rows.
- Live validation covers one manually configured institution, one
  payment/current account type, and one bank-advertised SCA procedure in the
  first compatibility-table row. No production credentials or payloads enter
  source control or CI.
- Account identifiers and balances match the corresponding bank view for every
  release test, allowing only documented timestamp differences.
- Repeated discovery and refresh do not duplicate accounts or change their
  stable identity. Reapplying a response with durable evidence that it belongs
  to the same persisted request/page produces the same latest visible balance
  state; identical bytes alone do not establish replay.
- Allocator exhaustion, reused source locators, closed-and-reopened accounts,
  changed endpoints, and ambiguous account candidates fail closed or remain
  separately visible without overwriting an existing local identity.
- A missing or unsupported value appears as unavailable, not zero.
- Mixed-currency totals are not produced.
- PINs and TANs are absent from the archive, local logs, dumps, and exported
  diagnostics. Sentinel-secret tests verify every supported error and support
  path. Transient buffers are cleared where practical; dumps are disabled or
  access-protected and are never uploaded automatically.
- Removing a connection removes all connection state and offers deletion of its
  cached accounts and values through the verified purge protocol, including
  retained generations and candidates under application control; external
  backups and physical-media limitations are shown. No credential-vault item
  exists.
- Archive round-trip, wrong-passphrase, tamper, truncation, hostile XML/ZIP,
  interrupted-save, prior-generation recovery, and lost-key behaviors pass.
- The named release owner and second human security reviewer approve the exact
  alpha artifact with no unresolved critical or high-severity findings.

Explicitly deferred: transactions, scheduled background sync, payments,
budgets, automatic institution directory, additional hosts, and broad
compatibility claims. Cloud sync is out of scope rather than deferred.

### M2 — Transactions and account details

**Outcome:** A read-only MVP explains what changed in each account.

**Indicative effort:** 10–14 weeks.

Scope:

- Booked and pending transaction retrieval in the formats actually advertised
  by each bank.
- Range requests, continuation/pagination, checkpoints, overlap sync, restart,
  and gap detection.
- Canonical transaction fields and a complete detail view.
- Immutable source observations with original binding/incarnation provenance,
  durable request/page replay evidence, collision/reference-reuse groups,
  pending-to-booked candidate relationships, bank-fact transaction revisions,
  separate user annotation revisions, append-only corrections, reversals, and
  returned payments.
- Search, filters, tags, notes, and privacy-conscious CSV export.
- Cached offline browsing and visible per-account sync freshness.
- Portable encrypted backup in the same XML/ZIP/AEAD family without PINs or
  TANs; restore validates before replacement and creates explicit reconnect work.

Exit criteria:

- The app retrieves the maximum supported history range and clearly states when
  an institution limits it; it does not promise an arbitrary history length.
- Ten repeated overlapping syncs of every conformance fixture yield no extra
  visible event or balance impact, while two identical rows in one source remain
  two distinct observations.
- An interrupted multi-page sync resumes without hiding a gap or publishing a
  partial run as complete.
- Unique evidence can link pending to booked without double counting; ambiguous,
  one-to-many, reference-reuse, correction, reversal, and rebooking cases remain
  separate and auditable until resolved.
- Exact source amount/currency, booking date, and value date survive round-trip
  persistence and export.
- A user can identify the source, freshness, status, and available raw bank
  reference fields for an individual transaction without viewing a raw protocol
  payload.
- Warm local search completes within 300 ms at p95 with 100,000 cached
  transactions on the versioned Windows reference profile.
- Accessibility, backup/restore, upgrade, and privacy-deletion tests pass for
  the read-only data model.

### M3 — Reliable multibank read-only beta

**Outcome:** The read-only product is supportable across its declared verified
compatibility table before payment code is enabled.

**Indicative effort:** 8–12 weeks.

Scope:

- Multiple institutions, multiple logins, more TAN methods/media, reconnection,
  and account relinking.
- Permission-aware on-device scheduled refresh while the client is running and
  a bank does not require interactive action. It may use a banking PIN only when
  that PIN is already held in bounded memory for an explicitly unlocked,
  user-opted-in foreground session; it never persists the PIN, opens an
  unattended prompt, or extends credential lifetime just for scheduling. When
  the PIN or SCA is unavailable, refresh becomes `UserActionRequired` without a
  retry/prompt loop. There are no server jobs.
- Per-institution concurrency, retry/backoff for safe reads, maintenance states,
  and atomic local commits.
- Versioned connector compatibility profiles shipped inside signed application
  releases; no remote profile-control channel.
- One flat verified compatibility table, expanded row by row without tiers.
- A separate known-incompatibility ledger records attempted combinations that
  fail or are unsafe, including evidence, limitation, affected build, owner, and
  last test date; failed rows cannot disappear merely because they never enter
  the verified table.
- Backup migration/restore hardening, fork/merge ID remapping, and explicit
  reauthentication because banking credentials are not stored or transferred.
- Versioned CAMT and MT940/MT942 account-data import for a limited offline
  fallback.
- Read-only beta support workflow, local test evidence, and user-initiated,
  previewable redacted support bundle.

Exit criteria:

- The verified table reaches at least three manually configured institutions
  across two banking groups and two distinct SCA procedures.
- Every verified row passes the recorded simulator suite plus 20 consecutive
  eligible live read runs; bank outage and user-cancelled runs are recorded, not
  hidden from the release evidence.
- A connector workaround can be disabled locally by configuration or by a
  signed application release without any remote switch.
- Offline data remains internally consistent after process termination, network
  loss, duplicated pages, reordered responses, and local clock changes.
- Restore preserves local identities and mappings, then creates an explicit
  reconnect task; it never silently drops protection or embeds credentials.
- Product, privacy, regulatory classification, and independent security reviews
  are recorded before public beta distribution, with no unresolved critical or
  high-severity findings.

### M4 — Safe SEPA money movement

**Outcome:** A controlled transactional alpha can initiate a single standard
SEPA credit transfer without lying about its status or risking an automatic
duplicate.

**Indicative effort:** 12–18 weeks.

Scope:

- Local draft, source-account selection, beneficiary entry, and validation.
- Verification of Payee workflow and bank warnings.
- Exact review, bank-controlled SCA/TAN continuation, single submission, and
  receipt.
- Durable payment state machine including accepted, rejected, cancelled,
  pending, and unknown outcomes.
- Duplicate-risk detection, manual recovery, order/transaction reconciliation,
  and user-visible local read-only safe mode.
- Local non-secret audit history and a payment-specific support runbook.
- An immutable local `PaymentAttempt` ID and locally unused `EndToEndId` are
  committed durably before the first request byte can be sent. A transport
  continuation reuses that attempt; a new authorization creates a new attempt.

Exit criteria:

- Controlled low-value transfers succeed for at least one explicitly verified
  payment row; unverified institutions cannot expose the action.
- The user sees the exact amount, currency, recipient name, IBAN, execution
  mode/date, remittance information, VoP outcome, and all bank warnings before
  authorization.
- A failure before submission is safely retryable. A failure after possible
  submission creates `Unknown`, is never automatically retried, and directs the
  user to reconciliation.
- Repeating an identical draft after an earlier attempt requires explicit user
  action and presents that earlier attempt.
- Blank, reused, or conflicting end-to-end and bank references never overwrite
  or coalesce a payment attempt or resulting booking; the conflict remains
  visible until uniquely reconciled.
- `Accepted` or `Completed` appears only when supported by an authoritative bank
  response; `Submitted` alone is not success.
- Every state transition is auditable without persisting PINs, TANs, or reusable
  challenge data.
- Failure injection passes at every network and SCA boundary.
- Legal authorization/registration requirements and production operating model
  for the purely user-operated client are signed off.
- An independent penetration test has no unresolved critical or high-severity
  findings, and the two-human candidate gate passes, before real users can
  enable payments.

### M5 — Everyday payments

**Outcome:** Common consumer money-management actions work where each bank
advertises them.

**Indicative effort:** 8–12 weeks.

Scope:

- Transfers between the user's own accounts.
- Instant SEPA transfers with limits, fees/warnings, VoP, and explicit selection.
- Scheduled transfers.
- Standing-order list, create, change, and delete.
- Direct-debit return and mandate-revocation writes where the account and bank
  explicitly advertise them, using the same exact review, SCA, durable-attempt,
  unknown-outcome, and reconciliation controls as other money movement.
- Beneficiary templates protected as financial data.
- Bank order lists, cancellation where supported, richer status reconciliation,
  and additional SCA challenge renderers.

Exit criteria:

- At least three institutions across two banking groups have one or more
  explicitly verified payment-operation rows; each row names its exact SCA path.
- Unsupported operations are absent or clearly disabled with an explanation;
  no bank-name heuristic enables a write.
- An instant payment cannot be silently downgraded to a standard transfer or
  vice versa.
- Creating, editing, or deleting a scheduled order shows its bank-confirmed
  final state or an explicit unknown/pending state.
- A direct-debit return or revocation is never inferred from a local state
  change; it has an authoritative bank-confirmed result or remains visibly
  pending/unknown and is never automatically retried.
- Templates never bypass current validation, VoP, review, or SCA.
- The payment conformance suite covers every supported operation, response
  category, challenge type, and ambiguous network boundary.

### M6 — Insights and planning

**Outcome:** The product moves from bank transport to useful, explainable
personal financial management.

**Indicative effort:** 8–12 weeks.

Scope:

- Categories, previewable rules, splits, tags, and merchant normalization.
- Budgets, recurring-item detection, upcoming obligations, low-balance alerts,
  cash-flow views, and clearly labeled forecasts.
- Net-worth history grouped by currency.
- Electronic statements, credit-card details, loans, and depot read-only views
  where supported.
- Bank mailbox messages/notices plus read-only direct-debit mandate views where
  advertised. Direct-debit return/revocation writes belong to M5 and are not
  implemented as reporting actions.
- Richer interoperable export and optional direct-to-client FinTS notifications
  without a Broiler relay.

Exit criteria:

- Every report can reveal exactly which source transactions and rules produced
  a value.
- Category rules are deterministic, reversible, and previewable before bulk
  application.
- Forecast, pending, booked, and manually entered values remain visually and
  semantically distinct.
- Alert evaluation runs entirely on-device and does not imply a guaranteed
  future balance.
- Extended account types have separate accuracy fixtures and do not reuse
  payment-account semantics where they differ.

### M7 — Windows 1.0

**Outcome:** Broiler Fond has a stable support promise, release process, and
Windows host suitable for a 1.0 declaration.

**Timing:** Evidence-driven; allow at least two quarters of beta operation.

Scope:

- Freeze and harden the evidence-backed FinTS 3.0 capability set promised for
  Windows 1.0; newer protocol expansion is deliberately outside this release.
- Windows packaging, signed update artifacts, incident response, compatibility
  rollback, storage-migration guarantees, and a published support lifecycle.
- Direct bank APIs only where a retail user-operated client can connect without
  a Broiler or licensed-partner relay, and only after separate evidence.
- Independent security, privacy, accessibility, and architecture review of the
  named release candidate.

Exit criteria:

- Every connector passes the same domain-contract, malformed-input, and
  failure-injection suites.
- Every published compatibility row passes its release corpus and live evidence
  window without silently lowering its claimed operation set.
- No unresolved critical/high security findings and no unresolved issue capable
  of silently changing a payment recipient, amount, currency, or status.
- Backup, recovery, deletion, store migration, connector downgrade, rollback,
  and device-loss exercises have recorded evidence.
- Product registration, legal review, incident processes, and disclosures
  required by the purely user-operated model are in place.
- Accessibility and localization claims are backed by test evidence rather
  than inferred from toolkit capability.

### M8 — Additional hosts

**Outcome:** Linux, macOS, and mobile releases reuse one shared, unforked kernel
and compatible versioned profile contracts without introducing a Broiler data
service.

**Timing:** Evidence-driven after the Windows 1.0 storage and kernel contracts
are stable. Hosts need not ship simultaneously.

Scope and exit criteria:

- Qualify Linux and macOS desktop hosts first; sequence mobile hosts according
  to demand, runtime support, SCA handoff behavior, and accessibility evidence.
- Keep all banking, identity, reconciliation, and persistence logic in the
  shared platform-neutral kernel; host code owns only presentation, lifecycle,
  secure input affordances, and packaging. Kernel changes remain possible, but
  they are versioned once, preserve declared compatibility, and must pass every
  qualified host rather than forming host-specific forks.
- Require `AesGcm.IsSupported`, archive interoperability test vectors, filesystem
  crash tests, TLS behavior, locale/accessibility checks, and live compatibility
  rows on each host/architecture combination.
- A profile created or backed up on one qualified host opens on every other
  qualified host with the same passphrase and exact local IDs. Bank PINs are
  entered again after transfer.
- Mobile lifecycle suspension during SCA, background restrictions, screenshots,
  clipboard, backups, and device-loss behavior have a dedicated threat model.
- Each desktop/mobile target runs trimming/linker analysis and published,
  trimmed execution tests. Where NativeAOT or another ahead-of-time mode is
  selected, serialization, reflection, cryptography, networking, globalization,
  and every connector path pass explicit AOT tests; unsupported dynamic-code
  paths fail the host qualification instead of being silently removed.
- Each host artifact independently passes the two-human prerelease/release gate.
- No host adds telemetry, cloud sync, credential relay, or platform-specific
  code to `Broiler.Fond.Kernel`.

### M9 — FinTS 4.1 protocol expansion

**Outcome:** Post-1.0 releases can add FinTS 4.1 capabilities where concrete
institution evidence warrants them, without treating specification recency as
support and without regressing the established FinTS 3.0 matrix.

**Timing:** Evidence-driven after Windows 1.0; M8 host work need not manufacture
or delay a protocol row, but every host advertised for that row must qualify it.

Scope and exit criteria:

- Implement only the FinTS 4.1 dialogue, security, segment, and business-
  transaction slices required by named candidate rows; unsupported or unknown
  versions fail closed.
- Preserve the connector contract, canonical identities, portable archive,
  payment safety state machine, and direct user-operated network boundary.
- Keep FinTS 3.0 and 4.1 capabilities explicit and separately testable; neither
  version is silently upgraded, downgraded, or inferred from an institution
  name.
- Add redacted golden fixtures, malformed-input/failure-injection coverage, and
  live evidence for every claimed institution/account/operation/SCA/host row.
- Record failed or unsafe combinations in the known-incompatibility ledger, and
  publish no support claim until the exact row meets the then-current release
  evidence threshold.
- Every distributed candidate passes the same legal-change review, independent
  security requirements, and two-human artifact gate as the existing release;
  payment-capable additions also pass the payment penetration-test gate.

### Separate professional track

Do not add professional workflows by merely exposing more buttons in the
consumer permission model. Create a separate plan for:

- EBICS connectivity.
- CAMT/pain batch exchange and bulk payments.
- Multiple users, roles, four-eyes approval, and distributed signatures.
- Accounting exports and longer audit retention.
- Organization-level key recovery, policy, support, and compliance.

## Cross-cutting definition of done

A capability is done only when all applicable items below are true:

- The connector contract, canonical model, UI states, and errors agree.
- Capability discovery controls availability; unsupported data is not fabricated.
- Unit, contract, golden-fixture, property, malformed-input, and failure tests
  cover the happy path and material error branches.
- On-device logs/traces, disabled dump paths, and explicit support export pass
  automated secret and financial-data checks; no telemetry path exists.
- Cancellation, timeout, restart, upgrade, and partial response are defined.
- Keyboard-only, screen-reader, high-contrast, text-scale, German/English, and
  locale-format behavior is tested.
- The threat model, privacy inventory, and data-retention table are updated.
- Kernel dependency verification proves no package, native, platform, UI, or
  third-party runtime dependency was introduced.
- Source-ID reuse, exact duplicate rows, ambiguous matching, revision, relink,
  restore, merge, and rollback cases preserve every local entity and never
  silently change totals.
- User documentation explains freshness, bank capability differences, and
  recovery from action-required or unknown states.
- Any distributed candidate passes the separate two-human release gate.

## Test strategy

- Redacted golden FinTS messages for parser and serializer behavior.
- Property-based tests for exact money, IBAN validation, dates, pagination,
  deduplication, and reconciliation.
- Fuzzing for malformed segments, hostile text, unexpected encodings, oversized
  payloads, deeply nested XML, duplicate fields, and unknown segment versions.
- A scriptable bank simulator for dialogue, BPD/UPD, TAN/SCA, pagination, VoP,
  payments, maintenance, and ambiguous connection failure.
- One domain-contract suite run against every connector.
- Controlled live test accounts across banking groups and SCA procedures, with no
  production secrets in CI or shared fixtures.
- Locally/CI-operated read-only test runs; tightly controlled low-value payment
  tests only after legal, security, and operational approval. Results are
  release evidence, never reports from installed user clients.
- Failure injection before and after every payment submission boundary.
- Store migration, backup/restore, corrupted-store, interrupted-generation,
  fork/merge, device-loss, key-loss, application-managed purge, retained-
  generation cleanup, external-backup disclosure, and downgrade exercises.
- Cross-host portable-store vectors cover NFC/UTF-8 passphrase derivation, exact
  XML lexical bytes, manifest scope, ZIP/framed-AEAD interoperability, and
  wrong-normalization failures.
- Static analysis, dependency/secret scanning, SBOM generation, signed artifact
  verification, and independent penetration testing before payments.
- Manual parity checks against bank-owned views as release evidence.

## Initial service and quality objectives

These are product targets, not claims about bank endpoint availability:

- The scripted Windows reference-profile soak runs for 24 hours without a crash,
  unbounded memory/handle growth, archive corruption, or lost committed data.
- Every verified compatibility row completes at least 20 consecutive eligible
  live safe-read runs before release; bank outage, maintenance, and explicit
  user cancellation are recorded separately.
- Cached account overview visible within two seconds at p95 on the reference
  profile.
- Local search within 300 ms at p95 for 100,000 transactions.
- Verified full-snapshot save and encrypted-backup operations each complete
  within 10 seconds at p95 on the same 100,000-transaction reference profile,
  with no more than 256 MiB additional working set.
- Application-issued write amplification stays within the store gate: at most
  one full candidate and 1.10 times the final encrypted-generation size plus
  the fixed header allowance per committed snapshot, with changed-logical-byte
  amplification reported rather than hidden.
- Support at least 20 connections, 100 accounts, and 100,000 transactions in one
  local profile without correctness degradation.
- Zero cases in which an uncertain payment is automatically submitted twice.
- Zero known leakage of PIN, TAN, encryption key, or raw financial content into
  local diagnostics, dumps, or support artifacts.
- Zero analytics, telemetry, automatic crash-upload, or background-reporting
  network requests in binary inspection and runtime network tests.

Release evidence records connection completion, time to first balance, sync
results by table row/kernel version, freshness, reconciliation cases, challenge
outcomes, payment-state coverage, unknown-payment recovery, archive performance,
including save/backup latency, peak memory, and write amplification, and recovery
tests. Evidence comes only from the simulator, fixtures, project test accounts,
and human testing; it is never collected from installed clients.

### Release-test profiles and terminology

Each release candidate stores its evidence against versioned profiles. A phrase
such as “representative” or “normal conditions” is not sufficient by itself.

- The initial **compatibility table** is flat: institution/manual endpoint label,
  FinTS and segment version, account type, operation, SCA procedure, Windows
  build, test date/owner, result, and known limitation. M1 contains one verified
  row. Anything absent is “not yet verified,” not implicitly supported. Rows are
  added only after their exact fixtures and live tests pass; there are no tiers.
- The separate **known-incompatibility ledger** records every materially tested
  institution/endpoint, protocol/segment, account type, operation, SCA, host,
  build, observed failure or safety reason, evidence owner/date, and retest
  state. It is not a support tier, but it prevents a pass-only table from hiding
  failures; a row moves or closes only with preserved superseding evidence.
- The **tested SCA set** is exactly the procedures named in verified rows. M1
  requires one procedure, not an artificial manual/visual/decoupled spread.
- **Normal bank conditions** for the time-to-first-balance usability measure
  means the endpoint is not in declared maintenance, completes TLS and protocol
  responses within the recorded test thresholds, credentials are valid, and
  the tester completes each requested SCA action within the scripted allowance.
  The raw timings and exclusions are retained with the result.
- The **Windows reference profile** pins Windows/.NET version, CPU architecture,
  memory, filesystem/storage, encrypted archive size, clean/warm state, build
  configuration, sample count, and percentile calculation. A performance gate
  cannot pass until that profile is committed as release evidence. M8 defines a
  separate profile for every additional host/architecture.
- An **eligible live run** begins after the bank endpoint passes the recorded
  availability precheck and excludes only declared maintenance, explicit user
  cancellation, and incomplete required SCA; every exclusion remains visible.
- **Every response category** means every typed connector outcome plus every
  operation-specific category recorded in the conformance corpus. Coverage is
  reported by connector, operation, and state transition.

## Principal risks and mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Institution-specific FinTS behavior | High | Capability negotiation, conformance fixtures, versioned compatibility profiles, and a small verified compatibility table |
| Changing TAN/SCA procedures | High | Explicit challenge state machine and one-at-a-time fixture/live qualification |
| Incorrect regulatory classification | Critical | Fixed user-operated boundary plus written review of the exact distribution, update, and support flows before external release |
| Credential or financial-data exposure | Critical | Passphrase-protected authenticated store, nonpersistent bank credentials, short secret lifetime, no telemetry, signed artifacts, and two-human review |
| Duplicate transfer after timeout | Critical | Durable `Unknown`, attempt history, reconciliation, explicit retry, and no automatic retry of ambiguous writes |
| Payee-name mismatch mishandled | Critical | First-class VoP states, exact warnings, deliberate override path only when bank/law permits, and audit evidence |
| Missing, duplicated, or colliding source records | High | Immutable observations, monotonic local IDs, full-tuple comparisons, fingerprints only as candidates, append-only revisions, and visible ambiguity |
| CAMT/MT940 or FinTS version drift | High | Versioned parsers, source-preserving normalization, golden fixtures, and reject-safe unknown handling |
| Mistyped, phished, or changed manual endpoint | High | Tell users to use an authenticated bank source, show the exact host separately, enforce HTTPS and ordinary PKI/hostname checks, quarantine changes, require reconfirmation/reauthentication, retain endpoint history, and disclose that TLS cannot prove the user chose the intended bank |
| Insufficient live test access | High | Secure test-account program and a small realistic compatibility table as milestone exit gates |
| Archive corruption, key loss, rollback, or stale deleted data | Critical | Authenticated frames, verified generation writes, bounded prior-generation retention, tested backup/restore, same-passphrase/no-reset warning, verified purge of managed generations/candidates, external-copy disclosure, and documented physical-erasure/anti-rollback limits |
| Snapshot store misses performance target | Medium | Bounded XML partitions, derived in-memory indexes, staged size gates, and measurement before increasing the supported profile limit |
| Scope creep into brokerage/business banking | Medium | Keep depot read-only and put EBICS, batches, and multi-user approval on a separate track |

## Resolved foundation decisions

These decisions are normative. M0 records their architecture boundaries; M1
freezes the binary/file and protocol details in ADRs without reopening their
product direction:

1. **Operating model:** purely user-operated; direct device-to-bank traffic; no
   Broiler or partner aggregation, AIS/PIS relay, cloud data, or remote jobs.
2. **Kernel:** new in-repository `Broiler.Fond.Kernel` for .NET 10+, grown by
   milestone, platform-independent, and free of third-party runtime dependencies.
3. **Hosts:** Windows first; Linux, macOS, and mobile follow as separately
   qualified hosts over the same kernel and archive.
4. **Local storage:** multiple versioned XML documents, ZIP compression, then a
   framed AES-256-GCM encrypted stream; PBKDF2/HKDF keys, migrations, backup,
   validation, and recovery use only .NET runtime APIs.
5. **Institution setup:** manual in the first milestones. A future automatic
   directory is deliberately deferred until its source, licence, freshness,
   signing, and distribution policy are chosen; it does not block early work.
6. **Support promise:** one flat compatibility table starting with one verified
   row and growing only through recorded fixture/live evidence.
7. **Telemetry:** none—no analytics, tracking, automatic crash upload, diagnostic
   upload, or background reporting.
8. **Identity:** monotonic local IDs are authoritative; source identifiers and
   hashes are evidence only. Collisions never silently overwrite or merge.
   Corrections are append-only and relinking is explicit, reversible, and
   portable through ID-preserving restore or validated ID-remapped merge.
9. **Release authority:** every distributed prerelease and release needs a named
   human release owner plus a second human security reviewer; payments also need
   an independent penetration test.
10. **Naming:** `Broiler` is the brand, `Fond - Finance on Demand` is the product,
    `Broiler Fond - Finance on Demand` is the full display name, and
    `Broiler.Fond` is the technical name. This is product branding, not a literal
    Git branch name; Git refs cannot contain spaces.

## Immediate next actions

1. Apply for the `Broiler Fond - Finance on Demand` FinTS product registration
   and record its owner.
2. Commission a short legal memo for the exact user-operated client,
   distribution/update mechanism, user-initiated support export, and direct
   payment flows.
3. Complete and continuously verify the M0 `net10.0` kernel/Windows-host
   skeleton and CI guard that rejects package, native, platform, host, and UI
   dependencies.
4. Freeze the XML/ZIP/framed-AEAD binary format, limits, KDF calibration,
   generation protocol, and interoperability/tamper vectors in ADRs.
5. Build the FinTS simulator and secret-redaction harness, then implement the M1
   dialogue/BPD/UPD/account/balance vertical slice.
6. Secure one controlled live test account and record the first manual
   institution/SCA/account-type compatibility row.
7. Assign the human release-owner/security-reviewer roles and create the
   artifact-level prerelease checklist.
8. Turn M1 exit criteria into tracked acceptance tests and backlog items.

## Authoritative references

References were reviewed on 2026-09-04. They are product inputs, not legal
advice.

- Deutsche Kreditwirtschaft, [FinTS overview](https://www.fints.org/de/startseite)
  — history, multibank purpose, and reported institution coverage.
- Deutsche Kreditwirtschaft, [FinTS specifications](https://www.fints.org/de/spezifikation)
  — FinTS 3.0/4.1 status and retired HBCI versions.
- Deutsche Kreditwirtschaft, [current FinTS changes](https://www.fints.org/de/spezifikation/aenderungen)
  and [news](https://www.fints.org/de/aktuelles) — current business transactions,
  VoP, response codes, instant payments, and other specification changes.
- Deutsche Kreditwirtschaft, [FinTS product registration](https://www.fints.org/de/hersteller/produktregistrierung)
  and [registration FAQ](https://www.fints.org/de/hersteller/faq-produktregistrierung)
  — registration requirement, product/library distinction, and processing
  guidance.
- Deutsche Kreditwirtschaft, [FinTS bank-list terms](https://www.fints.org/de/hersteller/bankenliste)
  — access, freshness, completeness, redistribution, and BPD constraints.
- German Federal Ministry of Justice, ZAG
  [section 1](https://www.gesetze-im-internet.de/zag_2018/__1.html),
  [section 2](https://www.gesetze-im-internet.de/zag_2018/__2.html),
  [section 10](https://www.gesetze-im-internet.de/zag_2018/__10.html),
  [section 16](https://www.gesetze-im-internet.de/zag_2018/__16.html),
  [section 34](https://www.gesetze-im-internet.de/zag_2018/__34.html),
  [section 36](https://www.gesetze-im-internet.de/zag_2018/__36.html),
  [section 49](https://www.gesetze-im-internet.de/zag_2018/__49.html),
  [section 51](https://www.gesetze-im-internet.de/zag_2018/__51.html), and
  [section 55](https://www.gesetze-im-internet.de/zag_2018/__55.html) — service
  definitions, authorization/registration, provider duties, consent, and SCA.
- EUR-Lex, [Regulation (EU) 2024/886](https://eur-lex.europa.eu/eli/reg/2024/886/oj/eng)
  — instant euro transfers and Verification of Payee; it does not define a
  universal local transaction identity for this application.
- European Payments Council,
  [current SCT rulebook and implementation guidelines](https://www.europeanpaymentscouncil.eu/what-we-do/epc-payment-schemes/sepa-credit-transfer/sepa-credit-transfer-rulebook-and)
  — current SEPA scheme rules and message guidance; source transaction
  references remain evidence with defined scopes rather than Fond primary keys.
- EUR-Lex, [consolidated Delegated Regulation (EU) 2018/389](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:02018R0389-20230912)
  — current PSD2 SCA, secure communication, reauthentication, and account-access
  exemption rules.
- BaFin, [payment-service classification guidance](https://www.bafin.de/ref/19629832)
  — the need to assess the concrete technical and contractual service design.
- EUR-Lex, [Digital Operational Resilience Act](https://eur-lex.europa.eu/eli/reg/2022/2554/oj)
  and [General Data Protection Regulation](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:32016R0679)
  — potential regulated-provider resilience duties and general personal-data
  obligations.
- German Federal Ministry of Justice,
  [BFSG section 1](https://www.gesetze-im-internet.de/bfsg/__1.html),
  [BFSG section 3](https://www.gesetze-im-internet.de/bfsg/__3.html), and
  [BFSGV section 17](https://www.gesetze-im-internet.de/bfsgv/__17.html) —
  scope and additional accessibility requirements for consumer banking
  services, authentication, security functions, and payments.
- European Parliament,
  [PSD3 procedure](https://oeil.europarl.europa.eu/oeil/en/procedure-file?reference=2023%2F0209%28COD%29)
  and [Payment Services Regulation procedure](https://oeil.europarl.europa.eu/oeil/en/procedure-file?reference=2023%2F0210%28COD%29)
  — pending successor framework to track through final adoption and transition.
- EUR-Lex, [Financial Data Access proposal procedure](https://eur-lex.europa.eu/procedure/EN/2023_205)
  — proposed access framework for financial data beyond payment accounts.
- Microsoft Learn, [.NET 10 `AesGcm`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm?view=net-10.0)
  and [`Encrypt` nonce requirement](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt?view=net-10.0)
  — authenticated encryption support, platform check, tag behavior, and the
  prohibition on nonce reuse with the same key.
- Microsoft Learn, [.NET 10 PBKDF2](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2?view=net-10.0)
  and [HKDF](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hkdf.derivekey?view=net-10.0)
  — BCL-only profile-master and per-generation content-key derivation.
- Microsoft Learn, [.NET 10 `ZipArchive`](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive?view=net-10.0),
  [`XmlReaderSettings`](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings?view=net-10.0),
  [`CryptographicOperations`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations?view=net-10.0),
  and [`FileStream.Flush(Boolean)`](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-10.0)
  — compressed multi-document storage, hardened XML parsing, buffer clearing,
  and durable write primitives.

## Change log

- **2026-09-06:** Added HISALS/HISPAS read-parameter schemas and conservative
  explicit-version capability evidence. Five independent fixtures and 108 checks
  cover options, limits, unknown versions, scope and signature conflicts.
  ADR 0013 keeps security validation, actual request/response codecs and live
  capability activation pending.

- **2026-09-06:** Added bounded BPD/UPD parameter evidence and explicit listed,
  blocked, unknown and ambiguous permission states. Five independent fixtures
  and 181 checks cover fields, duplicates, limits, optional data and resource
  bounds. ADR 0012 leaves capability activation, security and live ingestion pending.

- **2026-09-05:** Added typed HIRMG/HIRMS response schemas and atomic in-memory
  dialogue correlation, with explicit unknown, pending and indeterminate states.
  Five independent fixtures and 177 checks cover schemas, references, replays,
  concurrency and counter exhaustion. ADR 0011 retains BPD/UPD, security,
  transport and authenticated domain ingestion as pending work.

- **2026-09-05:** Added bounded FinTS 3.0 byte syntax, individual element encoding
  and outer framing based on the reviewed Formals specification. Six independent
  synthetic vectors and 1,350 checks cover byte preservation, malformed input,
  truncations and resource limits. ADR 0010 keeps typed schemas, dialogue,
  security, transport and live banking explicitly pending.

- **2026-09-05:** Added a test-only read workflow simulator, abstract manual
  challenge continuation, structured in-memory diagnostics/support previews and
  280 checks. Fixed missing refresh/offline warnings on rows without a unique
  balance. ADR 0009 records scope; FinTS codecs, live SCA, encrypted logs and
  export UI remain pending.

- **2026-09-05:** Implemented exact money parsing/addition, a frozen currency-code
  reference, immutable balance provenance and pure account-value projections with
  freshness, exclusions and per-currency totals. Added ADR 0008 and 109 checks
  including a 10,000-account projection; live ingestion and UI remain pending.

- **2026-09-05:** Implemented lossless account source locators and a pure exact
  rediscovery planner with quarantined changed, ambiguous and closed-lifetime
  candidates. Added ADR 0007 and 90 checks including a 10,000-account batch.
  Authentication, durable application and user-reviewed relinking remain pending.

- **2026-09-05:** Implemented in-memory store-wide identity allocation batches,
  stale-writer rejection, exhaustion and startup integrity validation; added
  ADR 0006 and 84 checks including a 100,000-record revision chain. Durable
  payload commits, branch integration and exact rediscovery remain pending.

- **2026-09-05:** Added proposed storage envelope and bootstrap snapshot ADRs,
  trusted bootstrap XSD, five independently generated synthetic vectors, and
  BCL-only reference/tamper verification in the test project. No user profile
  persistence enabled; full schemas, KDF calibration and human review pending.

- **2026-09-05:** Started M1 with manual endpoint validation, explicit URL
  confirmation, endpoint-change quarantine, and executable verification. Added
  the M1 acceptance backlog; live banking and release approval remain pending.

- **2026-09-04:** Added the implementation-free M0 repository baseline; clarified
  store purge/passphrase portability, endpoint verification, provenance and
  collision sequencing, compatibility-failure evidence, storage performance
  gates, direct-debit milestone ownership, shared-host qualification, and a
  separate post-1.0 M9 for FinTS 4.1.
- **2026-09-04:** Closed the foundation decisions: user-operated model, new
  dependency-free .NET 10+ kernel, Windows-first hosts, manual institutions,
  XML/ZIP/AEAD store, flat compatibility table, no telemetry, collision-safe
  identity/corrections/relinking, release-level human review, and exact naming.
- **2026-09-04:** Initial draft: product direction, feature scope, connector
  strategy, security/compliance gates, M0–M9 roadmap, acceptance criteria,
  risks, and next actions.
