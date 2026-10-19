# ADR 0133: Mobile Lifecycle Suspension, Transient Memory Hygiene & Device Threat Model

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0003](0003-operating-model-and-trust-boundary.md), [ADR 0033](0033-fints-session-credentials-and-pinned-buffers.md), [ADR 0076](0076-terminal-evidence-ownership-and-credential-cleanup.md), [ADR 0126](0126-security-controls-and-incident-response.md), [ADR 0130](0130-cross-platform-host-seam-and-platform-qualification.md), [docs/milestone-8.md](../milestone-8.md#m8-04-mobile--background-lifecycle-hardening--device-threat-model)

---

## 1. Context and Problem Statement

When extending Broiler Fond to mobile devices (iOS, Android) and modern sandboxed desktop environments, application lifecycle management poses unique security challenges:
1. **Decoupled SCA Background Handoff:** In modern German retail banking, decoupled SCA procedures (e.g., pushTAN, BestSign, photoTAN app) require the user to leave Broiler Fond and switch to the banking app to approve the login or payment.
2. **OS Suspension and Task Switcher Previews:** When backgrounded or switched, mobile operating systems create task-switcher snapshot thumbnails and may suspend or page process memory to disk. Sensitive financial balances and credentials must not be captured in OS previews or retained in suspended memory.
3. **Background Suspension Timeouts:** If the app remains in the background longer than the dialogue or TAN timeout, resuming must fail closed rather than attempting to transmit stale authentication tokens.
4. **Transient Clipboard Exposure:** Users frequently copy IBANs or payment references. Sensitive clipboard content must be automatically scrubbed after a bounded interval or immediately upon app suspension.

---

## 2. Decision and Architecture

### 2.1 Lifecycle State Model (`HostLifecycleState`)

Implemented in `Broiler.Fond.Kernel.Hosting`:
- **`ForegroundActive`:** App is active and receiving foreground user input.
- **`ForegroundPaused`:** App is transitioning to background, obscured by system dialogs, or entering the app switcher. Triggers privacy curtain affordances.
- **`BackgroundSuspended`:** App is in the background. Triggers immediate volatile secret wiping and clipboard scrubbing.
- **`Terminating`:** App process is terminating. Guarantees best-effort memory zeroization.

### 2.2 Mobile Lifecycle Coordinator (`MobileLifecycleCoordinator`)

Implemented in `Broiler.Fond.Kernel.Hosting`:
- **Volatile Secret Scrubbing:** Supports registration of volatile secret buffers and cleanup actions (`RegisterVolatileSecret(Action wipeAction)`). Upon transition to `BackgroundSuspended`, all registered secret buffers are immediately wiped via `CryptographicOperations.ZeroMemory`.
- **Decoupled SCA Timeout Guard:** `BeginDecoupledScaWindow(TimeSpan timeout)` starts a tracked background authorization interval. If the application is suspended and remains in the background past the timeout deadline, `ValidateForegroundResume()` fails closed, reporting `DecoupledScaTimedOut`.
- **Transient Clipboard Hygiene:** Coordinates with `IHostClipboardService` to scrub transient copied data immediately when `BackgroundSuspended` or `Terminating` is entered.
- **Privacy Curtain Signal:** Emits `PrivacyCurtainRequested` when entering `ForegroundPaused`, allowing host platforms to display an opaque shield to prevent screenshots by the OS task switcher.

---

## 3. Consequences

### Positive
- Prevents credential persistence in mobile OS memory dumps or hibernation images.
- Protects financial balance confidentiality against OS app switcher previews.
- Enforces strict fail-closed timeout boundaries during decoupled SCA.

### Negative / Trade-offs
- If a user takes too long to approve a pushTAN in their banking app, the session will expire and require restarting the dialogue.
