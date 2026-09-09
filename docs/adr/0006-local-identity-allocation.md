# ADR 0006: Local identity allocation and validation

- **Status:** Accepted (in-memory identity contract; durable integration pending)
- **Date:** 2026-09-05
- **Related:** [Roadmap identity rules](../roadmap.md#collision-free-local-identity-and-source-ambiguity),
  [snapshot candidate](0005-profile-snapshots-v1.md)

## Decision and scope

One `LocalIdentityLedger` owns the identity inventory for one active writable
store session. `LocalEntityId` is an unsigned 64-bit value in a single namespace
shared by all entity kinds, including institutions, connections, accounts,
incarnations, bindings, observations and balance snapshots. Reserved transaction
and revision categories do not enable transaction or payment functionality.
Existing typed ID structs remain value wrappers; constructing one is not an
allocation. Future entity factories must take their values from this ledger.

The implemented inventory contains identity metadata: ID, kind, typed local
references and an optional revision predecessor. It does not contain financial
entity payloads, profile/branch identity, archive generations, or source locators.
Thus the ledger alone does not implement durable atomic entity creation, restore,
import, rediscovery, relinking or semantic revision ownership checks.

## Counter and exhaustion

Zero is invalid. An empty new inventory starts with `NextId = 1`. Allocation
returns NextId and advances it by one. Persisted gaps are preserved; startup
never reconstructs or decreases the counter from the currently present records.

The largest issued ID is `UInt64.MaxValue - 1`. `UInt64.MaxValue` is reserved as
the exhausted NextId because the roadmap requires `NextId > Max(Id)` even after
the last allocation. An exhausted snapshot is valid and readable, but allocation
throws `IdentityAllocationExhaustedException`. Neither overflow nor a wrap to
zero is permitted. There is no random, timestamp, IBAN, reference or hash fallback.

Raw zero/invalid IDs remain representable in detached inventories so corruption
can be diagnosed without replacing or discarding records. Only a successfully
validated inventory can open a writable ledger.

## Staging and publication

`BeginBatch` captures the current immutable snapshot. A batch allocates
provisional IDs and stages the corresponding identity records. Nothing changes
in the ledger until `Commit` validates the complete candidate and atomically
publishes its records and counter under the ledger lock. Reads see a complete
old or new snapshot. Existing snapshots and caller-owned input lists cannot be
mutated through the returned collections.

Commit compares the captured snapshot by identity with the current snapshot.
Only one of two competing batches based on the same snapshot can publish. A
loser receives `StaleIdentityBatchException`; its caller must start a new batch
against current state and rebuild any dependent provisional references. Do not
retry using previously staged IDs. An empty commit preserves the same snapshot.

Validation failure, cancellation or stale-state rejection publishes nothing and
closes the batch. Disposal abandons it. IDs from an unpublished batch may later
be allocated again; they were never committed entities and must never be used as
durable replay evidence, exported identities or bank-request references. IDs of
committed records are never reused by this ledger. Refused allocation input does
not consume an ID, and a refused allocation at exhaustion does not prevent the
already staged final valid ID from being committed.

Caller-controlled reference enumeration occurs before taking the batch lock or
choosing an ID. This prevents enumeration callbacks from reentering allocation
between choosing a number and staging its record.

## Startup validation and recovery

`IdentitySnapshotValidator.Validate` reports structured issue codes and local
IDs; it does not echo account fields, external references or financial payloads.
`LocalIdentityLedger.TryOpen` returns the same diagnostics and a null ledger when
the inventory is invalid. The future host must route that outcome to recovery or
read-only inspection, retaining the authenticated source material.

Validation checks:

- nonzero NextId and entity IDs, recognized entity kinds;
- uniqueness across all kinds, including duplicate IDs in different categories;
- every allocated ID strictly below NextId;
- reference target existence, nonzero target IDs, and matching expected kinds;
- predecessors only on revision categories, predecessor existence and same kind;
- no self-cycle or multi-record cycle in the revision predecessor graph.

Reference resolution uses the complete inventory, so record order does not
invalidate forward references. Ordinary relationships may contain cycles; the
acyclic rule applies specifically to revision predecessor edges. Revision graph
validation is iterative and linear in records/edges, with no recursive stack
growth. A branching history is not silently merged; rules about ownership and
allowed successors require the future domain schema validators.

If duplicate IDs are found, a temporary diagnostic index may retain the first
entry to continue checking, but every duplicate remains in the original immutable
inventory and prevents writable opening. This is never a repair or deduplication
operation. A structurally valid inventory also cannot prove that an older,
previously deleted ID was never used: retaining the authenticated counter and
enforcing store lineage/history remain persistence responsibilities.

## Bounds

The domain inventory caps materialization at 1,000,000 entities, 64 ordinary
references per entity, and 4,000,000 total edges (including predecessors).
Construction throws on a size violation; a storage reader must retain the source
for recovery and enforce its own stream/byte bounds before constructing objects.
These bounds are ceilings, not a qualified supported profile size or a claim to
meet the roadmap's memory/performance gates.

Validation returns at most 100 issues. `IssueLimitReached` discloses that checking
stopped at the diagnostic limit; `IsValid` remains false. No record is removed to
meet that limit. Validation and commit accept cancellation. Full domain validation
must additionally enforce required reference roles/cardinality, ownership,
account/incarnation/binding provenance, immutable observations and revision scope.

## Required durable integration

The future profile store must own exactly one ledger per exclusive profile lock,
authorize the active allocation branch, and atomically persist the counter,
identity records and their complete entity payloads in one verified generation.
The current in-memory Commit must not be interpreted as a durable save. A durable
transaction coordinator must not publish a new application snapshot until its
generation has passed verification and promotion; failure must leave the old
snapshot current. This coordination is not implemented by the identity ledger.

Opening two independent ledgers from the same detached snapshot is possible and
does not create globally unique branches. The profile/session layer must prevent
that within one writable store. Restore/recovery must establish a new writable
branch before allocating, and import must validate and remap all entity IDs and
references. The local counter is never sufficient evidence for conflating records
across profiles, restored branches or independently authenticated generations.

## Verification

The BCL-only test runner checks mixed-kind sequential allocation, staging,
publication, disposal, cancellation, invalid commits, stale concurrent writers,
persisted counter gaps, exhaustion, immutable input copies, reentrant enumeration,
reference and entity bounds, every diagnostic category, forward references,
revision cycles, and a 100,000-record reverse-ordered revision chain.

No file, bank connection, credential or payment operation is introduced by this
increment. Filesystem fault injection, full entity schemas and cross-host release
evidence remain part of subsequent M1 work.
