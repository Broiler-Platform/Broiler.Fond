# ADR 0116: Beneficiary Templates and Address Book in Profile Store

## Status
Accepted

## Context
Milestone 5 introduces everyday payments, including an address book of saved beneficiary templates (`BeneficiaryTemplate`).
1. **Protected Financial Data:** Beneficiary names, IBANs, BICs, and recurring payment remittances are personal financial data. Templates are stored encrypted and authenticated inside the local profile archive (`.bffond`).
2. **Strict Workflow Bypass Guard:** A template provides pre-filled input for payment drafts. Applying a template *never* bypasses or short-circuits any safety boundary: SEPA validation, Verification of Payee (VoP), review screens, and bank-controlled SCA are strictly enforced on every resulting payment attempt.
3. **Template Lifecycle:** Users can add, edit, rename, and soft-delete templates. Soft-deletion preserves referential history while hiding the entry from active pickers.
4. **Backward Compatibility & Seamless Migration:** Older profiles from Milestone 1 (7-document), Milestone 2 (8-document), and Milestone 4 (9-document) must load seamlessly without errors or data loss. An absent `templates.xml` initializes an empty template collection, automatically upgrading to the 10-document archive on next save.
5. **Deterministic Serialization & BCL-Only Boundary:** Deterministic UTF-8 XML serialization against embedded `templates-v1.xsd`, byte-exact re-serialization guarantees, and zero third-party dependencies.

## Decisions

### 1. Domain Model (`BeneficiaryTemplate.cs`)
We implement in `src/Broiler.Fond.Kernel/Domain/Payments/BeneficiaryTemplate.cs`:
- `BeneficiaryTemplate`: Immutable record with `Id`, `Label`, `BeneficiaryName`, `BeneficiaryIban`, `BeneficiaryBic`, `DefaultRemittanceUnstructured`, `DefaultRemittanceCreditorReference`, `DefaultAmount`, `DefaultExecutionMode`, `IsSoftDeleted`, `CreatedAt`, and `UpdatedAt`.
- `PopulateDraft(PaymentDraftRevision draft)`: Implements the non-bypassing draft population invariant.
- `BeneficiaryTemplateValidator`: Ensures label $\le 70$ chars, beneficiary name $\le 70$ chars, valid MOD 97-10 IBAN checksum, valid BIC (8 or 11 chars), and positive EUR amount.

### 2. Local Entity Kind Extension
We extend `LocalEntityKind` in `src/Broiler.Fond.Kernel/Domain/Identifiers/IdentitySnapshot.cs` with `BeneficiaryTemplate`. Every template receives an immutable store-wide monotonic 64-bit entity ID recorded in `ledger.xml`.

### 3. XML Schema Definition (`templates-v1.xsd`)
We create `src/Broiler.Fond.Kernel/Storage/Schemas/templates-v1.xsd` embedded as `ProfileEnvelope.templates-v1.xsd`:
- Root `<templates xmlns="urn:broiler:fond:profile:1" version="1" profileId="..." branchId="..." generation="...">`.
- Repeated `<template id="..." isSoftDeleted="...">` entries containing `<label>`, `<beneficiary>`, optional `<defaultRemittance>`, optional `<defaultAmount>`, optional `<defaultCurrency>`, optional `<defaultExecutionMode>`, `<createdAt>`, and `<updatedAt>`.

### 4. Storage Archive Codec Integration (`StoreArchiveCodec.cs`)
We extend `StoreArchiveCodec.cs`:
- DTOs: `StoreBeneficiaryTemplateEntry` and `StoreTemplatesDocument`.
- `StoreSnapshot`: Includes `StoreTemplatesDocument? Templates` with `TemplatesOrDefault` fallback.
- Archive entry lists:
  - `LegacyStoreDocumentNames` (7 entries: M1)
  - `StoreDocumentNamesV2` (8 entries: M2)
  - `StoreDocumentNamesV3` (9 entries: M4)
  - `StoreDocumentNames` (10 entries: M5 modern, adding `templates.xml`)
- `SerializeTemplates`: Deterministic XML serialization with strict UTF-8, invariant culture, and canonical element ordering.
- `ParseAndValidateTemplates`: Validates against embedded `templates-v1.xsd`, depth ceiling $\le 8$, and lineage checking.
- `CreateStoreZip`: Includes `templates.xml` and manifest digest.
- `ExtractAndValidateSnapshotFromZipStream`: Extracts `templates.xml` if present, enforcing byte-exact equality against re-serialization. If missing, defaults to empty.
- `ValidateManifest`: Dynamically accommodates 6, 7, 8, or 9 document entries in `manifest.xml`.
- `ValidateIdentityAndSemanticInvariants`: Asserts template IDs exist in `ledger.xml` with kind `BeneficiaryTemplate`.
- `MergeSnapshotsWithIdRemapping`: Remaps template IDs into the target profile's monotonic allocator space.

## Consequences
- Beneficiary templates are protected as first-class financial records inside the encrypted `.bffond` container.
- Safe SEPA money movement cannot be bypassed using templates.
- Complete backward compatibility is maintained across all historical milestone archive formats.
