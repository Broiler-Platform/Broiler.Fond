# Balance refresh qualification — BR-09

## Decision and candidate

As of 2026-10-21, deterministic qualification covers the bounded BR-01–BR-08 implementation. **No live bank/procedure combination is qualified. General desktop balance-refresh release readiness is pending.** Priorities 1–7 now provide bounded desktop parameter acquisition/selection, synchronization (including restricted text TAN), reviewed discovery and first balance refresh. Durable parameter caching and broader discovery/read SCA remain unsupported. Passing deterministic tests does not establish live bank acceptance.

This is a local working-tree qualification record, based on commit `6df5b73d03e9a87ecc2184e9b03dfe529ee27be7` plus uncommitted changes. It is not an immutable release candidate, human approval, signature record or distribution authorization. Toolchain: .NET SDK `10.0.100-rc.1.25451.107`; Windows x64 `10.0.26200`; installed framework runtime reported `.NET 10.0.13`. No live banking credentials, bank requests or visual testing were used.

## Bounded support matrix

| Configuration | Deterministic evidence | Qualification limit |
| --- | --- | --- |
| German FinTS 3.0 PIN/TAN, explicit product registration and bank-permitted procedure provenance | Production candidate factory, synchronization and read pipeline tests | Synthetic identities and responses only; registration approval and bank acceptance are unverified |
| HKIDN v2 / HKVVB v3 / HNSHK v4, HKSYN and HKEND | Request construction, dialogue correlation, synchronization and closing tests | Supported restricted encoder policy; no automatic recovery or authentication fallback |
| HKSPA discovery with reviewed account identity | Discovery/review and reopen tests | Explicit desktop review supports new and matching manual accounts; durable parameter caching remains incomplete |
| Advertised HKSAL v6/v7/v8 within the kernel-supported account schemas | Candidate negotiation, target balance, continuation and ingestion tests | Exact account/evidence required; this is schema coverage, not three qualified bank implementations |
| Explicitly qualified read without interactive approval | Credential lifetime, prompt cancellation and pipeline tests | Bank-advertised permission required; a PIN-only envelope alone is insufficient |
| Signed process-2 text-TAN synchronization (`HKTAN` v6/v7 process `4` + `2`, `PIN:2` envelope, message-3 `HKEND`) | Challenge binding, single-use TAN consumption/disposal, expiry/cancel/reject and reopen tests | Supported for initial synchronization only; read-pipeline signed TAN continuation, decoupled approval, HHD/visual challenges and media-dependent procedures remain unsupported |
| Batch refresh and both desktop actions | Scripted batch/presentation tests, UI dispatcher and headless geometry checks | Connections supports explicit qualification/requalification, discovery/review and selected-bank refresh; unsupported procedures stop before dispatch |
| Windows x64 framework-dependent and self-contained packaging | Local Release publishes, runtime inventory, packaged smoke and platform checks | No signing, external distribution or live-bank qualification; Linux/macOS execution not tested here |

**Live compatibility table:** no entries. Add a named institution, endpoint provenance, product identity, protocol/segment versions, exact procedure, operating system, artifact digest and dated evidence only after that exact combination passes qualification. Never infer a row from a similar bank, shared processor or synthetic fixture.

## Reproducible deterministic checks

Run from the repository root:

```powershell
./eng/Invoke-CI.ps1 -Scope Full
dotnet run --project src/Broiler.Fond.Host.Desktop -c Release --no-build --no-restore -- --qualification
```

Full CI restores dependencies, verifies the kernel boundary and formatting, builds Release with warnings as errors and the enabled trim analyzers, runs kernel and presentation suites, and runs headless host smoke/self-tests. Production-assembly self-tests reject embedded banking fixtures. Source project references were checked separately for test dependencies; fixture resources belong to the test projects only.

The publisher deliberately does **not** produce trimmed, single-file or NativeAOT binaries. Enabled trim analyzers are checked by the normal build; this record does not claim successful trimmed/AOT execution. The current publish workflow checks both package variants with `--smoke` and `--qualification`, explicitly waits for each GUI-subsystem executable to exit, and never launches its interactive default mode during CI.

To repeat packaging locally, use a fresh output directory for each variant:

```powershell
dotnet publish src/Broiler.Fond.Host.Desktop -c Release -r win-x64 --self-contained false --source https://api.nuget.org/v3/index.json -o artifacts/br09/framework-dependent
dotnet publish src/Broiler.Fond.Host.Desktop -c Release -r win-x64 --self-contained true --source https://api.nuget.org/v3/index.json -o artifacts/br09/self-contained
```

Verify `coreclr.dll` exists only in the self-contained directory. Run each executable with `--smoke` and `--qualification` using `Start-Process -WindowStyle Hidden -Wait -PassThru` and inspect `ExitCode`. A bare PowerShell invocation of a GUI executable can return before completion. Do not use the interactive default invocation as a smoke test.

## Evidence and recovery coverage

Local results on 2026-10-21: `Invoke-CI.ps1 -Scope Full` passed, including restore, formatting, kernel boundary, Release build (zero warnings/errors, trim analyzers enabled), the full kernel runner, 184 combined desktop/UI self-test checks and all 2,630 presentation checks. Both Windows x64 publish variants passed runtime-inventory, packaged smoke and platform qualification checks. Detailed local logs use the prefix `broiler-fond-br09-` in the operating-system temporary directory; they are not retained immutable CI evidence. The initial formatting failure was resolved by applying the repository formatter to its 29 reported source/test files.

- Kernel: production request construction, supported version negotiation, malformed/mismatched evidence, continuation limits, cleanup, strict target ingestion and authenticated persistence.
- Presentation: synchronization/discovery/balance save and reopen, rejected setup before dispatch, credential disposal, stale context rejection, concurrency/backoff, partial batch results, cancellation and received-but-unsaved results.
- Desktop interaction: both buttons, duplicate prevention, shared cancellation, dispatcher affinity, cached-value preservation, unlocking the remembered profile, redacted opt-in diagnostics and measured long-dialog geometry.
- Privacy checks use synthetic secret markers to ensure alert/export redaction and credential-owner disposal. They are bounded regression checks, not a complete source/history/artifact secret scan or a guarantee of erasing managed/OS memory copies.

## Setup and troubleshooting

The current bounded flow is Connections → Connect → Discover Accounts → Refresh Balances. See [first balance](bank-first-balance.md) for the verified desktop flow, [signed approval](bank-signed-approval.md) for synchronization-only text-TAN limits, and [live qualification](bank-live-qualification.md) for the pending exact-bank evidence.

Configure the assigned product identity, unlock a writable profile outside Safe Mode, supply actual bank login details locally, and confirm the official endpoint. Procedure selection is explicit and volatile; Connections refresh requalifies after lock/unlock. Manual accounts require bank discovery/review; new accounts can also be discovered without manual entry.

If parameters or approval are unsupported, repeated PIN entry cannot resolve the missing capability. For transient failures, respect retry backoff. For received-but-unsaved data, resolve storage access before retrying. Missing balances remain unavailable. Keep PINs, TANs, passphrases, login identifiers and raw responses out of troubleshooting evidence.

## Remaining release gates

1. Keep the qualified scope bounded to supported setup and read procedures; unsupported media, bootstrap and discovery/read approval paths remain blocked.
2. Verify the assigned production product identity and confirmed endpoint through the appropriate process. No external registration status has been verified in this record.
3. After those dependencies pass, obtain separate authorization for a read-only bank test environment or user-approved account. Record expected account identities, balance kinds/currencies/timestamps, durable reopen results, cancellation and safe failure behavior. Keep credentials and raw protocol data out of evidence.
4. Repeat qualification on an immutable candidate and every claimed platform. Record artifact SHA-256 digests, signing/inventory evidence, and the exact supported matrix.
5. Complete the [release checklist](release/prerelease-checklist.md) and required human review before distribution. No release or draft was created by this milestone.

See [setup](bank-connection-setup.md), [authentication](bank-authentication.md), [discovery](bank-account-discovery.md), [batch behavior](bank-batch-refresh.md) and [desktop feedback](bank-refresh-ui.md).

Priority 8 preparation: the [exact live qualification worksheet](bank-live-qualification.md) and offline candidate-inventory generator are available. No live attempt or compatibility row is recorded. Latest priority 7 full CI passed with 2,844 presentation and 184 desktop/UI checks in addition to kernel verification; the older BR-09 packaging evidence above remains historical.
