# Broiler Fond - Finance on Demand

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

Broiler Fond - Finance on Demand is a planned privacy-first, multibank
homebanking application. It is a purely user-operated client: the installed
application connects directly to accounts that its user owns or is authorized
to use. Broiler does not receive credentials, account data, transaction data,
or payment instructions.

> **Milestone 0 status.** This repository contains a buildable but deliberately
> inert project skeleton. It has no bank connection, credential handling,
> persistence, payment, transaction-sync, or graphical UI implementation. It is
> not a registered FinTS product or a production banking client.

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

## Milestone 0 skeleton

The skeleton establishes the boundaries on which the first functional slice
will be built:

- `Broiler.Fond.Kernel` targets platform-neutral `net10.0` and contains only
  domain shapes, the host-facing boundary, and unimplemented internal seams.
- `Broiler.Fond.Host.Windows` is the initial `net10.0-windows` console shell.
- `Broiler.Fond.Kernel.Tests` is a small BCL-only executable verification
  runner; no external test framework or package feed is required.
- repository build rules and `eng/Verify-KernelBoundary.ps1` reject package,
  platform UI, native/COM, unsafe-code, and out-of-kernel project dependencies
  in the kernel.
- GitHub Actions verifies the full solution and smoke-runs the inert host on
  Windows, then independently builds the kernel and its checks on Linux. M0
  does not publish artifacts.

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
- [Architecture decisions](docs/adr/README.md)
- [Compatibility evidence](docs/compatibility/README.md)
- [Security policy](SECURITY.md)
- [Human review record](HUMAN_REVIEW.md)
- [Prerelease checklist](docs/release/prerelease-checklist.md)

Major implementation decisions are recorded as ADRs under `docs/adr/`.

## License

Broiler Fond - Finance on Demand is licensed under the
[Apache License 2.0](LICENSE).
