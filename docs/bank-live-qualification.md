# Exact live bank qualification — priority 8

Status on 2026-10-21: **prepared; live attempt not run; no compatibility row qualified**. Priorities 1–7 have deterministic coverage, including 2,844 presentation and 184 desktop/UI checks in the latest full CI. That evidence does not establish bank acceptance.

## Target and authorization required

Select one exact institution and environment (bank test system or explicitly approved account), bank code, official HTTPS FinTS endpoint and its bank-published source. Record the assigned product ID/version and verified registration status, the intended bank-advertised procedure and its supported scope. Never substitute synthetic fixture values.

The earlier task restriction excludes live and visual testing. Before a live request, obtain an explicit override identifying the target and read-only scope. Enter login details, profile passphrase, PIN and TAN only through the application's local prompts; do not put them in chat, command arguments, environment dumps, evidence files or raw protocol captures. Authorization to prepare this worksheet is not evidence of a successful live test.

No target, production registration evidence or new live-test authorization has been supplied in this task. These are the current external dependencies; no bank request has been attempted.

## Freeze and identify the candidate

Use a fixed candidate directory and preserve it for the run. Record platform/runtime and signing status separately. The working tree contains local changes, so a Git HEAD alone cannot identify the tested code. Do not call the present build an immutable release candidate.

Create an offline inventory without launching the app or accessing a bank:

```powershell
./eng/New-BankQualificationRecord.ps1 -ArtifactDirectory <candidate-directory> -OutputPath <new-local-evidence-file.json>
```

The script hashes DLL, EXE and JSON payload files, records source HEAD/dirty status and OS architecture, and creates pending target/authorization/evidence fields. It refuses to overwrite evidence. It does not verify signing, registration, artifact/source correspondence or native files with other extensions, and it does not mark any live check passed. For a published candidate, extend the preserved inventory to every distributed file and repeat the documented packaged smoke/platform checks. Retain the exact artifact while interpreting any result.

## Authorized read-only run

Use a separate local qualification profile with expected account identity and currency known to the operator. The operator compares bank portal values privately; retained evidence needs only comparison outcomes, balance kinds/currencies and appropriately minimized timestamps, not raw account identifiers or balances.

| Stage | Required evidence | Stop condition |
| --- | --- | --- |
| Setup and public parameters | Exact confirmed endpoint/source, assigned product, successful bounded parameter acquisition | Wrong endpoint, rejected product or unsupported parameters |
| Explicit procedure qualification | Exact advertised security function, profile/TAN versions and selection | Missing media, unsupported decoupled/visual approval or unsupported bootstrap |
| Connect | Accepted synchronization, validated closing exchange and persisted system state | Rejection, uncertain closure, unsupported approval or save failure |
| Discover Accounts | Validated account list, explicit operator review, supported active HKSAL capability | Ambiguous identity, unsupported discovery approval or unqualified capability |
| Refresh Balances | Closed HKSAL exchange, correct account/currency/balance-kind comparison, durable retrieval time | Wrong identity/currency, unsupported approval, protocol failure or unsaved data |
| Reopen | Same saved amounts and original retrieval time after lock/unlock | Lost/changed saved evidence |
| Later refresh | Explicit requalification as needed and a new successful saved retrieval time; unchanged amounts accepted | Expired procedure used silently or refresh not persisted |
| Cancel and safe feedback | Operator cancels a prompt; no successful save claimed; no secrets in alert/support preview | Late credential dispatch, secret exposure or misleading success |

Do not deliberately enter a wrong bank PIN or repeat rejected credentials to test failure handling. Use deterministic tests for destructive/lockout-prone failure injection. Bank-required approval during discovery/balance reads remains unsupported by the bounded read pipeline even when synchronization text TAN works. Stop and record the exact blocked stage rather than attempting authentication fallbacks.

The current automation surface has no supported way to operate and inspect this native desktop application's live flow without visual testing. A user/operator can run it locally and supply minimized stage outcomes. If agent-operated testing is required, a separately designed, reviewed live harness is needed; the existing simulator is not a substitute.

## Decision and compatibility row

For every stage retain dated outcome, artifact inventory reference and minimal support reference where relevant. A missing check stays NotRun/Blocked; it never becomes passed by inference. Recheck candidate hashes before and after the run. Record any candidate/configuration change and rerun affected checks.

Only after accepted live evidence and review add a row to the compatibility table with institution/environment, endpoint provenance, product/version, exact procedure, actual protocol/segment versions, OS/runtime, artifact digests, test dates, reviewed stages and explicit limitations. Do not generalize to other banks, processors, procedures or platforms. Qualification is separate from signing, distribution and release approval.
