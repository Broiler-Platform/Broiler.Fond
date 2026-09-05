# ADR 0001: Kernel boundary

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** Product architecture

## Context

The product starts on Windows but must later support Linux, macOS, and mobile
hosts. Its banking and domain behavior needs one portable source of truth. The
project also requires a new kernel that grows milestone by milestone without a
third-party runtime dependency.

## Decision

Create `Broiler.Fond.Kernel` as an in-repository library targeting `net10.0`.
The kernel may use only APIs supplied by the .NET 10+ runtime and base class
libraries.

The kernel must not contain or reference:

- NuGet `PackageReference` dependencies;
- host, UI, desktop, mobile, or operating-system-specific projects or APIs;
- a runtime identifier or platform-specific target framework;
- native binaries, COM interop, dynamic native loading, or unsafe code; or
- telemetry, analytics, crash-upload, or hosted-service clients.

Hosts depend inward on kernel contracts. The kernel never depends outward on a
host. The initial Windows executable lives in a separate project; later hosts
must reuse the same unforked kernel and compatible contracts.

Milestone 0 adds only skeletal contracts and automated boundary enforcement.
It does not add banking behavior.

## Consequences

- Portable domain, protocol, storage, and orchestration behavior can grow in
  one kernel across milestones.
- Platform integrations remain explicit host responsibilities.
- Convenience libraries cannot be added to the kernel; required behavior must
  be built from supported .NET runtime APIs.
- CI must build the kernel on Windows and Linux from the first milestone and
  expand its host matrix as hosts are introduced.

## Enforcement

Repository build targets and `eng/Verify-KernelBoundary.ps1` reject forbidden
project metadata, references, source patterns, and native artifacts. Human
review remains necessary because automated checks cannot prove architectural
or security correctness.

