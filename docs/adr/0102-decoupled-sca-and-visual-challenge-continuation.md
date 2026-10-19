# ADR 0102: Decoupled SCA, Polling Loop Boundaries, and Visual Challenge Parsers

## Status
Accepted

## Context
Under the PSD2 Regulatory Technical Standards (RTS) for Strong Customer Authentication (SCA), German financial institutions mandate two-factor authentication for sensitive read operations (such as initial account binding and 90-day balance/history refreshes) and all payment initiations.

In FinTS 3.0, banks implement SCA through two primary modern mechanisms beyond traditional SMS TAN:
1. **Decoupled / App Approval (Push-TAN / BestSign / App-Freigabe):**
   - The bank returns `HITAN` with process variant `4` (decoupled challenge) and an `OrderReference`.
   - The user opens their banking mobile app (e.g. Sparkasse S-pushTAN, VR SecureGo plus, DKB App, Postbank BestSign) and confirms the login or order.
   - The client application polls the bank using `HKTAN` with process variant `S` (status query) at bank-advertised intervals until approval, denial, or expiration.
2. **Visual & Matrix Challenges (chipTAN optisch / photoTAN / QR-Code):**
   - The bank returns `HITAN` containing binary or structured payload in the `HHDData` element (data element 5).
   - Payloads range from optical flicker sequences (HHD 1.3 / 1.4 sync and clock bar codes) to 2D matrix codes (photoTAN raw bytes, PNG/JPEG images) or QR codes (EPC GiroCode or authentication URLs).

The product principles demand:
- **BCL-only kernel:** Zero native libraries (e.g. no native libqrencode or OpenCV).
- **Ephemeral zeroization:** Any TAN credential entered or transmitted is zeroized immediately.
- **Fail-closed timeouts:** Polling loops must enforce monotonic deadlines and never hang indefinitely.
- **No unattended prompt loops:** If the bank demands interactive action during scheduled refresh, the dialogue aborts cleanly.

## Decisions

### 1. Visual Challenge Parser (`VisualChallengeParser`, `VisualChallengePayload`)
We implement pure .NET 10 visual challenge detection and extraction in `Broiler.Fond.Kernel.FinTs.Sca`:
- **Categorization (`VisualChallengeKind`):**
  - `MatrixCodeImage`: Binary payload containing PNG (`0x89 0x50 0x4E 0x47`) or JPEG (`0xFF 0xD8 0xFF`) images or `data:image/` text.
  - `QrCodeText`: Plain text payload containing QR payload (e.g. `BCD` GiroCode, or standard URL).
  - `FlickerCode`: Binary or hex-encoded DK HHD 1.3 / 1.4 optical flicker control and data bytes.
  - `MatrixCodeRaw`: Raw 2D matrix payload (e.g. DK photoTAN binary matrix data).
  - `PlainText`: Informational text-only challenge.
  - `None`: Absence of visual data.
- **Payload Safety:**
  - Enforces strict size bounds (max 64 KiB per `FinTsTanChallengeSet.MaximumHhdBytes`).
  - Safely copies payload bytes into immutable records without executing any untrusted code or script.

### 2. Decoupled Polling Coordinator (`DecoupledPollingCoordinator`)
We formalize the decoupled status polling state machine:
- **Monotonic Deadline:** Enforces an absolute deadline derived from `TimeProvider.GetUtcNow() + timeout`, clamped to the bank's `ValidUntil` timestamp if provided.
- **Query Interval Pacing:** Honors bank-advertised minimum wait intervals (`MinWaitTime` from BPD) with a minimum floor of 2 seconds to prevent accidental denial-of-service or IP rate-limiting.
- **Cancellation & Abortion:**
  - Supports cooperative `CancellationToken`.
  - On user cancellation or timeout, sends `HKEND` dialogue abort to release server-side state, clears transient buffers, and transitions connection state to `UserActionRequired` or `Cancelled`.
- **Query Limits:** Caps the maximum number of polling attempts (default 32 queries) to prevent infinite loops.

## Consequences

### Positive
- Enables full support for modern app-approval procedures (Sparkasse pushTAN, VR SecureGo, BestSign).
- Enables visual presentation of photoTAN, QR-TAN, and chipTAN optical flicker codes in client UI.
- Preserves the strict BCL-only kernel boundary without adding heavy native barcode dependencies.
- Prevents server-side session leaks and thread deadlocks through monotonic timing.

### Negative / Trade-offs
- Rendering animated flicker codes or complex QR displays requires host UI assistance (e.g. Windows presentation layer). The kernel provides the normalized payload and timing contracts.
