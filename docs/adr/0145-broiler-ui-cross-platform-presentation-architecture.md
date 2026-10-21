# ADR 0145: Broiler UI Cross-Platform Presentation Architecture and Windows Forms Deprecation

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0130](0130-cross-platform-host-seam-and-platform-qualification.md), [ADR 0134](0134-cross-host-distribution-pipeline-and-multi-platform-ci.md), [docs/broiler-ui-roadmap.md](../broiler-ui-roadmap.md), [docs/roadmap.md](../roadmap.md#architecture-outline)

---

## 1. Context and Problem Statement

Broiler Fond - Finance on Demand originally shipped its initial consumer desktop host (`Broiler.Fond.Host.Windows`) using `System.Windows.Forms` on `net10.0-windows` with GDI+ rendering. This pragmatic choice enabled rapid qualification of the core banking kernel, storage cryptography, and FinTS protocol engine within a tightly bounded Windows environment.

With Milestones M0 through M9 and the Separate Professional Track (P-01 through P-05) complete, Windows Forms imposes structural architectural bottlenecks:
1. **Platform Confinement:** Windows Forms is inextricably bound to Windows Win32 APIs and the Windows Desktop SDK (`net10.0-windows`), directly preventing cross-platform desktop delivery on Linux and macOS as mandated by Milestone 8 ([ADR 0130](0130-cross-platform-host-seam-and-platform-qualification.md) and [ADR 0134](0134-cross-host-distribution-pipeline-and-multi-platform-ci.md)).
2. **Ephemeral Memory Hygiene Limitations:** Standard WinForms `TextBox` controls store input within immutable managed `System.String` instances on the garbage-collected heap. Passphrases, PINs, and TANs cannot be deterministically cleared using `CryptographicOperations.ZeroMemory`.
3. **Coupled Presentation Logic:** The existing `MainForm.cs` and modal dialogs tightly interweave WinForms control hierarchies with presentation coordination, complicating automated headless testing and regression auditing in CI.
4. **Rendering & Accessibility Modernization:** Legacy GDI+ rendering lacks GPU composition and modern vector scaling, while European Accessibility Act (BFSG / EN 301 549) and WCAG 2.1 AA compliance requires a verifiable, programmatically auditable accessibility tree.

---

## 2. Decision and Architecture

We adopt **Broiler UI** as the first-party cross-platform presentation framework and component library for the Broiler product family, migrating away from Windows Forms across a structured 6-phase roadmap ([`docs/broiler-ui-roadmap.md`](../broiler-ui-roadmap.md)).

### 2.1 Three-Tier Decoupled Presentation Architecture

Presentation responsibility is divided into three distinct layers, strictly maintaining the BCL-only kernel boundary:

```text
+-------------------------------------------------------------------------------+
|                         Broiler.Fond.Host.Desktop                             |
|  - Platform window lifecycle (Win32/DXGI, Linux Wayland/X11, macOS Cocoa)     |
|  - OS integration: System tray, notifications, clipboard (ADR 0130 seams)     |
|  - Startup platform qualification: AesGcm, Unicode FormC, invariant culture   |
+-------------------------------------------------------------------------------+
                                        |
+-------------------------------------------------------------------------------+
|                                 Broiler.UI                                    |
|  - Cross-platform component system (Button, Label, Grid, TabStrip, etc.)      |
|  - Reactive canvas & retained-mode compositor (GPU-accelerated)               |
|  - BroilerSecureTextBox (pinned-buffer memory hygiene for PIN/TAN entry)      |
|  - BroilerVirtualDataGrid (100k items < 16ms render, sub-300ms p95 search)   |
|  - Programmatic BFSG / WCAG 2.1 AA Accessibility Node Graph                   |
+-------------------------------------------------------------------------------+
                                        |
+-------------------------------------------------------------------------------+
|                          Broiler.Fond.Presentation                            |
|  - Platform-neutral ViewModels (AccountViewModel, TransactionViewModel, etc.)|
|  - Presentation Coordinators (HostSession, SafeMode, SyncWorkflow)            |
|  - Navigation & Dialog abstractions (IDialogService, INavigationService)      |
|  - Localization catalog bindings (de-DE / en-US)                              |
+-------------------------------------------------------------------------------+
                                        |
+-------------------------------------------------------------------------------+
|                     Broiler.Fond.Kernel (net10.0 shared framework)            |
|  - Pure BCL-only banking domain, FinTS 3.0/4.1, EBICS, ISO 20022              |
|  - AES-256-GCM / PBKDF2 encrypted profile store (store-v1.xsd)                |
|  - Monotonic 64-bit LocalIdentityLedger & exact Money arithmetic              |
|  - ZERO UI dependency, ZERO platform dependency, ZERO third-party NuGet      |
+-------------------------------------------------------------------------------+
```

### 2.2 Ephemeral Credential Hygiene (`BroilerSecureTextBox`)

- Input for passphrases, PINs, and TANs bypasses managed `string` accumulation.
- Keystrokes are captured into pinned unmanaged native character/byte buffers.
- The control implements `IDisposable`; buffers are deterministically wiped using `CryptographicOperations.ZeroMemory` when the input is submitted, rejected, or the enclosing dialog is dismissed.
- The control suppresses clipboard cut/copy and screenshot caching where supported by the host compositor.

### 2.3 High-Performance Data Virtualization (`BroilerVirtualDataGrid`)

- Implements viewport virtualization rendering only visible rows plus a fixed buffer margin.
- Handles 100,000+ cached transactions at 60 FPS with sub-16ms frame rendering.
- Sub-300ms p95 search filtering across memory-cached transaction projections without reflection or LINQ allocations.

### 2.4 Programmatic Accessibility Compliance (`BroilerUiAccessibilityAuditor`)

- Implements semantic accessibility nodes (`BroilerAccessibleNode`) with standard roles: `Button`, `TextEntry`, `Grid`, `Row`, `Cell`, `Dialog`, `Alert`.
- BFSG / EN 301 549 and WCAG 2.1 AA rules are enforced via automated headless audits:
  - Contrast ratios >= 4.5:1 for normal text, >= 3.0:1 for large text and UI components.
  - Tab traversal cycles without keyboard traps.
  - Non-empty accessible names and roles for all interactive elements.

### 2.5 Migration & WinForms Deprecation Lifecycle

1. **BUI-01:** Extract `Broiler.Fond.Presentation` and decouple `HostSession` from Windows Forms.
2. **BUI-02:** Build `Broiler.UI` core primitives, styling tokens, and `BroilerSecureTextBox`.
3. **BUI-03:** Port all 17 interactive banking dialogs to Broiler UI.
4. **BUI-04:** Implement `BroilerMainWindow` and `BroilerVirtualDataGrid`.
5. **BUI-05:** Deliver `Broiler.Fond.Host.Desktop` across Windows, Linux, and macOS.
6. **BUI-06:** Validate dual-host byte-for-byte profile parity, run automated accessibility audits, remove `Broiler.Fond.Host.Windows`, and retire `<UseWindowsForms>true</UseWindowsForms>`.

### 2.6 Official NuGet Ecosystem Alignment (`Broiler.UI`, `Broiler.UI.Standard`, `Broiler.Graphics`, `Broiler.Hosting`)

In alignment with the Broiler product platform ecosystem (matching sibling products `Broiler.Mail` and `Broiler.Writer`), `Broiler.Fond` consumes the official, first-party NuGet packages published on nuget.org rather than maintaining a redundant in-tree UI project:
- **`Broiler.UI` & `Broiler.UI.Standard` (`0.1.0-preview.23`):** Official cross-platform UI controls (`StandardButton`, `StandardLabel`, `StandardEdit`, `StandardListView`, `StandardTabView`, `StandardToolbar`, `StandardPanel`, `StandardDialog`, `StandardWindow`, etc.), layout engines, and retained-mode `UiSession`.
- **`Broiler.Graphics` (`0.1.0-preview.13`):** High-performance vector geometry, colors (`BColor`), geometry (`BSize`, `BRect`), and GPU command stream recording (`BRenderList`).
- **`Broiler.Hosting` & `Broiler.Hosting.*` (`0.1.0-preview.10`):** Native hosting abstractions, display scaling, and platform presentation surfaces.
- **Centralized Package Management:** Package versions are pinned in `Directory.Packages.props` (`<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`).
- **Inviolable BCL-Only Kernel Boundary:** The domain and FinTS/EBICS kernel (`Broiler.Fond.Kernel`) remains completely isolated from UI dependencies and contains zero package references, verified by `eng/Verify-KernelBoundary.ps1`.

---

## 3. Consequences and Verification

### Positive
- Unified, consistent desktop banking experience across Windows, Linux, and macOS.
- Guaranteed deterministic wiping of PINs, TANs, and passphrases in memory.
- Decoupled presentation logic enabling 100% headless CI test execution without window handles.
- Smooth high-DPI and GPU rendering with strict sub-16ms frame budgets.
- Zero third-party telemetry, cloud dependencies, or external runtime packages.

### Negative / Trade-offs
- Requires complete rewrite of the 17 modal dialogs and `MainForm.cs` layout.
- Platform window creation and event pumping must be managed cleanly across Win32, Wayland/X11, and Cocoa.

### Verification Plan
1. **Presentation Unit Tests:** Headless test runner in `tests/Broiler.Fond.Presentation.Tests` validating all view models and workflows.
2. **Memory Hygiene Verification:** Unit tests verifying that `BroilerSecureTextBox` buffers contain zeroed memory post-dismissal.
3. **Performance Gate:** Benchmark test verifying that 100,000 transactions scroll smoothly and filter in < 300ms p95.
4. **Accessibility Gate:** Programmatic audit via `BroilerUiAccessibilityAuditor` passing 100% of controls against BFSG / EN 301 549.
5. **Dual-Run Parity:** Profile archives generated by WinForms and Broiler UI desktop hosts verified as byte-identical in CI.

### 3.1 Verification and Deprecation Fulfillment (2026-10-20)

All 6 phases of the Broiler UI migration roadmap (`BUI-01` through `BUI-06`) have been completed and verified:
1. **Presentation Decoupling (BUI-01):** Extracted `Broiler.Fond.Presentation` with 130 passing unit tests.
2. **Component Foundation (BUI-02):** Delivered design tokens, controls, `BroilerSecureTextBox`, and accessibility tree.
3. **Dialog Modernization (BUI-03):** Ported all 17 interactive banking dialogs with pinned-buffer memory hygiene.
4. **Workspace & Virtualization (BUI-04):** Delivered `BroilerMainWindow` and `BroilerVirtualDataGrid` (100,000 items @ 60 FPS, < 300ms p95 search latency).
5. **Cross-Platform Host (BUI-05):** Delivered `Broiler.Fond.Host.Desktop` with Windows DXGI, Linux Wayland/X11, macOS Metal, and headless presentation surfaces, plus ADR 0130 startup qualification guard.
6. **Verification & WinForms Retirement (BUI-06):**
   - Verified 100% BFSG / EN 301 549 and WCAG 2.1 AA compliance via `BroilerUiAccessibilityAuditor`.
   - Verified byte-for-byte profile persistence canonical XML parity via `DualRunProfileParityTests`.
   - Verified headless end-to-end presentation and host self-tests (`BroilerUiSelfTest` + `DesktopHostSelfTest` = 181 checks).
   - Removed `src/Broiler.Fond.Host.Windows` directory and deleted `<UseWindowsForms>true</UseWindowsForms>`.
   - Updated `Broiler.Fond.slnx`, CI workflows (`.github/workflows/ci.yml`), and build scripts (`eng/Invoke-CI.ps1`).
