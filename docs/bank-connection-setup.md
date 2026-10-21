# Bank connection setup and FinTS deployment configuration

Updated: 2026-10-21. BR-02 implements configuration and endpoint review; live desktop balance retrieval is still unavailable pending the later [balance refresh milestones](balance-refresh-roadmap.md).

## User workflow

1. Open **Connections → Add Connection**.
2. Enter the bank name, BLZ, and the HTTPS FinTS endpoint supplied by the bank.
3. Enter the **bank user ID**, and the customer ID if the bank requires a separate one. These identify the bank login; they are not the encrypted profile passphrase or banking PIN.
4. Save. The connection is saved without claiming successful bank synchronization.
5. Review the displayed complete endpoint, including scheme, ASCII hostname, port, path, and query. Acknowledge the checkbox only after independently checking the bank's information, then confirm.

Declining or closing endpoint review leaves the connection saved but unconfirmed. Use **Confirm Endpoint** later. Select a connection and choose **Edit Connection** to change login or endpoint details.

Endpoint confirmation is not TLS certificate approval, authentication, account discovery, or permission to perform payments. Ordinary HTTPS certificate validation still applies.

## Persisted state and editing

- The exact canonical HTTPS endpoint and its confirmation state are stored in the encrypted profile alongside the bank login identifiers. PINs and TANs are not collected by setup.
- Reviews capture profile ID, branch ID, session epoch, and the persisted profile generation. Edits, intervening saves, profile lock/switch, and edit/revert sequences invalidate stale reviews. The generation is a conservative persisted review revision; unrelated profile saves can require reviewing again.
- An effective endpoint change revokes trust and resets associated connection synchronization/system IDs. Login changes also reset that connection's bank session state. Bank-code changes invalidate the affected bank configuration.
- Previously verified account bindings become quarantined and advertised capabilities are removed for affected connections. Existing account identities, balances, and transaction history are retained. Later discovery/reverification must establish the new bank context.
- An unchanged endpoint retains its reviewed trust when login details change, but the login change still requires fresh authentication/synchronization.
- Safe Mode, read-only profiles, and locked/disposed sessions cannot accept setup changes.

## Legacy desktop profiles

Earlier desktop setup wrote `Verified` endpoints, `Active` system status, a fabricated `SYS1` system ID, and synchronized/contact metadata before contacting the bank. On local profile reload, the known legacy system-status pattern is normalized to unconfigured state, with the fake synchronization/contact evidence cleared; `Verified` does not become trusted `Confirmed`.

Known state aliases are mapped explicitly. Unknown state/system-status values block bank preparation and require review rather than being interpreted as enum defaults. Save/reopen retains normalized values. Accounts and saved financial history are preserved. This normalization is idempotent.

## Application product registration

The official FinTS manufacturer guidance requires an assigned application product registration number in the dialogue initialization's **Produktbezeichnung** field. It distinguishes the actual user-facing application from a registered library/kernel. Sources: [product registration](https://www.fints.org/de/hersteller/produktregistrierung), [registration FAQ](https://www.fints.org/de/hersteller/faq-produktregistrierung), checked 2026-10-21.

The repository's historical `KernelProduct.FinTsRegistration` (`BROILER-FOND` / `9999999999`) does not establish registration approval for production bank access. The desktop does not fall back to it for bank preparation.

The product owner/deployment administrator supplies the assigned application ID to the desktop process through:

| Setting | Purpose |
| --- | --- |
| `BROILER_FOND_FINTS_PRODUCT_ID` | The assigned FinTS **application** product registration number |
| `BROILER_FOND_FINTS_PRODUCT_VERSION` | Product version transmitted during initialization; defaults to `1.0` if omitted |

These settings are product metadata, not banking credentials. Do not place PINs, TANs, or profile passphrases in them. Restart the desktop process after changing its environment.

The configuration validates the protocol field limits and rejects missing/default placeholder values. It places the configured assigned ID in `FinTsProductRegistration.ProductIdentifier`, the actual field future request builders must send, as well as the registration metadata field. Valid syntax is **not** proof that an ID is assigned to this application or accepted by a bank. Assignment must be established by the product owner; BR-09 verifies actual interoperability against the supported configuration. No registration request or bank access was performed as part of BR-02.

Without usable configuration, refresh preflight reports `ProductConfigurationMissing` before creating a bank transport or requesting credentials. Future BR-03 request builders must consume the session's `ProductConfiguration.Registration`; they must not restore the historical placeholder fallback.

## HTTPS transport boundary

The desktop's confirmed transport is scoped to a captured reviewed configuration. It rejects endpoint replacement and stale profile/configuration contexts before sending, and discards responses if that context changed while waiting. The default kernel transport:

- allows HTTPS only, without embedded user information or URI fragments;
- disables automatic redirects, and rejects non-success responses including 3xx;
- uses platform TLS/certificate validation without bypass callbacks;
- bounds request/response sizes and applies a per-request timeout;
- distinguishes timeout, caller cancellation, TLS failure, and network failure;
- owns and disposes its HTTP client/handler.

Headless tests use injected HTTP handlers to exercise these outcomes without connecting to a bank. Injected handlers are trusted test/composition dependencies; production composition uses the default handler with redirects disabled. Later orchestration must pass the operation cancellation token, dispose the transport, and recheck the guarded commit context before saving returned bank data.
