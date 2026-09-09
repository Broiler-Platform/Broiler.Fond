"""Explicit maintainer tool; never run by CI or the product.

Regenerate PUBLIC SYNTHETIC ADR 0004 fixtures with an independent implementation.
Requires Python 3.11+ and cryptography (development tool only). Review resulting
diffs; do not regenerate expected values to make a failing test pass.
"""

import base64
import hashlib
import io
import json
from pathlib import Path
import struct
import unicodedata
import zipfile

from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF


ROOT = Path(__file__).resolve().parents[1]
DESTINATION = ROOT / "tests/Broiler.Fond.Kernel.Tests/Fixtures/ProfileEnvelope/v1.json"
NAMESPACE = "urn:broiler:fond:profile:1"
PROFILE_ID = "11111111-1111-4111-8111-111111111111"
BRANCH_ID = "22222222-2222-4222-8222-222222222222"
PASSPHRASE = "Äpfel é 🔐 "
DECOMPOSED = "A\u0308pfel e\u0301 🔐 "
PROFILE = (
    '<?xml version="1.0" encoding="utf-8"?>'
    f'<profile xmlns="{NAMESPACE}" version="1" profileId="{PROFILE_ID}" '
    f'branchId="{BRANCH_ID}" generation="1" nextId="1" savedAt="2000-01-01T00:00:00.0000000Z">'
    '<label /><note>Grüße 東京</note></profile>'
).encode("utf-8")
MANIFEST = (
    '<?xml version="1.0" encoding="utf-8"?>'
    f'<manifest xmlns="{NAMESPACE}" version="1" profileId="{PROFILE_ID}" '
    f'branchId="{BRANCH_ID}" generation="1">'
    f'<entry name="profile.xml" schema="1" bytes="{len(PROFILE)}" '
    f'sha256="{hashlib.sha256(PROFILE).hexdigest()}" /></manifest>'
).encode("utf-8")


def bootstrap_zip():
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, content in [("profile.xml", PROFILE), ("manifest.xml", MANIFEST)]:
            entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.create_system = 0
            entry.external_attr = 0x20  # DOS archive bit; no platform permissions.
            archive.writestr(entry, content)
    return output.getvalue()


def vector(name, payload, snapshot_number):
    profile_salt = bytes(range(32))
    snapshot_salt = bytes((i + snapshot_number * 32) % 256 for i in range(32))
    prefix = struct.pack(">I", snapshot_number)
    header = (
        b"BFONDPRF" + struct.pack("<HH", 1, 96) + bytes([1, 1, 1, 1])
        + struct.pack("<II", 600_000, 65_536)
        + profile_salt + snapshot_salt + prefix + bytes(4)
    )
    normalized = unicodedata.normalize("NFC", PASSPHRASE).encode("utf-8", "strict")
    master = hashlib.pbkdf2_hmac("sha256", normalized, profile_salt, 600_000, 32)
    content_key = HKDF(
        algorithm=hashes.SHA256(), length=32, salt=snapshot_salt,
        info=b"Broiler.Fond/profile-envelope/v1/content",
    ).derive(master)
    chunks = [(payload[i:i + 65_536], 0) for i in range(0, len(payload), 65_536)]
    chunks.append((b"", 1))
    envelope = bytearray(header)
    frames = []
    for ordinal, (plaintext, final) in enumerate(chunks):
        descriptor = struct.pack("<QIB", ordinal, len(plaintext), final)
        nonce = prefix + struct.pack(">Q", ordinal)
        aad = header + descriptor
        sealed = AESGCM(content_key).encrypt(nonce, plaintext, aad)
        frames.append({
            "descriptorHex": descriptor.hex(), "nonceHex": nonce.hex(),
            "aadHex": aad.hex(), "ciphertextSha256": hashlib.sha256(sealed[:-16]).hexdigest(),
            "tagHex": sealed[-16:].hex(),
        })
        envelope.extend(descriptor + sealed)
    return {
        "name": name, "passphrase": PASSPHRASE, "decomposedPassphrase": DECOMPOSED,
        "normalizedUtf8Hex": normalized.hex(), "headerHex": header.hex(),
        "masterKeyHex": master.hex(), "contentKeyHex": content_key.hex(),
        "payloadBase64": base64.b64encode(payload).decode("ascii"),
        "envelopeBase64": base64.b64encode(envelope).decode("ascii"),
        "envelopeSha256": hashlib.sha256(envelope).hexdigest(), "frames": frames,
    }


def main():
    cases = [("empty-envelope-not-a-zip", b""), ("bootstrap-zip", bootstrap_zip())]
    for length in (65_536, 65_537, 131_089):
        cases.append((f"framing-pattern-{length}", bytes((i * 31 + 7) % 256 for i in range(length))))
    document = {
        "notice": "PUBLIC SYNTHETIC TEST MATERIAL. Never use these keys, salts, or nonces for user data.",
        "format": "ADR-0004-v1-candidate", "profileXmlHex": PROFILE.hex(),
        "manifestXmlHex": MANIFEST.hex(),
        "vectors": [vector(name, payload, i + 1) for i, (name, payload) in enumerate(cases)],
    }
    DESTINATION.parent.mkdir(parents=True, exist_ok=True)
    DESTINATION.write_text(json.dumps(document, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
    print(f"Generated {len(cases)} public vectors: {DESTINATION}")


if __name__ == "__main__":
    main()
