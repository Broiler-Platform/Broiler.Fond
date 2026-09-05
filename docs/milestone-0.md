# Milestone 0 — Engineering foundation

## Outcome

Milestone 0 establishes a buildable, policy-enforced skeleton for **Broiler
Fond - Finance on Demand**. It intentionally provides zero homebanking
functionality.

The milestone creates places for later implementation while making the most
important architectural boundaries visible and automatically checkable from
the beginning.

## Included scaffold

| Area | Milestone 0 artifact |
| --- | --- |
| Repository | Solution, shared build settings, formatting rules, ignored files, and .NET 10 SDK selection policy |
| Kernel | `Broiler.Fond.Kernel` targeting `net10.0`, with skeletal domain and application contracts |
| Initial host | An inert Windows executable outside the kernel |
| Verification | A BCL-only executable test harness, inert-host smoke run, and kernel-boundary checks |
| Continuous integration | Windows full-build job and Linux kernel-build job |
| Architecture | Initial ADRs for the kernel, portable profile container, and user-operated boundary |
| Governance | Security policy, human-review record, compatibility ledger, and prerelease checklist |

## Deliberately inert

Milestone 0 must not:

- open a network connection or send a FinTS/HBCI message;
- accept, retain, transform, or log bank credentials, PINs, or TANs;
- create, read, encrypt, decrypt, or migrate a user profile;
- discover, claim, or connect to a live institution;
- retrieve accounts, balances, transactions, or statements;
- create, validate, authorize, or submit a payment;
- start background work, telemetry, analytics, crash upload, or reporting; or
- present itself as a supported banking product or distributable prerelease.

Placeholder interfaces and value types express boundaries only. They are not
partial banking implementations and must not simulate success.

## Exit criteria

The milestone is complete only when evidence shows all of the following:

- [ ] An SDK selected by the repository's .NET 10 policy restores and builds
      the solution without warnings; the exact SDK is captured as evidence.
- [ ] Formatting and analyzer checks pass.
- [ ] The kernel-boundary check proves that the kernel targets `net10.0`, uses
      no third-party package, contains no platform-specific dependency, and
      does not reference a host project.
- [ ] The BCL-only verification executable passes.
- [ ] The kernel builds on both Windows and Linux CI runners.
- [ ] The inert Windows host builds and its no-capability smoke run passes on
      the Windows CI runner.
- [ ] Repository and generated outputs contain no production secret or
      financial-data fixture.
- [ ] The governance documents exist with approval states left pending.
- [ ] No live institution appears in the verified compatibility table.

For a local equivalent of the full CI path, run:

```powershell
pwsh ./eng/Invoke-CI.ps1 -Scope Full
```

Use `-Scope Kernel` for the platform-independent kernel path.

## Handoff to Milestone 1

Milestone 1 may begin only from this enforced foundation. It adds the first
real vertical slice: manually configured access to one verified institution
row and read-only listing of accounts and balances. That functionality is not
backported into Milestone 0.
