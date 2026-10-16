"""Generate public synthetic syntax vectors independently of the .NET codec.

No bank recordings, keys or credentials. Body segments deliberately do not claim
business-schema or security-profile validity. Python standard library only.
"""
import base64
import json
from pathlib import Path

SYNTAX = b"+:'?@"


def text(value):
    return bytes(part for byte in value for part in ((63, byte) if byte in SYNTAX else (byte,)))


def scalar(value, binary=False):
    encoded = b"@" + str(len(value)).encode("ascii") + b"@" + value if binary else text(value)
    return encoded, {"binary": binary, "hex": value.hex()}


def segment(code, number, version, fields, reference=None):
    header = f"{code}:{number}:{version}" + (f":{reference}" if reference else "")
    encoded = [header.encode("ascii")]
    expected = []
    for field in fields:
        encoded.append(b":".join(element[0] for element in field))
        expected.append([element[1] for element in field])
    return b"+".join(encoded) + b"'", {
        "code": code, "number": number, "version": version,
        "reference": reference, "fields": expected,
    }


def frame(body, trailer_number, response=False):
    fields = [[scalar(b"000000000000")], [scalar(b"300")], [scalar(b"SYNTHETIC")], [scalar(b"1")]]
    if response:
        fields.append([scalar(b"SYNTHETIC"), scalar(b"1")])
    header = segment("HNHBK", 1, 3, fields)
    trailer = segment("HNHBS", trailer_number, 1, [[scalar(b"1")]])
    size = len(header[0]) + sum(len(item[0]) for item in body) + len(trailer[0])
    fields[0] = [scalar(f"{size:012d}".encode("ascii"))]
    return [segment("HNHBK", 1, 3, fields), *body, trailer]


vectors = []


def add(name, parts, framed):
    wire = b"".join(part[0] for part in parts)
    vectors.append({"name": name, "framed": framed, "wireBase64": base64.b64encode(wire).decode("ascii"),
                    "segments": [part[1] for part in parts]})


add("empty-structural-frame", frame([], 2), True)
add("escaped-text-and-omissions", [segment("ZTEST", 7, 0, [
    [scalar(b"A+:'?@Z")], [scalar(b"")],
    [scalar(b"first"), scalar(b""), scalar(b"last"), scalar(b"")], [scalar(b"")],
], reference=3)], False)
add("binary-octets", [segment("ZBIN", 3, 1, [
    [scalar(bytes(range(256)), True)], [scalar(b"", True)], [scalar(b"done")],
])], False)
add("response-reference-and-unknown-segment", frame([segment("ZNEW", 2, 999, [
    [scalar(b"Gr\xfc\xdfe\r\n")], [scalar(b"+:'?@\x00\xff", True)],
], reference=1)], 3, response=True), True)
add("opaque-security-wrapper", frame([
    segment("HNVSK", 998, 3, [[scalar(b"SYNTHETIC-WRAPPER")]]),
    segment("HNVSD", 999, 1, [[scalar(b"ZTEST:2:1+opaque'", True)]]),
], 4), True)
add("binary-component-and-empty-positions", [segment("ZDEG", 1, 1, [
    [scalar(b""), scalar(b"+:'?@", True), scalar(b""), scalar(b"tail")],
    [scalar(b"a"), scalar(b""), scalar(b"")],
])], False)

target = Path(__file__).resolve().parents[1] / "tests/Broiler.Fond.Kernel.Tests/Fixtures/FinTs/syntax-v1.json"
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps({"schemaVersion": 1, "vectors": vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(vectors)} public synthetic FinTS syntax vectors.")

# Separate typed-response cases with explicitly selected expected meanings. These
# are schema fixtures, not authenticated security-profile or business dialogues.
response_vectors = []


def response(name, body, meanings, conflicting=False):
    parts = frame(body, len(body) + 2, response=True)
    response_vectors.append({
        "name": name,
        "wireBase64": base64.b64encode(b"".join(part[0] for part in parts)).decode("ascii"),
        "segments": [part[1] for part in body],
        "meanings": meanings,
        "conflicting": conflicting,
    })


response("receipt", [segment("HIRMG", 2, 2, [
    [scalar(b"0010"), scalar(b""), scalar(b"PUBLIC-SECRET-REPLY")],
])], ["ReceiptReported"])
response("pending-and-pagination", [segment("HIRMG", 2, 2, [
    [scalar(b"0030"), scalar(b""), scalar(b"Pending")],
]), segment("HIRMS", 3, 2, [
    [scalar(b"3040"), scalar(b"3,4"), scalar(b"More + data?"), scalar(b"opaque:cursor"), scalar(b"")],
], reference=2)], ["AuthorizationPending", "MoreInformationAvailable"])
response("indeterminate", [segment("HIRMG", 2, 2, [
    [scalar(b"9000"), scalar(b""), scalar(b"PUBLIC-SECRET-REPLY"), scalar(b"ZREQ")],
])], ["ProcessingIndeterminate"])
response("unknown-code-and-data", [segment("HIRMG", 2, 2, [
    [scalar(b"7001"), scalar(b""), scalar(b"Unknown")],
]), segment("ZNEW", 3, 42, [[scalar(b"UNTRUSTED")]], reference=2)], ["Uninterpreted"])
response("conflicting-status", [segment("HIRMG", 2, 2, [
    [scalar(b"0010"), scalar(b""), scalar(b"Received")],
]), segment("HIRMS", 3, 2, [
    [scalar(b"9010"), scalar(b""), scalar(b"Cannot process")],
], reference=2)], ["ReceiptReported", "Uninterpreted"], conflicting=True)

response_target = target.with_name("responses-v1.json")
response_target.write_text(json.dumps({"schemaVersion": 1, "vectors": response_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(response_vectors)} public synthetic FinTS response vectors.")

parameter_vectors = []


def plain(value):
    return [scalar(value)]


def parameter_case(name, usage, permission_groups, expected_queries, include_bank=False, unknown=False):
    body = [segment("HIRMG", 2, 2, [[scalar(b"0010"), scalar(b""), scalar(b"Received")]])]
    if include_bank:
        body.append(segment("HIBPA", len(body) + 2, 3, [
            plain(b"7"), [scalar(b"280"), scalar(b"10020030")], plain(b"Synthetic Bank"), plain(b"0"),
            [scalar(b"1"), scalar(b"2")], [scalar(b"300"), scalar(b"220")], plain(b"0"), plain(b"120"), plain(b"0"),
        ]))
    if usage is not None:
        body.append(segment("HIUPA", len(body) + 2, 4, [plain(b"PUBLIC-USER"), plain(b"0"), plain(str(usage).encode())]))
    for permissions in permission_groups:
        fields = [
            [scalar(b"PUBLIC-ACCOUNT-SENTINEL"), scalar(b""), scalar(b"280"), scalar(b"10020030")],
            plain(b"SYNTHETIC-NOT-IBAN"), plain(b"CUSTOMER"), plain(b"1"), plain(b"EUR"),
            plain(b"Synthetic Owner"), plain(b""), plain(b""), plain(b""),
        ]
        fields.extend([[scalar(code.encode()), scalar(str(signatures).encode())] for code, signatures in permissions])
        body.append(segment("HIUPD", len(body) + 2, 6, fields))
    if unknown:
        body.append(segment("HIKOM", len(body) + 2, 4, [plain(b"UNACTIVATED-ENDPOINT")]))
    parts = frame(body, len(body) + 2, response=True)
    parameter_vectors.append({
        "name": name, "wireBase64": base64.b64encode(b"".join(part[0] for part in parts)).decode(),
        "accounts": len(permission_groups), "uninterpreted": int(unknown),
        "dialogueScoped": None if usage is None else True,
        "queries": [{"account": index, "operation": operation, "expected": evidence} for index, operation, evidence in expected_queries],
    })


parameter_case("bank-and-listed", 1, [[("HKSAL", 1), ("HKCAZ", 2)]],
               [(0, "HKSAL", "Listed"), (0, "HKXYZ", "Unknown")], include_bank=True)
parameter_case("unlisted-blocked", 0, [[("HKSAL", 1)]], [(0, "HKXYZ", "UnlistedBlocked")])
parameter_case("missing-user-scope", None, [[("HKSAL", 1)]], [(0, "HKXYZ", "Unknown")])
parameter_case("duplicates-preserved", 1, [[("HKSAL", 1), ("HKSAL", 2)], [("HKSAL", 1)]],
               [(0, "HKSAL", "Ambiguous"), (1, "HKSAL", "Listed")])
parameter_case("no-accounts-unknown-endpoint", 1, [], [], unknown=True)
parameter_target = target.with_name("parameters-v1.json")
parameter_target.write_text(json.dumps({"schemaVersion": 1, "vectors": parameter_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(parameter_vectors)} public synthetic FinTS parameter vectors.")

read_vectors = []


def read_case(name, operation, version, ads, issues, reply=b"0010"):
    body = [segment("HIRMG", 2, 2, [[scalar(reply), scalar(b""), scalar(b"Synthetic")]]),
            segment("HIBPA", 3, 3, [plain(b"1"), [scalar(b"280"), scalar(b"10020030")], plain(b"Bank"), plain(b"1"), plain(b"1"), plain(b"300")]),
            segment("HIUPA", 4, 4, [plain(b"PUBLIC-USER"), plain(b"1"), plain(b"1")]),
            segment("HIUPD", 5, 6, [[scalar(b"PUBLIC-ACCOUNT"), scalar(b""), scalar(b"280"), scalar(b"10020030")],
                                    plain(b"SYNTHETIC-IBAN"), plain(b"C"), plain(b"1"), plain(b"EUR"), plain(b"Owner"), plain(b""), plain(b""), plain(b""),
                                    [scalar(b"HKSAL"), scalar(b"2")], [scalar(b"HKSPA"), scalar(b"2")]])]
    for code, ad_version, fields in ads:
        body.append(segment(code, len(body) + 2, ad_version, fields))
    parts = frame(body, len(body) + 2, response=True)
    read_vectors.append({"name": name, "wireBase64": base64.b64encode(b"".join(p[0] for p in parts)).decode(),
                         "operation": operation, "version": version, "issues": issues})


balance = ("HISALS", 7, [plain(b"9"), plain(b"1"), plain(b"0")])
read_case("balance-matching", "Balance", 7, [balance], [])
read_case("duplicate-bank-version", "Balance", 7, [balance, balance], ["DuplicateAdvertisement"])
read_case("future-version-no-fallback", "Balance", 9, [balance, ("HISALS", 9, [plain(b"opaque")])], ["MissingAdvertisement", "UnsupportedVersion"])
read_case("single-account-disabled", "SepaAccountDetails", 3,
          [("HISPAS", 3, [plain(b"1"), plain(b"1"), plain(b"0"), [scalar(b"N"), scalar(b"J"), scalar(b"N"), scalar(b"N"), scalar(b"0")]])],
          ["SingleAccountRequestNotAdvertised"])
read_case("indeterminate-response", "Balance", 7, [balance], ["ResponseNeedsReview"], reply=b"9000")
target.with_name("read-capabilities-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": read_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_vectors)} public synthetic FinTS read-capability vectors.")

# Public plaintext PIN/TAN response wrappers, never client signatures or credentials.
pin_vectors = []


def pin_response(name, profile, options, expected, future=False):
    body = [segment("HIRMG", 2, 2, [[scalar(b"0010"), scalar(b""), scalar(b"received")]])]
    for option in options:
        body.append(segment("HIPINS", len(body) + 2, 1, [plain(b"1"), plain(b"1"), plain(b"0"),
                                                       [scalar(value) for value in option]]))
    if future:
        body.append(segment("HIPINS", len(body) + 2, 999, [plain(b"opaque")]))
    body.append(segment("ZBLOB", len(body) + 2, 999, [[scalar(bytes(range(256)), True)]]))
    inner = b"".join(item[0] for item in body)
    header = segment("HNVSK", 998, 3, [
        [scalar(b"PIN"), scalar(str(profile).encode("ascii"))], plain(b"998"), plain(b"1"),
        [scalar(b"1"), scalar(b""), scalar(b"PUBLIC-SYSTEM")],
        [scalar(b"1"), scalar(b"20260906"), scalar(b"120000")],
        [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), True), scalar(b"5"), scalar(b"1")],
        [scalar(b"280"), scalar(b"10020030"), scalar(b"PUBLIC-KEY-ID"), scalar(b"V"), scalar(b"0"), scalar(b"0")],
        plain(b"0"),
    ])
    parts = frame([header, segment("HNVSD", 999, 1, [[scalar(inner, True)]])], len(body) + 2, response=True)
    pin_vectors.append({"name": name, "wireBase64": base64.b64encode(b"".join(p[0] for p in parts)).decode("ascii"),
                        "bodyBase64": base64.b64encode(inner).decode("ascii"), "profileVersion": profile,
                        "advertisementCount": len(options), "balanceEvidence": expected})


pin_response("pin1-tan-required", 1, [[b"5", b"12", b"6", b"User + ID", b"", b"HKSAL", b"J"]], "TanReportedRequired")
pin_response("pin2-tan-not-reported-required", 2, [[b"0", b"99", b"0", b"", b"Customer: ID", b"HKSAL", b"N"]], "TanReportedNotRequired")
pin_response("omitted-bounds-unlisted", 2, [[b""]], "Unlisted")
pin_response("duplicate-operation-flags", 2, [[b"", b"", b"", b"", b"", b"HKSAL", b"J", b"HKSAL", b"N"]], "Ambiguous")
pin_response("future-advertisement-no-fallback", 2, [[b"", b"", b"", b"", b"", b"HKSAL", b"N"]], "Ambiguous", future=True)
target.with_name("pin-tan-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": pin_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(pin_vectors)} public synthetic FinTS PIN/TAN vectors.")

# HITANS/HITAN schema examples with independently chosen typed expectations.
tan_vectors = []


def tan_case(name, version, method, process, order_reference=b"PUBLIC-ORDER", duplicate=False, future=False):
    decoupled = version == 7 and method in (b"Decoupled", b"DecoupledPush")
    procedure = [b"900", b"2", b"PUBLIC_METHOD", method, b"1.0", b"Public method",
                 b"" if decoupled else b"6", b"" if decoupled else b"1", b"Approval", b"2048",
                 b"N", b"1", b"N", b"0", b"0", b"N", b"J", b"00", b"0", b"N", b"0"]
    if version == 7:
        procedure += [b"10", b"2", b"5", b"N", b"J"] if method == b"Decoupled" else [b""] * 5
    if process == b"1":
        procedure[1], procedure[11] = b"1", b"4"
    body = [segment("HIRMG", 2, 2, [[scalar(b"0030"), scalar(b""), scalar(b"Pending")]])]
    if future:
        body.append(segment("HITANS", 3, 99, [plain(b"opaque")]))
        body.append(segment("HITAN", 4, 99, [plain(b"opaque")], reference=2))
    else:
        options = [b"N", b"N", b"1" if process == b"1" else b"0"] + procedure * (2 if duplicate else 1)
        body.append(segment("HITANS", 3, version, [plain(b"1"), plain(b"1"), plain(b"0"), [scalar(v) for v in options]]))
        fields = [plain(process), [scalar(bytes(range(20)), True)] if process == b"1" else plain(b""), plain(order_reference),
                  plain(b"nochallenge" if order_reference == b"noref" else b"<b>PUBLIC</b><br>Confirm + : ' ? @"),
                  [scalar(bytes(range(256)), True)], [scalar(b"20260906"), scalar(b"123456")], plain(b"Public device")]
        body.append(segment("HITAN", 4, version, fields, reference=2))
        if duplicate:
            body.append(segment("HITAN", 5, version, fields, reference=2))
    wire = b"".join(p[0] for p in frame(body, len(body) + 2, response=True))
    tan_vectors.append({"name": name, "wireBase64": base64.b64encode(wire).decode("ascii"),
                        "version": version, "procedureCount": 0 if future else (2 if duplicate else 1),
                        "challengeCount": 0 if future else (2 if duplicate else 1), "decoupled": decoupled,
                        "duplicates": duplicate, "noReference": order_reference == b"noref", "process": process.decode("ascii")})


tan_case("v6-hash-and-text", 6, b"HHD", b"1")
tan_case("v7-decoupled-status", 7, b"Decoupled", b"S")
tan_case("v7-decoupled-push", 7, b"DecoupledPush", b"4")
tan_case("dummy-reference-is-evidence-only", 7, b"App", b"4", order_reference=b"noref")
tan_case("duplicate-procedure-and-challenge", 7, b"App", b"4", duplicate=True)
tan_case("future-versions-opaque", 99, b"Future", b"S", future=True)
target.with_name("tan-schemas-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": tan_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(tan_vectors)} public synthetic FinTS TAN-schema vectors.")

context_vectors = []


def tan_context_case(name, process=b"4", status=b"0030", permitted=b"900", reference=b"PUBLIC-ORDER", hash_mismatch=False, abort=False):
    variant_one, polling = process == b"1", process == b"S"
    version = 6 if variant_one else 7
    request_fields = [plain(process), plain(b"" if polling else b"HKIDN"), plain(b""),
                      [scalar(b"public hash", True)] if variant_one else plain(b""),
                      plain(b"PUBLIC-ORDER" if polling else b""), plain(b"N" if variant_one or polling else b"")]
    request = b"".join(p[0] for p in frame([segment("HKTAN", 2, version, request_fields)], 3))
    procedure = [b"900", b"1" if variant_one else b"2", b"PUBLIC_METHOD", b"Decoupled" if polling else b"App", b"1.0", b"Public method",
                 b"" if polling else b"6", b"" if polling else b"1", b"Approval", b"2048", b"N", b"4" if variant_one else b"1",
                 b"N", b"0", b"0", b"N", b"J", b"00", b"0", b"N", b"0"]
    if version == 7:
        procedure += [b"10", b"2", b"5", b"N", b"J"] if polling else [b""] * 5
    replies = [[scalar(b"3920"), scalar(b""), scalar(b"Reported methods"), scalar(permitted)],
               [scalar(status), scalar(b""), scalar(b"Reported state")]]
    body = [segment("HIRMG", 2, 2, [[scalar(b"9800" if abort else b"0010"), scalar(b""), scalar(b"Synthetic")]]),
            segment("HIRMS", 3, 2, replies, reference=2),
            segment("HITANS", 4, version, [plain(b"1"), plain(b"1"), plain(b"0"),
                [scalar(v) for v in [b"N", b"N", b"1" if variant_one else b"0"] + procedure]]),
            segment("HITAN", 5, version, [plain(process),
                [scalar(b"wrong hash" if hash_mismatch else b"public hash", True)] if variant_one else plain(b""),
                plain(reference), plain(b"nochallenge" if reference == b"noref" else b"Public challenge")], reference=2)]
    response_wire = b"".join(p[0] for p in frame(body, 6, response=True))
    issues = []
    if hash_mismatch:
        issues.append("HashMismatch")
    if permitted != b"900":
        issues.append("ProcedureNotListed")
    if abort:
        issues += ["ParameterResponseNeedsReview", "ResponseNeedsReview"]
    if polling and reference != b"PUBLIC-ORDER":
        issues.append("OrderReferenceMismatch")
    outcome = "NeedsReview" if issues else "ExemptionReported" if status == b"3076" else "DecoupledPendingReported" if polling else "ChallengeReported"
    context_vectors.append({"name": name, "requestBase64": base64.b64encode(request).decode("ascii"),
                            "responseBase64": base64.b64encode(response_wire).decode("ascii"), "issues": issues, "observation": outcome})


tan_context_case("request-bound-challenge")
tan_context_case("exemption-report-with-dummy", status=b"3076", reference=b"noref")
tan_context_case("hash-must-mirror-request", process=b"1", hash_mismatch=True)
tan_context_case("decoupled-pending-report", process=b"S", status=b"3956")
tan_context_case("status-order-reference-mismatch", process=b"S", status=b"3956", reference=b"OTHER-ORDER")
tan_context_case("procedure-not-listed", permitted=b"901")
tan_context_case("aborted-dialogue-still-reports-methods", abort=True)
target.with_name("tan-context-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": context_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(context_vectors)} public synthetic FinTS TAN-context vectors.")

# Explicit multi-message traces. Expected transitions are independently selected,
# not calculated by a Python copy of the .NET state machine.
sca_vectors = []


def numbered_frame(body, number, response=False):
    fields = [plain(b"000000000000"), plain(b"300"), plain(b"SYNTHETIC"), plain(str(number).encode("ascii"))]
    if response:
        fields.append([scalar(b"SYNTHETIC"), scalar(str(number).encode("ascii"))])
    trailer = segment("HNHBS", len(body) + 2, 1, [plain(str(number).encode("ascii"))])
    size = len(segment("HNHBK", 1, 3, fields)[0]) + sum(len(p[0]) for p in body) + len(trailer[0])
    fields[0] = plain(f"{size:012d}".encode("ascii"))
    return base64.b64encode(b"".join(p[0] for p in [segment("HNHBK", 1, 3, fields), *body, trailer])).decode("ascii")


def sca_case(name, method, steps, bank_limit=b"10", exemption=False, wrong_reference=False):
    decoupled = method in (b"Decoupled", b"DecoupledPush")
    procedure = [b"900", b"2", b"PUBLIC_METHOD", method, b"1.0", b"Public method", b"" if decoupled else b"6",
                 b"" if decoupled else b"1", b"Approval", b"2048", b"N", b"1", b"N", b"0", b"0", b"N", b"J", b"00", b"0", b"N", b"0"]
    procedure += [bank_limit, b"2", b"5", b"N", b"J"] if method == b"Decoupled" else [b""] * 5
    request = numbered_frame([segment("HKTAN", 2, 7, [plain(b"4"), plain(b"HKIDN")])], 1)
    response = numbered_frame([
        segment("HIRMG", 2, 2, [[scalar(b"0010"), scalar(b""), scalar(b"Synthetic")]]),
        segment("HIRMS", 3, 2, [[scalar(b"3920"), scalar(b""), scalar(b"Methods"), scalar(b"900")],
                               [scalar(b"3076" if exemption else b"0030"), scalar(b""), scalar(b"State")]], reference=2),
        segment("HITANS", 4, 7, [plain(b"1"), plain(b"1"), plain(b"0"), [scalar(v) for v in [b"N", b"N", b"0"] + procedure]]),
        segment("HITAN", 5, 7, [plain(b"4"), plain(b""), plain(b"noref" if exemption else b"PUBLIC-ORDER"),
                                plain(b"nochallenge" if exemption else b"Public challenge")], reference=2),
    ], 1, response=True)
    queries = []
    for number, status in [(2, b"3956"), (3, b"0020")]:
        queries.append({"requestBase64": numbered_frame([segment("HKTAN", 2, 7, [plain(b"S"), plain(b""), plain(b""), plain(b""), plain(b"PUBLIC-ORDER"), plain(b"N")])], number),
                        "responseBase64": numbered_frame([
                            segment("HIRMG", 2, 2, [[scalar(b"0010"), scalar(b""), scalar(b"Synthetic")]]),
                            segment("HIRMS", 3, 2, [[scalar(status), scalar(b""), scalar(b"State")]], reference=2),
                            segment("HITAN", 4, 7, [plain(b"S"), plain(b""), plain(b"OTHER-ORDER" if wrong_reference else b"PUBLIC-ORDER"), plain(b"nochallenge")], reference=2),
                        ], number, response=True)})
    sca_vectors.append({"name": name, "requestBase64": request, "responseBase64": response, "queries": queries, "steps": steps})


def step(action, expected, state, at=0, query=0):
    return {"action": action, "expected": expected, "state": state, "atMilliseconds": at, "query": query}


sca_case("single-user-handoff", b"App", [step("start", "Started", "AwaitingUserContinuation"),
    step("continue", "UserContinuationRecorded", "UserContinuationRecorded"), step("continue", "Terminal", "UserContinuationRecorded")])
sca_case("pending-then-execution-with-replay", b"Decoupled", [step("start", "Started", "WaitingToQuery"),
    step("query", "TooEarly", "WaitingToQuery", 1999), step("query", "QueryRecorded", "AwaitingQueryResponse", 2000),
    step("query", "WrongState", "AwaitingQueryResponse", 2000), step("response", "PendingAccepted", "WaitingToQuery", 2000),
    step("response", "ReplayRejected", "WaitingToQuery", 2000), step("query", "TooEarly", "WaitingToQuery", 6999, 1),
    step("query", "QueryRecorded", "AwaitingQueryResponse", 7000, 1), step("response", "ExecutionReported", "ExecutionReported", 7000, 1)])
sca_case("absolute-timeout", b"App", [step("start", "Started", "AwaitingUserContinuation"),
    step("snapshot", "Snapshot", "AwaitingUserContinuation", 9999), step("continue", "Terminal", "TimedOut", 10000)])
sca_case("cancel-in-flight", b"Decoupled", [step("start", "Started", "WaitingToQuery"), step("query", "QueryRecorded", "AwaitingQueryResponse", 2000),
    step("cancel", "Cancelled", "Cancelled", 2000), step("response", "Terminal", "Cancelled", 2000), step("start", "Terminal", "Cancelled", 2000)])
sca_case("bank-query-limit", b"Decoupled", [step("start", "Started", "WaitingToQuery"), step("query", "QueryRecorded", "AwaitingQueryResponse", 2000),
    step("response", "Terminal", "QueryLimitReached", 2000)], bank_limit=b"1")
sca_case("push-awaits-external-notification", b"DecoupledPush", [step("start", "Started", "AwaitingExternalNotification"),
    step("continue", "WrongState", "AwaitingExternalNotification"), step("stop", "Stopped", "Stopped")])
sca_case("exemption-report-is-terminal", b"App", [step("start", "ExemptionReported", "ExemptionReported"),
    step("continue", "Terminal", "ExemptionReported")], exemption=True)
sca_case("mismatched-bound-response-stops", b"Decoupled", [step("start", "Started", "WaitingToQuery"), step("query", "QueryRecorded", "AwaitingQueryResponse", 2000),
    step("response", "RejectedForReview", "NeedsReview", 2000)], wrong_reference=True)
target.with_name("sca-continuation-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": sca_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(sca_vectors)} public synthetic FinTS SCA-continuation traces.")

# Read schemas: expected domain-neutral observations are selected explicitly.
read_data_vectors = []
national = [b"PUBLIC-001", b"00", b"280", b"PUBLIC-BANK"]
international = [b"PUBLIC-IBAN", b"PUBLIC-BIC", *national]
sepa = [b"J", *international]


def group(values):
    return [scalar(value) for value in values]


def read_case(name, operation, version, request_fields, reports, all_accounts,
              request_accounts, discovered=0, balances=None, unknown=0):
    body = [segment("HIRMG", 2, 2, [group([b"0010", b"", b"Synthetic"])])]
    body += [segment(code, index + 3, response_version, fields, reference=2)
             for index, (code, response_version, fields) in enumerate(reports)]
    read_data_vectors.append({"name": name,
        "requestBase64": numbered_frame([segment(operation, 2, version, request_fields)], 1),
        "responseBase64": numbered_frame(body, 1, response=True),
        "allAccounts": all_accounts, "requestAccounts": request_accounts,
        "discoveredAccounts": discovered, "unknownReports": unknown,
        "balances": balances or []})


read_case("discovery-all-with-non-sepa-shell", "HKSPA", 1, [],
          [("HISPA", 1, [group(sepa), group([b"N", b"", b"", b"PUBLIC-002", b"", b"280", b"PUBLIC-BANK"])])], True, 0, discovered=2)
read_case("selected-discovery-retains-duplicates", "HKSPA", 1, [group(national), group(national)],
          [("HISPA", 1, [group(sepa), group(sepa)])], False, 2, discovered=2)
read_case("empty-discovery", "HKSPA", 1, [], [("HISPA", 1, [])], True, 0)
read_case("ambiguous-new-discovery-versions-stay-opaque", "HKSPA", 1, [],
          [("HISPA", 2, [plain(b"opaque")]), ("HISPA", 3, [plain(b"opaque")])], True, 0, unknown=2)
for version in (6, 7, 8):
    account = national if version == 6 else international
    fields = [group(account), plain(b"PUBLIC + Product: A?"), plain(b"EUR"),
              group([b"D", b"1234,56", b"EUR", b"20260907", b"123456"])]
    if version != 6:
        fields += [group([b"C", b"12,34", b"EUR", b"20260907"]), group([b"500,", b"EUR"]),
                   group([b"0,", b"EUR"]), plain(b""), group([b"3,", b"EUR"]), group([b"20260904"]), plain(b"20261001")]
    if version == 8:
        fields += [group([b"45,67", b"EUR"])]
    read_case(f"balance-v{version}-exact-and-optional", "HKSAL", version, [group(account), plain(b"N")],
              [("HISAL", version, fields)], False, 1, balances=[{"booked": "-1234.56", "currency": "EUR",
              "available": None if version == 6 else "0", "currencyConflict": False,
              "accountNumber": "PUBLIC-001", "productLabel": "PUBLIC + Product: A?"}])
read_case("debit-zero-and-currency-conflict", "HKSAL", 8, [group(international), plain(b"J"), plain(b"1"), plain(b"PUBLIC+PAGE")],
          [("HISAL", 8, [group(international), plain(b"PUBLIC"), plain(b"EUR"),
                         group([b"D", b"0,", b"EUR", b"20240229"]), plain(b""), plain(b""), group([b"1,23", b"USD"])])],
          True, 1, balances=[{"booked": "0", "currency": "EUR", "available": "1.23", "currencyConflict": True,
                             "accountNumber": "PUBLIC-001", "productLabel": "PUBLIC"}])
target.with_name("read-data-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": read_data_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_data_vectors)} public synthetic FinTS read-data vectors.")

# Context expectations are explicit, not computed by a second comparator.
read_context_vectors = []
context_parameters = numbered_frame([
    segment("HIRMG", 2, 2, [group([b"0010", b"", b"Synthetic"])]),
    segment("HIBPA", 3, 3, [plain(b"1"), group([b"280", b"PUBLIC-BANK"]), plain(b"Bank"), plain(b"1"), plain(b"1"), plain(b"300")]),
    segment("HIUPA", 4, 4, [plain(b"PUBLIC-USER"), plain(b"1"), plain(b"1")]),
    segment("HIUPD", 5, 6, [group(national), plain(b"PUBLIC-IBAN"), plain(b"C"), plain(b"1"), plain(b"EUR"), plain(b"Owner"),
                           plain(b""), plain(b""), plain(b""), group([b"HKSAL", b"1"]), group([b"HKSPA", b"1"])]),
    segment("HISALS", 6, 8, [plain(b"1"), plain(b"1"), plain(b"0"), plain(b"J")]),
    segment("HISPAS", 7, 1, [plain(b"1"), plain(b"1"), plain(b"0"), group([b"J", b"J", b"N"])]),
], 1, response=True)


def context_case(name, issues, outcome="NeedsReview", discovery=False, response_account=None,
                 response_version=None, reference=2, partial=False, unsolicited=False):
    request_fields = [group(national)] if discovery else [group(international[:2]), plain(b"N")]
    if unsolicited:
        request_fields += [plain(b""), plain(b"PUBLIC+PAGE")]
    request = numbered_frame([segment("HKSPA" if discovery else "HKSAL", 2, 1 if discovery else 8, request_fields)], 2)
    status = [b"3040" if partial else b"0020", b"", b"Synthetic"] + ([b"PUBLIC+PAGE"] if partial else [])
    version = response_version if response_version is not None else (1 if discovery else 8)
    fields = [group(sepa)] if discovery else [group(response_account or international[:2]), plain(b"PUBLIC"), plain(b"EUR"), group([b"C", b"1,", b"EUR", b"20260907"])]
    if version == 99:
        fields = [plain(b"opaque")]
    response = numbered_frame([
        segment("HIRMG", 2, 2, [group([b"0010", b"", b"Synthetic"])]),
        segment("HIRMS", 3, 2, [group(status)], reference=2),
        segment("HISPA" if discovery else "HISAL", 4, version, fields, reference=reference),
    ], 2, response=True)
    read_context_vectors.append({"name": name, "parametersBase64": context_parameters,
        "requestBase64": request, "responseBase64": response, "issues": issues, "outcome": outcome,
        "continuation": "PUBLIC+PAGE" if partial else None})


context_case("matching-balance", [], "ExecutionReported")
context_case("matching-selected-discovery", [], "ExecutionReported", discovery=True)
context_case("wrong-source-account", ["ResponseAccountMismatch"], response_account=[b"OTHER", b"PUBLIC-BIC"])
context_case("wrong-request-reference", ["ReferenceMismatch"], reference=1)
context_case("wrong-response-version", ["VersionMismatch"], response_version=7)
context_case("scoped-partial-token", [], "PartialReported", partial=True)
context_case("unsolicited-continuation", ["ContinuationScopeMismatch"], unsolicited=True)
context_case("unknown-response-schema", ["UnexpectedReport", "MissingReport"], response_version=99)
target.with_name("read-context-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": read_context_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_context_vectors)} public synthetic FinTS read-context vectors.")

refresh_request = read_context_vectors[0]["requestBase64"]
refresh_next_request = numbered_frame([segment("HKSAL", 2, 8, [group(international[:2]), plain(b"N"), plain(b""), plain(b"PUBLIC+PAGE")])], 3)


def refresh_response(number, partial=False):
    return numbered_frame([
        segment("HIRMG", 2, 2, [group([b"0010", b"", b"Synthetic"])]),
        segment("HIRMS", 3, 2, [group([b"3040" if partial else b"0020", b"", b"Synthetic"] + ([b"PUBLIC+PAGE"] if partial else []))], reference=2),
        segment("HISAL", 4, 8, [group(international[:2]), plain(b"PUBLIC"), plain(b"EUR"), group([b"C", b"1,", b"EUR", b"20260907"])], reference=2),
    ], number, response=True)


def refresh_step(action, expected, state, at=0, index=0):
    return {"action": action, "expected": expected, "state": state, "atMilliseconds": at, "index": index}


read_refresh_vectors = []


def refresh_case(name, responses, steps, maximum=16):
    read_refresh_vectors.append({"name": name, "parametersBase64": context_parameters,
        "requests": [refresh_request, refresh_next_request], "responses": responses, "maximumPages": maximum, "steps": steps})


refresh_case("one-execution-and-terminal-replay", [refresh_response(2)], [
    refresh_step("start", "RequestRecorded", "AwaitingResponse"), refresh_step("response", "ExecutionReported", "ExecutionReported"),
    refresh_step("response", "Terminal", "ExecutionReported"), refresh_step("start", "Terminal", "ExecutionReported")])
refresh_case("partial-continuation-and-replay", [refresh_response(2, True), refresh_response(3)], [
    refresh_step("start", "RequestRecorded", "AwaitingResponse"), refresh_step("response", "PartialAccepted", "WaitingToContinue"),
    refresh_step("response", "ReplayRejected", "WaitingToContinue"), refresh_step("continue", "RequestRecorded", "AwaitingResponse", index=1),
    refresh_step("response", "ReplayRejected", "AwaitingResponse"), refresh_step("response", "ExecutionReported", "ExecutionReported", index=1)])
refresh_case("deadline-is-not-extended", [refresh_response(2, True), refresh_response(3)], [
    refresh_step("start", "RequestRecorded", "AwaitingResponse"), refresh_step("response", "PartialAccepted", "WaitingToContinue", 9000),
    refresh_step("continue", "RequestRecorded", "AwaitingResponse", 9999, 1), refresh_step("response", "Terminal", "TimedOut", 10000, 1)])
refresh_case("cancel-in-flight", [refresh_response(2)], [refresh_step("start", "RequestRecorded", "AwaitingResponse"),
    refresh_step("cancel", "Cancelled", "Cancelled"), refresh_step("response", "Terminal", "Cancelled")])
refresh_case("partial-at-page-limit", [refresh_response(2, True)], [refresh_step("start", "RequestRecorded", "AwaitingResponse"),
    refresh_step("response", "Terminal", "PageLimitReached")], maximum=1)
refresh_case("unrelated-reference-keeps-pending", [read_context_vectors[3]["responseBase64"], refresh_response(2)], [
    refresh_step("start", "RequestRecorded", "AwaitingResponse"), refresh_step("response", "ContextMismatch", "AwaitingResponse"),
    refresh_step("response", "ExecutionReported", "ExecutionReported", index=1)])
refresh_case("bound-wrong-account-stops", [read_context_vectors[2]["responseBase64"], refresh_response(2)], [
    refresh_step("start", "RequestRecorded", "AwaitingResponse"), refresh_step("response", "RejectedForReview", "NeedsReview"),
    refresh_step("response", "Terminal", "NeedsReview", index=1)])
refresh_case("stop-before-start", [refresh_response(2)], [refresh_step("stop", "Stopped", "Stopped"),
    refresh_step("start", "Terminal", "Stopped"), refresh_step("response", "Terminal", "Stopped")])
target.with_name("read-refresh-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": read_refresh_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_refresh_vectors)} public synthetic FinTS read-refresh traces.")

read_availability_vectors = []


def availability_case(name, discovery, statuses, report, issues, outcome):
    request = numbered_frame([segment("HKSPA" if discovery else "HKSAL", 2, 1 if discovery else 8,
                              [group(national)] if discovery else [group(international[:2]), plain(b"N")])], 2)
    body = [segment("HIRMG", 2, 2, [group([b"0010", b"", b"Synthetic"])]),
            segment("HIRMS", 3, 2, [group(values) for values in statuses], reference=2)]
    if report == "empty":
        body.append(segment("HISPA", 4, 1, [], reference=2))
    elif report == "zero":
        body.append(segment("HISAL", 4, 8, [group(international[:2]), plain(b"PUBLIC"), plain(b"EUR"), group([b"C", b"0,", b"EUR", b"20260907"])], reference=2))
    read_availability_vectors.append({"name": name, "parametersBase64": context_parameters,
        "requestBase64": request, "responseBase64": numbered_frame(body, 2, response=True),
        "issues": issues, "outcome": outcome, "discoveryReports": 1 if report == "empty" else 0,
        "balanceReports": 1 if report == "zero" else 0})


availability_case("empty-discovery-unavailable", True, [[b"3010", b"", b"Synthetic"]], "empty", [], "UnavailableReported")
availability_case("status-only-discovery-unavailable", True, [[b"3010", b"", b"Synthetic"]], "none", [], "UnavailableReported")
availability_case("status-only-balance-unavailable", False, [[b"3010", b"", b"Synthetic"]], "none", [], "UnavailableReported")
availability_case("zero-balance-conflicts-with-unavailable", False, [[b"3010", b"", b"Synthetic"]], "zero", ["AvailabilityConflict"], "NeedsReview")
availability_case("missing-report-is-not-unavailable", False, [[b"0020", b"", b"Synthetic"]], "none", ["MissingReport"], "NeedsReview")
availability_case("empty-discovery-needs-explicit-status", True, [[b"0020", b"", b"Synthetic"]], "empty", ["MissingReport"], "NeedsReview")
availability_case("9210-remains-an-error", False, [[b"9210", b"", b"Synthetic"]], "none", ["MissingReport", "ResponseNeedsReview", "StatusNeedsReview"], "NeedsReview")
availability_case("partial-and-unavailable-conflict", False, [[b"3010", b"", b"Synthetic"], [b"3040", b"", b"Synthetic", b"PAGE"]], "none", ["AvailabilityConflict"], "NeedsReview")
target.with_name("read-availability-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": read_availability_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_availability_vectors)} public synthetic FinTS read-availability vectors.")

all_discovery_vectors = []


def all_discovery_case(name, user_accounts, returned_accounts, issues, row_issues, candidate_counts,
                       unmatched, outcome="NeedsReview", status=b"0020"):
    body = [segment("HIRMG", 2, 2, [group([b"0010", b"", b"Synthetic"])]),
            segment("HIBPA", 3, 3, [plain(b"1"), group([b"280", b"PUBLIC-BANK"]), plain(b"Bank"), plain(b"1"), plain(b"1"), plain(b"300")]),
            segment("HIUPA", 4, 4, [plain(b"PUBLIC-USER"), plain(b"1"), plain(b"1")])]
    for i, (number, iban, permission) in enumerate(user_accounts):
        body.append(segment("HIUPD", i + 5, 6, [group([number, b"00", b"280", b"PUBLIC-BANK"]), plain(iban), plain(b"C"), plain(b"1"), plain(b"EUR"),
                    plain(b"Owner"), plain(b""), plain(b""), plain(b""), group([permission, b"1"])]))
    body.append(segment("HISPAS", len(body) + 2, 1, [plain(b"1"), plain(b"1"), plain(b"0"), group([b"N", b"J", b"N"])]))
    response = [segment("HIRMG", 2, 2, [group([b"0010", b"", b"Synthetic"])]),
                segment("HIRMS", 3, 2, [group([status, b"", b"Synthetic"])], reference=2),
                segment("HISPA", 4, 1, [group(values) for values in returned_accounts], reference=2)]
    all_discovery_vectors.append({"name": name, "parametersBase64": numbered_frame(body, 1, response=True),
        "requestBase64": numbered_frame([segment("HKSPA", 2, 1, [])], 2), "responseBase64": numbered_frame(response, 2, response=True),
        "issues": issues, "rowIssues": row_issues, "candidateCounts": candidate_counts, "unmatched": unmatched, "outcome": outcome})


user_one = (b"PUBLIC-001", b"PUBLIC-IBAN", b"HKSPA")
user_two = (b"PUBLIC-002", b"", b"HKSPA")
non_sepa_two = [b"N", b"", b"", b"PUBLIC-002", b"00", b"280", b"PUBLIC-BANK"]
all_discovery_case("known-sepa-and-national-shell", [user_one, user_two], [sepa, non_sepa_two], [], [[], []], [1, 1], 0, "ExecutionReported")
all_discovery_case("new-source-account-needs-review", [user_one], [sepa, non_sepa_two], ["AccountNeedsReview"], [[], ["UnknownAccount"]], [1, 0], 0)
all_discovery_case("duplicate-returned-account", [user_one], [sepa, sepa], ["AccountNeedsReview", "UnmatchedUserAccounts"], [["DuplicateReturnedIdentity"], ["DuplicateReturnedIdentity"]], [1, 1], 1)
all_discovery_case("national-and-iban-point-to-different-upd", [user_one, user_two], [[b"J", b"PUBLIC-IBAN", b"PUBLIC-BIC", b"PUBLIC-002", b"00", b"280", b"PUBLIC-BANK"]],
                   ["AccountNeedsReview", "UnmatchedUserAccounts"], [["IdentityConflict", "AmbiguousUserAccount"]], [2], 2)
all_discovery_case("duplicate-upd-candidates", [user_one, user_one], [sepa], ["AccountNeedsReview", "UnmatchedUserAccounts"], [["AmbiguousUserAccount"]], [2], 2)
all_discovery_case("known-account-not-returned", [user_one, user_two], [sepa], ["UnmatchedUserAccounts"], [[]], [1], 1)
all_discovery_case("permission-is-not-assumed", [(b"PUBLIC-001", b"PUBLIC-IBAN", b"HKSAL")], [sepa], ["AccountNeedsReview", "UnmatchedUserAccounts"], [["PermissionNeedsReview"]], [1], 1)
all_discovery_case("unavailable-does-not-remove-upd", [user_one], [], [], [], [], 1, "UnavailableReported", b"3010")
target.with_name("all-discovery-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": all_discovery_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(all_discovery_vectors)} public synthetic FinTS all-account discovery vectors.")

all_attempt_vectors = []


def all_attempt_step(action, expected, state, at=0, index=0, evidence=False):
    return {"action": action, "expected": expected, "state": state, "atMilliseconds": at, "index": index, "evidence": evidence}


def all_attempt_case(name, template, responses, steps):
    all_attempt_vectors.append({"name": name, "parametersBase64": template["parametersBase64"],
        "requestBase64": template["requestBase64"], "responses": responses, "steps": steps})


clean_all = all_discovery_vectors[0]
clean_response = clean_all["responseBase64"]
all_attempt_case("execution-handed-out-once", clean_all, [clean_response], [
    all_attempt_step("start", "RequestRecorded", "AwaitingResponse"),
    all_attempt_step("response", "ExecutionReported", "ExecutionReported", evidence=True),
    all_attempt_step("response", "Terminal", "ExecutionReported"), all_attempt_step("start", "Terminal", "ExecutionReported")])
no_all = all_discovery_vectors[7]
all_attempt_case("unavailable-preserves-unmatched-evidence", no_all, [no_all["responseBase64"]], [
    all_attempt_step("start", "RequestRecorded", "AwaitingResponse"),
    all_attempt_step("response", "UnavailableReported", "UnavailableReported", evidence=True)])
ambiguous_all = all_discovery_vectors[3]
all_attempt_case("conflicting-candidates-returned-for-review", ambiguous_all, [ambiguous_all["responseBase64"]], [
    all_attempt_step("start", "RequestRecorded", "AwaitingResponse"),
    all_attempt_step("response", "RejectedForReview", "NeedsReview", evidence=True), all_attempt_step("response", "Terminal", "NeedsReview")])
all_attempt_case("cancel-before-response", clean_all, [clean_response], [all_attempt_step("start", "RequestRecorded", "AwaitingResponse"),
    all_attempt_step("cancel", "Cancelled", "Cancelled"), all_attempt_step("response", "Terminal", "Cancelled")])
all_attempt_case("absolute-timeout", clean_all, [clean_response], [all_attempt_step("start", "RequestRecorded", "AwaitingResponse"),
    all_attempt_step("snapshot", "Snapshot", "AwaitingResponse", 9999), all_attempt_step("response", "Terminal", "TimedOut", 10000)])
wrong_reference = base64.b64encode(base64.b64decode(clean_response).replace(b"HISPA:4:1:2", b"HISPA:4:1:1")).decode("ascii")
all_attempt_case("foreign-reference-keeps-pending", clean_all, [wrong_reference, clean_response], [
    all_attempt_step("start", "RequestRecorded", "AwaitingResponse"), all_attempt_step("response", "ContextMismatch", "AwaitingResponse"),
    all_attempt_step("response", "ExecutionReported", "ExecutionReported", index=1, evidence=True)])
all_attempt_case("stop-before-start", clean_all, [clean_response], [all_attempt_step("stop", "Stopped", "Stopped"),
    all_attempt_step("start", "Terminal", "Stopped"), all_attempt_step("response", "Terminal", "Stopped")])
partial_all = base64.b64encode(base64.b64decode(clean_response).replace(b"0020::Synthetic", b"3040::Synthetic")).decode("ascii")
all_attempt_case("unsupported-partial-requires-review", clean_all, [partial_all], [
    all_attempt_step("start", "RequestRecorded", "AwaitingResponse"), all_attempt_step("response", "RejectedForReview", "NeedsReview", evidence=True)])
target.with_name("all-discovery-attempt-v1.json").write_text(json.dumps({"schemaVersion": 1, "vectors": all_attempt_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(all_attempt_vectors)} public synthetic FinTS all-account discovery attempt traces.")

# Typed unsigned request inputs with explicitly chosen wire field layouts. This
# producer uses no .NET writer/parser output, security material or bank recording.
write_vectors = []


def write_vector(name, operation, version, accounts, fields, dialogue="SYNTHETIC", number=2,
                 all_accounts=False, maximum=None, continuation=None):
    body = segment(operation, 2, version, [[scalar(v.encode("latin-1")) for v in field] for field in fields])
    header_fields = [[scalar(b"000000000000")], [scalar(b"300")],
                     [scalar(dialogue.encode("latin-1"))], [scalar(str(number).encode("ascii"))]]
    trailer = segment("HNHBS", 3, 1, [[scalar(str(number).encode("ascii"))]])
    size = len(segment("HNHBK", 1, 3, header_fields)[0]) + len(body[0]) + len(trailer[0])
    header_fields[0] = [scalar(f"{size:012d}".encode("ascii"))]
    wire = segment("HNHBK", 1, 3, header_fields)[0] + body[0] + trailer[0]
    write_vectors.append({"name": name, "operation": operation, "version": version,
                          "dialogue": dialogue, "messageNumber": number, "accounts": accounts,
                          "allAccounts": all_accounts, "maximumEntries": maximum,
                          "continuationToken": continuation, "wireBase64": base64.b64encode(wire).decode("ascii")})


write_national = {"number": "PUBLIC-001", "subaccount": "", "country": "280", "institution": "PUBLIC-BANK"}
write_international = {"iban": "PUBLIC-IBAN", "bic": "PUBLIC-BIC"}
write_vector("discovery-all", "HKSPA", 1, [], [], number=1, all_accounts=True)
write_vector("discovery-selected-empty-subaccount", "HKSPA", 1, [write_national],
             [["PUBLIC-001", "", "280", "PUBLIC-BANK"]])
write_escaped = {"number": "A+:'?@ß", "subaccount": " 00 ", "country": "280", "institution": "Bänk"}
write_vector("discovery-duplicate-escaped-identifiers", "HKSPA", 1, [write_escaped, write_escaped],
             [["A+:'?@ß", " 00 ", "280", "Bänk"], ["A+:'?@ß", " 00 ", "280", "Bänk"]], dialogue="D+:'?@ü")
write_vector("balance-six-maximum", "HKSAL", 6, [write_national],
             [["PUBLIC-001", "", "280", "PUBLIC-BANK"], ["N"], ["9999"]], maximum=9999)
write_vector("balance-seven-international", "HKSAL", 7, [write_international],
             [["PUBLIC-IBAN", "PUBLIC-BIC"], ["N"]])
write_vector("balance-eight-combined-all", "HKSAL", 8, [dict(write_national, **write_international)],
             [["PUBLIC-IBAN", "PUBLIC-BIC", "PUBLIC-001", "", "280", "PUBLIC-BANK"], ["J"]], all_accounts=True)
write_vector("balance-eight-national-token", "HKSAL", 8, [write_national],
             [["", "", "PUBLIC-001", "", "280", "PUBLIC-BANK"], ["N"], [""], ["page+:'?@ü"]], continuation="page+:'?@ü")
write_vector("balance-six-omitted-institution", "HKSAL", 6, [{"number": "0001", "country": "280"}],
             [["0001", "", "280"], ["N"], ["1"], ["next"]], maximum=1, continuation="next")
write_maximum = {"number": "N" * 30, "subaccount": "S" * 30, "country": "280", "institution": "I" * 30,
                 "iban": "A" * 34, "bic": "B" * 11}
write_vector("balance-eight-field-limits", "HKSAL", 8, [write_maximum],
             [["A" * 34, "B" * 11, "N" * 30, "S" * 30, "280", "I" * 30], ["N"], ["1"], ["?" * 35]],
             dialogue="?" * 30, number=9999, maximum=1, continuation="?" * 35)
write_vector("balance-seven-national-minimum", "HKSAL", 7, [{"number": "0001", "country": "280"}],
             [["", "", "0001", "", "280"], ["N"]])
target.with_name("unsigned-read-requests-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": write_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(write_vectors)} public synthetic unsigned read-request vectors.")

# Initialization layouts and inputs are explicit, independent of the .NET schemas.
# The anonymous customer identifier has TEN nines; the PDF's superscript footnote
# 3 is not part of that identifier (confirmed by the anonymous response guidance).
initialization_vectors = []


def initialization_vector(name, identification, preparation):
    body = [segment("HKIDN", 2, 2, [[scalar(v.encode("latin-1")) for v in field] for field in identification]),
            segment("HKVVB", 3, 3, [[scalar(v.encode("latin-1"))] for v in preparation])]
    header_fields = [[scalar(b"000000000000")], [scalar(b"300")], [scalar(b"0")], [scalar(b"1")]]
    trailer = segment("HNHBS", 4, 1, [[scalar(b"1")]])
    size = len(segment("HNHBK", 1, 3, header_fields)[0]) + sum(len(p[0]) for p in body) + len(trailer[0])
    header_fields[0] = [scalar(f"{size:012d}".encode("ascii"))]
    wire = segment("HNHBK", 1, 3, header_fields)[0] + b"".join(p[0] for p in body) + trailer[0]
    initialization_vectors.append({"name": name, "country": identification[0][0], "institution": identification[0][1],
                                   "customerId": identification[1][0], "systemId": identification[2][0], "systemStatus": int(identification[3][0]),
                                   "bankParameterVersion": int(preparation[0]), "userParameterVersion": int(preparation[1]),
                                   "language": int(preparation[2]), "productIdentifier": preparation[3], "productVersion": preparation[4],
                                   "anonymous": identification[1][0] == "9999999999", "wireBase64": base64.b64encode(wire).decode("ascii")})


initialization_vector("anonymous-first-contact", [["280", "PUBLIC-BANK"], ["9999999999"], ["0"], ["0"]], ["0", "0", "0", "PUBLIC-PRODUCT", "0.1"])
initialization_vector("anonymous-cached-versions", [["280", "PUBLIC-BANK"], ["9999999999"], ["0"], ["0"]], ["12", "34", "1", "PUBLIC-PRODUCT", "0.2"])
initialization_vector("identified-system-required", [["280", "PUBLIC-BANK"], ["PUBLIC-CUSTOMER"], ["PUBLIC-SYSTEM"], ["1"]], ["1", "2", "2", "PUBLIC-PRODUCT", "1.0"])
initialization_vector("identified-system-not-required", [["280", "PUBLIC-BANK"], ["PUBLIC-CUSTOMER"], ["0"], ["0"]], ["0", "0", "3", "PUBLIC-PRODUCT", "1.0"])
initialization_vector("unassigned-system-observation", [["280", "PUBLIC-BANK"], ["PUBLIC-CUSTOMER"], ["0"], ["1"]], ["0", "0", "0", "PUBLIC-PRODUCT", "1.0"])
initialization_vector("card-system-observation", [["280", "PUBLIC-BANK"], ["PUBLIC-CUSTOMER"], ["PUBLIC-CARD"], ["0"]], ["0", "0", "0", "PUBLIC-PRODUCT", "1.0"])
initialization_vector("escaped-latin1-and-internal-spaces", [["280", "Bänk+:'?@"], ["Cü+:'?@ Z"], ["S+:'?@"], ["1"]], ["3", "4", "1", "Prüf+:'?@ Produkt", "+:'?@"])
initialization_vector("maximum-field-lengths", [["000", "I" * 30], ["C" * 30], ["S" * 30], ["1"]], ["999", "999", "3", "?" * 25, "?" * 5])
target.with_name("initialization-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": initialization_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(initialization_vectors)} public synthetic initialization vectors.")

initialization_context_vectors = []


def initialization_context(name, issues=(), anonymous=False, bank=True, user=True, account=True,
                           reference_dialogue="SYNTHETIC", bank_reference=3, bank_institution="PUBLIC-BANK",
                           returned_user="PUBLIC-USER", customer="PUBLIC-CUSTOMER", account_institution="PUBLIC-BANK",
                           bank_version=1, user_version=2, zero_upd=False, update=False, unknown=False,
                           receipt_only=False, error=False):
    request = initialization_vectors[0 if anonymous else 2]["wireBase64"]
    if zero_upd:
        request = base64.b64encode(base64.b64decode(request).replace(b"HKVVB:3:3+1+2+", b"HKVVB:3:3+1+0+")).decode("ascii")
    body = [segment("HIRMG", 2, 2, [group([b"9050" if error else b"0010", b"", b"PUBLIC-REPLY"])])]
    if not receipt_only and not error:
        body.append(segment("HIRMS", len(body) + 2, 2, [group([b"0020", b"", b"PUBLIC-ID-REPLY"])], reference=2))
        fields = [group([b"0020", b"", b"PUBLIC-PREP-REPLY"])]
        if update:
            fields.append(group([b"3050", b"", b"PUBLIC-UPDATE"]))
        body.append(segment("HIRMS", len(body) + 2, 2, fields, reference=3))
    if bank:
        body.append(segment("HIBPA", len(body) + 2, 3, [plain(str(bank_version).encode()), group([b"280", bank_institution.encode()]),
                    plain(b"PUBLIC-BANK-LABEL"), plain(b"1"), group([b"1", b"2", b"3"]), plain(b"300")], reference=bank_reference))
    if user:
        body.append(segment("HIUPA", len(body) + 2, 4, [plain(returned_user.encode()), plain(str(user_version).encode()), plain(b"0")], reference=3))
    if account:
        body.append(segment("HIUPD", len(body) + 2, 6, [group([b"PUBLIC-001", b"00", b"280", account_institution.encode()]),
                    plain(b"PUBLIC-IBAN"), plain(customer.encode()), plain(b"1"), plain(b"EUR"), plain(b"PUBLIC-HOLDER")], reference=3))
    if unknown:
        body.append(segment("HISPAS", len(body) + 2, 1, [plain(b"1"), plain(b"1"), plain(b"0"), group([b"J", b"J", b"N"])], reference=3))
    header_fields = [plain(b"000000000000"), plain(b"300"), plain(b"SYNTHETIC"), plain(b"1"), group([reference_dialogue.encode(), b"1"])]
    trailer = segment("HNHBS", len(body) + 2, 1, [plain(b"1")])
    size = len(segment("HNHBK", 1, 3, header_fields)[0]) + sum(len(p[0]) for p in body) + len(trailer[0])
    header_fields[0] = plain(f"{size:012d}".encode())
    response = segment("HNHBK", 1, 3, header_fields)[0] + b"".join(p[0] for p in body) + trailer[0]
    initialization_context_vectors.append({"name": name, "requestBase64": request,
        "responseBase64": base64.b64encode(response).decode("ascii"), "expectedUserId": None if anonymous else "PUBLIC-USER",
        "issues": list(issues), "outcome": "NeedsReview" if issues else "ExecutionReported"})


initialization_context("identified-distinct-user-and-customer")
initialization_context("anonymous-bank-only", anonymous=True, user=False, account=False)
initialization_context("dialogue-scoped-upd-zero", user_version=0, zero_upd=True)
initialization_context("omitted-cached-parameters", ["MissingBankParameters", "MissingUserParameters"], bank=False, user=False, account=False)
initialization_context("foreign-message-reference", ["MessageMismatch"], reference_dialogue="OTHER")
initialization_context("parameter-references-identification", ["ReferenceMismatch"], bank_reference=2)
initialization_context("foreign-bank-identity", ["InstitutionMismatch"], bank_institution="OTHER-BANK")
initialization_context("foreign-user-identity", ["UserMismatch"], returned_user="OTHER-USER")
initialization_context("foreign-account-customer", ["CustomerMismatch"], customer="OTHER-CUSTOMER")
initialization_context("foreign-account-institution", ["AccountInstitutionMismatch"], account_institution="OTHER-BANK")
initialization_context("scoped-parameter-update", bank_version=2, user_version=3, update=True)
initialization_context("uninterpreted-advertisement", ["UninterpretedParameters"], unknown=True)
initialization_context("receipt-without-execution", ["StatusNeedsReview"], receipt_only=True)
initialization_context("error-with-parameter-data", ["ResponseNeedsReview", "StatusNeedsReview"], error=True)
target.with_name("initialization-context-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": initialization_context_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(initialization_context_vectors)} public synthetic initialization-context vectors.")

initialization_attempt_vectors = []


def init_step(action, expected, state, at=0, index=0, evidence=False):
    return {"action": action, "expected": expected, "state": state, "atMilliseconds": at, "index": index, "evidence": evidence}


def init_attempt(name, source, steps, responses=None, missing_user=False):
    initialization_attempt_vectors.append({"name": name, "requestBase64": source["requestBase64"],
        "expectedUserId": None if missing_user else source["expectedUserId"],
        "responses": responses if responses is not None else [source["responseBase64"]], "steps": steps})


init_start = init_step("start", "RequestRecorded", "AwaitingResponse")
init_done = init_step("response", "ExecutionReported", "ExecutionReported", evidence=True)
init_attempt("identified-handed-out-once", initialization_context_vectors[0], [init_start, init_done,
             init_step("response", "Terminal", "ExecutionReported"), init_step("start", "Terminal", "ExecutionReported")])
init_attempt("anonymous-bank-only", initialization_context_vectors[1], [init_start, init_done])
init_attempt("foreign-reference-keeps-pending", initialization_context_vectors[0], [init_start,
             init_step("response", "ContextMismatch", "AwaitingResponse"),
             init_step("response", "ExecutionReported", "ExecutionReported", index=1, evidence=True)],
             [initialization_context_vectors[4]["responseBase64"], initialization_context_vectors[0]["responseBase64"]])
init_attempt("missing-user-context-at-start", initialization_context_vectors[0], [
             init_step("start", "RejectedForReview", "NeedsReview"), init_step("response", "Terminal", "NeedsReview")], missing_user=True)
init_attempt("missing-parameters-returned-for-review", initialization_context_vectors[3], [init_start,
             init_step("response", "RejectedForReview", "NeedsReview", evidence=True), init_step("response", "Terminal", "NeedsReview")])
init_attempt("cancel-before-response", initialization_context_vectors[0], [init_start,
             init_step("cancel", "Cancelled", "Cancelled"), init_step("response", "Terminal", "Cancelled")])
init_attempt("absolute-timeout", initialization_context_vectors[0], [init_start,
             init_step("snapshot", "Snapshot", "AwaitingResponse", at=9999), init_step("response", "Terminal", "TimedOut", at=10000)])
init_attempt("stop-before-start", initialization_context_vectors[0], [init_step("stop", "Stopped", "Stopped"),
             init_step("start", "Terminal", "Stopped"), init_step("response", "Terminal", "Stopped")])
target.with_name("initialization-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": initialization_attempt_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(initialization_attempt_vectors)} public synthetic initialization-attempt traces.")

synchronization_vectors = []


def synchronization_vector(name, mode, reports, expected, message_code=b"0010"):
    context = initialization_vectors[4 if mode == 0 else 2]
    request = base64.b64decode(context["wireBase64"])
    request = request.replace(b"HNHBS:4:1+1'", segment("HKSYN", 4, 3, [plain(str(mode).encode())])[0] + b"HNHBS:5:1+1'")
    request = request[:10] + f"{len(request):012d}".encode() + request[22:]
    body = [segment("HIRMG", 2, 2, [group([message_code, b"", b"PUBLIC-REPLY"])]),
            segment("HIRMS", 3, 2, [group([b"0020", b"", b"PUBLIC-SYNC-REPLY"])], reference=4)]
    for version, fields in reports:
        body.append(segment("HISYN", len(body) + 2, version, [plain(value.encode("latin-1")) for value in fields], reference=4))
    response = b"".join(p[0] for p in frame(body, len(body) + 2, response=True))
    synchronization_vectors.append({"name": name, "mode": mode, "input": {k: context[k] for k in
        ("country", "institution", "customerId", "systemId", "systemStatus", "bankParameterVersion", "userParameterVersion", "language", "productIdentifier", "productVersion")},
        "requestBase64": base64.b64encode(request).decode(), "responseBase64": base64.b64encode(response).decode(),
        "reports": expected, "uninterpreted": sum(version != 4 for version, _ in reports)})


def sync_observation(shape, system=None, message=None, signing=None, digital=None):
    return {"shape": shape, "systemId": system, "messageNumber": message, "signingReference": signing, "digitalReference": digital}


synchronization_vector("assigned-system-escaped", 0, [(4, ["PUBLIC+:'?@ü"])], [sync_observation("SystemId", system="PUBLIC+:'?@ü")])
synchronization_vector("last-message-maximum", 1, [(4, ["", "9999"])], [sync_observation("LastMessageNumber", message=9999)])
synchronization_vector("signing-reference-exact-maximum", 2, [(4, ["", "", "9999999999999999"])], [sync_observation("SignatureReferences", signing="9999999999999999")])
synchronization_vector("separate-signature-references", 2, [(4, ["", "", "0", "9999999999999998"])], [sync_observation("SignatureReferences", signing="0", digital="9999999999999998")])
synchronization_vector("empty-report", 0, [(4, [])], [sync_observation("Empty")])
synchronization_vector("conflicting-system-and-message", 0, [(4, ["PUBLIC-SYSTEM", "1", "", ""])], [sync_observation("Conflicting", system="PUBLIC-SYSTEM", message=1)])
synchronization_vector("digital-reference-without-signing", 2, [(4, ["", "", "", "1"])], [sync_observation("Conflicting", digital="1")])
synchronization_vector("duplicate-reports-preserved", 0, [(4, ["PUBLIC-A"]), (4, ["PUBLIC-B"])], [sync_observation("SystemId", system="PUBLIC-A"), sync_observation("SystemId", system="PUBLIC-B")])
synchronization_vector("unsupported-response-opaque", 0, [(5, ["PUBLIC-UNKNOWN"])], [])
synchronization_vector("error-without-report", 1, [], [], message_code=b"9050")
target.with_name("synchronization-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": synchronization_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(synchronization_vectors)} public synthetic synchronization vectors.")

synchronization_context_vectors = []


def synchronization_context(name, source_index, profile, issues=(), prior=None, last=None, card=False, response_index=None, replacements=()):
    source = synchronization_vectors[source_index]
    request = base64.b64decode(source["requestBase64"])
    if card:
        request = request.replace(b"PUBLIC-CUSTOMER+PUBLIC-SYSTEM+1'", b"PUBLIC-CUSTOMER+PUBLIC-CARD+0'")
    request = request[:10] + f"{len(request):012d}".encode() + request[22:]
    response = base64.b64decode(synchronization_vectors[source_index if response_index is None else response_index]["responseBase64"])
    for before, after in replacements:
        response = response.replace(before, after)
    response = response[:10] + f"{len(response):012d}".encode() + response[22:]
    synchronization_context_vectors.append({"name": name, "requestBase64": base64.b64encode(request).decode(),
        "responseBase64": base64.b64encode(response).decode(), "profile": profile, "previousDialogueId": prior,
        "lastSubmittedMessageNumber": last, "issues": list(issues),
        "nextStep": "StopForReview" if issues else "CloseAndReinitializeRequired"})


synchronization_context("pin-system-assignment", 0, "PinTan2")
synchronization_context("rah-software-system-assignment", 0, "Rah10")
synchronization_context("message-recovery-explicit-prior-scope", 1, "PinTan2", prior="PUBLIC-PREVIOUS", last=9999)
synchronization_context("message-recovery-missing-prior-scope", 1, "PinTan2", ["RecoveryContextMissing"])
synchronization_context("rah-software-single-reference", 2, "Rah10", replacements=[(b"9999999999999999", b"42")])
synchronization_context("rah-card-separate-references", 3, "Rah7", card=True)
synchronization_context("pin-signature-recovery-prohibited", 2, "PinTan2", ["ModeNotPermitted"], replacements=[(b"9999999999999999", b"42")])
synchronization_context("card-system-assignment-prohibited", 0, "Rah7", ["ModeNotPermitted", "RequestSystemMismatch"])
synchronization_context("rah-nine-layout-unresolved", 3, "Rah9", ["ProfileNeedsReview"], card=True)
synchronization_context("wrong-mode-shape", 1, "PinTan2", ["ModeShapeMismatch"], prior="PUBLIC-PREVIOUS", last=9999, response_index=0)
synchronization_context("empty-report-needs-review", 4, "PinTan2", ["ModeShapeMismatch"])
synchronization_context("unknown-response-needs-review", 8, "PinTan2", ["MissingReport", "UninterpretedReports"])
synchronization_context("duplicate-responses-needs-review", 7, "PinTan2", ["DuplicateReport"])
synchronization_context("foreign-synchronization-reference", 0, "PinTan2", ["ReferenceMismatch"], replacements=[(b"HISYN:4:4:4", b"HISYN:4:4:3")])
synchronization_context("reported-message-exceeds-submitted", 1, "PinTan2", ["RecoveryContextMismatch"], prior="PUBLIC-PREVIOUS", last=9)
target.with_name("synchronization-context-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": synchronization_context_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(synchronization_context_vectors)} public synthetic synchronization-context vectors.")

dialogue_end_vectors = []


def end_frame(dialogue, number, body, reference_number=None, reference_dialogue=None):
    fields = [plain(b"000000000000"), plain(b"300"), plain(dialogue.encode("latin-1")), plain(str(number).encode())]
    if reference_number is not None:
        fields.append(group([(reference_dialogue if reference_dialogue is not None else dialogue).encode("latin-1"), str(reference_number).encode()]))
    trailer = segment("HNHBS", len(body) + 2, 1, [plain(str(number).encode())])
    size = len(segment("HNHBK", 1, 3, fields)[0]) + sum(len(p[0]) for p in body) + len(trailer[0])
    fields[0] = plain(f"{size:012d}".encode())
    return segment("HNHBK", 1, 3, fields)[0] + b"".join(p[0] for p in body) + trailer[0]


def dialogue_end_vector(name, message_codes, scoped_codes=(), dialogue="SYNTHETIC", client=2, bank=2,
                        reference_dialogue=None, extra=False, issues=(), outcome="ClosureReported"):
    request = end_frame(dialogue, client, [segment("HKEND", 2, 1, [plain(dialogue.encode("latin-1"))])])
    body = [segment("HIRMG", 2, 2, [group([code.encode(), b"", b"PUBLIC-REPLY"]) for code in message_codes])]
    if scoped_codes:
        body.append(segment("HIRMS", 3, 2, [group([code.encode(), b"", b"PUBLIC-CLOSE-REPLY"]) for code in scoped_codes], reference=2))
    if extra:
        body.append(segment("ZDATA", len(body) + 2, 1, [plain(b"PUBLIC-DATA")], reference=2))
    response = end_frame(dialogue, bank, body, client, reference_dialogue)
    dialogue_end_vectors.append({"name": name, "dialogueId": dialogue, "clientMessageNumber": client, "expectedBankMessageNumber": bank,
        "requestBase64": base64.b64encode(request).decode(), "responseBase64": base64.b64encode(response).decode(), "issues": list(issues), "outcome": outcome,
        "closeReported": "0100" in (*message_codes, *scoped_codes), "abortReported": "9800" in (*message_codes, *scoped_codes)})


dialogue_end_vector("message-close", ["0100"])
dialogue_end_vector("scoped-close", ["0010"], ["0100"])
dialogue_end_vector("escaped-dialogue-independent-counters", ["0100"], dialogue="D+:'?@ü", client=9, bank=4)
dialogue_end_vector("dialogue-and-counter-limits", ["0100"], dialogue="?" * 30, client=9999, bank=9999)
dialogue_end_vector("explicit-abort", ["9800"], outcome="AbortReported")
dialogue_end_vector("execution-without-close", ["0010"], ["0020"], issues=["MissingTermination"], outcome="NeedsReview")
dialogue_end_vector("close-abort-conflict", ["0100", "9800"], issues=["ConflictingTermination", "ResponseNeedsReview"], outcome="NeedsReview")
dialogue_end_vector("data-on-close", ["0100"], extra=True, issues=["UnexpectedData"], outcome="NeedsReview")
dialogue_end_vector("foreign-close-reference", ["0100"], reference_dialogue="OTHER", issues=["MessageMismatch"], outcome="NeedsReview")
dialogue_end_vector("duplicate-close-across-levels", ["0100"], ["0100"], issues=["StatusNeedsReview"], outcome="NeedsReview")
target.with_name("dialogue-end-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": dialogue_end_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(dialogue_end_vectors)} public synthetic dialogue-end vectors.")

synchronization_attempt_vectors = []


def sync_step(action, expected, state, at=0, index=0, sync=False, close=False, requests=1, accepted=0):
    return {"action": action, "expected": expected, "state": state, "atMilliseconds": at, "index": index,
            "synchronizationEvidence": sync, "closingEvidence": close, "requests": requests, "accepted": accepted}


def sync_attempt(name, steps, source_index=0, close_index=0, sync_responses=None, close_responses=None):
    source = synchronization_context_vectors[source_index]
    closing = dialogue_end_vectors[close_index]
    synchronization_attempt_vectors.append({"name": name, "requestBase64": source["requestBase64"],
        "profile": source["profile"], "previousDialogueId": source["previousDialogueId"],
        "lastSubmittedMessageNumber": source["lastSubmittedMessageNumber"], "closeRequestBase64": closing["requestBase64"],
        "synchronizationResponses": sync_responses or [source["responseBase64"]],
        "closingResponses": close_responses or [closing["responseBase64"]], "steps": steps})


sync_start = sync_step("start", "RequestRecorded", "AwaitingSynchronization")
sync_reply = sync_step("synchronization", "CloseRequired", "AwaitingCloseRequest", sync=True, accepted=1)
sync_close = sync_step("recordClose", "CloseRequestRecorded", "AwaitingCloseResponse", requests=2, accepted=1)
sync_done = sync_step("close", "ReinitializationRequired", "ReinitializationRequired", close=True, requests=2, accepted=2)
sync_attempt("system-sync-close-once", [sync_start, sync_reply, sync_close, sync_done,
    sync_step("close", "Terminal", "ReinitializationRequired", requests=2, accepted=2)])
sync_attempt("message-recovery-close", [sync_start, sync_reply, sync_close, sync_done], source_index=2)
sync_attempt("signature-recovery-close", [sync_start, sync_reply, sync_close, sync_done], source_index=5)
sync_attempt("foreign-sync-keeps-pending", [sync_start,
    sync_step("synchronization", "ContextMismatch", "AwaitingSynchronization"),
    sync_step("synchronization", "CloseRequired", "AwaitingCloseRequest", index=1, sync=True, accepted=1), sync_close, sync_done],
    sync_responses=[synchronization_context_vectors[13]["responseBase64"], synchronization_context_vectors[0]["responseBase64"]])
sync_attempt("foreign-close-keeps-pending", [sync_start, sync_reply, sync_close,
    sync_step("close", "ContextMismatch", "AwaitingCloseResponse", requests=2, accepted=1),
    sync_step("close", "ReinitializationRequired", "ReinitializationRequired", index=1, close=True, requests=2, accepted=2)],
    close_responses=[dialogue_end_vectors[8]["responseBase64"], dialogue_end_vectors[0]["responseBase64"]])
sync_attempt("bound-sync-review", [sync_start,
    sync_step("synchronization", "RejectedForReview", "NeedsReview", sync=True),
    sync_step("recordClose", "Terminal", "NeedsReview")], source_index=10)
sync_attempt("bound-close-review", [sync_start, sync_reply, sync_close,
    sync_step("close", "RejectedForReview", "NeedsReview", close=True, requests=2, accepted=1)], close_index=5)
sync_attempt("explicit-abort-prevents-another-close", [sync_start, sync_reply, sync_close,
    sync_step("close", "AbortReported", "Aborted", close=True, requests=2, accepted=2),
    sync_step("recordClose", "Terminal", "Aborted", requests=2, accepted=2)], close_index=4)
sync_attempt("cancel-between-stages", [sync_start, sync_reply,
    sync_step("cancel", "Cancelled", "Cancelled", accepted=1),
    sync_step("recordClose", "Terminal", "Cancelled", accepted=1)])
sync_attempt("deadline-includes-close-wait", [sync_start, sync_reply,
    sync_step("recordClose", "CloseRequestRecorded", "AwaitingCloseResponse", at=9999, requests=2, accepted=1),
    sync_step("close", "Terminal", "TimedOut", at=10000, requests=2, accepted=1)])
sync_attempt("missing-recovery-before-recording", [
    sync_step("start", "RejectedForReview", "NeedsReview", requests=0)], source_index=3)
sync_attempt("stop-before-start", [sync_step("stop", "Stopped", "Stopped", requests=0),
    sync_step("start", "Terminal", "Stopped", requests=0)])
target.with_name("synchronization-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": synchronization_attempt_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(synchronization_attempt_vectors)} public synthetic synchronization-attempt traces.")

signature_header_vectors = []


def signature_header_vector(name, profile=1, function="999", control="PUBLIC-REF", role=1, party=1, system="PUBLIC-SYSTEM",
                            reference="1", date="20260908", time="123456", hash_code="999", signature="10", operation="16",
                            country="280", institution="PUBLIC-BANK", user="PUBLIC-USER", key_number=0, key_version=0,
                            timestamp_empty=False, optional_empty=False, number=2):
    stamp = [b"1"] if timestamp_empty else [b"1", date.encode(), time.encode()]
    fields = [group([b"PIN", str(profile).encode()]), plain(function.encode()), plain(control.encode("latin-1")),
              plain(b"1"), plain(str(role).encode()), group([str(party).encode(), b"", system.encode("latin-1")]),
              plain(reference.encode()), group(stamp), group([b"1", hash_code.encode(), b"1"] + ([b""] if optional_empty else [])),
              group([b"6", signature.encode(), operation.encode()]),
              group([country.encode(), institution.encode("latin-1"), user.encode("latin-1"), b"S", str(key_number).encode(), str(key_version).encode()])]
    if optional_empty:
        fields.append(plain(b""))
    wire = segment("HNSHK", number, 4, fields)[0]
    signature_header_vectors.append({"name": name, "wireBase64": base64.b64encode(wire).decode(), "segmentNumber": number,
        "profileVersion": profile, "securityFunction": function, "controlReference": control, "securitySupplierRole": role,
        "securityParty": party, "systemId": system, "securityReferenceNumber": reference,
        "securityDate": None if timestamp_empty or not date else date, "securityTime": None if timestamp_empty or not time else time,
        "hashAlgorithmCode": hash_code, "signatureAlgorithmCode": signature, "operationModeCode": operation,
        "countryCode": country, "institutionId": institution, "userId": user, "keyNumber": key_number, "keyVersion": key_version})


signature_header_vector("single-step-header")
signature_header_vector("two-step-lower-bound", profile=2, function="900")
signature_header_vector("two-step-upper-bound", profile=2, function="997", role=3, party=2, number=3)
signature_header_vector("escaped-identifiers", control="R+:'?@ü", system="S+:'?@ü", institution="B+:'?@ü", user="U+:'?@ü", role=4)
signature_header_vector("omitted-timestamp", timestamp_empty=True)
signature_header_vector("date-without-time", date="20240229", time="", optional_empty=True)
signature_header_vector("maximum-fields", control="R" * 14, system="S" * 30, reference="9999999999999999",
                        country="999", institution="B" * 30, user="U" * 30, key_number=999, key_version=999, date="99991231", time="235959", number=999)
signature_header_vector("zero-system-needs-request-context", system="0", reference="0", institution="", signature="001", operation="000")
signature_header_vector("dictionary-hash-observation", profile=2, function="920", hash_code="6", signature="999", operation="999")
signature_header_vector("text-reference-and-internal-spaces", control="00", system="PUBLIC SYSTEM", user="PUBLIC USER", date="00010101", time="000000", optional_empty=True)
target.with_name("pin-tan-signature-header-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": signature_header_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(signature_header_vectors)} public synthetic PIN/TAN signature-header vectors.")

signature_context_vectors = []


def signature_context_vector(name, issues=(), profile=2, function=900, sync=False, missing=False,
                             header_replacements=(), permitted=(900, 999), one_step=True, duplicate_ad=False,
                             duplicate_procedure=False, future=False, reference_dialogue="SYNTHETIC", user="PUBLIC-USER", abort=False):
    procedure = [b"900", b"2", b"PUBLIC_METHOD", b"App", b"1.0", b"Public method", b"6", b"1", b"Approval", b"2048",
                 b"N", b"1", b"N", b"0", b"0", b"N", b"J", b"00", b"0", b"N", b"0"] + [b""] * 5
    prep = [group([b"0020", b"", b"PUBLIC-PREP-REPLY"])]
    if permitted is not None:
        prep.append(group([b"3920", b"", b"PUBLIC-PERMISSIONS"] + [str(p).encode() for p in permitted]))
    body = [segment("HIRMG", 2, 2, [group([b"9800" if abort else b"0010", b"", b"PUBLIC-REPLY"])]),
            segment("HIRMS", 3, 2, [group([b"0020", b"", b"PUBLIC-ID-REPLY"])], reference=2),
            segment("HIRMS", 4, 2, prep, reference=3),
            segment("HIBPA", 5, 3, [plain(b"1"), group([b"280", b"PUBLIC-BANK"]), plain(b"PUBLIC-BANK-LABEL"), plain(b"1"), group([b"1", b"2", b"3"]), plain(b"300")], reference=3),
            segment("HIUPA", 6, 4, [plain(user.encode()), plain(b"2"), plain(b"0")], reference=3)]
    options = [b"J" if one_step else b"N", b"N", b"0"] + procedure * (2 if duplicate_procedure else 1)
    for _ in range(2 if duplicate_ad else 1):
        body.append(segment("HITANS", len(body) + 2, 7, [plain(b"1"), plain(b"1"), plain(b"0"), group(options)], reference=3))
    if future:
        body.append(segment("HITANS", len(body) + 2, 99, [plain(b"PUBLIC-OPAQUE")], reference=3))
    response = end_frame("SYNTHETIC", 1, body, 1, reference_dialogue)
    header = base64.b64decode(signature_header_vectors[0 if profile == 1 else 1]["wireBase64"])
    if sync:
        header = header.replace(b"PUBLIC-SYSTEM", b"0")
    for before, after in header_replacements:
        header = header.replace(before, after)
    signature_context_vectors.append({"name": name, "issues": list(issues), "requestKind": "synchronization" if sync else "initialization",
        "requestBase64": synchronization_vectors[0]["requestBase64"] if sync else initialization_vectors[2]["wireBase64"],
        "originRequestBase64": initialization_vectors[2]["wireBase64"], "procedureResponseBase64": base64.b64encode(response).decode(),
        "headerBase64": base64.b64encode(header).decode(), "expectedUserId": "PUBLIC-USER", "controlReference": "PUBLIC-REF",
        "profileVersion": profile, "securityFunction": function, "tanSegmentVersion": 7, "missingProcedureContext": missing})


signature_context_vector("two-step-initialization")
signature_context_vector("one-step-explicitly-reported", profile=1, function=999)
signature_context_vector("system-id-synchronization-zero", sync=True)
signature_context_vector("missing-procedure-context", ["MissingProcedureContext"], missing=True)
signature_context_vector("customer-is-not-user", ["IdentityMismatch"], header_replacements=[(b"PUBLIC-USER", b"PUBLIC-CUSTOMER")])
signature_context_vector("foreign-system", ["SystemMismatch"], header_replacements=[(b"PUBLIC-SYSTEM", b"OTHER-SYSTEM")])
signature_context_vector("foreign-control-reference", ["ControlMismatch"], header_replacements=[(b"PUBLIC-REF", b"OTHER-REF")])
signature_context_vector("different-header-procedure", ["SelectionMismatch"], header_replacements=[(b"+900+", b"+920+")])
signature_context_vector("missing-3920", ["MissingPermissionReport", "ProcedureNotListed"], permitted=None)
signature_context_vector("not-listed-for-user", ["ProcedureNotListed"], permitted=(999,))
signature_context_vector("one-step-not-reported", ["OneStepNotReportedAllowed"], profile=1, function=999, one_step=False)
signature_context_vector("duplicate-advertisement", ["AmbiguousAdvertisement"], duplicate_ad=True)
signature_context_vector("duplicate-procedure", ["AmbiguousProcedure"], duplicate_procedure=True)
signature_context_vector("future-version-preserved", ["UninterpretedParameters"], future=True)
signature_context_vector("foreign-origin-reference", ["ProcedureScopeMismatch"], reference_dialogue="OTHER")
signature_context_vector("foreign-origin-user", ["ProcedureScopeMismatch"], user="OTHER-USER")
signature_context_vector("aborted-origin-needs-review", ["ProcedureScopeMismatch", "ProcedureResponseNeedsReview"], abort=True)
signature_context_vector("duplicate-permission", ["AmbiguousPermissions"], permitted=(900, 900))
target.with_name("pin-tan-signature-context-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": signature_context_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(signature_context_vectors)} public synthetic PIN/TAN signature-context vectors.")

signature_encoding_vectors = []
for source in signature_header_vectors:
    # Rebuild canonical omissions from typed values, independently of the .NET writer.
    stamp = [b"1"]
    if source["securityDate"] is not None:
        stamp.append(source["securityDate"].encode())
        if source["securityTime"] is not None:
            stamp.append(source["securityTime"].encode())
    fields = [group([b"PIN", str(source["profileVersion"]).encode()]), plain(source["securityFunction"].encode()),
              plain(source["controlReference"].encode("latin-1")), plain(b"1"), plain(str(source["securitySupplierRole"]).encode()),
              group([str(source["securityParty"]).encode(), b"", source["systemId"].encode("latin-1")]), plain(source["securityReferenceNumber"].encode()),
              group(stamp), group([b"1", source["hashAlgorithmCode"].encode(), b"1"]),
              group([b"6", source["signatureAlgorithmCode"].encode(), source["operationModeCode"].encode()]),
              group([source["countryCode"].encode(), source["institutionId"].encode("latin-1"), source["userId"].encode("latin-1"),
                     b"S", str(source["keyNumber"]).encode(), str(source["keyVersion"]).encode()])]
    wire = segment("HNSHK", source["segmentNumber"], 4, fields)[0]
    signature_encoding_vectors.append({"name": source["name"], "segmentNumber": source["segmentNumber"],
        "input": {k: v for k, v in source.items() if k not in ("name", "segmentNumber", "wireBase64")},
        "wireBase64": base64.b64encode(wire).decode()})
target.with_name("pin-tan-signature-encoding-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": signature_encoding_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(signature_encoding_vectors)} public synthetic PIN/TAN signature-encoding vectors.")

credential_buffer_vectors = []


def credential_step(action, result, state, copies=0, written=0, at=0, capacity=99):
    return {"action": action, "result": result, "state": state, "copies": copies, "written": written, "atMilliseconds": at, "capacity": capacity}


def credential_trace(name, kind, steps, value=b"PUBLIC-PIN"):
    credential_buffer_vectors.append({"name": name, "kind": kind, "sourceBase64": base64.b64encode(value).decode(), "lifetimeMilliseconds": 10000, "steps": steps})


credential_trace("pin-reusable-until-disposed", "Pin", [
    credential_step("copy", "Copied", "Available", 1, 10), credential_step("copy", "Copied", "Available", 2, 10),
    credential_step("dispose", "Disposed", "Disposed", 2), credential_step("copy", "Unavailable", "Disposed", 2)])
credential_trace("tan-consumed-once", "Tan", [
    credential_step("copy", "Copied", "Consumed", 1, 10), credential_step("copy", "Unavailable", "Consumed", 1)], b"PUBLIC-TAN")
credential_trace("short-destination-does-not-consume", "Tan", [
    credential_step("copy", "DestinationTooSmall", "Available", capacity=9), credential_step("copy", "Copied", "Consumed", 1, 10)], b"PUBLIC-TAN")
credential_trace("cancel-pin", "Pin", [credential_step("cancel", "Cancelled", "Cancelled"), credential_step("copy", "Unavailable", "Cancelled")])
credential_trace("cancel-tan", "Tan", [credential_step("cancel", "Cancelled", "Cancelled"), credential_step("copy", "Unavailable", "Cancelled")], b"PUBLIC-TAN")
credential_trace("deadline-equality", "Tan", [credential_step("copy", "Unavailable", "Expired", at=10000)], b"PUBLIC-TAN")
credential_trace("pin-use-does-not-renew-deadline", "Pin", [
    credential_step("copy", "Copied", "Available", 1, 10, at=9999), credential_step("copy", "Unavailable", "Expired", 1, at=10000)])
credential_trace("clock-regression", "Pin", [credential_step("snapshot", "Snapshot", "ClockInvalid", at=-1), credential_step("copy", "Unavailable", "ClockInvalid")])
credential_trace("dispose-before-use", "Tan", [credential_step("dispose", "Disposed", "Disposed"), credential_step("copy", "Unavailable", "Disposed")], b"PUBLIC-TAN")
credential_trace("opaque-bytes-at-local-limit", "Tan", [credential_step("copy", "Copied", "Consumed", 1, 99)], bytes(range(99)))
target.with_name("credential-buffer-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": credential_buffer_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(credential_buffer_vectors)} public synthetic credential-buffer traces.")

signature_trailer_vectors = []


def signature_trailer_vector(name, source_index, pin=b"PUBLIC-PIN", tan=None, variant_one=False, control="PUBLIC-REF", result="Written"):
    context = dict(signature_context_vectors[source_index])
    if variant_one:
        response = base64.b64decode(context["procedureResponseBase64"])
        response = response.replace(b"900:2:PUBLIC_METHOD", b"900:1:PUBLIC_METHOD").replace(b"2048:N:1:N", b"2048:N:4:N")
        context["procedureResponseBase64"] = base64.b64encode(response).decode()
    context["headerBase64"] = base64.b64encode(base64.b64decode(context["headerBase64"]).replace(b"PUBLIC-REF", text(control.encode("latin-1")))).decode()
    context["controlReference"] = control
    number = 6 if context["requestKind"] == "synchronization" else 5
    fields = [plain(control.encode("latin-1")), plain(b""), group([pin] + ([tan] if tan is not None else []))]
    wire = segment("HNSHA", number, 2, fields)[0]
    signature_trailer_vectors.append({"name": name, "context": context, "segmentNumber": number,
        "pinBase64": base64.b64encode(pin).decode(), "tanBase64": None if tan is None else base64.b64encode(tan).decode(),
        "result": result, "pinCopies": 1 if result in ("Written", "InvalidCredentialText") else 0,
        "tanConsumed": tan is not None and result in ("Written", "InvalidCredentialText") and all(b >= 32 and not 127 <= b <= 160 for b in pin),
        "wireBase64": base64.b64encode(wire).decode() if result == "Written" else None})


signature_trailer_vector("one-step-pin-only", 1)
signature_trailer_vector("one-step-pin-and-tan", 1, tan=b"PUBLIC-TAN")
signature_trailer_vector("two-step-variant-two-pin-only", 0)
signature_trailer_vector("two-step-variant-one-tan", 0, tan=b"PUBLIC-TAN", variant_one=True)
signature_trailer_vector("variant-two-rejects-trailer-tan", 0, tan=b"PUBLIC-TAN", result="TanNotPermitted")
signature_trailer_vector("system-sync-pin-only", 2)
signature_trailer_vector("escaped-credential-and-control", 1, pin="P+:'?@ü".encode("latin-1"), tan="T+:'?@ü".encode("latin-1"), control="R+:'?@ü")
signature_trailer_vector("maximum-escaped-values", 1, pin=b"?" * 99, tan=b"@" * 99, control="?" * 14)
signature_trailer_vector("spaces-preserved", 1, pin=b" PIN ", tan=b" TAN ")
signature_trailer_vector("control-octet-rejected", 1, pin=b"PUBLIC\x00PIN", result="InvalidCredentialText")
signature_trailer_vector("invalid-tan-consumed-without-output", 1, tan=b"PUBLIC\x7fTAN", result="InvalidCredentialText")
signature_trailer_vector("unresolved-context-rejected", 3, tan=b"PUBLIC-TAN", result="ContextNeedsReview")
target.with_name("pin-tan-signature-trailer-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": signature_trailer_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(signature_trailer_vectors)} public synthetic PIN/TAN signature-trailer vectors.")


def public_text_segments(wire):
    # These restricted public request/header fixtures contain escaped text, never binary payloads.
    result = []
    start = position = 0
    while position < len(wire):
        if wire[position] == ord('?'):
            position += 2
            continue
        if wire[position] == ord("'"):
            result.append(wire[start:position + 1])
            start = position + 1
        position += 1
    assert start == len(wire)
    return result


request_assembly_vectors = []
assembly_sources = json.loads(json.dumps(signature_trailer_vectors))
optional = json.loads(json.dumps(signature_trailer_vectors[0]))
optional["name"] = "empty-certificate-preserved"
optional["context"]["headerBase64"] = base64.b64encode(base64.b64decode(optional["context"]["headerBase64"])[:-1] + b"+'").decode()
assembly_sources.append(optional)
escaped = json.loads(json.dumps(signature_trailer_vectors[0]))
escaped["name"] = "escaped-preparation-product"
request_body = public_text_segments(base64.b64decode(escaped["context"]["requestBase64"]))[1:-1]
request_body[1] = segment("HKVVB", 3, 3, [plain(b"1"), plain(b"2"), plain(b"1"), plain("Product+:'?@ü".encode("latin-1")), plain(b"1?@")])[0]
escaped["context"]["requestBase64"] = base64.b64encode(end_frame("0", 1, [(b, None) for b in request_body])).decode()
assembly_sources.append(escaped)
for source in assembly_sources:
    wire = None
    if source["result"] == "Written":
        unsigned_body = public_text_segments(base64.b64decode(source["context"]["requestBase64"]))[1:-1]
        body = [(base64.b64decode(source["context"]["headerBase64"]), None)]
        for number, original in enumerate(unsigned_body, start=3):
            code, _, rest = original.split(b":", 2)
            body.append((code + b":" + str(number).encode() + b":" + rest, None))
        body.append((base64.b64decode(source["wireBase64"]), None))
        wire = end_frame("0", 1, body)
    request_assembly_vectors.append({**source, "wireBase64": None if wire is None else base64.b64encode(wire).decode()})
target.with_name("pin-tan-request-assembly-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": request_assembly_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(request_assembly_vectors)} public synthetic PIN/TAN request-assembly vectors.")

request_envelope_sources = json.loads(json.dumps(request_assembly_vectors))
for name, timestamp in [("omitted-envelope-timestamp", b"1"), ("date-only-envelope-timestamp", b"1:20240229")]:
    source = json.loads(json.dumps(request_assembly_vectors[0]))
    source["name"] = name
    for owner, key in [(source["context"], "headerBase64"), (source, "wireBase64")]:
        data = base64.b64decode(owner[key]).replace(b"1:20260908:123456", timestamp)
        if key == "wireBase64":
            data = data[:10] + f"{len(data):012d}".encode() + data[22:]
        owner[key] = base64.b64encode(data).decode()
    source["envelopeTimestamp"] = timestamp.decode()
    request_envelope_sources.append(source)
escaped_identity = json.loads(json.dumps(request_assembly_vectors[0]))
escaped_identity["name"] = "escaped-envelope-identities"
for owner, keys in [(escaped_identity, ["wireBase64"]), (escaped_identity["context"], ["requestBase64", "originRequestBase64", "procedureResponseBase64", "headerBase64"])]:
    for key in keys:
        data = base64.b64decode(owner[key])
        for original, value in [(b"PUBLIC-BANK", "B+:'?@ü"), (b"PUBLIC-SYSTEM", "S+:'?@ü"), (b"PUBLIC-USER", "U+:'?@ü")]:
            data = data.replace(original, text(value.encode("latin-1")))
        if key != "headerBase64":
            data = data[:10] + f"{len(data):012d}".encode() + data[22:]
        owner[key] = base64.b64encode(data).decode()
escaped_identity["context"]["expectedUserId"] = "U+:'?@ü"
request_envelope_sources.append(escaped_identity)
request_envelope_vectors = []
for source in request_envelope_sources:
    wire = payload = None
    stamp = source.get("envelopeTimestamp", "1:20260908:123456").encode().split(b":")
    escaped = source["name"] == "escaped-envelope-identities"
    system = "0" if source["context"]["requestKind"] == "synchronization" else "S+:'?@ü" if escaped else "PUBLIC-SYSTEM"
    bank, user = ("B+:'?@ü", "U+:'?@ü") if escaped else ("PUBLIC-BANK", "PUBLIC-USER")
    if source["result"] == "Written":
        inner_segments = public_text_segments(base64.b64decode(source["wireBase64"]))
        payload = b"".join(inner_segments[1:-1])
        security = segment("HNVSK", 998, 3, [group([b"PIN", str(source["context"]["profileVersion"]).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", system.encode("latin-1")]), group(stamp),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([b"280", bank.encode("latin-1"), user.encode("latin-1"), b"V", b"0", b"0"]), plain(b"0")])[0]
        data = segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0]
        fields = [plain(b"000000000000"), plain(b"300"), plain(b"0"), plain(b"1")]
        size = len(segment("HNHBK", 1, 3, fields)[0]) + len(security) + len(data) + len(inner_segments[-1])
        fields[0] = plain(f"{size:012d}".encode())
        wire = segment("HNHBK", 1, 3, fields)[0] + security + data + inner_segments[-1]
    request_envelope_vectors.append({**source, "plainWireBase64": source["wireBase64"], "wireBase64": None if wire is None else base64.b64encode(wire).decode(),
        "payloadBase64": None if payload is None else base64.b64encode(payload).decode()})
target.with_name("pin-tan-request-envelope-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": request_envelope_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(request_envelope_vectors)} public synthetic PIN/TAN request-envelope vectors.")

request_binding_vectors = []


def request_binding_vector(name, issues=(), sync=False, one_step=False, wrapped=True, profile=None, bank_reference=4,
                           sync_reference=5, report_reference=None, dialogue="SYNTHETIC", reference_dialogue=None,
                           message=1, reference_message=1, omit_outer_reference=False, extra=None, abort=False, foreign_user=False):
    context = signature_context_vectors[2 if sync else 1 if one_step else 0]
    body = [segment("HIRMG", 2, 2, [group([b"9800" if abort else b"0010", b"", b"PUBLIC-REPLY"])])]
    if not abort:
        if report_reference is not None:
            body.append(segment("HIRMS", len(body) + 2, 2, [group([b"9050", b"", b"PUBLIC-REVIEW"])], reference=report_reference))
        else:
            for ref in ([3, 4, 5] if sync else [3, 4]):
                body.append(segment("HIRMS", len(body) + 2, 2, [group([b"0020", b"", b"PUBLIC-REPLY"])], reference=ref))
        if sync:
            body.append(segment("HISYN", len(body) + 2, 4, [plain(b"PUBLIC-ASSIGNED")], reference=sync_reference))
        else:
            body.append(segment("HIBPA", len(body) + 2, 3, [plain(b"1"), group([b"280", b"PUBLIC-BANK"]), plain(b"PUBLIC-BANK"), plain(b"1"), group([b"1", b"2", b"3"]), plain(b"300")], reference=bank_reference))
            body.append(segment("HIUPA", len(body) + 2, 4, [plain(b"FOREIGN-USER" if foreign_user else b"PUBLIC-USER"), plain(b"2"), plain(b"0")], reference=4))
        if extra is not None:
            code, version, reference = extra
            body.append(segment(code, len(body) + 2, version, [plain(b"PUBLIC-OPAQUE")], reference=reference))
    header_fields = [plain(b"000000000000"), plain(b"300"), plain(dialogue.encode()), plain(str(message).encode())]
    if not omit_outer_reference:
        header_fields.append(group([(reference_dialogue if reference_dialogue is not None else dialogue).encode(), str(reference_message).encode()]))
    trailer = segment("HNHBS", len(body) + 2, 1, [plain(str(message).encode())])[0]
    if wrapped:
        security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile if profile is not None else context["profileVersion"]).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", b"PUBLIC-SYSTEM"]), group([b"1"]),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([b"280", b"PUBLIC-BANK", b"PUBLIC-USER", b"V", b"0", b"0"]), plain(b"0")])[0]
        payload = b"".join(p[0] for p in body)
        middle = security + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0]
    else:
        middle = b"".join(p[0] for p in body)
    size = len(segment("HNHBK", 1, 3, header_fields)[0]) + len(middle) + len(trailer)
    header_fields[0] = plain(f"{size:012d}".encode())
    wire = segment("HNHBK", 1, 3, header_fields)[0] + middle + trailer
    roles = {1: "MessageHeader", 998: "EnvelopeHeader", 999: "EnvelopeData", 2: "SignatureHeader", 3: "Identification", 4: "Preparation",
             5: "Synchronization" if sync else "SignatureTrailer", 6: "SignatureTrailer" if sync else "MessageTrailer"}
    if sync:
        roles[7] = "MessageTrailer"
    links = [{"code": p[1]["code"], "reference": p[1]["reference"], "role": roles.get(p[1]["reference"])} for p in body[1:]]
    request_binding_vectors.append({"name": name, "context": context, "responseBase64": base64.b64encode(wire).decode(),
        "wrapped": wrapped, "issues": list(issues), "dialogue": dialogue, "references": links, "hasErrors": abort or report_reference is not None})


request_binding_vector("two-step-renumbered-initialization")
request_binding_vector("one-step-renumbered-initialization", one_step=True)
request_binding_vector("system-synchronization-reference-five", sync=True)
request_binding_vector("old-preparation-reference-is-identification", ["SegmentRoleMismatch"], bank_reference=3)
request_binding_vector("sync-report-references-preparation", ["SegmentRoleMismatch"], sync=True, sync_reference=4)
request_binding_vector("missing-data-reference", ["MissingSegmentReference", "SegmentRoleMismatch"], bank_reference=None)
request_binding_vector("unknown-data-reference", ["UnknownSegmentReference", "SegmentRoleMismatch"], bank_reference=997)
request_binding_vector("signature-error-reference-two", report_reference=2)
request_binding_vector("envelope-error-reference-998", report_reference=998)
request_binding_vector("unknown-status-reference", ["UnknownSegmentReference"], report_reference=997)
request_binding_vector("plain-response-not-envelope", ["MissingEnvelope"], wrapped=False)
request_binding_vector("foreign-envelope-profile", ["ProfileMismatch"], profile=1)
request_binding_vector("foreign-message-dialogue", ["MessageMismatch"], reference_dialogue="OTHER")
request_binding_vector("foreign-reference-counter", ["MessageMismatch"], reference_message=2)
request_binding_vector("foreign-bank-counter", ["MessageMismatch"], message=2)
request_binding_vector("missing-outer-reference", ["MessageMismatch"], omit_outer_reference=True)
request_binding_vector("unassigned-dialogue", ["InvalidAssignedDialogue"], dialogue="0")
request_binding_vector("padded-dialogue", ["InvalidAssignedDialogue"], dialogue=" PADDED")
request_binding_vector("unknown-scoped-data", ["UninterpretedData"], extra=("HIXYZ", 1, 4))
request_binding_vector("future-parameter-version", ["UninterpretedData"], extra=("HITANS", 99, 4))
request_binding_vector("unexpected-synchronization-data", ["SegmentRoleMismatch"], extra=("HISYN", 4, 4))
request_binding_vector("bound-abort-is-not-execution", abort=True)
request_binding_vector("bound-user-still-needs-semantic-checks", foreign_user=True)
target.with_name("pin-tan-request-binding-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": request_binding_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(request_binding_vectors)} public synthetic PIN/TAN request-binding vectors.")

pin_tan_initialization_vectors = []


def pin_tan_initialization_vector(name, issues=(), parameter_issues=(), source_index=0, one_step=False,
                                 edits=(), shift=True, profile=None, envelope_system="PUBLIC-SYSTEM", envelope_bank="PUBLIC-BANK",
                                 envelope_user="PUBLIC-USER", envelope_country="280", wrapped=True, bank_version=1, user_version=2):
    context = signature_context_vectors[1 if one_step else 0]
    original = public_text_segments(base64.b64decode(initialization_context_vectors[source_index]["responseBase64"]))[1:-1]
    body = []
    for wire in original:
        header, fields = wire.split(b"+", 1)
        parts = header.split(b":")
        if len(parts) == 4 and shift:
            parts[3] = str(int(parts[3]) + 1).encode()
        body.append(b":".join(parts) + b"+" + fields)
    payload = b"".join(body)
    for before, after in edits:
        payload = payload.replace(before, after)
    body = public_text_segments(payload)
    security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile if profile is not None else context["profileVersion"]).encode()]), plain(b"998"), plain(b"1"),
        group([b"1", b"", envelope_system.encode("latin-1")]), group([b"1"]),
        [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
        group([envelope_country.encode(), envelope_bank.encode("latin-1"), envelope_user.encode("latin-1"), b"V", b"0", b"0"]), plain(b"0")])
    middle = [security, segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])] if wrapped else [(wire, None) for wire in body]
    wire = b"".join(part[0] for part in frame(middle, len(body) + 2, response=True))
    pin_tan_initialization_vectors.append({"name": name, "context": context, "responseBase64": base64.b64encode(wire).decode(), "wrapped": wrapped,
        "issues": list(issues), "parameterIssues": list(parameter_issues), "bankVersionChanged": None if bank_version is None else bank_version != 1,
        "userVersionChanged": None if user_version is None else user_version != 2})


pin_tan_initialization_vector("two-step-scoped-execution")
pin_tan_initialization_vector("one-step-scoped-execution", one_step=True)
pin_tan_initialization_vector("message-level-execution", edits=[(b"HIRMG:2:2+0010", b"HIRMG:2:2+0020")])
pin_tan_initialization_vector("dialogue-only-upd-zero", source_index=2, user_version=0)
pin_tan_initialization_vector("missing-bank-and-user", ["ParametersNeedReview"], ["MissingBankParameters", "MissingUserParameters"], source_index=3, bank_version=None, user_version=None)
pin_tan_initialization_vector("old-parameter-reference", ["BindingNeedsReview"], source_index=5)
pin_tan_initialization_vector("foreign-bank", ["ParametersNeedReview"], ["InstitutionMismatch"], source_index=6)
pin_tan_initialization_vector("foreign-user", ["ParametersNeedReview"], ["UserMismatch"], source_index=7)
pin_tan_initialization_vector("foreign-account-customer", ["ParametersNeedReview"], ["CustomerMismatch"], source_index=8)
pin_tan_initialization_vector("foreign-account-bank", ["ParametersNeedReview"], ["AccountInstitutionMismatch"], source_index=9)
pin_tan_initialization_vector("parameter-update-at-preparation-four", source_index=10, bank_version=2, user_version=3)
pin_tan_initialization_vector("uninterpreted-parameters", ["ParametersNeedReview"], ["UninterpretedParameters"], source_index=11)
pin_tan_initialization_vector("receipt-only", ["StatusNeedsReview"], source_index=12)
pin_tan_initialization_vector("bank-error-with-parameters", ["ResponseNeedsReview", "StatusNeedsReview"], source_index=13)
pin_tan_initialization_vector("signature-success-is-not-identification", ["StatusNeedsReview"], edits=[(b"HIRMS:3:2:3", b"HIRMS:3:2:2")])
pin_tan_initialization_vector("signature-error", ["ResponseNeedsReview", "StatusNeedsReview"], edits=[(b"HIRMS:3:2:3+0020", b"HIRMS:3:2:2+9050")])
pin_tan_initialization_vector("duplicate-execution-status", ["ResponseNeedsReview", "StatusNeedsReview"], edits=[(b"0020::PUBLIC-ID-REPLY", b"0020::PUBLIC-ID-REPLY+0020::PUBLIC-DUPLICATE")])
pin_tan_initialization_vector("pending-status", ["StatusNeedsReview"], edits=[(b"0020::PUBLIC-ID-REPLY", b"0030::PUBLIC-ID-REPLY")])
pin_tan_initialization_vector("permission-report-needs-integration", ["StatusNeedsReview"], edits=[(b"0020::PUBLIC-PREP-REPLY", b"0020::PUBLIC-PREP-REPLY+3920::PUBLIC-PERMISSIONS:900")])
pin_tan_initialization_vector("status-element-reference", ["StatusNeedsReview"], edits=[(b"0020::PUBLIC-ID-REPLY", b"0020:1:PUBLIC-ID-REPLY")])
pin_tan_initialization_vector("status-parameter", ["StatusNeedsReview"], edits=[(b"0020::PUBLIC-ID-REPLY", b"0020::PUBLIC-ID-REPLY:PUBLIC-PARAM")])
pin_tan_initialization_vector("update-without-parameters", ["StatusNeedsReview", "ParametersNeedReview"], ["MissingBankParameters", "MissingUserParameters"], source_index=3,
    edits=[(b"0020::PUBLIC-PREP-REPLY", b"0020::PUBLIC-PREP-REPLY+3050::PUBLIC-UPDATE")], bank_version=None, user_version=None)
pin_tan_initialization_vector("protocol-missing", ["ParametersNeedReview"], ["ProtocolNotAdvertised"], edits=[(b"+1+1:2:3+300'", b"+1+1:2:3+220'")])
pin_tan_initialization_vector("language-missing", ["ParametersNeedReview"], ["LanguageNotAdvertised"], edits=[(b"+1+1:2:3+300'", b"+1+1:3+300'")])
pin_tan_initialization_vector("foreign-envelope-system", ["EnvelopeIdentityMismatch"], envelope_system="OTHER-SYSTEM")
pin_tan_initialization_vector("foreign-envelope-bank", ["EnvelopeIdentityMismatch"], envelope_bank="OTHER-BANK")
pin_tan_initialization_vector("foreign-envelope-user", ["EnvelopeIdentityMismatch"], envelope_user="OTHER-USER")
pin_tan_initialization_vector("foreign-envelope-country", ["EnvelopeIdentityMismatch"], envelope_country="999")
pin_tan_initialization_vector("foreign-profile", ["BindingNeedsReview"], profile=1)
pin_tan_initialization_vector("plain-response", ["BindingNeedsReview"], wrapped=False)
target.with_name("pin-tan-initialization-evidence-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": pin_tan_initialization_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(pin_tan_initialization_vectors)} public synthetic assembled PIN/TAN initialization-evidence vectors.")

pin_tan_synchronization_vectors = []


def pin_tan_synchronization_vector(name, issues=(), details=(), mode=0, one_step=False, reports=None, version=4, reference=5,
                                  reply_reference=5, reply=b"0020", message=b"0010", duplicate_status=False, reply_parameter=False,
                                  previous=None, last=None, envelope_system=None, envelope_user="PUBLIC-USER", envelope_bank="PUBLIC-BANK",
                                  envelope_country="280", profile=None, wrapped=True, extra=False):
    context = json.loads(json.dumps(signature_context_vectors[2 if mode == 0 else 0]))
    context["requestKind"] = "synchronization"
    context["requestBase64"] = synchronization_vectors[0 if mode == 0 else 1]["requestBase64"]
    if one_step:
        context["profileVersion"], context["securityFunction"] = 1, 999
        context["headerBase64"] = base64.b64encode(base64.b64decode(context["headerBase64"]).replace(b"PIN:2+900+", b"PIN:1+999+")).decode()
    if reports is None:
        reports = [["PUBLIC-ASSIGNED"]] if mode == 0 else [["", "10"]]
    body = [segment("HIRMG", 2, 2, [group([message, b"", b"PUBLIC-REPLY"])])]
    fields = [group([reply, b"", b"PUBLIC-SYNC-REPLY"] + ([b"PUBLIC-PARAM"] if reply_parameter else []))]
    if duplicate_status:
        fields.append(group([reply, b"", b"PUBLIC-DUPLICATE"]))
    body.append(segment("HIRMS", 3, 2, fields, reference=reply_reference))
    for values in reports:
        body.append(segment("HISYN", len(body) + 2, version, [plain(value.encode("latin-1")) for value in values], reference=reference))
    if extra:
        body.append(segment("HIBPA", len(body) + 2, 3, [plain(b"1"), group([b"280", b"PUBLIC-BANK"]), plain(b"PUBLIC-BANK"), plain(b"1"), group([b"1", b"2", b"3"]), plain(b"300")], reference=4))
    envelope_system = envelope_system if envelope_system is not None else "PUBLIC-ASSIGNED" if mode == 0 else "PUBLIC-SYSTEM"
    security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile if profile is not None else context["profileVersion"]).encode()]), plain(b"998"), plain(b"1"),
        group([b"1", b"", envelope_system.encode("latin-1")]), group([b"1"]),
        [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
        group([envelope_country.encode(), envelope_bank.encode("latin-1"), envelope_user.encode("latin-1"), b"V", b"0", b"0"]), plain(b"0")])
    middle = [security, segment("HNVSD", 999, 1, [[scalar(b"".join(p[0] for p in body), binary=True)]])] if wrapped else body
    wire = b"".join(part[0] for part in frame(middle, len(body) + 2, response=True))
    pin_tan_synchronization_vectors.append({"name": name, "context": context, "responseBase64": base64.b64encode(wire).decode(), "wrapped": wrapped,
        "issues": list(issues), "synchronizationIssues": list(details), "previousDialogueId": previous, "lastSubmittedMessageNumber": last,
        "reportCount": len(reports) if version == 4 else 0})


pin_tan_synchronization_vector("two-step-assigned-system")
pin_tan_synchronization_vector("one-step-assigned-system", one_step=True)
pin_tan_synchronization_vector("escaped-assigned-system", reports=[["PUBLIC+:'?@ü"]], envelope_system="PUBLIC+:'?@ü")
pin_tan_synchronization_vector("bounded-message-recovery", mode=1, previous="PUBLIC-PREVIOUS", last=10)
pin_tan_synchronization_vector("maximum-message-recovery", mode=1, previous="PUBLIC-PREVIOUS", last=9999, reports=[["", "9999"]])
pin_tan_synchronization_vector("missing-recovery-context", ["SynchronizationNeedsReview"], ["RecoveryContextMissing"], mode=1)
pin_tan_synchronization_vector("reported-counter-exceeds-submission", ["SynchronizationNeedsReview"], ["RecoveryContextMismatch"], mode=1, previous="PUBLIC-PREVIOUS", last=9)
pin_tan_synchronization_vector("reused-prior-dialogue", ["SynchronizationNeedsReview"], ["RecoveryContextMismatch"], mode=1, previous="SYNTHETIC", last=10)
pin_tan_synchronization_vector("recovery-context-on-assignment", ["SynchronizationNeedsReview"], ["RecoveryContextMismatch"], previous="PUBLIC-PREVIOUS", last=10)
pin_tan_synchronization_vector("missing-report", ["SynchronizationNeedsReview"], ["MissingReport"], reports=[])
pin_tan_synchronization_vector("duplicate-assignment-reports", ["SynchronizationNeedsReview"], ["DuplicateReport"], reports=[["PUBLIC-A"], ["PUBLIC-B"]])
pin_tan_synchronization_vector("future-report-version", ["BindingNeedsReview", "SynchronizationNeedsReview"], ["MissingReport", "UninterpretedReports"], version=5)
pin_tan_synchronization_vector("empty-report", ["SynchronizationNeedsReview"], ["ModeShapeMismatch"], reports=[[]])
pin_tan_synchronization_vector("conflicting-report-fields", ["SynchronizationNeedsReview"], ["ModeShapeMismatch"], reports=[["PUBLIC-ASSIGNED", "1"]])
pin_tan_synchronization_vector("cross-mode-message-report", ["SynchronizationNeedsReview"], ["ModeShapeMismatch"], reports=[["", "1"]])
pin_tan_synchronization_vector("reserved-zero-assignment", ["SynchronizationNeedsReview"], ["InvalidSystemId"], reports=[["0"]])
pin_tan_synchronization_vector("unknown-assignment", ["SynchronizationNeedsReview"], ["InvalidSystemId"], reports=[["unbekannt"]])
pin_tan_synchronization_vector("preparation-success-not-sync", ["StatusNeedsReview"], reply_reference=4)
pin_tan_synchronization_vector("signature-success-not-sync", ["StatusNeedsReview"], reply_reference=2)
pin_tan_synchronization_vector("message-level-execution", message=b"0020", reply_reference=3)
pin_tan_synchronization_vector("pending-sync", ["StatusNeedsReview"], reply=b"0030")
pin_tan_synchronization_vector("bank-abort", ["ResponseNeedsReview", "StatusNeedsReview"], message=b"9800")
pin_tan_synchronization_vector("duplicate-status", ["ResponseNeedsReview", "StatusNeedsReview"], duplicate_status=True)
pin_tan_synchronization_vector("status-parameter", ["StatusNeedsReview"], reply_parameter=True)
pin_tan_synchronization_vector("old-report-reference", ["BindingNeedsReview"], reference=4)
pin_tan_synchronization_vector("assignment-envelope-zero", ["EnvelopeIdentityMismatch"], envelope_system="0")
pin_tan_synchronization_vector("foreign-envelope-user", ["EnvelopeIdentityMismatch"], envelope_user="OTHER-USER")
pin_tan_synchronization_vector("foreign-envelope-bank", ["EnvelopeIdentityMismatch"], envelope_bank="OTHER-BANK")
pin_tan_synchronization_vector("foreign-envelope-country", ["EnvelopeIdentityMismatch"], envelope_country="999")
pin_tan_synchronization_vector("recovery-envelope-system-mismatch", ["EnvelopeIdentityMismatch"], mode=1, previous="PUBLIC-PREVIOUS", last=10, envelope_system="OTHER-SYSTEM")
pin_tan_synchronization_vector("foreign-profile", ["BindingNeedsReview"], profile=1)
pin_tan_synchronization_vector("plain-response", ["BindingNeedsReview"], wrapped=False)
pin_tan_synchronization_vector("returned-parameters-need-integration", ["SynchronizationNeedsReview"], ["UninterpretedReports"], extra=True)
target.with_name("pin-tan-synchronization-evidence-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": pin_tan_synchronization_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(pin_tan_synchronization_vectors)} public synthetic assembled PIN/TAN synchronization-evidence vectors.")

pin_tan_initialization_attempt_vectors = []


def owned_init_step(action, transition, state, at=0, candidates=1, handled=0, evidence=False, index=0):
    return {"action": action, "transition": transition, "state": state, "atMilliseconds": at,
            "candidates": candidates, "handled": handled, "evidence": evidence, "index": index}


def owned_init_trace(name, steps, source=0, responses=None, wrong_kind=False):
    context = signature_context_vectors[2] if wrong_kind else pin_tan_initialization_vectors[source]["context"]
    selected = [pin_tan_initialization_vectors[source]] if responses is None else responses
    pin_tan_initialization_attempt_vectors.append({"name": name, "context": context,
        "responses": [{"wireBase64": v["responseBase64"], "wrapped": v["wrapped"]} for v in selected], "steps": steps})


owned_start = owned_init_step("start", "RequestRecorded", "AwaitingResponse")
owned_done = owned_init_step("response", "ExecutionReported", "ExecutionReported", handled=1, evidence=True)
owned_init_trace("one-time-execution-handoff", [owned_start, owned_done,
    owned_init_step("response", "Terminal", "ExecutionReported", handled=1), owned_init_step("start", "Terminal", "ExecutionReported", handled=1)])
owned_init_trace("one-step-profile", [owned_start, owned_done], source=1)
owned_init_trace("foreign-segment-keeps-candidate", [owned_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse"),
    owned_init_step("response", "ExecutionReported", "ExecutionReported", handled=1, evidence=True, index=1)], responses=[pin_tan_initialization_vectors[5], pin_tan_initialization_vectors[0]])
owned_init_trace("foreign-message-keeps-candidate", [owned_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse"),
    owned_init_step("response", "ExecutionReported", "ExecutionReported", handled=1, evidence=True, index=1)], responses=[request_binding_vectors[12], pin_tan_initialization_vectors[0]])
owned_init_trace("scoped-user-review-consumed-once", [owned_start, owned_init_step("response", "RejectedForReview", "NeedsReview", handled=1, evidence=True),
    owned_init_step("response", "Terminal", "NeedsReview", handled=1)], source=7)
owned_init_trace("missing-envelope-ends-for-review", [owned_start, owned_init_step("response", "RejectedForReview", "NeedsReview", handled=1, evidence=True)], source=29)
owned_init_trace("explicit-cancellation", [owned_start, owned_init_step("cancel", "Cancelled", "Cancelled", at=50), owned_init_step("response", "Terminal", "Cancelled", at=50)])
owned_init_trace("stop-before-start", [owned_init_step("stop", "Stopped", "Stopped", candidates=0), owned_init_step("start", "Terminal", "Stopped", candidates=0)])
owned_init_trace("deadline-equality", [owned_start, owned_init_step("response", "Terminal", "TimedOut", at=10000)])
owned_init_trace("mismatch-does-not-renew-time", [owned_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse", at=9999),
    owned_init_step("response", "Terminal", "TimedOut", at=10000, index=1)], responses=[request_binding_vectors[12], pin_tan_initialization_vectors[0]])
owned_init_trace("clock-regression", [owned_start, owned_init_step("snapshot", "Snapshot", "ClockInvalid", at=-1)])
owned_init_trace("wrong-candidate-kind", [owned_init_step("start", "RejectedForReview", "NeedsReview", candidates=0)], wrong_kind=True)
owned_init_trace("dispose-pending-metadata", [owned_start, owned_init_step("dispose", "Stopped", "Stopped"), owned_init_step("response", "Terminal", "Stopped")])
target.with_name("pin-tan-initialization-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": pin_tan_initialization_attempt_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(pin_tan_initialization_attempt_vectors)} public synthetic assembled PIN/TAN initialization-attempt traces.")

pin_tan_synchronization_attempt_vectors = []


def owned_sync_trace(name, steps, source=0, responses=None, wrong_kind=False):
    selected = pin_tan_synchronization_vectors[source]
    context = signature_context_vectors[0] if wrong_kind else selected["context"]
    replies = [selected] if responses is None else responses
    pin_tan_synchronization_attempt_vectors.append({"name": name, "context": context,
        "previousDialogueId": selected["previousDialogueId"], "lastSubmittedMessageNumber": selected["lastSubmittedMessageNumber"],
        "responses": [{"wireBase64": v["responseBase64"], "wrapped": v["wrapped"]} for v in replies], "steps": steps})


sync_start = owned_init_step("start", "CandidateRecorded", "AwaitingResponse")
sync_done = owned_init_step("response", "CloseAndReinitializeRequired", "CloseAndReinitializeRequired", handled=1, evidence=True)
sync_review = owned_init_step("response", "RejectedForReview", "NeedsReview", handled=1, evidence=True)
owned_sync_trace("one-time-mandatory-closing-handoff", [sync_start, sync_done,
    owned_init_step("response", "Terminal", "CloseAndReinitializeRequired", handled=1),
    owned_init_step("start", "Terminal", "CloseAndReinitializeRequired", handled=1)])
owned_sync_trace("one-step-profile", [sync_start, sync_done], source=1)
owned_sync_trace("foreign-segment-keeps-candidate", [sync_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse"),
    owned_init_step("response", "CloseAndReinitializeRequired", "CloseAndReinitializeRequired", handled=1, evidence=True, index=1)],
    responses=[pin_tan_synchronization_vectors[24], pin_tan_synchronization_vectors[0]])
owned_sync_trace("foreign-message-keeps-candidate", [sync_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse"),
    owned_init_step("response", "CloseAndReinitializeRequired", "CloseAndReinitializeRequired", handled=1, evidence=True, index=1)],
    responses=[request_binding_vectors[12], pin_tan_synchronization_vectors[0]])
owned_sync_trace("scoped-review-consumed-once", [sync_start, sync_review, owned_init_step("response", "Terminal", "NeedsReview", handled=1)], source=26)
owned_sync_trace("missing-envelope-review", [sync_start, sync_review], source=31)
owned_sync_trace("explicit-cancellation", [sync_start, owned_init_step("cancel", "Cancelled", "Cancelled", at=50), owned_init_step("response", "Terminal", "Cancelled", at=50)])
owned_sync_trace("stop-before-start", [owned_init_step("stop", "Stopped", "Stopped", candidates=0), owned_init_step("start", "Terminal", "Stopped", candidates=0)])
owned_sync_trace("deadline-equality", [sync_start, owned_init_step("response", "Terminal", "TimedOut", at=10000)])
owned_sync_trace("mismatch-does-not-renew-time", [sync_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse", at=9999),
    owned_init_step("response", "Terminal", "TimedOut", at=10000, index=1)], responses=[request_binding_vectors[12], pin_tan_synchronization_vectors[0]])
owned_sync_trace("clock-regression", [sync_start, owned_init_step("snapshot", "Snapshot", "ClockInvalid", at=-1)])
owned_sync_trace("wrong-candidate-kind", [owned_init_step("start", "RejectedForReview", "NeedsReview", candidates=0)], wrong_kind=True)
owned_sync_trace("dispose-pending-recovery", [sync_start, owned_init_step("dispose", "Stopped", "Stopped"), owned_init_step("response", "Terminal", "Stopped")], source=3)
owned_sync_trace("bounded-message-recovery", [sync_start, sync_done], source=3)
owned_sync_trace("maximum-message-recovery", [sync_start, sync_done], source=4)
owned_sync_trace("missing-recovery-rejected-before-recording", [owned_init_step("start", "RejectedForReview", "NeedsReview", candidates=0)], source=5)
owned_sync_trace("recovery-on-assignment-rejected-before-recording", [owned_init_step("start", "RejectedForReview", "NeedsReview", candidates=0)], source=8)
owned_sync_trace("counter-exceeds-submission-review", [sync_start, sync_review], source=6)
owned_sync_trace("prior-dialogue-reuse-review", [sync_start, sync_review], source=7)
owned_sync_trace("bank-abort-withholds-positive-handoff", [sync_start, sync_review], source=21)
owned_sync_trace("duplicate-reports-review", [sync_start, sync_review], source=10)
target.with_name("pin-tan-synchronization-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": pin_tan_synchronization_attempt_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(pin_tan_synchronization_attempt_vectors)} public synthetic assembled PIN/TAN synchronization-attempt traces.")

pin_tan_closing_vectors = []


def pin_tan_closing_vector(name, source=0, issues=(), dialogue="SYNTHETIC", client=2, bank=2,
                           system=None, country="280", institution="PUBLIC-BANK", user="PUBLIC-USER",
                           profile=None, function=None, role=1, party=1, number=2,
                           control="PUBLIC-CLOSE", pin=b"PUBLIC-PIN", escaped_dialogue=False):
    sync = json.loads(json.dumps(pin_tan_synchronization_vectors[source]))
    if escaped_dialogue:
        reply = base64.b64decode(sync["responseBase64"]).replace(b"SYNTHETIC", text(dialogue.encode("latin-1")))
        reply = reply[:10] + f"{len(reply):012d}".encode() + reply[22:]
        sync["responseBase64"] = base64.b64encode(reply).decode()
    profile = sync["context"]["profileVersion"] if profile is None else profile
    function = sync["context"]["securityFunction"] if function is None else function
    system = system if system is not None else "PUBLIC-SYSTEM" if source in (3, 4) else "PUBLIC+:'?@ü" if source == 2 else "PUBLIC-ASSIGNED"
    header = segment("HNSHK", number, 4, [group([b"PIN", str(profile).encode()]), plain(str(function).encode()), plain(control.encode("latin-1")),
        plain(b"1"), plain(str(role).encode()), group([str(party).encode(), b"", system.encode("latin-1")]), plain(b"1"),
        group([b"1", b"20260909", b"123456"]), group([b"1", b"999", b"1"]), group([b"6", b"10", b"16"]),
        group([country.encode(), institution.encode("latin-1"), user.encode("latin-1"), b"S", b"0", b"0"])])[0]
    closing = end_frame(dialogue, client, [segment("HKEND", 2, 1, [plain(dialogue.encode("latin-1"))])])
    wire = payload = None
    if not issues:
        payload = header + segment("HKEND", 3, 1, [plain(dialogue.encode("latin-1"))])[0] + segment("HNSHA", 4, 2, [plain(control.encode("latin-1")), plain(b""), plain(pin)])[0]
        security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", system.encode("latin-1")]), group([b"1", b"20260909", b"123456"]),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([country.encode(), institution.encode("latin-1"), user.encode("latin-1"), b"V", b"0", b"0"]), plain(b"0")])
        middle = [security, segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])]
        ending = segment("HNHBS", 5, 1, [plain(str(client).encode())])[0]
        fields = [plain(b"000000000000"), plain(b"300"), plain(dialogue.encode("latin-1")), plain(str(client).encode())]
        size = len(segment("HNHBK", 1, 3, fields)[0]) + sum(len(p[0]) for p in middle) + len(ending)
        fields[0] = plain(f"{size:012d}".encode())
        wire = segment("HNHBK", 1, 3, fields)[0] + b"".join(p[0] for p in middle) + ending
    pin_tan_closing_vectors.append({"name": name, "synchronization": sync, "requestBase64": base64.b64encode(closing).decode(),
        "expectedBankMessageNumber": bank, "headerBase64": base64.b64encode(header).decode(), "issues": list(issues),
        "pinBase64": base64.b64encode(pin).decode(), "wireBase64": None if wire is None else base64.b64encode(wire).decode(),
        "payloadBase64": None if payload is None else base64.b64encode(payload).decode()})


pin_tan_closing_vector("two-step-system-assignment")
pin_tan_closing_vector("one-step-system-assignment", source=1)
pin_tan_closing_vector("escaped-system-dialogue-control-pin", source=2, dialogue="D+:'?@ü", control="C+:'?@ü", pin="P+:'?@ü".encode("latin-1"), escaped_dialogue=True)
pin_tan_closing_vector("message-recovery", source=3)
pin_tan_closing_vector("maximum-recovery-is-not-close-counter", source=4)
pin_tan_closing_vector("maximum-pin-escaping", pin=b"?" * 99)
pin_tan_closing_vector("foreign-dialogue", issues=["DialogueMismatch"], dialogue="PUBLIC-OTHER")
pin_tan_closing_vector("foreign-client-counter", issues=["CounterMismatch"], client=3)
pin_tan_closing_vector("foreign-bank-counter", issues=["CounterMismatch"], bank=3)
pin_tan_closing_vector("old-zero-system", issues=["SystemMismatch"], system="0")
pin_tan_closing_vector("foreign-user", issues=["IdentityMismatch"], user="PUBLIC-OTHER")
pin_tan_closing_vector("foreign-bank", issues=["IdentityMismatch"], institution="PUBLIC-OTHER")
pin_tan_closing_vector("foreign-country", issues=["IdentityMismatch"], country="999")
pin_tan_closing_vector("profile-switch", issues=["SelectionMismatch"], profile=1, function=999)
pin_tan_closing_vector("procedure-switch", issues=["SelectionMismatch"], function=901)
pin_tan_closing_vector("foreign-header-role", issues=["HeaderRoleNeedsReview"], role=3)
pin_tan_closing_vector("foreign-header-party", issues=["HeaderRoleNeedsReview"], party=2)
pin_tan_closing_vector("foreign-header-number", issues=["HeaderRoleNeedsReview"], number=3)
pin_tan_closing_vector("bank-abort-cannot-close", source=21, issues=["SynchronizationNeedsReview", "SystemMismatch"])
pin_tan_closing_vector("foreign-system-in-recovery", source=3, issues=["SystemMismatch"], system="PUBLIC-OTHER")
target.with_name("pin-tan-dialogue-end-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": pin_tan_closing_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(pin_tan_closing_vectors)} public synthetic PIN/TAN dialogue-end vectors.")

pin_tan_closing_response_vectors = []


def closing_response_vector(name, issues=(), binding_issues=(), source=0, message_codes=("0100",), scoped_codes=("0020",), reference=3,
                            dialogue=None, reference_dialogue=None, message=2, reference_message=2, omit_reference=False,
                            wrapped=True, profile=None, system=None, country="280", bank="PUBLIC-BANK", user="PUBLIC-USER",
                            element="", parameter=None, extra=None, extra_reply=None, outcome="NeedsReview"):
    context = pin_tan_closing_vectors[source]
    dialogue = dialogue if dialogue is not None else "D+:'?@ü" if source == 2 else "SYNTHETIC"
    profile = profile if profile is not None else 1 if source == 1 else 2
    system = system if system is not None else "PUBLIC-SYSTEM" if source in (3, 4) else "PUBLIC+:'?@ü" if source == 2 else "PUBLIC-ASSIGNED"
    def replies(codes, scoped=False):
        return [group([code.encode(), element.encode() if scoped else b"", b"PUBLIC-CLOSE-REPLY"] + ([] if parameter is None else [parameter.encode()])) for code in codes]
    body = [segment("HIRMG", 2, 2, replies(message_codes))]
    if scoped_codes:
        body.append(segment("HIRMS", len(body) + 2, 2, replies(scoped_codes, True), reference=reference))
    if extra_reply is not None:
        ref, code = extra_reply
        body.append(segment("HIRMS", len(body) + 2, 2, replies([code], True), reference=ref))
    if extra is not None:
        code, version, ref = extra
        body.append(segment(code, len(body) + 2, version, [plain(b"PUBLIC-OPAQUE")], reference=ref))
    fields = [plain(b"000000000000"), plain(b"300"), plain(dialogue.encode("latin-1")), plain(str(message).encode())]
    if not omit_reference:
        fields.append(group([(reference_dialogue if reference_dialogue is not None else dialogue).encode("latin-1"), str(reference_message).encode()]))
    ending = segment("HNHBS", len(body) + 2, 1, [plain(str(message).encode())])[0]
    payload = b"".join(p[0] for p in body)
    if wrapped:
        security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", system.encode("latin-1")]), group([b"1"]),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([country.encode(), bank.encode("latin-1"), user.encode("latin-1"), b"V", b"0", b"0"]), plain(b"0")])[0]
        middle = security + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0]
    else:
        middle = payload
    size = len(segment("HNHBK", 1, 3, fields)[0]) + len(middle) + len(ending)
    fields[0] = plain(f"{size:012d}".encode())
    wire = segment("HNHBK", 1, 3, fields)[0] + middle + ending
    roles = {1: "MessageHeader", 998: "EnvelopeHeader", 999: "EnvelopeData", 2: "SignatureHeader", 3: "DialogueEnd", 4: "SignatureTrailer", 5: "MessageTrailer"}
    codes = list(message_codes) + list(scoped_codes) + ([] if extra_reply is None else [extra_reply[1]])
    pin_tan_closing_response_vectors.append({"name": name, "context": context, "responseBase64": base64.b64encode(wire).decode(),
        "wrapped": wrapped, "issues": list(issues), "bindingIssues": list(binding_issues), "outcome": outcome,
        "closeReported": "0100" in codes, "abortReported": "9800" in codes,
        "references": [{"code": p[1]["code"], "reference": p[1]["reference"], "role": roles.get(p[1]["reference"])} for p in body[1:]]})


closing_response_vector("two-step-message-closure", outcome="ClosureReported")
closing_response_vector("one-step-message-closure", source=1, outcome="ClosureReported")
closing_response_vector("escaped-dialogue-and-system", source=2, outcome="ClosureReported")
closing_response_vector("message-recovery-closing", source=3, outcome="ClosureReported")
closing_response_vector("maximum-recovery-closing", source=4, outcome="ClosureReported")
closing_response_vector("scoped-hkend-closure", message_codes=["0010"], scoped_codes=["0100"], outcome="ClosureReported")
closing_response_vector("message-only-closure", scoped_codes=[], outcome="ClosureReported")
closing_response_vector("empty-optional-reply-parameter", parameter="", outcome="ClosureReported")
closing_response_vector("message-abort", message_codes=["9800"], scoped_codes=[], outcome="AbortReported")
closing_response_vector("execution-is-not-termination", ["MissingTermination"], message_codes=["0020"])
closing_response_vector("receipt-is-not-termination", ["MissingTermination"], message_codes=["0010"], scoped_codes=[])
closing_response_vector("pending-is-not-termination", ["StatusNeedsReview", "MissingTermination"], message_codes=["0030"], scoped_codes=[])
closing_response_vector("indeterminate-processing", ["StatusNeedsReview", "MissingTermination", "ResponseNeedsReview"], message_codes=["9000"], scoped_codes=[])
closing_response_vector("duplicate-close-in-message", ["StatusNeedsReview", "ResponseNeedsReview"], message_codes=["0100", "0100"])
closing_response_vector("duplicate-close-across-levels", ["StatusNeedsReview"], scoped_codes=["0100"])
closing_response_vector("close-and-abort", ["ConflictingTermination"], message_codes=["9800"], scoped_codes=["0100"])
closing_response_vector("scoped-abort-needs-review", ["StatusNeedsReview", "ResponseNeedsReview"], message_codes=["0010"], scoped_codes=["9800"])
closing_response_vector("unrelated-bank-error", ["StatusNeedsReview", "ResponseNeedsReview"], scoped_codes=["9050"])
closing_response_vector("scoped-element-reference", ["StatusNeedsReview"], element="1")
closing_response_vector("nonempty-reply-parameter", ["StatusNeedsReview"], parameter="PUBLIC-PARAM")
closing_response_vector("unknown-status", ["StatusNeedsReview"], scoped_codes=["0999"])
for ref in (1, 2, 4, 5, 998, 999):
    closing_response_vector(f"mapped-nonclosing-role-{ref}", ["StatusNeedsReview"], reference=ref)
closing_response_vector("signature-close-is-not-hkend-close", ["StatusNeedsReview"], message_codes=["0010"], scoped_codes=["0100"], reference=2)
closing_response_vector("unknown-reference", ["BindingNeedsReview", "StatusNeedsReview"], ["UnknownSegmentReference"], reference=997)
closing_response_vector("foreign-dialogue", ["BindingNeedsReview"], ["MessageMismatch"], dialogue="PUBLIC-OTHER")
closing_response_vector("foreign-reference-dialogue", ["BindingNeedsReview"], ["MessageMismatch"], reference_dialogue="PUBLIC-OTHER")
closing_response_vector("foreign-bank-counter", ["BindingNeedsReview"], ["MessageMismatch"], message=3)
closing_response_vector("old-client-counter", ["BindingNeedsReview"], ["MessageMismatch"], reference_message=1)
closing_response_vector("missing-outer-reference", ["BindingNeedsReview"], ["MessageMismatch"], omit_reference=True)
closing_response_vector("plain-response", ["BindingNeedsReview"], ["MissingEnvelope"], wrapped=False)
closing_response_vector("foreign-profile", ["BindingNeedsReview"], ["ProfileMismatch"], profile=1)
closing_response_vector("foreign-system", ["EnvelopeIdentityMismatch"], system="PUBLIC-OTHER")
closing_response_vector("foreign-country", ["EnvelopeIdentityMismatch"], country="999")
closing_response_vector("foreign-bank", ["EnvelopeIdentityMismatch"], bank="PUBLIC-OTHER")
closing_response_vector("foreign-user", ["EnvelopeIdentityMismatch"], user="PUBLIC-OTHER")
closing_response_vector("unexpected-hiend", ["BindingNeedsReview", "UnexpectedData"], ["UninterpretedData"], extra=("HIEND", 1, 3))
closing_response_vector("unexpected-synchronization-report", ["BindingNeedsReview", "UnexpectedData"], ["UninterpretedData"], extra=("HISYN", 4, 3))
closing_response_vector("data-without-reference", ["BindingNeedsReview", "UnexpectedData"], ["MissingSegmentReference", "UninterpretedData"], extra=("HIXYZ", 1, None))
closing_response_vector("data-with-unknown-reference", ["BindingNeedsReview", "UnexpectedData"], ["UnknownSegmentReference", "UninterpretedData"], extra=("HIXYZ", 1, 997))
closing_response_vector("duplicate-abort", ["StatusNeedsReview"], message_codes=["9800", "9800"], scoped_codes=[])
closing_response_vector("security-error-after-close", ["StatusNeedsReview", "ResponseNeedsReview"], extra_reply=(2, "9050"))
target.with_name("pin-tan-dialogue-end-response-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": pin_tan_closing_response_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(pin_tan_closing_response_vectors)} public synthetic PIN/TAN dialogue-end response vectors.")

pin_tan_closing_attempt_vectors = []
closing_replies = {v["name"]: v for v in pin_tan_closing_response_vectors}


def owned_closing_trace(name, steps, source="two-step-message-closure", responses=None):
    selected = closing_replies[source]
    replies = [selected] if responses is None else [closing_replies[n] for n in responses]
    pin_tan_closing_attempt_vectors.append({"name": name, "context": selected["context"],
        "responses": [{"wireBase64": v["responseBase64"], "wrapped": v["wrapped"]} for v in replies], "steps": steps})


closing_start = owned_init_step("start", "CandidateRecorded", "AwaitingResponse")
closing_done = owned_init_step("response", "ReinitializationRequired", "ReinitializationRequired", handled=1, evidence=True)
closing_review = owned_init_step("response", "RejectedForReview", "NeedsReview", handled=1, evidence=True)
owned_closing_trace("one-time-reinitialization-handoff", [closing_start, closing_done,
    owned_init_step("response", "Terminal", "ReinitializationRequired", handled=1), owned_init_step("start", "Terminal", "ReinitializationRequired", handled=1)])
owned_closing_trace("one-step-profile", [closing_start, closing_done], source="one-step-message-closure")
owned_closing_trace("foreign-reference-keeps-candidate", [closing_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse"),
    owned_init_step("response", "ReinitializationRequired", "ReinitializationRequired", handled=1, evidence=True, index=1)], responses=["unknown-reference", "two-step-message-closure"])
owned_closing_trace("foreign-dialogue-keeps-candidate", [closing_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse"),
    owned_init_step("response", "ReinitializationRequired", "ReinitializationRequired", handled=1, evidence=True, index=1)], responses=["foreign-dialogue", "two-step-message-closure"])
owned_closing_trace("scoped-review-consumed-once", [closing_start, closing_review, owned_init_step("response", "Terminal", "NeedsReview", handled=1)], source="foreign-user")
owned_closing_trace("missing-envelope-review", [closing_start, closing_review], source="plain-response")
owned_closing_trace("explicit-cancellation", [closing_start, owned_init_step("cancel", "Cancelled", "Cancelled", at=50), owned_init_step("response", "Terminal", "Cancelled", at=50)])
owned_closing_trace("stop-before-start", [owned_init_step("stop", "Stopped", "Stopped", candidates=0), owned_init_step("start", "Terminal", "Stopped", candidates=0)])
owned_closing_trace("deadline-equality", [closing_start, owned_init_step("response", "Terminal", "TimedOut", at=10000)])
owned_closing_trace("mismatch-does-not-renew-time", [closing_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse", at=9999),
    owned_init_step("response", "Terminal", "TimedOut", at=10000, index=1)], responses=["foreign-dialogue", "two-step-message-closure"])
owned_closing_trace("clock-regression", [closing_start, owned_init_step("snapshot", "Snapshot", "ClockInvalid", at=-1)])
owned_closing_trace("bank-abort-consumed-once", [closing_start, owned_init_step("response", "AbortReported", "Aborted", handled=1, evidence=True),
    owned_init_step("response", "Terminal", "Aborted", handled=1), owned_init_step("start", "Terminal", "Aborted", handled=1)], source="message-abort")
owned_closing_trace("dispose-pending-candidate", [closing_start, owned_init_step("dispose", "Stopped", "Stopped"), owned_init_step("response", "Terminal", "Stopped")])
owned_closing_trace("signature-close-is-review", [closing_start, closing_review], source="signature-close-is-not-hkend-close")
owned_closing_trace("recovery-maximum-still-reinitializes", [closing_start, closing_done], source="maximum-recovery-closing")
owned_closing_trace("conflicting-termination-review", [closing_start, closing_review], source="close-and-abort")
owned_closing_trace("unexpected-data-review", [closing_start, closing_review], source="unexpected-hiend")
owned_closing_trace("execution-does-not-close", [closing_start, closing_review], source="execution-is-not-termination")
owned_closing_trace("missing-data-reference-keeps-candidate", [closing_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse"),
    owned_init_step("response", "ReinitializationRequired", "ReinitializationRequired", handled=1, evidence=True, index=1)], responses=["data-without-reference", "two-step-message-closure"])
owned_closing_trace("escaped-closing-context", [closing_start, closing_done], source="escaped-dialogue-and-system")
target.with_name("pin-tan-dialogue-end-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": pin_tan_closing_attempt_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(pin_tan_closing_attempt_vectors)} public synthetic PIN/TAN dialogue-end attempt traces.")

initialization_procedure_vectors = []


def initialization_procedure_vector(name, issues=(), source=0, one_step=False, remove_ad=False, edits=(), extra=None, wrapped=True,
                                    profile=None, system="PUBLIC-SYSTEM", dual_versions=False, version_six=False):
    context = json.loads(json.dumps(signature_context_vectors[1 if one_step else 0]))
    original = public_text_segments(base64.b64decode(signature_context_vectors[source]["procedureResponseBase64"]))[1:-1]
    body = []
    for wire in original:
        parts, fields = wire.split(b"+", 1)
        header = parts.split(b":")
        if remove_ad and header[0] == b"HITANS":
            continue
        header[1] = str(len(body) + 2).encode()
        if len(header) == 4:
            header[3] = str(int(header[3]) + 1).encode()
        wire = b":".join(header) + b"+" + fields
        for before, after in edits:
            wire = wire.replace(before, after)
        body.append(wire)
    if version_six or dual_versions:
        index = next(i for i, b in enumerate(body) if b.startswith(b"HITANS:"))
        sixth = body[index].replace(b":7:4+", b":6:4+")[:-6] + b"'"
        if dual_versions:
            parts, fields = sixth.split(b"+", 1)
            header = parts.split(b":"); header[1] = str(len(body) + 2).encode()
            body.append(b":".join(header) + b"+" + fields)
        else:
            body[index] = sixth
            context["tanSegmentVersion"] = 6
            origin = public_text_segments(base64.b64decode(context["procedureResponseBase64"]))[1:-1]
            origin = [b.replace(b":7:3+", b":6:3+")[:-6] + b"'" if b.startswith(b"HITANS:") else b for b in origin]
            context["procedureResponseBase64"] = base64.b64encode(end_frame("SYNTHETIC", 1, [(b, None) for b in origin], 1)).decode()
    if extra is not None:
        code, version, reference = extra
        body.append(segment(code, len(body) + 2, version, [plain(b"PUBLIC-OPAQUE")], reference=reference)[0])
    payload = b"".join(body)
    if wrapped:
        security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile if profile is not None else context["profileVersion"]).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", system.encode()]), group([b"1"]),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([b"280", b"PUBLIC-BANK", b"PUBLIC-USER", b"V", b"0", b"0"]), plain(b"0")])[0]
        middle = security + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0]
    else:
        middle = payload
    fields = [plain(b"000000000000"), plain(b"300"), plain(b"SYNTHETIC"), plain(b"1"), group([b"OTHER" if source == 14 else b"SYNTHETIC", b"1"])]
    ending = segment("HNHBS", len(body) + 2, 1, [plain(b"1")])[0]
    size = len(segment("HNHBK", 1, 3, fields)[0]) + len(middle) + len(ending)
    fields[0] = plain(f"{size:012d}".encode())
    wire = segment("HNHBK", 1, 3, fields)[0] + middle + ending
    initialization_procedure_vectors.append({"name": name, "context": context, "responseBase64": base64.b64encode(wire).decode(), "wrapped": wrapped, "issues": list(issues)})


initialization_procedure_vector("two-step-returned-selection")
initialization_procedure_vector("one-step-returned-selection", one_step=True)
initialization_procedure_vector("version-six-returned-selection", version_six=True)
initialization_procedure_vector("dual-versions-select-exact-seven", dual_versions=True)
initialization_procedure_vector("missing-permission", ["MissingPermissionReport", "SelectionNotReported"], source=8)
initialization_procedure_vector("selected-function-not-permitted", ["SelectionNotReported"], source=9)
initialization_procedure_vector("one-step-not-allowed", ["OneStepNotReportedAllowed"], source=10, one_step=True)
initialization_procedure_vector("duplicate-advertisement", ["AmbiguousAdvertisement"], source=11)
initialization_procedure_vector("duplicate-procedure", ["AmbiguousProcedure"], source=12)
initialization_procedure_vector("future-advertisement", ["InitializationNeedsReview"], source=13)
initialization_procedure_vector("foreign-message-reference", ["InitializationNeedsReview"], source=14)
initialization_procedure_vector("foreign-returned-user", ["InitializationNeedsReview"], source=15)
initialization_procedure_vector("aborted-response", ["InitializationNeedsReview"], source=16)
initialization_procedure_vector("duplicate-permission-function", ["AmbiguousPermissions"], source=17)
initialization_procedure_vector("missing-advertisement", ["MissingAdvertisement"], remove_ad=True)
initialization_procedure_vector("foreign-permission-role", ["InitializationNeedsReview", "PermissionScopeMismatch"], edits=[(b"HIRMS:4:2:4+", b"HIRMS:4:2:2+")])
initialization_procedure_vector("old-hitans-reference", ["InitializationNeedsReview"], edits=[(b"HITANS:7:7:4+", b"HITANS:7:7:3+")])
initialization_procedure_vector("unknown-data-remains-review", ["InitializationNeedsReview"], extra=("HIXYZ", 1, 4))
initialization_procedure_vector("plain-response-remains-review", ["InitializationNeedsReview"], wrapped=False)
initialization_procedure_vector("foreign-envelope-profile", ["InitializationNeedsReview"], profile=1)
initialization_procedure_vector("foreign-envelope-system", ["InitializationNeedsReview"], system="OTHER")
initialization_procedure_vector("advertisement-zero-orders", ["AdvertisementRequirementsNeedReview"], edits=[(b"HITANS:7:7:4+1+1+", b"HITANS:7:7:4+0+1+")])
initialization_procedure_vector("advertisement-two-signatures", ["AdvertisementRequirementsNeedReview"], edits=[(b"HITANS:7:7:4+1+1+", b"HITANS:7:7:4+1+2+")])
initialization_procedure_vector("changed-procedure-not-selected", ["SelectionNotReported"], edits=[(b"J:N:0:900:2:", b"J:N:0:901:2:")])
initialization_procedure_vector("permission-does-not-prove-execution", ["InitializationNeedsReview"], edits=[(b"0020::PUBLIC-PREP-REPLY", b"0010::PUBLIC-PREP-REPLY")])
initialization_procedure_vector("unrelated-status-remains-review", ["InitializationNeedsReview"], edits=[(b"0020::PUBLIC-ID-REPLY", b"0030::PUBLIC-ID-REPLY")])
initialization_procedure_vector("duplicate-permission-reports", ["InitializationNeedsReview", "AmbiguousPermissions"], edits=[(b"3920::PUBLIC-PERMISSIONS:900:999", b"3920::PUBLIC-PERMISSIONS:900:999+3920::PUBLIC-SECOND:900")])
initialization_procedure_vector("nonpermission-parameter-remains-review", ["InitializationNeedsReview"], edits=[(b"0020::PUBLIC-ID-REPLY", b"0020::PUBLIC-ID-REPLY:PUBLIC-PARAM")])
target.with_name("pin-tan-initialization-procedures-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": initialization_procedure_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(initialization_procedure_vectors)} public synthetic assembled initialization procedure vectors.")

initialization_requirements_vectors = []


def initialization_requirements_vector(name, issues=(), source=0, minimum=5, maximum=12, tan_maximum=6,
                                       operations=(("HKSAL", "J"), ("HKSPA", "N")), copies=1, version=1, future=False,
                                       reference=4, orders=1, signatures=1, labels=("PUBLIC-USER-LABEL", "PUBLIC-CUSTOMER-LABEL"),
                                       operation_evidence="TanReportedRequired"):
    original = initialization_procedure_vectors[source]
    wire = base64.b64decode(original["responseBase64"])
    if original["wrapped"]:
        prefix, binary = wire.split(b"HNVSD:999:1+@", 1)
        length, rest = binary.split(b"@", 1)
        body = public_text_segments(rest[:int(length)])
    else:
        parts = public_text_segments(wire); prefix = parts[0]; body = parts[1:-1]
    options = [(b"" if value is None else str(value).encode()) for value in (minimum, maximum, tan_maximum)]
    options += [value.encode("latin-1") for value in labels]
    options += [value.encode() for pair in operations for value in pair]
    for _ in range(copies):
        body.append(segment("HIPINS", len(body) + 2, version, [plain(str(orders).encode()), plain(str(signatures).encode()), plain(b"0"), group(options)], reference=reference)[0])
    if future:
        body.append(segment("HIPINS", len(body) + 2, 99, [plain(b"PUBLIC-OPAQUE")], reference=4)[0])
    payload = b"".join(body)
    middle = segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0] if original["wrapped"] else payload
    closing = segment("HNHBS", len(body) + 2, 1, [plain(b"1")])[0]
    wire = prefix + middle + closing
    wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]
    initialization_requirements_vectors.append({"name": name, "context": original["context"], "responseBase64": base64.b64encode(wire).decode(),
        "wrapped": original["wrapped"], "issues": list(issues), "minimumPinLength": minimum, "maximumPinLength": maximum, "maximumTanLength": tan_maximum,
        "operationEvidence": "Unknown" if issues else operation_evidence})


initialization_requirements_vector("two-step-hipins")
initialization_requirements_vector("one-step-hipins", source=1)
initialization_requirements_vector("version-six-procedure-with-hipins", source=2)
initialization_requirements_vector("both-procedure-versions-with-hipins", source=3)
initialization_requirements_vector("missing-lengths-stay-null", minimum=None, maximum=None, tan_maximum=None)
initialization_requirements_vector("partial-lengths-stay-null", maximum=None, tan_maximum=None)
initialization_requirements_vector("upper-length-bound", minimum=1, maximum=99, tan_maximum=99)
initialization_requirements_vector("zero-minimum-needs-review", ["UnsupportedRequirements"], minimum=0)
initialization_requirements_vector("zero-maximum-needs-review", ["ConflictingPinBounds", "UnsupportedRequirements"], maximum=0)
initialization_requirements_vector("zero-tan-length-needs-review", ["UnsupportedRequirements"], tan_maximum=0)
initialization_requirements_vector("reversed-pin-bounds", ["ConflictingPinBounds"], minimum=13)
initialization_requirements_vector("duplicate-hipins", ["AmbiguousAdvertisement"], copies=2)
initialization_requirements_vector("future-only-hipins", ["ProceduresNeedReview", "MissingAdvertisement", "UnsupportedVersion"], version=99)
initialization_requirements_vector("future-and-known-hipins", ["ProceduresNeedReview", "UnsupportedVersion"], future=True)
initialization_requirements_vector("missing-hipins", ["MissingAdvertisement"], copies=0)
initialization_requirements_vector("duplicate-operation", ["AmbiguousOperations"], operations=(("HKSAL", "J"), ("HKSAL", "J")))
initialization_requirements_vector("conflicting-operation-flags", ["AmbiguousOperations"], operations=(("HKSAL", "J"), ("HKSAL", "N")))
initialization_requirements_vector("no-operation-list", operations=(), operation_evidence="Unlisted")
initialization_requirements_vector("not-required-is-only-report", operations=(("HKSAL", "N"),), operation_evidence="TanReportedNotRequired")
initialization_requirements_vector("old-hipins-reference", ["ProceduresNeedReview"], reference=3)
initialization_requirements_vector("missing-hipins-reference", ["ProceduresNeedReview"], reference=None)
initialization_requirements_vector("foreign-profile", ["ProceduresNeedReview"], source=19)
initialization_requirements_vector("foreign-envelope-system", ["ProceduresNeedReview"], source=20)
initialization_requirements_vector("foreign-permission-role", ["ProceduresNeedReview"], source=15)
initialization_requirements_vector("missing-permission-remains-review", ["ProceduresNeedReview"], source=4)
initialization_requirements_vector("unrelated-data-remains-review", ["ProceduresNeedReview"], source=17)
initialization_requirements_vector("zero-orders-needs-review", ["UnsupportedRequirements"], orders=0)
initialization_requirements_vector("two-signatures-needs-review", ["UnsupportedRequirements"], signatures=2)
initialization_requirements_vector("escaped-labels", labels=("U+:'?@ü", "C+:'?@ü"))
initialization_requirements_vector("plain-response-remains-review", ["ProceduresNeedReview"], source=18)
target.with_name("pin-tan-initialization-requirements-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": initialization_requirements_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(initialization_requirements_vectors)} public synthetic assembled initialization requirements vectors.")

# First post-initialization read signature expectations, generated without .NET output.
read_signature_vectors = []


def read_signature_vector(name, issues=(), source=0, read=4, dialogue="SYNTHETIC", client=2, bank=2,
                          replacements=(), selection_version=None, selection_function=None, selection_profile=None):
    origin = initialization_requirements_vectors[source]
    context = origin["context"]
    header = base64.b64decode(context["headerBase64"]).replace(b"PUBLIC-REF", b"NEXT-REF")
    for before, after in replacements:
        assert before in header
        header = header.replace(before, after)
    body = public_text_segments(base64.b64decode(write_vectors[read]["wireBase64"]))[1]
    wire = end_frame(dialogue, client, [(body, None)])
    operation = "TanReportedNotRequired" if read in (0, 1, 2) or source == 18 else "TanReportedRequired"
    read_signature_vectors.append({"name": name, "issues": list(issues), "initialization": origin,
        "requestBase64": base64.b64encode(wire).decode(), "headerBase64": base64.b64encode(header).decode(),
        "expectedBankMessageNumber": bank, "controlReference": "NEXT-REF",
        "profileVersion": selection_profile or context["profileVersion"],
        "securityFunction": selection_function or context["securityFunction"],
        "tanSegmentVersion": selection_version or context["tanSegmentVersion"],
        "operationEvidence": "Unknown" if issues else operation})


read_signature_vector("all-account-discovery", read=0)
read_signature_vector("selected-account-discovery", read=1)
read_signature_vector("balance-six", read=3)
read_signature_vector("balance-seven")
read_signature_vector("balance-eight-all", read=5)
read_signature_vector("one-step", source=1)
read_signature_vector("tan-version-six", source=2)
read_signature_vector("distinct-tan-versions", source=3)
read_signature_vector("foreign-dialogue", ["DialogueMismatch"], dialogue="OTHER")
read_signature_vector("old-client-counter", ["CounterMismatch"], client=1)
read_signature_vector("skipped-client-counter", ["CounterMismatch"], client=3)
read_signature_vector("old-bank-counter", ["CounterMismatch"], bank=1)
read_signature_vector("skipped-bank-counter", ["CounterMismatch"], bank=3)
read_signature_vector("foreign-user", ["IdentityMismatch"], replacements=[(b"PUBLIC-USER", b"PUBLIC-CUSTOMER")])
read_signature_vector("foreign-bank", ["IdentityMismatch"], replacements=[(b"PUBLIC-BANK", b"OTHER-BANK")])
read_signature_vector("foreign-country", ["IdentityMismatch"], replacements=[(b"280:", b"276:")])
read_signature_vector("foreign-system", ["SystemMismatch"], replacements=[(b"PUBLIC-SYSTEM", b"OTHER")])
read_signature_vector("zero-system", ["SystemMismatch"], replacements=[(b"PUBLIC-SYSTEM", b"0")])
read_signature_vector("unknown-system", ["SystemMismatch"], replacements=[(b"PUBLIC-SYSTEM", b"unbekannt")])
read_signature_vector("changed-header-function", ["SelectionMismatch"], replacements=[(b"+900+", b"+920+")])
read_signature_vector("changed-selected-version", ["SelectionMismatch"], source=3, selection_version=6)
read_signature_vector("changed-selected-profile", ["SelectionMismatch"], selection_profile=1, selection_function=999,
                      replacements=[(b"PIN:2+900+", b"PIN:1+999+")])
read_signature_vector("foreign-control", ["ControlMismatch"], replacements=[(b"NEXT-REF", b"OTHER-REF")])
read_signature_vector("foreign-header-position", ["HeaderRoleNeedsReview"], replacements=[(b"HNSHK:2:", b"HNSHK:3:")])
read_signature_vector("foreign-supplier-role", ["HeaderRoleNeedsReview"], replacements=[(b"NEXT-REF+1+1+", b"NEXT-REF+1+3+")])
read_signature_vector("foreign-security-party", ["HeaderRoleNeedsReview"], replacements=[(b"1::PUBLIC-SYSTEM", b"2::PUBLIC-SYSTEM")])
read_signature_vector("continuation-is-not-first-read", ["ContinuationNeedsReview"], read=6)
read_signature_vector("conflicting-returned-bounds", ["InitializationNeedsReview"], source=10)
read_signature_vector("duplicate-returned-hipins", ["InitializationNeedsReview"], source=11)
read_signature_vector("foreign-initialization-profile", ["InitializationNeedsReview"], source=21)
read_signature_vector("missing-returned-hipins", ["InitializationNeedsReview"], source=14)
read_signature_vector("unlisted-operation", ["OperationRequirementsNeedReview"], source=17)
read_signature_vector("reported-n-is-not-exemption", source=18)
read_signature_vector("unknown-bounds-stay-unknown", source=4)
read_signature_vector("missing-returned-permission", ["InitializationNeedsReview"], source=24)
read_signature_vector("changed-function-in-both-selection-and-header", ["SelectionMismatch"], selection_function=920,
                      replacements=[(b"+900+", b"+920+")])
read_signature_vector("combined-scope-failures", ["DialogueMismatch", "CounterMismatch", "IdentityMismatch"],
                      dialogue="OTHER", client=3, replacements=[(b"PUBLIC-USER", b"OTHER-USER")])
target.with_name("pin-tan-read-signature-context-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_signature_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_signature_vectors)} public synthetic first-read signature-context vectors.")

read_capability_context_vectors = []


def read_capability_context_vector(name, issues=(), source=0, operation="HKSAL", version=7, ad_version=None,
                                  copies=1, spa_version=1, single="J", national="J", entries="J", orders=1, signatures=1,
                                  permissions=(("HKSAL", 1), ("HKSPA", 1)), usage=0, account_copies=1,
                                  iban="PUBLIC-IBAN", customer="PUBLIC-CUSTOMER", institution="PUBLIC-BANK", reference=4,
                                  future=False, unrelated=False, all_accounts=False, maximum=None, with_national=False,
                                  national_selection=True, request_iban="PUBLIC-IBAN", request_number="PUBLIC-001",
                                  account_limit=False, permission_limit=False, initialization_matches=True, capability_issues=(), request_issues=()):
    origin = json.loads(json.dumps(initialization_requirements_vectors[source]))
    wire = base64.b64decode(origin["responseBase64"])
    prefix, binary = wire.split(b"HNVSD:999:1+@", 1)
    length, rest = binary.split(b"@", 1)
    body = public_text_segments(rest[:int(length)])
    if usage == 1:
        body = [part.replace(b"HIUPA:6:4:4+PUBLIC-USER+2+0", b"HIUPA:6:4:4+PUBLIC-USER+2+1") for part in body]
    account_fields = [group([b"PUBLIC-001", b"", b"280", institution.encode()]), plain(iban.encode()), plain(customer.encode()),
                      plain(b"1"), plain(b"EUR"), plain(b"Public owner"), plain(b""), plain(b""),
                      group([b"E", b"10,", b"EUR"]) if account_limit else plain(b"")]
    for code, count in permissions:
        elements = [code.encode(), str(count).encode()]
        if permission_limit and code == operation:
            elements += [b"E", b"10,", b"EUR"]
        account_fields.append(group(elements))
    for _ in range(account_copies):
        body.append(segment("HIUPD", len(body) + 2, 6, account_fields, reference=4)[0])
    for _ in range(copies):
        av = ad_version or version
        fields = [plain(str(orders).encode()), plain(str(signatures).encode()), plain(b"0")]
        if av == 8:
            fields.append(plain(entries.encode()))
        body.append(segment("HISALS", len(body) + 2, av, fields, reference=reference)[0])
    spa_options = [single.encode(), national.encode(), b"N"]
    if spa_version >= 2: spa_options.append(b"N")
    if spa_version == 3: spa_options.append(b"0")
    body.append(segment("HISPAS", len(body) + 2, spa_version, [plain(b"1"), plain(b"1"), plain(b"0"), group(spa_options)], reference=reference)[0])
    if future: body.append(segment("HISALS", len(body) + 2, 99, [plain(b"PUBLIC-OPAQUE")], reference=4)[0])
    if unrelated: body.append(segment("HIXYZ", len(body) + 2, 1, [plain(b"PUBLIC-OPAQUE")], reference=4)[0])
    payload = b"".join(body)
    wire = prefix + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0] + segment("HNHBS", len(body) + 2, 1, [plain(b"1")])[0]
    wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]
    origin["responseBase64"] = base64.b64encode(wire).decode()
    connection = [request_number.encode(), b"", b"280", b"PUBLIC-BANK"]
    if operation == "HKSPA":
        fields = [] if all_accounts else [group(connection)]
        request_version = 1
    else:
        request_version = version
        account = connection if version == 6 else [request_iban.encode(), b"PUBLIC-BIC"] + (connection if with_national else [])
        fields = [group(account), plain(b"J" if all_accounts else b"N")]
        if maximum is not None: fields.append(plain(str(maximum).encode()))
    request_wire = end_frame("SYNTHETIC", 2, [segment(operation, 2, request_version, fields)])
    context = origin["context"]
    header = base64.b64decode(context["headerBase64"]).replace(b"PUBLIC-REF", b"NEXT-REF")
    read_capability_context_vectors.append({"name": name, "initialization": origin, "initializationMatches": initialization_matches,
        "requestBase64": base64.b64encode(request_wire).decode(), "headerBase64": base64.b64encode(header).decode(),
        "nationalSelection": national_selection, "issues": list(issues), "capabilityIssues": list(capability_issues), "requestIssues": list(request_issues)})


cap_review = ["CapabilityNeedsReview", "RequestNeedsReview"]
cap_unknown = cap_review + ["SignatureRequirementsNeedReview"]
init_review = ["SignatureNeedsReview"] + cap_review
read_capability_context_vector("balance-seven-listed")
read_capability_context_vector("balance-six-listed", version=6)
read_capability_context_vector("balance-eight-listed", version=8)
read_capability_context_vector("selected-discovery-listed", operation="HKSPA", ad_version=7)
read_capability_context_vector("one-step-listed", source=1)
read_capability_context_vector("tan-version-six-listed", source=2)
read_capability_context_vector("national-balance-eight-with-explicit-spa", version=8, with_national=True)
read_capability_context_vector("entry-count-advertised", version=8, maximum=10)
read_capability_context_vector("blocked-unlisted-operation", cap_unknown, permissions=(("HKSPA", 1),), capability_issues=["PermissionBlocked"])
read_capability_context_vector("unknown-unlisted-operation", cap_unknown, permissions=(("HKSPA", 1),), usage=1, capability_issues=["PermissionUnknown"])
read_capability_context_vector("duplicate-operation-permission", cap_unknown, permissions=(("HKSAL", 1), ("HKSAL", 1)), capability_issues=["PermissionAmbiguous"])
read_capability_context_vector("two-customer-signatures", ["SignatureRequirementsNeedReview"], permissions=(("HKSAL", 2),))
read_capability_context_vector("below-bank-signature-minimum", cap_unknown, signatures=2, capability_issues=["SignatureConflict"])
read_capability_context_vector("zero-orders", cap_review, orders=0, capability_issues=["ZeroOrderCapacity"])
read_capability_context_vector("duplicate-advertisement", cap_unknown, copies=2, capability_issues=["DuplicateAdvertisement"])
read_capability_context_vector("missing-advertisement", cap_unknown, copies=0, capability_issues=["MissingAdvertisement"])
read_capability_context_vector("different-advertised-version", cap_unknown, ad_version=6, capability_issues=["MissingAdvertisement"])
read_capability_context_vector("missing-international-account", cap_review, iban="", capability_issues=["MissingInternationalIdentity"], request_issues=["RequestAccountMismatch"])
read_capability_context_vector("duplicate-account-identity", cap_review, account_copies=2, capability_issues=["DuplicateAccountIdentity"])
read_capability_context_vector("request-iban-mismatch", ["RequestNeedsReview"], request_iban="OTHER-IBAN", request_issues=["RequestAccountMismatch"])
read_capability_context_vector("request-national-mismatch", ["RequestNeedsReview"], version=6, request_number="OTHER-ACCOUNT", request_issues=["RequestAccountMismatch"])
read_capability_context_vector("all-discovery-outside-single-account-path", ["RequestNeedsReview"], operation="HKSPA", ad_version=7, all_accounts=True, request_issues=["UnsupportedAccountScope"])
read_capability_context_vector("all-balances-outside-single-account-path", ["RequestNeedsReview"], all_accounts=True, request_issues=["UnsupportedAccountScope"])
read_capability_context_vector("single-discovery-not-advertised", cap_review, operation="HKSPA", ad_version=7, single="N", capability_issues=["SingleAccountRequestNotAdvertised"])
read_capability_context_vector("entry-count-not-advertised", ["RequestNeedsReview"], version=8, maximum=10, entries="N", request_issues=["EntryCountNotAdvertised"])
read_capability_context_vector("entry-count-unknown-in-seven", ["RequestNeedsReview"], maximum=10, request_issues=["EntryCountNotAdvertised"])
read_capability_context_vector("national-option-not-advertised", ["RequestNeedsReview"], with_national=True, national="N", request_issues=["NationalAccountNotAdvertised"])
read_capability_context_vector("national-option-not-selected", ["RequestNeedsReview"], with_national=True, national_selection=False, request_issues=["NationalAccountNotAdvertised"])
read_capability_context_vector("national-option-version-two", with_national=True, spa_version=2)
read_capability_context_vector("national-option-version-three", with_national=True, spa_version=3)
read_capability_context_vector("future-read-version-remains-review", init_review, future=True, initialization_matches=False, capability_issues=["ResponseNeedsReview"])
read_capability_context_vector("unrelated-data-remains-review", init_review, unrelated=True, initialization_matches=False, capability_issues=["ResponseNeedsReview"])
read_capability_context_vector("old-read-advertisement-reference", init_review, reference=3, initialization_matches=False, capability_issues=["ResponseNeedsReview"])
read_capability_context_vector("missing-read-advertisement-reference", init_review, reference=None, initialization_matches=False, capability_issues=["ResponseNeedsReview"])
read_capability_context_vector("foreign-returned-customer", init_review, customer="OTHER-CUSTOMER", initialization_matches=False, capability_issues=["ResponseNeedsReview"])
read_capability_context_vector("foreign-returned-bank", init_review, institution="OTHER-BANK", initialization_matches=False, capability_issues=["ResponseNeedsReview", "InstitutionContextMismatch"])
read_capability_context_vector("unresolved-hipins-remains-review", init_review, source=10, initialization_matches=False, capability_issues=["ResponseNeedsReview"])
read_capability_context_vector("account-limit-requires-review", ["LimitsNeedReview"], account_limit=True)
read_capability_context_vector("operation-limit-requires-review", ["LimitsNeedReview"], permission_limit=True)
read_capability_context_vector("zero-upd-signatures-with-one-bank-signature", cap_unknown, permissions=(("HKSAL", 0),), capability_issues=["SignatureConflict"])
read_capability_context_vector("zero-upd-and-bank-signatures-still-one-local-header", permissions=(("HKSAL", 0),), signatures=0)
read_capability_context_vector("tan-not-required-remains-report", source=18)
read_capability_context_vector("nullable-credential-bounds", source=4)
target.with_name("pin-tan-read-capability-context-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_capability_context_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_capability_context_vectors)} public synthetic integrated read-capability vectors.")

read_credential_vectors = []


def read_credential_vector(name, kind, value, issues=(), source=0, bounds=(5, 12, 6), procedure_maximum=6,
                           input_format="1", decoupled=False):
    context = json.loads(json.dumps(read_capability_context_vectors[source]))
    origin = context["initialization"]
    wire = base64.b64decode(origin["responseBase64"])
    prefix, binary = wire.split(b"HNVSD:999:1+@", 1)
    length, rest = binary.split(b"@", 1)
    body = public_text_segments(rest[:int(length)])
    for i, part in enumerate(body):
        if part.startswith(b"HIPINS:"):
            fields = part.split(b"+", 4)
            old_options = fields[4].split(b":", 3)
            fields[4] = b":".join([b"" if n is None else str(n).encode() for n in bounds] + [old_options[3]])
            body[i] = b"+".join(fields)
        elif part.startswith(b"HITANS:"):
            before = b"App:1.0:Public method:6:1:Approval"
            after = (b"DecoupledPush:1.0:Public method:::Approval" if decoupled else
                     b"App:1.0:Public method:" + str(procedure_maximum).encode() + b":" + input_format.encode() + b":Approval")
            assert before in part
            body[i] = part.replace(before, after)
    payload = b"".join(body)
    wire = prefix + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0] + segment("HNHBS", len(body) + 2, 1, [plain(b"1")])[0]
    wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]
    origin["responseBase64"] = base64.b64encode(wire).decode()
    read_credential_vectors.append({"name": name, "context": context, "kind": kind,
                                    "valueBase64": base64.b64encode(value).decode(), "issues": list(issues)})


read_credential_vector("pin-at-minimum", "Pin", b"12345")
read_credential_vector("pin-at-maximum", "Pin", b"123456789012")
read_credential_vector("pin-too-short", "Pin", b"1234", ["PinTooShort"])
read_credential_vector("pin-too-long", "Pin", b"1234567890123", ["PinTooLong"])
read_credential_vector("pin-empty", "Pin", b"", ["EmptyValue"])
read_credential_vector("pin-local-limit", "Pin", b"P" * 99, bounds=(1, 99, 99))
read_credential_vector("pin-over-local-limit", "Pin", b"P" * 100, ["LocalLengthExceeded"], bounds=(1, 99, 99))
read_credential_vector("pin-missing-minimum", "Pin", b"12345", ["MissingPinBounds"], bounds=(None, 12, 6))
read_credential_vector("pin-missing-maximum", "Pin", b"12345", ["MissingPinBounds"], bounds=(5, None, 6))
read_credential_vector("pin-all-bounds-unknown", "Pin", b"12345", ["MissingPinBounds"], bounds=(None, None, None))
read_credential_vector("pin-known-minimum-still-applies", "Pin", b"1234", ["MissingPinBounds", "PinTooShort"], bounds=(5, None, 6))
read_credential_vector("pin-known-maximum-still-applies", "Pin", b"1234567890123", ["MissingPinBounds", "PinTooLong"], bounds=(None, 12, 6))
read_credential_vector("pin-spaces-and-delimiters-preserved", "Pin", b" +:'?@ ")
read_credential_vector("pin-latin-one-preserved", "Pin", bytes([161, 196, 223, 252, 255]))
read_credential_vector("pin-control-rejected", "Pin", b"12\x0034", ["InvalidText"])
read_credential_vector("pin-nbsp-rejected", "Pin", b"12\xa034", ["InvalidText"])
read_credential_vector("tan-numeric-leading-zeros", "Tan", b"000123")
read_credential_vector("tan-single-digit-not-fixed-length", "Tan", b"0")
read_credential_vector("tan-empty", "Tan", b"", ["EmptyValue"])
read_credential_vector("tan-both-maxima-exceeded", "Tan", b"1234567", ["HipinsTanTooLong", "ProcedureTanTooLong"])
read_credential_vector("tan-procedure-maximum-exceeded", "Tan", b"1234567", ["ProcedureTanTooLong"], bounds=(5, 12, 8))
read_credential_vector("tan-hipins-maximum-exceeded", "Tan", b"1234567", ["HipinsTanTooLong"], procedure_maximum=8)
read_credential_vector("tan-distinct-maxima-both-respected", "Tan", b"123456", bounds=(5, 12, 8))
read_credential_vector("tan-missing-hipins-no-fallback", "Tan", b"123456", ["MissingTanBound"], bounds=(5, 12, None))
read_credential_vector("tan-missing-hipins-known-procedure-still-applies", "Tan", b"1234567", ["MissingTanBound", "ProcedureTanTooLong"], bounds=(5, 12, None))
read_credential_vector("tan-zero-procedure-maximum", "Tan", b"123456", ["ProcedureRequirementsNeedReview"], procedure_maximum=0)
read_credential_vector("tan-numeric-rejects-letters", "Tan", b"12A456", ["TanFormatMismatch"])
read_credential_vector("tan-numeric-rejects-whitespace", "Tan", b"12345 ", ["TanFormatMismatch"])
read_credential_vector("tan-numeric-rejects-non-ascii-digits", "Tan", bytes([178]) * 6, ["TanFormatMismatch"])
read_credential_vector("tan-control-and-format-issues", "Tan", b"12\n456", ["InvalidText", "TanFormatMismatch"])
read_credential_vector("tan-alphanumeric", "Tan", b"Ab01z9", input_format="2")
read_credential_vector("tan-alphanumeric-delimiters", "Tan", b"+:'?@ ", input_format="2")
read_credential_vector("tan-alphanumeric-latin-one", "Tan", bytes([161, 196, 223, 252, 255]), input_format="2")
read_credential_vector("tan-one-step-only-hipins-bound", "Tan", b"Ab01z9", source=4, procedure_maximum=1)
read_credential_vector("tan-one-step-missing-bound", "Tan", b"123456", ["MissingTanBound"], source=4, bounds=(5, 12, None))
read_credential_vector("tan-version-six", "Tan", b"123456", source=5)
read_credential_vector("tan-decoupled-input-not-supported", "Tan", b"123456", ["TanInputNotSupported"], decoupled=True)
read_credential_vector("pin-for-decoupled-remains-comparable", "Pin", b"12345", decoupled=True)
read_credential_vector("tan-n-flag-does-not-waive-format", "Tan", b"ABC123", ["TanFormatMismatch"], source=41)
read_credential_vector("tan-n-flag-still-compares-supplied-value", "Tan", b"123456", source=41)
read_credential_vector("unqualified-context-withholds-input-diagnostics", "Pin", b"", ["ContextNeedsReview"], source=8)
read_credential_vector("tan-local-limit", "Tan", b"1" * 99, bounds=(1, 99, 99), procedure_maximum=99)
read_credential_vector("tan-over-local-limit", "Tan", b"1" * 100, ["LocalLengthExceeded"], bounds=(1, 99, 99), procedure_maximum=99)
read_credential_vector("conflicting-pin-bounds-stay-context-review", "Pin", b"12345", ["ContextNeedsReview"], bounds=(13, 12, 6))
read_credential_vector("zero-hipins-bound-stays-context-review", "Tan", b"123456", ["ContextNeedsReview"], bounds=(5, 12, 0))
target.with_name("pin-tan-read-credential-comparison-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_credential_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_credential_vectors)} public synthetic first-read credential-comparison vectors.")

read_pin_trailer_vectors = []


def read_pin_trailer_vector(name, source=0, result="Written", copies=1, pin=None, control="NEXT-REF"):
    original = read_credential_vectors[source]
    context = json.loads(json.dumps(original["context"]))
    value = base64.b64decode(original["valueBase64"]) if pin is None else pin
    header = base64.b64decode(context["headerBase64"])
    context["headerBase64"] = base64.b64encode(header.replace(b"NEXT-REF", text(control.encode("latin-1")))).decode()
    wire = segment("HNSHA", 4, 2, [plain(control.encode("latin-1")), plain(b""), group([value])])[0]
    read_pin_trailer_vectors.append({"name": name, "context": context, "controlReference": control,
        "pinBase64": base64.b64encode(value).decode(), "result": result, "pinCopies": copies,
        "wireBase64": base64.b64encode(wire).decode() if result == "Written" else None})


read_pin_trailer_vector("pin-minimum")
read_pin_trailer_vector("pin-maximum", source=1)
read_pin_trailer_vector("pin-too-short", source=2, result="CredentialRequirementsNeedReview")
read_pin_trailer_vector("pin-too-long", source=3, result="CredentialRequirementsNeedReview")
read_pin_trailer_vector("pin-local-maximum", source=5)
read_pin_trailer_vector("unknown-minimum-before-owner-access", source=7, result="CredentialRequirementsNeedReview", copies=0)
read_pin_trailer_vector("unknown-maximum-before-owner-access", source=8, result="CredentialRequirementsNeedReview", copies=0)
read_pin_trailer_vector("unknown-bounds-before-owner-access", source=9, result="CredentialRequirementsNeedReview", copies=0)
read_pin_trailer_vector("pin-delimiters-and-spaces", source=12)
read_pin_trailer_vector("pin-latin-one", source=13)
read_pin_trailer_vector("pin-control-rejected", source=14, result="InvalidCredentialText")
read_pin_trailer_vector("pin-nbsp-rejected", source=15, result="InvalidCredentialText")
read_pin_trailer_vector("decoupled-pin-component", source=37)
read_pin_trailer_vector("unqualified-account-before-owner-access", source=40, pin=b"PUBLIC-PIN", result="ContextNeedsReview", copies=0)
read_pin_trailer_vector("conflicting-bounds-before-owner-access", source=43, result="ContextNeedsReview", copies=0)
read_pin_trailer_vector("zero-hipins-bound-before-owner-access", source=44, pin=b"PUBLIC-PIN", result="ContextNeedsReview", copies=0)
read_pin_trailer_vector("one-step-pin-component", source=33, pin=b"PUBLIC-PIN")
read_pin_trailer_vector("tan-version-six-pin-component", source=35, pin=b"PUBLIC-PIN")
read_pin_trailer_vector("n-flag-does-not-prove-completeness", source=39, pin=b"PUBLIC-PIN")
read_pin_trailer_vector("maximum-escaped-pin", source=5, pin=b"?" * 99)
read_pin_trailer_vector("escaped-control-and-pin", source=12, control="R+:'?@ü")
read_pin_trailer_vector("maximum-control", control="?" * 14)
target.with_name("pin-tan-read-pin-trailer-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_pin_trailer_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_pin_trailer_vectors)} public synthetic first-read PIN trailer vectors.")

read_request_assembly_vectors = []


def read_request_assembly_vector(name, trailer_source=0, read_source=None, result=None, copies=None, dialogue="SYNTHETIC", escaped_account=False):
    original = read_pin_trailer_vectors[trailer_source]
    context = json.loads(json.dumps(original["context"] if read_source is None else read_capability_context_vectors[read_source]))
    control = original["controlReference"] if read_source is None else "NEXT-REF"
    value = base64.b64decode(original["pinBase64"])
    result = result or original["result"]
    copies = original["pinCopies"] if copies is None else copies
    request = public_text_segments(base64.b64decode(context["requestBase64"]))[1]
    origin = context["initialization"]
    wire = base64.b64decode(origin["responseBase64"])
    prefix, binary = wire.split(b"HNVSD:999:1+@", 1)
    length, rest = binary.split(b"@", 1)
    payload = rest[:int(length)]
    if escaped_account:
        escaped = text("A+:'?@ü".encode("latin-1"))
        request = request.replace(b"PUBLIC-001", escaped)
        payload = payload.replace(b"PUBLIC-001", escaped)
    if dialogue != "SYNTHETIC":
        prefix = prefix.replace(b"+SYNTHETIC+1+SYNTHETIC:1'", b"+" + text(dialogue.encode("latin-1")) + b"+1+" + text(dialogue.encode("latin-1")) + b":1'")
    tail = rest[int(length) + 1:]
    wire = prefix + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0] + tail
    wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]
    origin["responseBase64"] = base64.b64encode(wire).decode()
    context["requestBase64"] = base64.b64encode(end_frame(dialogue, 2, [(request, None)])).decode()
    header = base64.b64decode(context["headerBase64"])
    parts, fields = request.split(b"+", 1)
    elements = parts.split(b":"); elements[1] = b"3"
    request = b":".join(elements) + b"+" + fields
    trailer = segment("HNSHA", 4, 2, [plain(control.encode("latin-1")), plain(b""), group([value])])[0]
    assembled = end_frame(dialogue, 2, [(header, None), (request, None), (trailer, None)])
    read_request_assembly_vectors.append({"name": name, "context": context, "controlReference": control,
        "pinBase64": base64.b64encode(value).decode(), "result": result, "pinCopies": copies,
        "wireBase64": base64.b64encode(assembled).decode() if result == "Written" else None})


for index, original in enumerate(read_pin_trailer_vectors):
    read_request_assembly_vector(original["name"], trailer_source=index)
read_request_assembly_vector("balance-six", read_source=1)
read_request_assembly_vector("balance-eight", read_source=2)
read_request_assembly_vector("single-account-discovery", read_source=3)
read_request_assembly_vector("national-balance-eight", read_source=6)
read_request_assembly_vector("entry-count-preserved", read_source=7)
read_request_assembly_vector("escaped-assigned-dialogue", dialogue="D+:'?@ü")
read_request_assembly_vector("escaped-account-identity", read_source=3, escaped_account=True)
read_request_assembly_vector("all-balances-rejected-before-owner", read_source=22, result="ContextNeedsReview", copies=0)
read_request_assembly_vector("unhandled-limit-rejected-before-owner", read_source=37, result="ContextNeedsReview", copies=0)
target.with_name("pin-tan-read-request-assembly-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_request_assembly_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_request_assembly_vectors)} public synthetic first-read request-assembly vectors.")

read_envelope_sources = json.loads(json.dumps(read_request_assembly_vectors))
for name, stamp in [("absent-envelope-timestamp", b"1"), ("date-only-envelope-timestamp", b"1:20260908")]:
    source = json.loads(json.dumps(read_request_assembly_vectors[0]))
    source["name"] = name
    for owner, key in [(source["context"], "headerBase64"), (source, "wireBase64")]:
        value = base64.b64decode(owner[key])
        assert b"1:20260908:123456" in value
        value = value.replace(b"1:20260908:123456", stamp)
        if key == "wireBase64": value = value[:10] + f"{len(value):012d}".encode() + value[22:]
        owner[key] = base64.b64encode(value).decode()
    source["envelopeTimestamp"] = stamp.decode()
    read_envelope_sources.append(source)
read_request_envelope_vectors = []
for source in read_envelope_sources:
    payload = wire = None
    profile = source["context"]["initialization"]["context"]["profileVersion"]
    read_header = public_text_segments(base64.b64decode(source["context"]["requestBase64"]))[1].split(b"+", 1)[0].split(b":")
    if source["result"] == "Written":
        parts = public_text_segments(base64.b64decode(source["wireBase64"]))
        payload = b"".join(parts[1:-1])
        stamp = source.get("envelopeTimestamp", "1:20260908:123456").encode().split(b":")
        security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", b"PUBLIC-SYSTEM"]), group(stamp),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([b"280", b"PUBLIC-BANK", b"PUBLIC-USER", b"V", b"0", b"0"]), plain(b"0")])[0]
        wire = parts[0] + security + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0] + parts[-1]
        wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]
    read_request_envelope_vectors.append({**source, "candidateMatches": source["result"] != "ContextNeedsReview",
        "readCode": read_header[0].decode(), "readVersion": int(read_header[2]), "profileVersion": profile,
        "plainWireBase64": source["wireBase64"], "wireBase64": None if wire is None else base64.b64encode(wire).decode(),
        "payloadBase64": None if payload is None else base64.b64encode(payload).decode()})
target.with_name("pin-tan-read-request-envelope-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_request_envelope_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_request_envelope_vectors)} public synthetic first-read envelope vectors.")

read_response_binding_vectors = []


def read_response_binding_vector(name, issues=(), source=0, wrapped=True, profile=None, dialogue="SYNTHETIC",
                                 reference_dialogue=None, message=2, reference_message=2, omit_reference=False,
                                 data_reference=3, data_code=None, data_version=None, reports=((3, "0020"),),
                                 message_code="0010", no_data=False, duplicate=False, extra=None,
                                 foreign_identity=False, foreign_account=False, opaque=False, data_fields=None, reply_fields=None, envelope_identity=None,
                                 candidate_override=None):
    candidate = read_request_envelope_vectors[source] if candidate_override is None else candidate_override
    profile = candidate["profileVersion"] if profile is None else profile
    code = data_code or ("HISPA" if candidate["readCode"] == "HKSPA" else "HISAL")
    version = candidate["readVersion"] if data_version is None else data_version
    body = [segment("HIRMG", 2, 2, [group([message_code.encode(), b"", b"PUBLIC-REPLY"])])]
    for reference, reply in reports:
        body.append(segment("HIRMS", len(body) + 2, 2, reply_fields[reference] if reply_fields is not None and reference in reply_fields else
            [group([reply.encode(), b"", b"PUBLIC-REPLY"])], reference=reference))
    if not no_data:
        number = b"FOREIGN-ACCOUNT" if foreign_account else b"PUBLIC-001"
        if opaque or (code, version) not in [("HISPA", 1), ("HISAL", 6), ("HISAL", 7), ("HISAL", 8)]:
            fields = [plain(b"PUBLIC-OPAQUE")]
        elif code == "HISPA":
            fields = [group([b"J", b"DE02120300000000202051", b"BYLADEM1001", number, b"", b"280", b"PUBLIC-BANK"])]
        else:
            account = [number, b"", b"280", b"PUBLIC-BANK"] if version == 6 else [b"DE02120300000000202051", b"BYLADEM1001"]
            if foreign_account and version != 6: account[0] = b"DE89370400440532013000"
            fields = [group(account), plain(b"PUBLIC-ACCOUNT"), plain(b"EUR"), group([b"C", b"1,", b"EUR", b"20260910"])]
        if data_fields is not None: fields = data_fields
        for _ in range(2 if duplicate else 1):
            body.append(segment(code, len(body) + 2, version, fields, reference=data_reference))
    if extra is not None:
        extra_code, extra_version, extra_reference = extra
        body.append(segment(extra_code, len(body) + 2, extra_version, [plain(b"PUBLIC-OPAQUE")], reference=extra_reference))
    fields = [plain(b"000000000000"), plain(b"300"), plain(dialogue.encode("latin-1")), plain(str(message).encode())]
    if not omit_reference:
        fields.append(group([(dialogue if reference_dialogue is None else reference_dialogue).encode("latin-1"), str(reference_message).encode()]))
    ending = segment("HNHBS", len(body) + 2, 1, [plain(str(message).encode())])[0]
    payload = b"".join(p[0] for p in body)
    if wrapped:
        identity = b"FOREIGN-USER" if foreign_identity else b"PUBLIC-USER"
        system = b"FOREIGN-SYSTEM" if foreign_identity else b"PUBLIC-SYSTEM"
        country, bank = b"280", b"PUBLIC-BANK"
        if envelope_identity is not None: country, bank, identity, system = envelope_identity
        security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", system]), group([b"1"]),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([country, bank, identity, b"V", b"0", b"0"]), plain(b"0")])[0]
        middle = security + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0]
    else:
        middle = payload
    wire = segment("HNHBK", 1, 3, fields)[0] + middle + ending
    wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]
    roles = {1: "MessageHeader", 998: "EnvelopeHeader", 999: "EnvelopeData", 2: "SignatureHeader",
             3: "ReadOperation", 4: "SignatureTrailer", 5: "MessageTrailer"}
    read_response_binding_vectors.append({"name": name, "candidate": candidate, "responseBase64": base64.b64encode(wire).decode(),
        "wrapped": wrapped, "issues": list(issues), "hasErrors": any(code.startswith("9") for code in [message_code] + [p[1] for p in reports]),
        "references": [{"code": p[1]["code"], "reference": p[1]["reference"], "role": roles.get(p[1]["reference"])} for p in body[1:]]})


read_response_binding_vector("balance-seven-reference-three")
read_response_binding_vector("balance-six-reference-three", source=22)
read_response_binding_vector("balance-eight-reference-three", source=23)
read_response_binding_vector("discovery-reference-three", source=24)
read_response_binding_vector("one-step-profile", source=16)
read_response_binding_vector("tan-version-six", source=17)
read_response_binding_vector("decoupled-pin-candidate", source=12)
read_response_binding_vector("escaped-dialogue", source=27, dialogue="D+:'?@ü")
read_response_binding_vector("missing-pin-bounds-candidate", source=5)
read_response_binding_vector("old-unsigned-data-reference-two", ["SegmentRoleMismatch"], data_reference=2)
read_response_binding_vector("missing-data-reference", ["MissingSegmentReference", "SegmentRoleMismatch"], data_reference=None)
read_response_binding_vector("unknown-data-reference", ["UnknownSegmentReference", "SegmentRoleMismatch"], data_reference=997)
for reference in [1, 998, 999, 2, 3, 4, 5]:
    read_response_binding_vector(f"error-on-role-{reference}", reports=[(reference, "9050")], no_data=True)
read_response_binding_vector("unknown-reply-reference", ["UnknownSegmentReference"], reports=[(997, "9050")], no_data=True)
read_response_binding_vector("signature-success-is-not-read-execution", reports=[(2, "0020")], no_data=True)
read_response_binding_vector("message-abort", message_code="9800", reports=[], no_data=True)
read_response_binding_vector("message-receipt-without-data", reports=[], no_data=True)
read_response_binding_vector("unavailable-report", reports=[(3, "3010")], no_data=True)
read_response_binding_vector("pending-report", reports=[(3, "0030")], no_data=True)
read_response_binding_vector("missing-envelope", ["MissingEnvelope"], wrapped=False)
read_response_binding_vector("wrong-profile", ["ProfileMismatch"], profile=1)
read_response_binding_vector("wrong-bank-counter", ["MessageMismatch"], message=3)
read_response_binding_vector("wrong-client-reference", ["MessageMismatch"], reference_message=1)
read_response_binding_vector("wrong-dialogue", ["MessageMismatch"], dialogue="FOREIGN")
read_response_binding_vector("wrong-reference-dialogue", ["MessageMismatch"], reference_dialogue="FOREIGN")
read_response_binding_vector("missing-message-reference", ["MessageMismatch"], omit_reference=True)
read_response_binding_vector("zero-dialogue", ["MessageMismatch"], dialogue="0")
read_response_binding_vector("padded-dialogue", ["MessageMismatch"], dialogue="SYNTHETIC ")
read_response_binding_vector("discovery-data-for-balance", ["SegmentRoleMismatch"], data_code="HISPA", data_version=1)
read_response_binding_vector("balance-data-for-discovery", ["SegmentRoleMismatch"], source=24, data_code="HISAL", data_version=8)
read_response_binding_vector("future-read-version", ["UninterpretedData"], data_version=99)
read_response_binding_vector("known-version-needs-semantic-comparison", data_version=8)
read_response_binding_vector("unknown-data-code", ["UninterpretedData"], extra=("HIZZZ", 1, 3))
read_response_binding_vector("unexpected-parameter-data", ["UninterpretedData"], extra=("HIBPA", 3, 3))
read_response_binding_vector("unknown-data-missing-reference", ["UninterpretedData", "MissingSegmentReference"], extra=("HIZZZ", 1, None))
read_response_binding_vector("foreign-envelope-identity-needs-semantics", foreign_identity=True)
read_response_binding_vector("foreign-account-needs-semantics", foreign_account=True)
read_response_binding_vector("duplicate-data-needs-semantics", duplicate=True)
read_response_binding_vector("opaque-payload-needs-schema-validation", opaque=True)
read_response_binding_vector("combined-scope-and-role-failures", ["MissingEnvelope", "MessageMismatch", "UnknownSegmentReference", "SegmentRoleMismatch", "UninterpretedData"],
                             wrapped=False, message=3, data_reference=997, data_version=99)
target.with_name("pin-tan-read-response-binding-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_response_binding_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_response_binding_vectors)} public synthetic first-read response-binding vectors.")

read_semantic_vectors = []


def read_semantic_vector(name, read_issues=(), issues=(), outcome="ExecutionReported", source=0, replies=("0020",),
                         token=None, parameters=None, element=b"", empty=False, accounts=1, currency=b"EUR", booked_currency=None,
                         amount=b"1,", foreign_account=False, foreign_bic=False, due=False, optional=False,
                         candidate_override=None, national_account=False, account_override=None, **options):
    candidate = read_request_envelope_vectors[source] if candidate_override is None else candidate_override
    version = options.get("data_version", candidate["readVersion"])
    discovery = options.get("data_code", "HISPA" if candidate["readCode"] == "HKSPA" else "HISAL") == "HISPA"
    national = [b"FOREIGN-ACCOUNT" if foreign_account else b"PUBLIC-001", b"", b"280", b"PUBLIC-BANK"]
    international = [b"FOREIGN-IBAN" if foreign_account else b"PUBLIC-IBAN", b"FOREIGN-BIC" if foreign_bic else b"PUBLIC-BIC"]
    if discovery:
        fields = [] if empty else [group([b"J"] + international + national) for _ in range(accounts)]
    else:
        account = national if version == 6 else international + (national if source == 25 or national_account else [])
        if account_override is not None: account = account_override
        fields = [group(account), plain(b"PUBLIC-ACCOUNT"), plain(currency), group([b"C", amount, booked_currency or currency, b"20260910"])]
        if optional:
            fields += [group([b"D", b"2,", currency, b"20260910"]), plain(b""), group([b"3,", currency]), plain(b""), plain(b""), group([b"20260910", b"123456"])]
        if due:
            while len(fields) < 10: fields.append(plain(b""))
            fields.append(plain(b"20260911"))
    if version not in (1, 6, 7, 8): fields = [plain(b"PUBLIC-OPAQUE")]
    reply_values = [group([code.encode(), element, b"PUBLIC-REPLY"] + (parameters if parameters is not None else
        [token.encode("latin-1")] if code == "3040" and token is not None else [])) for code in replies]
    if "reports" not in options: options["reports"] = [(3, replies[0])] if replies else []
    read_response_binding_vector(name, source=source, candidate_override=candidate_override, data_fields=fields, reply_fields={3: reply_values}, **options)
    vector = read_response_binding_vectors.pop()
    vector.update({"issues": list(issues) + (["ReadNeedsReview"] if read_issues else []), "readIssues": list(read_issues),
                   "outcome": "NeedsReview" if issues or read_issues else outcome, "token": token if "3040" in replies and
                   token is not None and not discovery and parameters is None else None})
    if "3040" in replies and not discovery and parameters and parameters[0] and not any(parameters[1:]):
        vector["token"] = parameters[0].decode("latin-1")
    read_semantic_vectors.append(vector)


read_semantic_vector("balance-seven")
read_semantic_vector("balance-six", source=22)
read_semantic_vector("balance-eight", source=23)
read_semantic_vector("discovery", source=24)
read_semantic_vector("one-step-profile", source=16)
read_semantic_vector("tan-six-profile", source=17)
read_semantic_vector("decoupled-response-observation", source=12)
read_semantic_vector("escaped-dialogue", source=27, dialogue="D+:'?@ü")
read_semantic_vector("national-balance-eight", source=25)
read_semantic_vector("separate-optional-values", source=23, optional=True)
read_semantic_vector("real-zero-balance", amount=b"0,")
read_semantic_vector("balance-unavailable", replies=("3010",), no_data=True, outcome="UnavailableReported")
read_semantic_vector("discovery-unavailable-absent", source=24, replies=("3010",), no_data=True, outcome="UnavailableReported")
read_semantic_vector("discovery-unavailable-empty", source=24, replies=("3010",), empty=True, outcome="UnavailableReported")
read_semantic_vector("execution-and-unavailable", replies=("0020", "3010"), no_data=True, outcome="UnavailableReported")
read_semantic_vector("unavailable-empty-parameter", replies=("3010",), parameters=[b""], no_data=True, outcome="UnavailableReported")
read_semantic_vector("partial-balance", replies=("3040",), token="PUBLIC-NEXT", outcome="PartialReported")
read_semantic_vector("execution-and-partial", replies=("0020", "3040"), token="P+:'?@ü", outcome="PartialReported")
read_semantic_vector("unavailable-with-zero", ["AvailabilityConflict"], replies=("3010",), amount=b"0,")
read_semantic_vector("unavailable-and-partial", ["AvailabilityConflict"], replies=("3010", "3040"), token="PUBLIC-NEXT", no_data=True)
read_semantic_vector("missing-report", ["MissingReport"], no_data=True)
read_semantic_vector("empty-discovery-without-unavailable", ["MissingReport"], source=24, empty=True)
read_semantic_vector("duplicate-reports", ["DuplicateReport"], duplicate=True)
read_semantic_vector("duplicate-discovery-accounts", ["DuplicateReport"], source=24, accounts=2)
read_semantic_vector("foreign-iban", ["ResponseAccountMismatch"], foreign_account=True)
read_semantic_vector("foreign-national-account", ["ResponseAccountMismatch"], source=22, foreign_account=True)
read_semantic_vector("foreign-discovery-account", ["ResponseAccountMismatch"], source=24, foreign_account=True)
read_semantic_vector("foreign-bic", ["ResponseAccountMismatch"], foreign_bic=True)
read_semantic_vector("wrong-version", ["VersionMismatch"], data_version=8)
read_semantic_vector("wrong-account-currency", ["CurrencyMismatch"], currency=b"USD")
read_semantic_vector("conflicting-amount-currency", ["CurrencyMismatch"], booked_currency=b"USD")
read_semantic_vector("unhandled-due-date", ["DueDateNeedsReview"], due=True)
read_semantic_vector("foreign-envelope-identity", issues=["EnvelopeIdentityMismatch"], foreign_identity=True)
read_semantic_vector("missing-envelope", issues=["BindingNeedsReview"], wrapped=False)
read_semantic_vector("wrong-profile", issues=["BindingNeedsReview"], profile=1)
read_semantic_vector("foreign-message", issues=["BindingNeedsReview"], dialogue="FOREIGN")
read_semantic_vector("foreign-message-retains-token", issues=["BindingNeedsReview"], dialogue="FOREIGN", replies=("3040",), token="PUBLIC-NEXT")
read_semantic_vector("old-data-reference", issues=["BindingNeedsReview"], data_reference=2)
read_semantic_vector("unknown-data", ["UnexpectedReport"], issues=["BindingNeedsReview"], extra=("HIZZZ", 1, 3))
read_semantic_vector("future-data-version", ["UnexpectedReport", "MissingReport"], issues=["BindingNeedsReview"], data_version=99)
read_semantic_vector("security-success-not-read-success", ["StatusNeedsReview"], reports=[(2, "0020")], replies=())
read_semantic_vector("extra-security-success", ["StatusNeedsReview"], reports=[(2, "0020"), (3, "0020")])
read_semantic_vector("security-unavailable-not-read-unavailable", ["ResponseNeedsReview", "StatusNeedsReview", "MissingReport"], reports=[(2, "3010")], replies=(), no_data=True)
read_semantic_vector("read-error", ["ResponseNeedsReview", "StatusNeedsReview", "MissingReport"], replies=("9210",), no_data=True)
read_semantic_vector("pending", ["StatusNeedsReview"], replies=("0030",))
read_semantic_vector("message-abort", ["ResponseNeedsReview", "StatusNeedsReview", "MissingReport"], message_code="9800", replies=(), no_data=True)
read_semantic_vector("missing-scoped-status", ["StatusNeedsReview"], replies=())
read_semantic_vector("element-specific-execution", ["StatusNeedsReview"], element=b"1")
read_semantic_vector("execution-parameter", ["StatusNeedsReview"], parameters=[b"PUBLIC-EXTRA"])
read_semantic_vector("duplicate-execution", ["ResponseNeedsReview", "StatusNeedsReview"], replies=("0020", "0020"))
read_semantic_vector("invalid-unavailable-parameter", ["StatusNeedsReview", "MissingReport"], replies=("3010",), parameters=[b"PUBLIC-EXTRA"], no_data=True)
read_semantic_vector("partial-without-token", ["ContinuationNeedsReview"], replies=("3040",))
read_semantic_vector("partial-extra-parameter", ["ContinuationNeedsReview"], replies=("3040",), parameters=[b"PUBLIC-NEXT", b"EXTRA"])
read_semantic_vector("discovery-partial", ["ContinuationNeedsReview"], source=24, replies=("3040",), token="PUBLIC-NEXT")
for index, name in enumerate(["country", "bank", "user", "system"]):
    identity = [b"280", b"PUBLIC-BANK", b"PUBLIC-USER", b"PUBLIC-SYSTEM"]
    identity[index] = b"276" if index == 0 else b"FOREIGN"
    read_semantic_vector("foreign-envelope-" + name, issues=["EnvelopeIdentityMismatch"], envelope_identity=identity)
read_semantic_vector("partial-trailing-empty-parameter", replies=("3040",), parameters=[b"PUBLIC-NEXT", b""], outcome="PartialReported")
target.with_name("pin-tan-read-semantics-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_semantic_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_semantic_vectors)} public synthetic first-read semantic vectors.")

read_attempt_vectors = []
read_semantic_by_name = {v["name"]: v for v in read_semantic_vectors}


def read_attempt_trace(name, steps, source="balance-seven", responses=None):
    selected = read_semantic_by_name[source]
    incoming = [selected] if responses is None else [read_semantic_by_name[n] for n in responses]
    read_attempt_vectors.append({"name": name, "candidate": selected["candidate"],
        "responses": [{"wireBase64": v["responseBase64"], "wrapped": v["wrapped"]} for v in incoming], "steps": steps})


read_start = owned_init_step("start", "CandidateRecorded", "AwaitingResponse")
read_done = owned_init_step("response", "ExecutionReported", "ExecutionReported", handled=1, evidence=True)
read_review = owned_init_step("response", "RejectedForReview", "NeedsReview", handled=1, evidence=True)
read_attempt_trace("one-time-execution", [read_start, read_done, owned_init_step("response", "Terminal", "ExecutionReported", handled=1),
    owned_init_step("start", "Terminal", "ExecutionReported", handled=1)])
for name in ["balance-six", "balance-eight", "discovery", "one-step-profile", "decoupled-response-observation", "escaped-dialogue"]:
    read_attempt_trace(name, [read_start, read_done], source=name)
for source, state in [("partial-balance", "PartialReported"), ("balance-unavailable", "UnavailableReported"), ("discovery-unavailable-empty", "UnavailableReported")]:
    read_attempt_trace(source, [read_start, owned_init_step("response", state, state, handled=1, evidence=True),
        owned_init_step("response", "Terminal", state, handled=1), owned_init_step("start", "Terminal", state, handled=1)], source=source)
for source in ["foreign-envelope-user", "missing-envelope", "wrong-profile", "wrong-version", "foreign-iban", "read-error", "unknown-data", "security-success-not-read-success"]:
    read_attempt_trace(source, [read_start, read_review, owned_init_step("response", "Terminal", "NeedsReview", handled=1)], source=source)
for source in ["foreign-message", "old-data-reference"]:
    read_attempt_trace(source + "-then-valid", [read_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse", at=500),
        owned_init_step("response", "ExecutionReported", "ExecutionReported", at=700, handled=1, evidence=True, index=1)], responses=[source, "balance-seven"])
read_attempt_trace("deadline-equality", [read_start, owned_init_step("response", "Terminal", "TimedOut", at=10000)])
read_attempt_trace("mismatch-does-not-renew-deadline", [read_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse", at=9999),
    owned_init_step("response", "Terminal", "TimedOut", at=10000, index=1)], responses=["foreign-message", "balance-seven"])
read_attempt_trace("second-start-does-not-renew-deadline", [read_start, owned_init_step("start", "WrongState", "AwaitingResponse", at=9999),
    owned_init_step("snapshot", "Snapshot", "TimedOut", at=10000)])
read_attempt_trace("cancel-pending", [read_start, owned_init_step("cancel", "Cancelled", "Cancelled", at=50), owned_init_step("response", "Terminal", "Cancelled", at=50)])
read_attempt_trace("stop-ready", [owned_init_step("stop", "Stopped", "Stopped", candidates=0), owned_init_step("start", "Terminal", "Stopped", candidates=0)])
read_attempt_trace("dispose-pending", [read_start, owned_init_step("dispose", "Stopped", "Stopped"), owned_init_step("response", "Terminal", "Stopped")])
read_attempt_trace("clock-regression", [read_start, owned_init_step("snapshot", "Snapshot", "ClockInvalid", at=-1)])
target.with_name("pin-tan-read-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_attempt_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_attempt_vectors)} public synthetic first-read attempt traces.")

read_continuation_vectors = []


def read_continuation_vector(name, issues=(), request_issues=(), previous="partial-balance", source=None, token="PUBLIC-NEXT",
                             returned_token="PUBLIC-NEXT", dialogue="SYNTHETIC", message=3, bank_message=3,
                             version=None, maximum="unchanged", all_accounts=False, account_edit=None, remove_national=False,
                             trailing_empty=False, discovery=False):
    if source is None:
        prior = read_semantic_by_name[previous]
    else:
        read_semantic_vector(name + "-prior", source=source, replies=("3040",), token=returned_token,
                             dialogue=dialogue if source == 27 else "SYNTHETIC", outcome="PartialReported")
        prior = read_semantic_vectors.pop()
    unsigned = public_text_segments(base64.b64decode(prior["candidate"]["context"]["requestBase64"]))[1]
    parts = unsigned[:-1].split(b"+")
    account = parts[1].split(b":")
    original_version = int(parts[0].split(b":")[2])
    if account_edit is not None: account[account_edit[0]] = account_edit[1]
    if remove_national: account = account[:2]
    if trailing_empty: account += [b"", b"", b"", b""]
    if discovery:
        wire_read = segment("HKSPA", 2, 1, [group(account)])[0]
    else:
        entries = parts[3] if len(parts) > 3 else b""
        if maximum != "unchanged": entries = b"" if maximum is None else str(maximum).encode()
        fields = [group(account), plain(b"J" if all_accounts else b"N"), plain(entries)]
        if token is not None: fields.append(plain(token.encode("latin-1")))
        wire_read = segment("HKSAL", 2, version or original_version, fields)[0]
    wire = end_frame(dialogue, message, [(wire_read, None)])
    read_continuation_vectors.append({"name": name, "previous": prior, "requestBase64": base64.b64encode(wire).decode(),
        "expectedBankMessageNumber": bank_message, "issues": list(issues), "requestIssues": list(request_issues)})


read_continuation_vector("balance-seven-second-page")
read_continuation_vector("balance-six-second-page", source=22)
read_continuation_vector("balance-eight-second-page", source=23)
read_continuation_vector("national-balance-eight", source=25)
read_continuation_vector("entry-count-preserved", source=26)
read_continuation_vector("one-step-context", source=16)
read_continuation_vector("tan-six-context", source=17)
read_continuation_vector("decoupled-context", source=12)
read_continuation_vector("escaped-dialogue", source=27, dialogue="D+:'?@ü")
read_continuation_vector("escaped-token", previous="execution-and-partial", token="P+:'?@ü")
read_continuation_vector("token-whitespace-preserved", source=0, returned_token=" PUBLIC-NEXT ", token=" PUBLIC-NEXT ")
read_continuation_vector("maximum-token-length", source=0, returned_token="?" * 35, token="?" * 35)
read_continuation_vector("equivalent-empty-account-components", trailing_empty=True)
read_continuation_vector("missing-token", ["TokenMismatch"], token=None)
read_continuation_vector("empty-token", ["TokenMismatch"], token="")
read_continuation_vector("changed-token", ["TokenMismatch"], token="OTHER")
read_continuation_vector("token-case-is-significant", ["TokenMismatch"], token="public-next")
read_continuation_vector("token-must-not-be-trimmed", ["TokenMismatch"], source=0, returned_token=" PUBLIC-NEXT ", token="PUBLIC-NEXT")
read_continuation_vector("foreign-dialogue", ["DialogueMismatch", "RequestNeedsReview"], ["ParameterDialogueMismatch"], dialogue="FOREIGN")
read_continuation_vector("repeated-client-counter", ["CounterMismatch"], message=2)
read_continuation_vector("skipped-client-counter", ["CounterMismatch"], message=4)
read_continuation_vector("repeated-bank-counter", ["CounterMismatch"], bank_message=2)
read_continuation_vector("skipped-bank-counter", ["CounterMismatch"], bank_message=4)
read_continuation_vector("changed-version", ["OperationMismatch", "RequestNeedsReview"], ["CapabilityScopeMismatch"], version=8)
read_continuation_vector("changed-iban", ["AccountMismatch", "RequestNeedsReview"], ["RequestAccountMismatch"], account_edit=(0, b"FOREIGN-IBAN"))
read_continuation_vector("changed-bic", ["AccountMismatch"], account_edit=(1, b"FOREIGN-BIC"))
for index, field in enumerate(["number", "subaccount", "country", "institution"]):
    read_continuation_vector("changed-national-" + field, ["AccountMismatch", "RequestNeedsReview"], ["RequestAccountMismatch"], source=22,
                             account_edit=(index, b"276" if index == 2 else b"FOREIGN"))
read_continuation_vector("removed-national-components", ["AccountMismatch"], source=25, remove_national=True)
read_continuation_vector("changed-entry-count", ["OptionsMismatch"], source=26, maximum=42)
read_continuation_vector("removed-entry-count", ["OptionsMismatch"], source=26, maximum=None)
read_continuation_vector("new-entry-count-not-advertised", ["OptionsMismatch", "RequestNeedsReview"], ["EntryCountNotAdvertised"], maximum=42)
read_continuation_vector("all-accounts-not-allowed", ["OptionsMismatch", "UnsupportedAccountScope", "RequestNeedsReview"], ["UnsupportedAccountScope"], all_accounts=True)
read_continuation_vector("changed-to-discovery", ["OperationMismatch", "TokenMismatch", "RequestNeedsReview"], ["CapabilityScopeMismatch"], source=22, discovery=True)
read_continuation_vector("execution-is-not-partial", ["PartialRequired", "TokenMismatch"], previous="balance-seven")
read_continuation_vector("unavailable-is-not-partial", ["PartialRequired", "TokenMismatch"], previous="balance-unavailable")
read_continuation_vector("review-token-cannot-qualify", ["PreviousNeedsReview", "PartialRequired"], previous="foreign-message-retains-token")
read_continuation_vector("invalid-partial-without-token", ["PreviousNeedsReview", "PartialRequired", "TokenMismatch"], previous="partial-without-token")
read_continuation_vector("combined-next-request-mismatches", ["TokenMismatch", "DialogueMismatch", "CounterMismatch", "AccountMismatch", "RequestNeedsReview"],
                         ["ParameterDialogueMismatch", "RequestAccountMismatch"], token="OTHER", dialogue="FOREIGN", message=4, bank_message=4, account_edit=(0, b"FOREIGN-IBAN"))
target.with_name("pin-tan-read-continuation-context-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_continuation_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_continuation_vectors)} public synthetic balance-continuation context vectors.")

read_continuation_signature_vectors = []


def read_continuation_signature_vector(name, source=0, issues=(), control="PAGE2-REF", expected_control=None,
                                       selection_profile=None, selection_function=None, selection_version=None,
                                       operation="TanReportedRequired", **header_options):
    continuation = read_continuation_vectors[source]
    origin = continuation["previous"]["candidate"]["context"]["initialization"]["context"]
    options = {"profile": origin["profileVersion"], "function": str(origin["securityFunction"]), "control": control, **header_options}
    signature_header_vector(name, **options)
    header = signature_header_vectors.pop()
    all_issues = (["ContinuationNeedsReview"] if continuation["issues"] else []) + list(issues)
    read_continuation_signature_vectors.append({"name": name, "continuation": continuation, "header": header,
        "controlReference": control if expected_control is None else expected_control,
        "selectionProfile": origin["profileVersion"] if selection_profile is None else selection_profile,
        "selectionFunction": origin["securityFunction"] if selection_function is None else selection_function,
        "selectionVersion": origin["tanSegmentVersion"] if selection_version is None else selection_version,
        "issues": all_issues, "operationEvidence": "Unknown" if all_issues else operation})


for index, original in enumerate(read_continuation_vectors):
    read_continuation_signature_vector(original["name"], source=index)
read_continuation_signature_vector("foreign-user", issues=["IdentityMismatch"], user="FOREIGN")
read_continuation_signature_vector("foreign-institution", issues=["IdentityMismatch"], institution="FOREIGN")
read_continuation_signature_vector("foreign-country", issues=["IdentityMismatch"], country="276")
read_continuation_signature_vector("foreign-system", issues=["SystemMismatch"], system="FOREIGN")
read_continuation_signature_vector("zero-system", issues=["SystemMismatch"], system="0")
read_continuation_signature_vector("unknown-system", issues=["SystemMismatch"], system="unbekannt")
read_continuation_signature_vector("changed-header-function", issues=["SelectionMismatch"], function="920")
read_continuation_signature_vector("changed-selection-function", issues=["SelectionMismatch"], selection_function=920)
read_continuation_signature_vector("changed-both-functions", issues=["SelectionMismatch"], function="920", selection_function=920)
read_continuation_signature_vector("changed-selected-tan-version", issues=["SelectionMismatch"], selection_version=6)
read_continuation_signature_vector("changed-profile-and-header", issues=["SelectionMismatch"], profile=1, function="999", selection_profile=1, selection_function=999)
read_continuation_signature_vector("header-profile-mismatch", issues=["SelectionMismatch"], profile=1, function="999")
read_continuation_signature_vector("control-mismatch", issues=["ControlMismatch"], expected_control="OTHER-REF")
read_continuation_signature_vector("header-position-mismatch", issues=["HeaderRoleNeedsReview"], number=3)
read_continuation_signature_vector("supplier-role-mismatch", issues=["HeaderRoleNeedsReview"], role=3)
read_continuation_signature_vector("security-party-mismatch", issues=["HeaderRoleNeedsReview"], party=2)
read_continuation_signature_vector("escaped-control", control="P+:'?@ü")
read_continuation_signature_vector("maximum-control", control="?" * 14)
read_continuation_signature_vector("reused-control-is-not-replay-proof", control="NEXT-REF")
read_continuation_signature_vector("timestamp-absent", timestamp_empty=True)
read_continuation_signature_vector("date-only", date="20260910", time="")
read_continuation_signature_vector("maximum-security-reference", reference="9999999999999999")
read_continuation_signature_vector("zero-security-reference", reference="0")
read_continuation_signature_vector("observed-key-and-fillers", key_number=7, key_version=8, hash_code="3", signature="999")
read_continuation_signature_vector("optional-empty-fields", optional_empty=True)
read_continuation_signature_vector("combined-continuation-and-header-issues", source=40, issues=["IdentityMismatch", "SystemMismatch", "SelectionMismatch", "ControlMismatch", "HeaderRoleNeedsReview"],
                                   user="FOREIGN", system="FOREIGN", function="920", expected_control="OTHER", role=4)
for name, source, operation in [("missing-pin-bounds-retained", 5, "TanReportedRequired"), ("reported-n-does-not-waive-sca", 18, "TanReportedNotRequired")]:
    read_continuation_vector(name, source=source)
    read_continuation_signature_vector(name, source=len(read_continuation_vectors) - 1, operation=operation)
    read_continuation_vectors.pop()
target.with_name("pin-tan-read-continuation-signature-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_continuation_signature_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_continuation_signature_vectors)} public synthetic continuation signature-context vectors.")

read_continuation_pin_vectors = []


def read_continuation_pin_vector(name, source=0, pin=None, result=None, copies=None):
    context = read_continuation_signature_vectors[source]
    value = base64.b64decode(context["continuation"]["previous"]["candidate"]["pinBase64"]) if pin is None else pin
    if result is None:
        result = "ContextNeedsReview" if context["issues"] else "CredentialRequirementsNeedReview" if context["name"] == "missing-pin-bounds-retained" else "Written"
    if copies is None: copies = 1 if result == "Written" else 0
    wire = segment("HNSHA", 4, 2, [plain(context["controlReference"].encode("latin-1")), plain(b""), group([value])])[0] if result == "Written" else None
    read_continuation_pin_vectors.append({"name": name, "context": context, "pinBase64": base64.b64encode(value).decode(),
        "result": result, "pinCopies": copies, "wireBase64": None if wire is None else base64.b64encode(wire).decode()})


for index, original in enumerate(read_continuation_signature_vectors):
    read_continuation_pin_vector(original["name"], source=index)
read_continuation_pin_vector("pin-too-short", pin=b"1234", result="CredentialRequirementsNeedReview", copies=1)
read_continuation_pin_vector("pin-too-long", pin=b"1234567890123", result="CredentialRequirementsNeedReview", copies=1)
read_continuation_pin_vector("pin-minimum", pin=b"12345")
read_continuation_pin_vector("pin-maximum", pin=b"123456789012")
read_continuation_pin_vector("pin-whitespace-and-delimiters", pin=b" +:'?@ ")
read_continuation_pin_vector("pin-latin-one", pin="ÄÖÜéß".encode("latin-1"))
read_continuation_pin_vector("pin-control-rejected", pin=b"1234\n", result="InvalidCredentialText", copies=1)
read_continuation_pin_vector("pin-del-rejected", pin=b"1234\x7f", result="InvalidCredentialText", copies=1)
read_continuation_pin_vector("pin-nbsp-rejected", pin=b"1234\xa0", result="InvalidCredentialText", copies=1)
for name, source, pin, result in [("missing-maximum", 6, b"12345", "CredentialRequirementsNeedReview"),
                                  ("missing-both-bounds", 7, b"12345", "CredentialRequirementsNeedReview"),
                                  ("maximum-pin", 4, b"1" * 99, "Written"), ("maximum-escaped-pin-and-control", 4, b"?" * 99, "Written")]:
    read_continuation_vector(name, source=source)
    read_continuation_signature_vector(name, source=len(read_continuation_vectors) - 1, control="?" * 14 if name == "maximum-escaped-pin-and-control" else "PAGE2-REF")
    read_continuation_pin_vector(name, source=len(read_continuation_signature_vectors) - 1, pin=pin, result=result)
    read_continuation_signature_vectors.pop(); read_continuation_vectors.pop()
target.with_name("pin-tan-read-continuation-pin-trailer-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_continuation_pin_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_continuation_pin_vectors)} public synthetic continuation PIN trailer vectors.")

read_continuation_request_vectors = []
for source in read_continuation_pin_vectors:
    assembled = None
    if source["result"] == "Written":
        context = source["context"]
        original = public_text_segments(base64.b64decode(context["continuation"]["requestBase64"]))
        header = base64.b64decode(context["header"]["wireBase64"])
        role, fields = original[1].split(b"+", 1)
        code, _, version = role.split(b":")
        request = code + b":3:" + version + b"+" + fields
        trailer = base64.b64decode(source["wireBase64"])
        assembled = original[0] + header + request + trailer + b"HNHBS:5:1+3'"
        assembled = assembled[:10] + f"{len(assembled):012d}".encode() + assembled[22:]
    read_continuation_request_vectors.append({**source,
        "wireBase64": None if assembled is None else base64.b64encode(assembled).decode()})

target.with_name("pin-tan-read-continuation-request-assembly-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_continuation_request_vectors}, indent=2) + "\n", encoding="utf-8")
print(f"Generated {len(read_continuation_request_vectors)} public synthetic continuation request-assembly vectors.")

read_continuation_envelope_vectors = []
for source in read_continuation_request_vectors:
    header = source["context"]["header"]
    profile = header["profileVersion"]
    read_header = public_text_segments(base64.b64decode(source["context"]["continuation"]["requestBase64"]))[1].split(b"+", 1)[0].split(b":")
    payload = wire = None
    if source["result"] == "Written":
        parts = public_text_segments(base64.b64decode(source["wireBase64"]))
        payload = b"".join(parts[1:-1])
        stamp = [b"1"]
        if header["securityDate"] is not None:
            stamp.append(header["securityDate"].encode())
            if header["securityTime"] is not None: stamp.append(header["securityTime"].encode())
        security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", header["systemId"].encode("latin-1")]), group(stamp),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([header["countryCode"].encode(), header["institutionId"].encode("latin-1"), header["userId"].encode("latin-1"), b"V", b"0", b"0"]), plain(b"0")])[0]
        wire = parts[0] + security + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0] + parts[-1]
        wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]
    read_continuation_envelope_vectors.append({**source, "candidateMatches": not source["context"]["issues"],
        "readCode": read_header[0].decode(), "readVersion": int(read_header[2]), "profileVersion": profile,
        "plainWireBase64": source["wireBase64"], "wireBase64": None if wire is None else base64.b64encode(wire).decode(),
        "payloadBase64": None if payload is None else base64.b64encode(payload).decode()})

target.with_name("pin-tan-read-continuation-request-envelope-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_continuation_envelope_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(read_continuation_envelope_vectors)} public synthetic continuation envelope vectors.")

continuation_response_binding_vectors = []
continuation_candidates = {v["name"]: v for v in read_continuation_envelope_vectors}


def continuation_response_binding_vector(name, issues=(), source="balance-seven-second-page", **options):
    candidate = continuation_candidates[source]
    assert candidate["candidateMatches"]
    read_response_binding_vector(name, issues, candidate_override=candidate,
                                 **{"message": 3, "reference_message": 3, **options})
    continuation_response_binding_vectors.append(read_response_binding_vectors.pop())


for name, source in [("balance-seven-reference-three", "balance-seven-second-page"),
                     ("balance-six-reference-three", "balance-six-second-page"),
                     ("balance-eight-reference-three", "balance-eight-second-page"),
                     ("national-balance-eight", "national-balance-eight"),
                     ("one-step-profile", "one-step-context"), ("tan-version-six", "tan-six-context"),
                     ("decoupled-pin-candidate", "decoupled-context")]:
    continuation_response_binding_vector(name, source=source)
continuation_response_binding_vector("escaped-dialogue", source="escaped-dialogue", dialogue="D+:'?@ü")
for name in ["entry-count-preserved", "escaped-token", "maximum-token-length", "token-whitespace-preserved"]:
    continuation_response_binding_vector(name, source=name)
continuation_response_binding_vector("missing-pin-bounds-candidate", source="missing-pin-bounds-retained")
continuation_response_binding_vector("old-unsigned-data-reference-two", ["SegmentRoleMismatch"], data_reference=2)
continuation_response_binding_vector("missing-data-reference", ["MissingSegmentReference", "SegmentRoleMismatch"], data_reference=None)
continuation_response_binding_vector("unknown-data-reference", ["UnknownSegmentReference", "SegmentRoleMismatch"], data_reference=997)
for reference in [1, 998, 999, 2, 3, 4, 5]:
    continuation_response_binding_vector(f"error-on-role-{reference}", reports=[(reference, "9050")], no_data=True)
continuation_response_binding_vector("unknown-reply-reference", ["UnknownSegmentReference"], reports=[(997, "9050")], no_data=True)
continuation_response_binding_vector("signature-success-is-not-read-execution", reports=[(2, "0020")], no_data=True)
continuation_response_binding_vector("message-abort", message_code="9800", reports=[], no_data=True)
continuation_response_binding_vector("message-receipt-without-data", reports=[], no_data=True)
continuation_response_binding_vector("unavailable-report", reports=[(3, "3010")], no_data=True)
continuation_response_binding_vector("pending-report", reports=[(3, "0030")], no_data=True)
continuation_response_binding_vector("partial-report-needs-semantics", reply_fields={3: [group([b"3040", b"", b"PUBLIC-PARTIAL", b"PAGE-THREE"])]})
continuation_response_binding_vector("repeated-token-needs-semantics", reply_fields={3: [group([b"3040", b"", b"PUBLIC-PARTIAL", b"PUBLIC-NEXT"])]})
continuation_response_binding_vector("missing-envelope", ["MissingEnvelope"], wrapped=False)
continuation_response_binding_vector("wrong-profile", ["ProfileMismatch"], profile=1)
continuation_response_binding_vector("old-bank-counter", ["MessageMismatch"], message=2)
continuation_response_binding_vector("future-bank-counter", ["MessageMismatch"], message=4)
continuation_response_binding_vector("old-client-reference", ["MessageMismatch"], reference_message=2)
continuation_response_binding_vector("future-client-reference", ["MessageMismatch"], reference_message=4)
continuation_response_binding_vector("first-page-counters", ["MessageMismatch"], message=2, reference_message=2)
continuation_response_binding_vector("wrong-dialogue", ["MessageMismatch"], dialogue="FOREIGN")
continuation_response_binding_vector("wrong-reference-dialogue", ["MessageMismatch"], reference_dialogue="FOREIGN")
continuation_response_binding_vector("missing-message-reference", ["MessageMismatch"], omit_reference=True)
continuation_response_binding_vector("zero-dialogue", ["MessageMismatch"], dialogue="0")
continuation_response_binding_vector("padded-dialogue", ["MessageMismatch"], dialogue="SYNTHETIC ")
continuation_response_binding_vector("discovery-data-for-balance", ["SegmentRoleMismatch"], data_code="HISPA", data_version=1)
continuation_response_binding_vector("future-read-version", ["UninterpretedData"], data_version=99)
continuation_response_binding_vector("known-version-needs-semantic-comparison", data_version=8)
continuation_response_binding_vector("unknown-data-code", ["UninterpretedData"], extra=("HIZZZ", 1, 3))
continuation_response_binding_vector("unexpected-parameter-data", ["UninterpretedData"], extra=("HIBPA", 3, 3))
continuation_response_binding_vector("unknown-data-missing-reference", ["UninterpretedData", "MissingSegmentReference"], extra=("HIZZZ", 1, None))
continuation_response_binding_vector("foreign-envelope-identity-needs-semantics", foreign_identity=True)
continuation_response_binding_vector("foreign-account-needs-semantics", foreign_account=True)
continuation_response_binding_vector("duplicate-data-needs-semantics", duplicate=True)
continuation_response_binding_vector("opaque-payload-needs-schema-validation", opaque=True)
continuation_response_binding_vector("conflicting-status-needs-semantics", reports=[(3, "9050")])
continuation_response_binding_vector("combined-scope-and-role-failures", ["MissingEnvelope", "MessageMismatch", "UnknownSegmentReference", "SegmentRoleMismatch", "UninterpretedData"],
                                     wrapped=False, message=2, data_reference=997, data_version=99)
target.with_name("pin-tan-read-continuation-response-binding-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": continuation_response_binding_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(continuation_response_binding_vectors)} public synthetic continuation response-binding vectors.")

continuation_semantic_vectors = []


def continuation_semantic_vector(name, read_issues=(), issues=(), source="balance-seven-second-page", **options):
    candidate = continuation_candidates[source]
    assert candidate["candidateMatches"]
    read_semantic_vector(name, read_issues, issues, candidate_override=candidate,
                         national_account=source == "national-balance-eight",
                         **{"message": 3, "reference_message": 3, **options})
    continuation_semantic_vectors.append(read_semantic_vectors.pop())


continuation_semantic_vector("balance-seven")
continuation_semantic_vector("balance-six", source="balance-six-second-page")
continuation_semantic_vector("balance-eight", source="balance-eight-second-page")
continuation_semantic_vector("one-step-profile", source="one-step-context")
continuation_semantic_vector("tan-six-profile", source="tan-six-context")
continuation_semantic_vector("decoupled-response-observation", source="decoupled-context")
continuation_semantic_vector("escaped-dialogue", source="escaped-dialogue", dialogue="D+:'?@ü")
continuation_semantic_vector("national-balance-eight", source="national-balance-eight")
continuation_semantic_vector("separate-optional-values", source="balance-eight-second-page", optional=True)
continuation_semantic_vector("real-zero-balance", amount=b"0,")
continuation_semantic_vector("balance-unavailable", replies=("3010",), no_data=True, outcome="UnavailableReported")
continuation_semantic_vector("execution-and-unavailable", replies=("0020", "3010"), no_data=True, outcome="UnavailableReported")
continuation_semantic_vector("unavailable-empty-parameter", replies=("3010",), parameters=[b""], no_data=True, outcome="UnavailableReported")
continuation_semantic_vector("partial-balance", replies=("3040",), token="PAGE-THREE", outcome="PartialReported")
continuation_semantic_vector("execution-and-partial", replies=("0020", "3040"), token="P+:'?@ü", outcome="PartialReported")
continuation_semantic_vector("unavailable-with-zero", ["AvailabilityConflict"], replies=("3010",), amount=b"0,")
continuation_semantic_vector("unavailable-and-partial", ["AvailabilityConflict"], replies=("3010", "3040"), token="PAGE-THREE", no_data=True)
continuation_semantic_vector("missing-report", ["MissingReport"], no_data=True)
continuation_semantic_vector("duplicate-reports", ["DuplicateReport"], duplicate=True)
continuation_semantic_vector("foreign-iban", ["ResponseAccountMismatch"], foreign_account=True)
continuation_semantic_vector("foreign-national-account", ["ResponseAccountMismatch"], source="balance-six-second-page", foreign_account=True)
continuation_semantic_vector("foreign-bic", ["ResponseAccountMismatch"], foreign_bic=True)
continuation_semantic_vector("wrong-version", ["VersionMismatch"], data_version=8)
continuation_semantic_vector("wrong-account-currency", ["CurrencyMismatch"], currency=b"USD")
continuation_semantic_vector("conflicting-amount-currency", ["CurrencyMismatch"], booked_currency=b"USD")
continuation_semantic_vector("unhandled-due-date", ["DueDateNeedsReview"], due=True)
continuation_semantic_vector("foreign-envelope-identity", issues=["EnvelopeIdentityMismatch"], foreign_identity=True)
continuation_semantic_vector("missing-envelope", issues=["BindingNeedsReview"], wrapped=False)
continuation_semantic_vector("wrong-profile", issues=["BindingNeedsReview"], profile=1)
continuation_semantic_vector("foreign-message", issues=["BindingNeedsReview"], dialogue="FOREIGN")
continuation_semantic_vector("foreign-message-retains-token", issues=["BindingNeedsReview"], dialogue="FOREIGN", replies=("3040",), token="PAGE-THREE")
continuation_semantic_vector("old-data-reference", issues=["BindingNeedsReview"], data_reference=2)
continuation_semantic_vector("unknown-data", ["UnexpectedReport"], issues=["BindingNeedsReview"], extra=("HIZZZ", 1, 3))
continuation_semantic_vector("future-data-version", ["UnexpectedReport", "MissingReport"], issues=["BindingNeedsReview"], data_version=99)
continuation_semantic_vector("security-success-not-read-success", ["StatusNeedsReview"], reports=[(2, "0020")], replies=())
continuation_semantic_vector("extra-security-success", ["StatusNeedsReview"], reports=[(2, "0020"), (3, "0020")])
continuation_semantic_vector("security-unavailable-not-read-unavailable", ["ResponseNeedsReview", "StatusNeedsReview", "MissingReport"], reports=[(2, "3010")], replies=(), no_data=True)
continuation_semantic_vector("read-error", ["ResponseNeedsReview", "StatusNeedsReview", "MissingReport"], replies=("9210",), no_data=True)
continuation_semantic_vector("pending", ["StatusNeedsReview"], replies=("0030",))
continuation_semantic_vector("message-abort", ["ResponseNeedsReview", "StatusNeedsReview", "MissingReport"], message_code="9800", replies=(), no_data=True)
continuation_semantic_vector("missing-scoped-status", ["StatusNeedsReview"], replies=())
continuation_semantic_vector("element-specific-execution", ["StatusNeedsReview"], element=b"1")
continuation_semantic_vector("execution-parameter", ["StatusNeedsReview"], parameters=[b"PUBLIC-EXTRA"])
continuation_semantic_vector("duplicate-execution", ["ResponseNeedsReview", "StatusNeedsReview"], replies=("0020", "0020"))
continuation_semantic_vector("invalid-unavailable-parameter", ["StatusNeedsReview", "MissingReport"], replies=("3010",), parameters=[b"PUBLIC-EXTRA"], no_data=True)
continuation_semantic_vector("partial-without-token", ["ContinuationNeedsReview"], replies=("3040",))
continuation_semantic_vector("partial-extra-parameter", ["ContinuationNeedsReview"], replies=("3040",), parameters=[b"PAGE-THREE", b"EXTRA"])
for index, name in enumerate(["country", "bank", "user", "system"]):
    identity = [b"280", b"PUBLIC-BANK", b"PUBLIC-USER", b"PUBLIC-SYSTEM"]
    identity[index] = b"276" if index == 0 else b"FOREIGN"
    continuation_semantic_vector("foreign-envelope-" + name, issues=["EnvelopeIdentityMismatch"], envelope_identity=identity)
continuation_semantic_vector("partial-trailing-empty-parameter", replies=("3040",), parameters=[b"PAGE-THREE", b""], outcome="PartialReported")

continuation_semantic_vector("entry-count-preserved", source="entry-count-preserved")
continuation_semantic_vector("maximum-request-token", source="maximum-token-length")
continuation_semantic_vector("missing-pin-bounds-still-observations", source="missing-pin-bounds-retained")
continuation_semantic_vector("first-page-counters", issues=["BindingNeedsReview"], message=2, reference_message=2)
continuation_semantic_vector("future-bank-counter", issues=["BindingNeedsReview"], message=4)
continuation_semantic_vector("future-client-reference", issues=["BindingNeedsReview"], reference_message=4)
continuation_semantic_vector("second-page-different-amount", amount=b"2,")
continuation_semantic_vector("repeated-token-observation", replies=("3040",), token="PUBLIC-NEXT", outcome="PartialReported")
continuation_semantic_vector("token-whitespace-preserved", replies=("3040",), token=" PAGE-THREE ", outcome="PartialReported")
continuation_semantic_vector("maximum-returned-token", replies=("3040",), token="?" * 35, outcome="PartialReported")
for name, token in [("overlong-token", "?" * 36), ("control-token", "PAGE\nTHREE"), ("empty-token", "")]:
    continuation_semantic_vector(name, ["ContinuationNeedsReview"], replies=("3040",), token=token)
    continuation_semantic_vectors[-1]["token"] = None
    if name in ("control-token", "overlong-token"): continuation_semantic_vectors[-1]["parseError"] = "InvalidResponse"
continuation_semantic_vector("partial-without-report", ["MissingReport"], replies=("3040",), token="PAGE-THREE", no_data=True)
continuation_semantic_vector("duplicate-partial", ["StatusNeedsReview"], replies=("3040", "3040"), token="PAGE-THREE")
continuation_semantic_vectors[-1]["token"] = None
continuation_semantic_vector("foreign-envelope-retains-token", issues=["EnvelopeIdentityMismatch"], foreign_identity=True, replies=("3040",), token="PAGE-THREE")
continuation_semantic_vector("discovery-data-for-balance", ["UnexpectedReport", "MissingReport", "VersionMismatch", "ResponseAccountMismatch"],
                             issues=["BindingNeedsReview"], data_code="HISPA", data_version=1)
for index, name in enumerate(["number", "subaccount", "country", "institution"]):
    account = [b"PUBLIC-IBAN", b"PUBLIC-BIC", b"PUBLIC-001", b"", b"280", b"PUBLIC-BANK"]
    account[index + 2] = b"276" if index == 2 else b"FOREIGN"
    continuation_semantic_vector("changed-national-" + name, ["ResponseAccountMismatch"], source="national-balance-eight", account_override=account)
continuation_semantic_vector("missing-national-components", ["ResponseAccountMismatch"], source="national-balance-eight", account_override=[b"PUBLIC-IBAN", b"PUBLIC-BIC"])

target.with_name("pin-tan-read-continuation-semantics-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": continuation_semantic_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(continuation_semantic_vectors)} public synthetic continuation semantic vectors.")

continuation_attempt_vectors = []
continuation_semantic_by_name = {v["name"]: v for v in continuation_semantic_vectors}


def continuation_attempt_trace(name, steps, source="balance-seven", responses=None):
    selected = continuation_semantic_by_name[source]
    incoming = [selected] if responses is None else [continuation_semantic_by_name[n] for n in responses]
    continuation_attempt_vectors.append({"name": name, "candidate": selected["candidate"],
        "responses": [{"wireBase64": v["responseBase64"], "wrapped": v["wrapped"]} for v in incoming], "steps": steps})


continuation_start = owned_init_step("start", "CandidateRecorded", "AwaitingResponse")
continuation_done = owned_init_step("response", "ExecutionReported", "ExecutionReported", handled=1, evidence=True)
continuation_review = owned_init_step("response", "RejectedForReview", "NeedsReview", handled=1, evidence=True)
continuation_attempt_trace("one-time-execution", [continuation_start, continuation_done,
    owned_init_step("response", "Terminal", "ExecutionReported", handled=1),
    owned_init_step("start", "Terminal", "ExecutionReported", handled=1)])
for name in ["balance-six", "balance-eight", "one-step-profile", "tan-six-profile", "decoupled-response-observation", "escaped-dialogue", "national-balance-eight"]:
    continuation_attempt_trace(name, [continuation_start, continuation_done], source=name)
for source, state in [("partial-balance", "PartialReported"), ("balance-unavailable", "UnavailableReported")]:
    continuation_attempt_trace(source, [continuation_start, owned_init_step("response", state, state, handled=1, evidence=True),
        owned_init_step("response", "Terminal", state, handled=1), owned_init_step("start", "Terminal", state, handled=1)], source=source)
for source in ["foreign-envelope-user", "missing-envelope", "wrong-profile", "wrong-version", "foreign-iban", "read-error", "unknown-data", "security-success-not-read-success"]:
    continuation_attempt_trace(source, [continuation_start, continuation_review, owned_init_step("response", "Terminal", "NeedsReview", handled=1)], source=source)
for source in ["foreign-message", "old-data-reference"]:
    continuation_attempt_trace(source + "-then-valid", [continuation_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse", at=500),
        owned_init_step("response", "ExecutionReported", "ExecutionReported", at=700, handled=1, evidence=True, index=1)], responses=[source, "balance-seven"])
continuation_attempt_trace("deadline-equality", [continuation_start, owned_init_step("response", "Terminal", "TimedOut", at=10000)])
continuation_attempt_trace("mismatch-does-not-renew-deadline", [continuation_start, owned_init_step("response", "ContextMismatch", "AwaitingResponse", at=9999),
    owned_init_step("response", "Terminal", "TimedOut", at=10000, index=1)], responses=["foreign-message", "balance-seven"])
continuation_attempt_trace("second-start-does-not-renew-deadline", [continuation_start, owned_init_step("start", "WrongState", "AwaitingResponse", at=9999),
    owned_init_step("snapshot", "Snapshot", "TimedOut", at=10000)])
continuation_attempt_trace("cancel-pending", [continuation_start, owned_init_step("cancel", "Cancelled", "Cancelled", at=50), owned_init_step("response", "Terminal", "Cancelled", at=50)])
continuation_attempt_trace("stop-ready", [owned_init_step("stop", "Stopped", "Stopped", candidates=0), owned_init_step("start", "Terminal", "Stopped", candidates=0)])
continuation_attempt_trace("dispose-pending", [continuation_start, owned_init_step("dispose", "Stopped", "Stopped"), owned_init_step("response", "Terminal", "Stopped")])
continuation_attempt_trace("clock-regression", [continuation_start, owned_init_step("snapshot", "Snapshot", "ClockInvalid", at=-1)])
target.with_name("pin-tan-read-continuation-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": continuation_attempt_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(continuation_attempt_vectors)} public synthetic continuation attempt traces.")

read_later_continuation_vectors = []


def read_later_continuation_vector(name, issues=(), request_issues=(), previous="partial-balance", source=None,
                                   token="PAGE-THREE", returned_token="PAGE-THREE", dialogue="SYNTHETIC",
                                   message=4, bank_message=4, page_number=3, version=None, maximum="unchanged",
                                   all_accounts=False, account_edit=None, remove_national=False,
                                   trailing_empty=False, discovery=False):
    if source is None:
        prior = continuation_semantic_by_name[previous]
    else:
        continuation_semantic_vector(name + "-prior", source=source, replies=("3040",), token=returned_token,
                                     dialogue=dialogue if source == "escaped-dialogue" else "SYNTHETIC", outcome="PartialReported")
        prior = continuation_semantic_vectors.pop()
    unsigned = public_text_segments(base64.b64decode(prior["candidate"]["context"]["continuation"]["requestBase64"]))[1]
    parts = unsigned[:-1].split(b"+")
    account = parts[1].split(b":")
    original_version = int(parts[0].split(b":")[2])
    if account_edit is not None: account[account_edit[0]] = account_edit[1]
    if remove_national: account = account[:2]
    if trailing_empty: account += [b"", b"", b"", b""]
    if discovery:
        wire_read = segment("HKSPA", 2, 1, [group(account)])[0]
    else:
        entries = parts[3] if len(parts) > 3 else b""
        if maximum != "unchanged": entries = b"" if maximum is None else str(maximum).encode()
        fields = [group(account), plain(b"J" if all_accounts else b"N"), plain(entries)]
        if token is not None: fields.append(plain(token.encode("latin-1")))
        wire_read = segment("HKSAL", 2, version or original_version, fields)[0]
    wire = end_frame(dialogue, message, [(wire_read, None)])
    read_later_continuation_vectors.append({
        "name": name,
        "previous": prior,
        "requestBase64": base64.b64encode(wire).decode(),
        "expectedBankMessageNumber": bank_message,
        "pageNumber": page_number,
        "issues": list(issues),
        "requestIssues": list(request_issues),
    })


read_later_continuation_vector("balance-seven-third-page")
read_later_continuation_vector("balance-six-third-page", source="balance-six-second-page")
read_later_continuation_vector("balance-eight-third-page", source="balance-eight-second-page")
read_later_continuation_vector("national-balance-eight", source="national-balance-eight")
read_later_continuation_vector("entry-count-preserved", source="entry-count-preserved")
read_later_continuation_vector("one-step-context", source="one-step-context")
read_later_continuation_vector("tan-six-context", source="tan-six-context")
read_later_continuation_vector("decoupled-context", source="decoupled-context")
read_later_continuation_vector("escaped-dialogue", source="escaped-dialogue", dialogue="D+:'?@ü")
read_later_continuation_vector("escaped-token", previous="execution-and-partial", token="P+:'?@ü")
read_later_continuation_vector("token-whitespace-preserved", previous="token-whitespace-preserved", token=" PAGE-THREE ")
read_later_continuation_vector("maximum-token-length", previous="maximum-returned-token", token="?" * 35)
read_later_continuation_vector("equivalent-empty-account-components", trailing_empty=True)
read_later_continuation_vector("missing-token", ["TokenMismatch"], token=None)
read_later_continuation_vector("empty-token", ["TokenMismatch"], token="")
read_later_continuation_vector("changed-token", ["TokenMismatch"], token="OTHER")
read_later_continuation_vector("token-case-is-significant", ["TokenMismatch"], token="page-three")
read_later_continuation_vector("token-must-not-be-trimmed", ["TokenMismatch"], previous="token-whitespace-preserved", token="PAGE-THREE")
read_later_continuation_vector("foreign-dialogue", ["DialogueMismatch", "RequestNeedsReview"], ["ParameterDialogueMismatch"], dialogue="FOREIGN")
read_later_continuation_vector("repeated-client-counter", ["CounterMismatch"], message=3)
read_later_continuation_vector("skipped-client-counter", ["CounterMismatch"], message=5)
read_later_continuation_vector("repeated-bank-counter", ["CounterMismatch"], bank_message=3)
read_later_continuation_vector("skipped-bank-counter", ["CounterMismatch"], bank_message=5)
read_later_continuation_vector("changed-version", ["OperationMismatch", "RequestNeedsReview"], ["CapabilityScopeMismatch"], version=8)
read_later_continuation_vector("changed-iban", ["AccountMismatch", "RequestNeedsReview"], ["RequestAccountMismatch"], account_edit=(0, b"FOREIGN-IBAN"))
read_later_continuation_vector("changed-bic", ["AccountMismatch"], account_edit=(1, b"FOREIGN-BIC"))
for index, field in enumerate(["number", "subaccount", "country", "institution"]):
    read_later_continuation_vector("changed-national-" + field, ["AccountMismatch", "RequestNeedsReview"], ["RequestAccountMismatch"], source="balance-six-second-page",
                                  account_edit=(index, b"276" if index == 2 else b"FOREIGN"))
read_later_continuation_vector("removed-national-components", ["AccountMismatch"], source="national-balance-eight", remove_national=True)
read_later_continuation_vector("changed-entry-count", ["OptionsMismatch"], source="entry-count-preserved", maximum=42)
read_later_continuation_vector("removed-entry-count", ["OptionsMismatch"], source="entry-count-preserved", maximum=None)
read_later_continuation_vector("new-entry-count-not-advertised", ["OptionsMismatch", "RequestNeedsReview"], ["EntryCountNotAdvertised"], maximum=42)
read_later_continuation_vector("all-accounts-not-allowed", ["OptionsMismatch", "UnsupportedAccountScope", "RequestNeedsReview"], ["UnsupportedAccountScope"], all_accounts=True)
read_later_continuation_vector("changed-to-discovery", ["OperationMismatch", "TokenMismatch", "RequestNeedsReview"], ["CapabilityScopeMismatch"], source="balance-six-second-page", discovery=True)
read_later_continuation_vector("execution-is-not-partial", ["PartialRequired", "TokenMismatch"], previous="balance-seven")
read_later_continuation_vector("unavailable-is-not-partial", ["PartialRequired", "TokenMismatch"], previous="balance-unavailable")
read_later_continuation_vector("review-token-cannot-qualify", ["PreviousNeedsReview", "PartialRequired"], previous="foreign-message-retains-token")
read_later_continuation_vector("invalid-partial-without-token", ["PreviousNeedsReview", "PartialRequired", "TokenMismatch"], previous="partial-without-token")
read_later_continuation_vector("combined-next-request-mismatches", ["TokenMismatch", "DialogueMismatch", "CounterMismatch", "AccountMismatch", "RequestNeedsReview"],
                              ["ParameterDialogueMismatch", "RequestAccountMismatch"], token="OTHER", dialogue="FOREIGN", message=5, bank_message=5, account_edit=(0, b"FOREIGN-IBAN"))

target.with_name("pin-tan-read-later-continuation-context-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_later_continuation_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(read_later_continuation_vectors)} public synthetic later-continuation context vectors.")

pin_tan_read_refresh_vectors = []


def pin_tan_refresh_step(action, transition, state, at=0, requests=1, pages=0, balances=0, index=0):
    return {
        "action": action,
        "transition": transition,
        "state": state,
        "atMilliseconds": at,
        "requests": requests,
        "pages": pages,
        "balances": balances,
        "index": index,
    }


def pin_tan_refresh_trace(name, steps, initial="balance-seven", continuations=(), responses=None, maximum=16):
    init_cand = read_semantic_by_name[initial]["candidate"]
    cont_cands = [continuation_semantic_by_name[c]["candidate"] for c in continuations]
    if responses is None:
        selected_resps = [read_semantic_by_name[initial]] + [continuation_semantic_by_name[c] for c in continuations]
    else:
        selected_resps = [read_semantic_by_name.get(r) or continuation_semantic_by_name[r] for r in responses]
    pin_tan_read_refresh_vectors.append({
        "name": name,
        "initialCandidate": init_cand,
        "continuationCandidates": cont_cands,
        "responses": [{"wireBase64": v["responseBase64"], "wrapped": v["wrapped"]} for v in selected_resps],
        "maximumPages": maximum,
        "steps": steps,
    })


start_step = pin_tan_refresh_step("start", "CandidateRecorded", "AwaitingResponse", at=0, requests=1, pages=0, balances=0)

pin_tan_refresh_trace("one-execution-and-terminal", [
    start_step,
    pin_tan_refresh_step("response", "ExecutionReported", "ExecutionReported", at=0, requests=1, pages=1, balances=1, index=0),
    pin_tan_refresh_step("response", "Terminal", "ExecutionReported", at=0, requests=1, pages=1, balances=1, index=0),
    pin_tan_refresh_step("start", "Terminal", "ExecutionReported", at=0, requests=1, pages=1, balances=1),
], initial="balance-seven")

pin_tan_refresh_trace("two-page-continuation", [
    start_step,
    pin_tan_refresh_step("response", "PartialAccepted", "WaitingToContinue", at=0, requests=1, pages=1, balances=1, index=0),
    pin_tan_refresh_step("response", "ReplayRejected", "WaitingToContinue", at=0, requests=1, pages=1, balances=1, index=0),
    pin_tan_refresh_step("continue", "CandidateRecorded", "AwaitingResponse", at=0, requests=2, pages=1, balances=1, index=0),
    pin_tan_refresh_step("response", "ReplayRejected", "AwaitingResponse", at=0, requests=2, pages=1, balances=1, index=0),
    pin_tan_refresh_step("response", "ExecutionReported", "ExecutionReported", at=0, requests=2, pages=2, balances=2, index=1),
], initial="partial-balance", continuations=["balance-seven"])

pin_tan_refresh_trace("one-page-unavailable", [
    start_step,
    pin_tan_refresh_step("response", "UnavailableReported", "UnavailableReported", at=0, requests=1, pages=1, balances=0, index=0),
    pin_tan_refresh_step("response", "Terminal", "UnavailableReported", at=0, requests=1, pages=1, balances=0, index=0),
    pin_tan_refresh_step("start", "Terminal", "UnavailableReported", at=0, requests=1, pages=1, balances=0),
], initial="balance-unavailable")

pin_tan_refresh_trace("continuation-unavailable", [
    start_step,
    pin_tan_refresh_step("response", "PartialAccepted", "WaitingToContinue", at=0, requests=1, pages=1, balances=1, index=0),
    pin_tan_refresh_step("continue", "CandidateRecorded", "AwaitingResponse", at=0, requests=2, pages=1, balances=1, index=0),
    pin_tan_refresh_step("response", "UnavailableReported", "UnavailableReported", at=0, requests=2, pages=2, balances=1, index=1),
], initial="partial-balance", continuations=["balance-unavailable"])

pin_tan_refresh_trace("deadline-is-not-extended", [
    start_step,
    pin_tan_refresh_step("response", "PartialAccepted", "WaitingToContinue", at=9000, requests=1, pages=1, balances=1, index=0),
    pin_tan_refresh_step("continue", "CandidateRecorded", "AwaitingResponse", at=9999, requests=2, pages=1, balances=1, index=0),
    pin_tan_refresh_step("response", "Terminal", "TimedOut", at=10000, requests=2, pages=1, balances=1, index=1),
], initial="partial-balance", continuations=["balance-seven"])

pin_tan_refresh_trace("partial-at-page-limit", [
    start_step,
    pin_tan_refresh_step("response", "Terminal", "PageLimitReached", at=0, requests=1, pages=1, balances=1, index=0),
], initial="partial-balance", maximum=1)

pin_tan_refresh_trace("two-page-limit-reached", [
    start_step,
    pin_tan_refresh_step("response", "PartialAccepted", "WaitingToContinue", at=0, requests=1, pages=1, balances=1, index=0),
    pin_tan_refresh_step("continue", "CandidateRecorded", "AwaitingResponse", at=0, requests=2, pages=1, balances=1, index=0),
    pin_tan_refresh_step("response", "Terminal", "PageLimitReached", at=0, requests=2, pages=2, balances=2, index=1),
], initial="partial-balance", continuations=["partial-balance"], maximum=2)

pin_tan_refresh_trace("unrelated-response-keeps-pending", [
    start_step,
    pin_tan_refresh_step("response", "ContextMismatch", "AwaitingResponse", at=500, requests=1, pages=0, balances=0, index=0),
    pin_tan_refresh_step("response", "ExecutionReported", "ExecutionReported", at=700, requests=1, pages=1, balances=1, index=1),
], initial="balance-seven", responses=["foreign-message", "balance-seven"])

pin_tan_refresh_trace("bound-wrong-account-stops", [
    start_step,
    pin_tan_refresh_step("response", "RejectedForReview", "NeedsReview", at=0, requests=1, pages=0, balances=0, index=0),
    pin_tan_refresh_step("response", "Terminal", "NeedsReview", at=0, requests=1, pages=0, balances=0, index=0),
], initial="balance-seven", responses=["foreign-iban"])

pin_tan_refresh_trace("cancel-in-flight", [
    start_step,
    pin_tan_refresh_step("cancel", "Cancelled", "Cancelled", at=50, requests=1, pages=0, balances=0),
    pin_tan_refresh_step("response", "Terminal", "Cancelled", at=50, requests=1, pages=0, balances=0, index=0),
], initial="balance-seven")

pin_tan_refresh_trace("cancel-while-waiting-to-continue", [
    start_step,
    pin_tan_refresh_step("response", "PartialAccepted", "WaitingToContinue", at=0, requests=1, pages=1, balances=1, index=0),
    pin_tan_refresh_step("cancel", "Cancelled", "Cancelled", at=0, requests=1, pages=1, balances=1),
    pin_tan_refresh_step("continue", "Terminal", "Cancelled", at=0, requests=1, pages=1, balances=1, index=0),
], initial="partial-balance", continuations=["balance-seven"])

pin_tan_refresh_trace("stop-before-start", [
    pin_tan_refresh_step("stop", "Stopped", "Stopped", at=0, requests=0, pages=0, balances=0),
    pin_tan_refresh_step("start", "Terminal", "Stopped", at=0, requests=0, pages=0, balances=0),
    pin_tan_refresh_step("response", "Terminal", "Stopped", at=0, requests=0, pages=0, balances=0, index=0),
], initial="balance-seven")

pin_tan_refresh_trace("dispose-pending", [
    start_step,
    pin_tan_refresh_step("dispose", "Stopped", "Stopped", at=0, requests=1, pages=0, balances=0),
    pin_tan_refresh_step("response", "Terminal", "Stopped", at=0, requests=1, pages=0, balances=0, index=0),
], initial="balance-seven")

pin_tan_refresh_trace("clock-regression", [
    start_step,
    pin_tan_refresh_step("snapshot", "Snapshot", "ClockInvalid", at=-1, requests=1, pages=0, balances=0),
], initial="balance-seven")

pin_tan_refresh_trace("mismatch-does-not-renew-deadline", [
    start_step,
    pin_tan_refresh_step("response", "ContextMismatch", "AwaitingResponse", at=9999, requests=1, pages=0, balances=0, index=0),
    pin_tan_refresh_step("response", "Terminal", "TimedOut", at=10000, requests=1, pages=0, balances=0, index=1),
], initial="balance-seven", responses=["foreign-message", "balance-seven"])

pin_tan_refresh_trace("second-start-does-not-renew-deadline", [
    start_step,
    pin_tan_refresh_step("start", "WrongState", "AwaitingResponse", at=9999, requests=1, pages=0, balances=0),
    pin_tan_refresh_step("snapshot", "Snapshot", "TimedOut", at=10000, requests=1, pages=0, balances=0),
], initial="balance-seven")

for profile in ["balance-six", "balance-eight", "national-balance-eight", "tan-six-profile", "one-step-profile", "decoupled-response-observation", "escaped-dialogue"]:
    pin_tan_refresh_trace(profile, [
        start_step,
        pin_tan_refresh_step("response", "ExecutionReported", "ExecutionReported", at=0, requests=1, pages=1, balances=1, index=0),
    ], initial=profile)

target.with_name("pin-tan-read-refresh-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": pin_tan_read_refresh_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(pin_tan_read_refresh_vectors)} public synthetic PIN/TAN read refresh attempt traces.")

pin_tan_all_account_capability_vectors = []


def all_account_capability_context_vector(name, issues=(), source=0, spa_version=1, single="J", national="J",
                                         orders=1, signatures=1, copies=1, future=False, unrelated=False,
                                         permissions=(("HKSPA", 1),), usage=0, accounts=1,
                                         institution="PUBLIC-BANK", reference=4,
                                         account_limit=False, permission_limit=False,
                                         initialization_matches=True, operation="HKSPA",
                                         request_version=1, request_account=False,
                                         bank_protocol=300, dialogue="SYNTHETIC", message_number=2, bank_number=2,
                                         has_spa=True, foreign_bank=False, ambiguous_permission=False,
                                         hipins_spa="N", continuation=None, control_reference="NEXT-REF"):
    origin = json.loads(json.dumps(initialization_requirements_vectors[source]))
    wire = base64.b64decode(origin["responseBase64"])
    prefix, binary = wire.split(b"HNVSD:999:1+@", 1)
    length, rest = binary.split(b"@", 1)
    body = public_text_segments(rest[:int(length)])
    if hipins_spa == "J":
        body = [part.replace(b"HKSPA:N", b"HKSPA:J") for part in body]
    if usage == 1:
        body = [part.replace(b"HIUPA:6:4:4+PUBLIC-USER+2+0", b"HIUPA:6:4:4+PUBLIC-USER+2+1") for part in body]
    if bank_protocol != 300:
        body = [part.replace(b"+1:2:3+300'", f"+1:2:3+{bank_protocol}'".encode()) for part in body]
    bank_name = "OTHER-BANK" if foreign_bank else institution
    for acc_idx in range(accounts):
        acc_num = f"PUBLIC-{acc_idx+1:03d}"
        iban = f"DE8937040044053201300{acc_idx}"
        account_fields = [group([acc_num.encode(), b"", b"280", bank_name.encode()]), plain(iban.encode()), plain(b"PUBLIC-CUSTOMER"),
                          plain(b"1"), plain(b"EUR"), plain(b"Public owner"), plain(b""), plain(b""),
                          group([b"E", b"10,", b"EUR"]) if account_limit else plain(b"")]
        for code, count in permissions:
            elements = [code.encode(), str(count).encode()]
            if permission_limit and code == "HKSPA":
                elements += [b"E", b"10,", b"EUR"]
            account_fields.append(group(elements))
        if ambiguous_permission:
            account_fields.append(group([b"HKSPA", b"1"]))
        body.append(segment("HIUPD", len(body) + 2, 6, account_fields, reference=4)[0])
    if has_spa:
        for _ in range(copies):
            spa_options = [single.encode(), national.encode(), b"N"]
            if spa_version >= 2: spa_options.append(b"N")
            if spa_version == 3: spa_options.append(b"0")
            fields = [plain(str(orders).encode()), plain(str(signatures).encode()), plain(b"0"), group(spa_options)]
            body.append(segment("HISPAS", len(body) + 2, spa_version, fields, reference=reference)[0])
    if future: body.append(segment("HISPAS", len(body) + 2, 99, [plain(b"PUBLIC-OPAQUE")], reference=4)[0])
    if unrelated: body.append(segment("HIXYZ", len(body) + 2, 1, [plain(b"PUBLIC-OPAQUE")], reference=4)[0])
    payload = b"".join(body)
    wire = prefix + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0] + segment("HNHBS", len(body) + 2, 1, [plain(b"1")])[0]
    wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]
    origin["responseBase64"] = base64.b64encode(wire).decode()
    if request_account:
        connection = [b"PUBLIC-001", b"", b"280", b"PUBLIC-BANK"]
        fields = [group(connection)]
    elif operation == "HKSAL":
        request_version = request_version if request_version != 1 else 7
        fields = [group([b"PUBLIC-IBAN", b"PUBLIC-BIC"]), plain(b"N")]
        if continuation:
            fields.extend([plain(b""), plain(continuation.encode())])
    else:
        fields = []
    request_wire = end_frame(dialogue, message_number, [segment(operation, 2, request_version, fields)])
    context = origin["context"]
    header = base64.b64decode(context["headerBase64"]).replace(b"PUBLIC-REF", b"NEXT-REF")
    if control_reference != "NEXT-REF":
        header = header.replace(b"NEXT-REF", control_reference.encode("latin-1"))
    if dialogue != "SYNTHETIC":
        header = header.replace(b"SYNTHETIC", dialogue.encode("latin-1"))
    op_evidence = "TanReportedRequired" if hipins_spa == "J" else ("TanReportedNotRequired" if hipins_spa == "N" else "Unknown")
    pin_tan_all_account_capability_vectors.append({
        "name": name,
        "initialization": origin,
        "initializationMatches": initialization_matches,
        "requestBase64": base64.b64encode(request_wire).decode(),
        "headerBase64": base64.b64encode(header).decode(),
        "expectedBankMessageNumber": bank_number,
        "issues": list(issues),
        "operationEvidence": "Unknown" if issues or not initialization_matches else op_evidence
    })


all_account_capability_context_vector("valid-all-discovery")
all_account_capability_context_vector("valid-all-discovery-tan-required", hipins_spa="J")
all_account_capability_context_vector("valid-single-not-advertised", single="N")
all_account_capability_context_vector("valid-empty-upd-accounts", accounts=0, usage=1)
all_account_capability_context_vector("valid-multiple-upd-accounts", accounts=3)
all_account_capability_context_vector("valid-one-step-initialization", source=1)
all_account_capability_context_vector("valid-tan-version-six", source=2)
all_account_capability_context_vector("valid-nullable-pin-bounds", source=4)
all_account_capability_context_vector("signature-needs-review", ["SignatureNeedsReview", "ParametersNeedReview"], source=10, initialization_matches=False)
all_account_capability_context_vector("signature-control-mismatch", ["SignatureNeedsReview"], control_reference="OTHER-REF")
all_account_capability_context_vector("missing-advertisement", ["MissingAdvertisement"], has_spa=False)
all_account_capability_context_vector("duplicate-advertisement", ["AdvertisementNeedsReview"], copies=2)
all_account_capability_context_vector("zero-orders", ["AdvertisementNeedsReview"], orders=0)
all_account_capability_context_vector("signature-requirements-two", ["SignatureRequirementsNeedReview"], signatures=2)
all_account_capability_context_vector("signature-requirements-zero", ["SignatureRequirementsNeedReview"], signatures=0)
all_account_capability_context_vector("operation-blocked-all-accounts", ["OperationBlocked"], usage=0, permissions=(("HKSAL", 1),))
all_account_capability_context_vector("operation-blocked-empty-accounts", ["OperationBlocked"], accounts=0, usage=0)
all_account_capability_context_vector("limits-need-review-account-limit", ["LimitsNeedReview"], account_limit=True)
all_account_capability_context_vector("limits-need-review-operation-limit", ["LimitsNeedReview"], permission_limit=True)
all_account_capability_context_vector("request-needs-review-selected-account", ["RequestNeedsReview"], request_account=True)
all_account_capability_context_vector("request-needs-review-hksal", ["RequestNeedsReview"], operation="HKSAL")
all_account_capability_context_vector("request-needs-review-continuation", ["SignatureNeedsReview", "RequestNeedsReview"], operation="HKSAL", continuation="CONT-001")
all_account_capability_context_vector("parameters-need-review-protocol-not-300", ["SignatureNeedsReview", "ParametersNeedReview"], bank_protocol=220, initialization_matches=False)
all_account_capability_context_vector("parameters-need-review-dialogue-mismatch", ["SignatureNeedsReview", "ParametersNeedReview"], dialogue="OTHER-DIALOGUE")
all_account_capability_context_vector("parameters-need-review-foreign-bank", ["SignatureNeedsReview", "ParametersNeedReview"], foreign_bank=True, initialization_matches=False)
all_account_capability_context_vector("parameters-need-review-ambiguous-permission", ["ParametersNeedReview"], ambiguous_permission=True)
all_account_capability_context_vector("different-advertised-version", ["MissingAdvertisement"], spa_version=2)

target.with_name("pin-tan-all-account-capability-context-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": pin_tan_all_account_capability_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(pin_tan_all_account_capability_vectors)} public synthetic PIN/TAN all-account capability context vectors.")

all_account_request_envelope_vectors = []


def all_account_request_envelope_vector(name, context_source=0, pin=b"12345", control="NEXT-REF",
                                        result="Written", copies=1, envelope_timestamp=None,
                                        dialogue="SYNTHETIC", custom_context=None):
    if custom_context is not None:
        context = json.loads(json.dumps(custom_context))
    else:
        context = json.loads(json.dumps(pin_tan_all_account_capability_vectors[context_source]))

    origin = context["initialization"]
    if dialogue != "SYNTHETIC":
        wire = base64.b64decode(origin["responseBase64"])
        prefix, binary = wire.split(b"HNVSD:999:1+@", 1)
        length, rest = binary.split(b"@", 1)
        payload = rest[:int(length)]
        prefix = prefix.replace(b"+SYNTHETIC+1+SYNTHETIC:1'", b"+" + text(dialogue.encode("latin-1")) + b"+1+" + text(dialogue.encode("latin-1")) + b":1'")
        tail = rest[int(length) + 1:]
        wire = prefix + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0] + tail
        wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]
        origin["responseBase64"] = base64.b64encode(wire).decode()

        header_bytes = base64.b64decode(context["headerBase64"]).replace(b"SYNTHETIC", dialogue.encode("latin-1"))
        context["headerBase64"] = base64.b64encode(header_bytes).decode()

        request_bytes = end_frame(dialogue, 2, [segment("HKSPA", 2, 1, [])])
        context["requestBase64"] = base64.b64encode(request_bytes).decode()

    if control != "NEXT-REF":
        header_bytes = base64.b64decode(context["headerBase64"]).replace(b"NEXT-REF", text(control.encode("latin-1")))
        context["headerBase64"] = base64.b64encode(header_bytes).decode()

    if envelope_timestamp is not None:
        header_bytes = base64.b64decode(context["headerBase64"]).replace(b"1:20260908:123456", envelope_timestamp.encode())
        context["headerBase64"] = base64.b64encode(header_bytes).decode()

    candidate_matches = (len(context["issues"]) == 0 and context["initializationMatches"])

    request_raw = base64.b64decode(context["requestBase64"])
    req_segments = public_text_segments(request_raw)
    req_op_seg = req_segments[1]
    if b"+" in req_op_seg:
        req_head, req_rest = req_op_seg.split(b"+", 1)
        req_op_elements = req_head.split(b":")
        read_code = req_op_elements[0].decode()
        read_version = int(req_op_elements[2].rstrip(b"'"))
        req_op_elements[1] = b"3"
        renumbered_req_op = b":".join(req_op_elements) + b"+" + req_rest
    else:
        req_head = req_op_seg.rstrip(b"'")
        req_op_elements = req_head.split(b":")
        read_code = req_op_elements[0].decode()
        read_version = int(req_op_elements[2])
        req_op_elements[1] = b"3"
        renumbered_req_op = b":".join(req_op_elements) + b"'"

    profile = context["initialization"]["context"]["profileVersion"]

    wire = payload = plain_wire = None
    if result == "Written":
        header = base64.b64decode(context["headerBase64"])
        trailer = segment("HNSHA", 4, 2, [plain(control.encode("latin-1")), plain(b""), group([pin])])[0]

        assembled = end_frame(dialogue, 2, [(header, None), (renumbered_req_op, None), (trailer, None)])
        plain_wire = base64.b64encode(assembled).decode()

        parts = public_text_segments(assembled)
        payload = b"".join(parts[1:-1])

        stamp_str = envelope_timestamp or "1:20260908:123456"
        stamp = stamp_str.encode().split(b":")

        security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", b"PUBLIC-SYSTEM"]), group(stamp),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([b"280", b"PUBLIC-BANK", b"PUBLIC-USER", b"V", b"0", b"0"]), plain(b"0")])[0]

        envelope_wire = parts[0] + security + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0] + parts[-1]
        envelope_wire = envelope_wire[:10] + f"{len(envelope_wire):012d}".encode() + envelope_wire[22:]
        wire = base64.b64encode(envelope_wire).decode()
        payload_b64 = base64.b64encode(payload).decode()
    else:
        payload_b64 = None

    all_account_request_envelope_vectors.append({
        "name": name,
        "context": context,
        "controlReference": control,
        "pinBase64": base64.b64encode(pin).decode(),
        "result": result,
        "pinCopies": copies,
        "candidateMatches": candidate_matches,
        "readCode": read_code,
        "readVersion": read_version,
        "profileVersion": profile,
        "plainWireBase64": plain_wire,
        "wireBase64": wire,
        "payloadBase64": payload_b64,
    })


# 1. Base 27 capability context vectors
for idx, original in enumerate(pin_tan_all_account_capability_vectors):
    if idx == 7:  # valid-nullable-pin-bounds: requirements have no min/max pin, fails before owner access
        all_account_request_envelope_vector(original["name"], context_source=idx, result="CredentialRequirementsNeedReview", copies=0)
    elif original["issues"] or not original["initializationMatches"]:
        all_account_request_envelope_vector(original["name"], context_source=idx, result="ContextNeedsReview", copies=0)
    else:
        all_account_request_envelope_vector(original["name"], context_source=idx)

# 2. Credential error vectors
all_account_request_envelope_vector("pin-too-short", context_source=0, pin=b"123", result="CredentialRequirementsNeedReview", copies=1)
all_account_request_envelope_vector("pin-too-long", context_source=0, pin=b"1234567890123", result="CredentialRequirementsNeedReview", copies=1)
all_account_request_envelope_vector("pin-control-rejected", context_source=0, pin=b"12\x0134", result="InvalidCredentialText", copies=1)
all_account_request_envelope_vector("pin-nbsp-rejected", context_source=0, pin=bytes([49, 50, 160, 51, 52]), result="InvalidCredentialText", copies=1)

# 3. Escaping & controls
all_account_request_envelope_vector("escaped-assigned-dialogue", context_source=0, dialogue="D+:'?@ü")
all_account_request_envelope_vector("escaped-control", context_source=0, control="R+:'?@ü")
all_account_request_envelope_vector("maximum-control", context_source=0, control="?" * 14)
all_account_request_envelope_vector("pin-delimiters-and-spaces", context_source=0, pin=b"+:'?@ 123")

# 4. Envelope timestamps
all_account_request_envelope_vector("absent-envelope-timestamp", context_source=0, envelope_timestamp="1")
all_account_request_envelope_vector("date-only-envelope-timestamp", context_source=0, envelope_timestamp="1:20260908")

target.with_name("pin-tan-all-account-request-envelope-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": all_account_request_envelope_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(all_account_request_envelope_vectors)} public synthetic PIN/TAN all-account request-envelope vectors.")

all_account_response_vectors = []
all_account_candidates = {v["name"]: v for v in all_account_request_envelope_vectors}


def all_account_response_vector(name, binding_issues=(), all_account_issues=(), response_issues=(),
                                outcome="ExecutionReported", source="valid-all-discovery",
                                wrapped=True, profile=None, dialogue=None, reference_dialogue=None,
                                message=2, reference_message=2, omit_reference=False,
                                data_reference=3, data_code="HISPA", data_version=1,
                                reports=((3, "0020"),), message_code="0010", no_data=False,
                                duplicate=False, extra=None, envelope_identity=None,
                                data_fields=None, reply_fields=None, accounts=None,
                                empty_discovery=False, test_semantics=True):
    candidate = all_account_candidates[source]
    profile = candidate["profileVersion"] if profile is None else profile
    if dialogue is None:
        req_raw = base64.b64decode(candidate["context"]["requestBase64"])
        parts = public_text_segments(req_raw)
        hnhbk_fields = parts[0].split(b"+")
        dial = hnhbk_fields[3].decode("latin-1")
    else:
        dial = dialogue
    ref_dial = dial if reference_dialogue is None else reference_dialogue

    body = [segment("HIRMG", 2, 2, [group([message_code.encode(), b"", b"PUBLIC-REPLY"])])]
    ref_reports = {}
    for ref, reply in reports:
        if ref not in ref_reports:
            ref_reports[ref] = []
        ref_reports[ref].append(reply)

    for ref, rep_list in ref_reports.items():
        if reply_fields is not None and ref in reply_fields:
            fields = reply_fields[ref]
        else:
            fields = [group([rep.encode(), b"", b"PUBLIC-REPLY"]) for rep in rep_list]
        body.append(segment("HIRMS", len(body) + 2, 2, fields, reference=ref))

    if not no_data:
        if data_fields is not None:
            fields = data_fields
        elif data_code == "HISAL":
            fields = [
                group([b"DE89370400440532013000", b"PUBLIC-BIC"]),
                plain(b"PUBLIC-ACCOUNT"),
                plain(b"EUR"),
                group([b"C", b"1,", b"EUR", b"20260910"])
            ]
        elif empty_discovery:
            fields = []
        elif accounts is not None:
            fields = []
            for acc in accounts:
                iban, bic, num, sub, ctry, inst = acc
                sepa = b"J" if iban else b"N"
                fields.append(group([sepa, iban.encode(), bic.encode(), num.encode(), sub.encode(), ctry.encode(), inst.encode()]))
        elif source == "valid-multiple-upd-accounts":
            fields = [
                group([b"J", b"DE89370400440532013000", b"PUBLIC-BIC", b"PUBLIC-001", b"", b"280", b"PUBLIC-BANK"]),
                group([b"J", b"DE89370400440532013001", b"PUBLIC-BIC", b"PUBLIC-002", b"", b"280", b"PUBLIC-BANK"]),
                group([b"J", b"DE89370400440532013002", b"PUBLIC-BIC", b"PUBLIC-003", b"", b"280", b"PUBLIC-BANK"]),
            ]
        else:
            fields = [group([b"J", b"DE89370400440532013000", b"PUBLIC-BIC", b"PUBLIC-001", b"", b"280", b"PUBLIC-BANK"])]

        for _ in range(2 if duplicate else 1):
            body.append(segment(data_code, len(body) + 2, data_version, fields, reference=data_reference))

    if extra is not None:
        extra_code, extra_version, extra_reference = extra
        body.append(segment(extra_code, len(body) + 2, extra_version, [plain(b"PUBLIC-OPAQUE")], reference=extra_reference))

    fields = [plain(b"000000000000"), plain(b"300"), plain(dial.encode("latin-1")), plain(str(message).encode())]
    if not omit_reference:
        fields.append(group([ref_dial.encode("latin-1"), str(reference_message).encode()]))
    ending = segment("HNHBS", len(body) + 2, 1, [plain(str(message).encode())])[0]
    payload = b"".join(p[0] for p in body)

    if wrapped:
        country, bank, user, system = b"280", b"PUBLIC-BANK", b"PUBLIC-USER", b"PUBLIC-SYSTEM"
        if envelope_identity is not None:
            country, bank, user, system = envelope_identity
        security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile).encode()]), plain(b"998"), plain(b"1"),
            group([b"1", b"", system]), group([b"1"]),
            [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
            group([country, bank, user, b"V", b"0", b"0"]), plain(b"0")])[0]
        middle = security + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0]
    else:
        middle = payload

    wire = segment("HNHBK", 1, 3, fields)[0] + middle + ending
    wire = wire[:10] + f"{len(wire):012d}".encode() + wire[22:]

    roles = {1: "MessageHeader", 998: "EnvelopeHeader", 999: "EnvelopeData", 2: "SignatureHeader",
             3: "ReadOperation", 4: "SignatureTrailer", 5: "MessageTrailer"}

    effective_issues = list(all_account_issues)
    if binding_issues:
        effective_issues.append("BindingNeedsReview")
    if response_issues:
        effective_issues.append("ResponseNeedsReview")

    has_matching_refs = len(binding_issues) == 0
    has_matching_ev = len(effective_issues) == 0

    all_account_response_vectors.append({
        "name": name,
        "candidate": candidate,
        "responseBase64": base64.b64encode(wire).decode(),
        "wrapped": wrapped,
        "bindingIssues": list(binding_issues),
        "hasMatchingReferences": has_matching_refs,
        "hasErrors": any(code.startswith("9") for code in [message_code] + [p[1] for p in reports]),
        "issues": effective_issues,
        "responseIssues": list(response_issues),
        "outcome": outcome if has_matching_ev else "NeedsReview",
        "hasMatchingEvidence": has_matching_ev,
        "testSemantics": test_semantics,
        "references": [{"code": p[1]["code"], "reference": p[1]["reference"], "role": roles.get(p[1]["reference"])} for p in body[1:]]
    })


# 1. Clean valid cases
all_account_response_vector("clean-discovery")
all_account_response_vector("clean-multiple-upd-accounts", source="valid-multiple-upd-accounts")
all_account_response_vector("clean-one-step-profile", source="valid-one-step-initialization")
all_account_response_vector("clean-tan-version-six", source="valid-tan-version-six")
all_account_response_vector("clean-escaped-dialogue", source="escaped-assigned-dialogue", dialogue="D+:'?@ü")
all_account_response_vector("clean-unavailable-absent", reports=((3, "3010"),), no_data=True, outcome="UnavailableReported")
all_account_response_vector("clean-unavailable-empty", reports=((3, "3010"),), empty_discovery=True, outcome="UnavailableReported")
all_account_response_vector("clean-unavailable-with-execution", reports=((3, "0020"), (3, "3010")), no_data=True, outcome="UnavailableReported")
all_account_response_vector("clean-unavailable-empty-parameter", reports=((3, "3010"),), reply_fields={3: [group([b"3010", b"", b"PUBLIC-REPLY", b""])]}, no_data=True, outcome="UnavailableReported")

# 2. Binding failures
all_account_response_vector("missing-envelope", binding_issues=["MissingEnvelope"], wrapped=False)
all_account_response_vector("wrong-profile", binding_issues=["ProfileMismatch"], profile=1)
all_account_response_vector("wrong-bank-counter", binding_issues=["MessageMismatch"], message=3)
all_account_response_vector("wrong-client-reference", binding_issues=["MessageMismatch"], reference_message=1)
all_account_response_vector("wrong-dialogue", binding_issues=["MessageMismatch"], dialogue="FOREIGN")
all_account_response_vector("wrong-reference-dialogue", binding_issues=["MessageMismatch"], reference_dialogue="FOREIGN")
all_account_response_vector("missing-message-reference", binding_issues=["MessageMismatch"], omit_reference=True)
all_account_response_vector("zero-dialogue", binding_issues=["MessageMismatch"], dialogue="0")
all_account_response_vector("padded-dialogue", binding_issues=["MessageMismatch"], dialogue="SYNTHETIC ")
all_account_response_vector("balance-data-for-discovery", binding_issues=["SegmentRoleMismatch"], data_code="HISAL", data_version=7, test_semantics=False)
all_account_response_vector("data-references-signature-header", binding_issues=["SegmentRoleMismatch"], data_reference=2, test_semantics=False)
all_account_response_vector("missing-data-reference", binding_issues=["MissingSegmentReference", "SegmentRoleMismatch"], data_reference=None, test_semantics=False)
all_account_response_vector("unknown-data-reference", binding_issues=["UnknownSegmentReference", "SegmentRoleMismatch"], data_reference=997, test_semantics=False)
all_account_response_vector("future-read-version", binding_issues=["UninterpretedData"], data_version=99, test_semantics=False)
all_account_response_vector("unknown-data-code", binding_issues=["UninterpretedData"], extra=("HIZZZ", 1, 3), test_semantics=False)
all_account_response_vector("unexpected-parameter-data", binding_issues=["UninterpretedData"], extra=("HIBPA", 3, 3), test_semantics=False)
all_account_response_vector("unknown-data-missing-reference", binding_issues=["UninterpretedData", "MissingSegmentReference"], extra=("HIZZZ", 1, None), test_semantics=False)
all_account_response_vector("combined-binding-failures",
                            binding_issues=["MissingEnvelope", "MessageMismatch", "UnknownSegmentReference", "SegmentRoleMismatch", "UninterpretedData"],
                            wrapped=False, message=3, data_reference=997, data_version=99, test_semantics=False)

# 3. Envelope identity mismatches
all_account_response_vector("foreign-envelope-country", all_account_issues=["EnvelopeIdentityMismatch"],
                            envelope_identity=[b"276", b"PUBLIC-BANK", b"PUBLIC-USER", b"PUBLIC-SYSTEM"])
all_account_response_vector("foreign-envelope-bank", all_account_issues=["EnvelopeIdentityMismatch"],
                            envelope_identity=[b"280", b"FOREIGN-BANK", b"PUBLIC-USER", b"PUBLIC-SYSTEM"])
all_account_response_vector("foreign-envelope-user", all_account_issues=["EnvelopeIdentityMismatch"],
                            envelope_identity=[b"280", b"PUBLIC-BANK", b"FOREIGN-USER", b"PUBLIC-SYSTEM"])
all_account_response_vector("foreign-envelope-system", all_account_issues=["EnvelopeIdentityMismatch"],
                            envelope_identity=[b"280", b"PUBLIC-BANK", b"PUBLIC-USER", b"FOREIGN-SYSTEM"])

# 4. Status and report issues
all_account_response_vector("security-success-not-read-execution", all_account_issues=["UnmatchedUserAccounts"],
                            response_issues=["StatusNeedsReview", "MissingReport"],
                            reports=((2, "0020"),), no_data=True)
all_account_response_vector("extra-security-success", response_issues=["StatusNeedsReview"],
                            reports=((2, "0020"), (3, "0020")))
all_account_response_vector("security-unavailable-not-read-unavailable", all_account_issues=["UnmatchedUserAccounts"],
                            response_issues=["ResponseNeedsReview", "StatusNeedsReview", "MissingReport"],
                            reports=((2, "3010"),), no_data=True)
all_account_response_vector("read-error", all_account_issues=["UnmatchedUserAccounts"],
                            response_issues=["ResponseNeedsReview", "StatusNeedsReview", "MissingReport"],
                            reports=((3, "9210"),), no_data=True)
all_account_response_vector("read-pending", response_issues=["StatusNeedsReview"],
                            reports=((3, "0030"),))
all_account_response_vector("message-abort", all_account_issues=["UnmatchedUserAccounts"],
                            response_issues=["ResponseNeedsReview", "StatusNeedsReview", "MissingReport"],
                            message_code="9800", reports=(), no_data=True)
all_account_response_vector("missing-scoped-status", response_issues=["StatusNeedsReview"],
                            reports=())
all_account_response_vector("element-specific-execution", response_issues=["StatusNeedsReview"],
                            reply_fields={3: [group([b"0020", b"1", b"PUBLIC-REPLY"])]})
all_account_response_vector("execution-parameter", response_issues=["StatusNeedsReview"],
                            reply_fields={3: [group([b"0020", b"", b"PUBLIC-REPLY", b"EXTRA"])]})
all_account_response_vector("duplicate-execution", response_issues=["ResponseNeedsReview", "StatusNeedsReview"],
                            reports=((3, "0020"), (3, "0020")))
all_account_response_vector("discovery-partial-unsupported", response_issues=["ContinuationNeedsReview"],
                            reports=((3, "3040"),), reply_fields={3: [group([b"3040", b"", b"PUBLIC-REPLY", b"TOKEN"])]})
all_account_response_vector("unavailable-with-accounts-conflict", response_issues=["AvailabilityConflict"],
                            reports=((3, "3010"),))
all_account_response_vector("missing-report-without-unavailable", all_account_issues=["UnmatchedUserAccounts"],
                            response_issues=["MissingReport"],
                            reports=((3, "0020"),), no_data=True)
all_account_response_vector("duplicate-discovery-reports", all_account_issues=["AccountNeedsReview", "UnmatchedUserAccounts"],
                            response_issues=["DuplicateReport"], duplicate=True)
all_account_response_vector("empty-discovery-without-unavailable", all_account_issues=["UnmatchedUserAccounts"],
                            response_issues=["MissingReport"],
                            reports=((3, "0020"),), empty_discovery=True)

# 5. Account matching semantics
all_account_response_vector("unmatched-user-accounts", all_account_issues=["UnmatchedUserAccounts"],
                            source="valid-multiple-upd-accounts",
                            accounts=[("DE89370400440532013000", "PUBLIC-BIC", "PUBLIC-001", "", "280", "PUBLIC-BANK")])
all_account_response_vector("unknown-returned-account", all_account_issues=["AccountNeedsReview", "UnmatchedUserAccounts"],
                            accounts=[("DE89370400440532099999", "PUBLIC-BIC", "FOREIGN-001", "", "280", "PUBLIC-BANK")])
all_account_response_vector("duplicate-returned-identity", all_account_issues=["AccountNeedsReview", "UnmatchedUserAccounts"],
                            accounts=[
                                ("DE89370400440532013000", "PUBLIC-BIC", "PUBLIC-001", "", "280", "PUBLIC-BANK"),
                                ("DE89370400440532013000", "PUBLIC-BIC", "PUBLIC-001", "", "280", "PUBLIC-BANK")
                            ])
all_account_response_vector("institution-mismatch", all_account_issues=["AccountNeedsReview", "UnmatchedUserAccounts"],
                            accounts=[("DE89370400440532013000", "PUBLIC-BIC", "PUBLIC-001", "", "280", "OTHER-BANK")])
all_account_response_vector("identity-conflict", all_account_issues=["AccountNeedsReview", "UnmatchedUserAccounts"],
                            accounts=[("DE89370400440532099999", "PUBLIC-BIC", "PUBLIC-001", "", "280", "PUBLIC-BANK")])
all_account_response_vector("opaque-payload-needs-schema-validation",
                            data_fields=[plain(b"PUBLIC-OPAQUE")], test_semantics=False)

target.with_name("pin-tan-all-account-response-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": all_account_response_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(all_account_response_vectors)} public synthetic PIN/TAN all-account response vectors.")

all_account_attempt_vectors = []
all_account_response_by_name = {v["name"]: v for v in all_account_response_vectors}


def all_account_attempt_trace(name, steps, source="clean-discovery", responses=None):
    selected = all_account_response_by_name[source]
    incoming = [selected] if responses is None else [all_account_response_by_name[n] for n in responses]
    all_account_attempt_vectors.append({
        "name": name,
        "candidate": selected["candidate"],
        "responses": [{"wireBase64": v["responseBase64"], "wrapped": v["wrapped"]} for v in incoming],
        "steps": steps
    })


all_account_start = owned_init_step("start", "CandidateRecorded", "AwaitingResponse")
all_account_done = owned_init_step("response", "ExecutionReported", "ExecutionReported", handled=1, evidence=True)
all_account_review = owned_init_step("response", "RejectedForReview", "NeedsReview", handled=1, evidence=True)
all_account_unavailable = owned_init_step("response", "UnavailableReported", "UnavailableReported", handled=1, evidence=True)

# 1. Clean one-time execution and terminal states
all_account_attempt_trace("one-time-execution", [
    all_account_start, all_account_done,
    owned_init_step("response", "Terminal", "ExecutionReported", handled=1),
    owned_init_step("start", "Terminal", "ExecutionReported", handled=1)
])

# 2. Clean discovery variations
for name in ["clean-multiple-upd-accounts", "clean-one-step-profile", "clean-tan-version-six", "clean-escaped-dialogue"]:
    all_account_attempt_trace(name, [all_account_start, all_account_done], source=name)

# 3. Clean unavailable outcomes
for source in ["clean-unavailable-absent", "clean-unavailable-empty", "clean-unavailable-with-execution"]:
    all_account_attempt_trace(source, [
        all_account_start, all_account_unavailable,
        owned_init_step("response", "Terminal", "UnavailableReported", handled=1),
        owned_init_step("start", "Terminal", "UnavailableReported", handled=1)
    ], source=source)

# 4. Binding / Response review outcomes
for source in [
    "missing-envelope", "wrong-profile", "foreign-envelope-user", "read-error",
    "unmatched-user-accounts", "unknown-returned-account", "duplicate-returned-identity",
    "institution-mismatch", "identity-conflict", "duplicate-discovery-reports"
]:
    all_account_attempt_trace(source, [
        all_account_start, all_account_review,
        owned_init_step("response", "Terminal", "NeedsReview", handled=1)
    ], source=source)

# 5. ContextMismatch: foreign message/roles preserving active AwaitingResponse state
for source in ["wrong-dialogue", "balance-data-for-discovery", "data-references-signature-header"]:
    all_account_attempt_trace(source + "-then-valid", [
        all_account_start,
        owned_init_step("response", "ContextMismatch", "AwaitingResponse", at=500),
        owned_init_step("response", "ExecutionReported", "ExecutionReported", at=700, handled=1, evidence=True, index=1)
    ], responses=[source, "clean-discovery"])

all_account_attempt_trace("mismatch-does-not-renew-deadline", [
    all_account_start,
    owned_init_step("response", "ContextMismatch", "AwaitingResponse", at=9999),
    owned_init_step("response", "Terminal", "TimedOut", at=10000, index=1)
], responses=["wrong-dialogue", "clean-discovery"])

# 6. Deadlines and lifecycle edge cases
all_account_attempt_trace("deadline-equality", [
    all_account_start,
    owned_init_step("response", "Terminal", "TimedOut", at=10000)
])
all_account_attempt_trace("second-start-does-not-renew-deadline", [
    all_account_start,
    owned_init_step("start", "WrongState", "AwaitingResponse", at=9999),
    owned_init_step("snapshot", "Snapshot", "TimedOut", at=10000)
])
all_account_attempt_trace("cancel-pending", [
    all_account_start,
    owned_init_step("cancel", "Cancelled", "Cancelled", at=50),
    owned_init_step("response", "Terminal", "Cancelled", at=50)
])
all_account_attempt_trace("stop-ready", [
    owned_init_step("stop", "Stopped", "Stopped", candidates=0),
    owned_init_step("start", "Terminal", "Stopped", candidates=0)
])
all_account_attempt_trace("dispose-pending", [
    all_account_start,
    owned_init_step("dispose", "Stopped", "Stopped"),
    owned_init_step("response", "Terminal", "Stopped")
])
all_account_attempt_trace("clock-regression", [
    all_account_start,
    owned_init_step("snapshot", "Snapshot", "ClockInvalid", at=-1)
])

target.with_name("pin-tan-all-account-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": all_account_attempt_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(all_account_attempt_vectors)} public synthetic PIN/TAN all-account attempt traces.")

def make_session_closing(message=2, dialogue="SYNTHETIC", code="0100", profile=2, system="PUBLIC-SYSTEM", country="280", institution="PUBLIC-BANK", user="PUBLIC-USER", scoped_code="0020"):
    req_body = end_frame(dialogue, message, [segment("HKEND", 2, 1, [plain(dialogue.encode("latin-1"))])])
    header = segment("HNSHK", 2, 4, [group([b"PIN", str(profile).encode()]), plain(b"900"), plain(b"PUBLIC-CLOSE"),
        plain(b"1"), plain(b"1"), group([b"1", b"", system.encode("latin-1")]),
        plain(b"1"), group([b"1", b"20260909", b"123456"]), group([b"1", b"999", b"1"]),
        group([b"6", b"10", b"16"]), group([country.encode(), institution.encode("latin-1"), user.encode("latin-1"), b"S", b"0", b"0"])])[0]

    def replies(codes, scoped=False):
        return [group([c.encode(), b"" if scoped else b"", b"PUBLIC-CLOSE-REPLY"]) for c in codes]

    body = [segment("HIRMG", 2, 2, replies([code]))]
    if scoped_code:
        body.append(segment("HIRMS", len(body) + 2, 2, replies([scoped_code], True), reference=3))

    fields = [plain(b"000000000000"), plain(b"300"), plain(dialogue.encode("latin-1")), plain(str(message).encode()),
              group([dialogue.encode("latin-1"), str(message).encode()])]
    ending = segment("HNHBS", len(body) + 2, 1, [plain(str(message).encode())])[0]
    payload = b"".join(p[0] for p in body)
    security = segment("HNVSK", 998, 3, [group([b"PIN", str(profile).encode()]), plain(b"998"), plain(b"1"),
        group([b"1", b"", system.encode("latin-1")]), group([b"1"]),
        [scalar(b"2"), scalar(b"2"), scalar(b"13"), scalar(bytes(8), binary=True), scalar(b"5"), scalar(b"1")],
        group([country.encode(), institution.encode("latin-1"), user.encode("latin-1"), b"V", b"0", b"0"]), plain(b"0")])[0]
    middle = security + segment("HNVSD", 999, 1, [[scalar(payload, binary=True)]])[0]
    size = len(segment("HNHBK", 1, 3, fields)[0]) + len(middle) + len(ending)
    fields[0] = plain(f"{size:012d}".encode())
    wire = segment("HNHBK", 1, 3, fields)[0] + middle + ending
    return {
        "requestBase64": base64.b64encode(req_body).decode(),
        "headerBase64": base64.b64encode(header).decode(),
        "expectedBankMessageNumber": message,
        "responseBase64": base64.b64encode(wire).decode(),
        "wrapped": True,
        "code": code,
    }

read_session_attempt_vectors = []
init_req_by_name = {v["name"]: v for v in initialization_requirements_vectors}

def session_step(action, transition, state, stage="Operation", at=0, requests=1, handled=0, pages=0, balances=0, accounts=0, index=None):
    return {
        "action": action,
        "transition": transition,
        "state": state,
        "stage": stage,
        "atMilliseconds": at,
        "requests": requests,
        "handled": handled,
        "pages": pages,
        "balances": balances,
        "accounts": accounts,
        "index": index,
    }

def read_session_attempt_trace(name, steps, init="two-step-hipins", op_type="None",
                               all_account="clean-discovery", balance_initial="balance-seven",
                               balance_continuations=(), closing_msg=None, closing_code="0100",
                               scoped_code="0020", maximum=16, extra_responses=None):
    init_cand = init_req_by_name[init]
    all_acc = all_account_response_by_name[all_account] if op_type == "AllAccount" else None
    bal_init = read_semantic_by_name[balance_initial] if op_type == "BalanceRefresh" else None
    bal_conts = [continuation_semantic_by_name[c] for c in balance_continuations] if op_type == "BalanceRefresh" else []

    # A session must accept the same parameter evidence used to qualify its operation.
    if all_acc is not None:
        init_cand = all_acc["candidate"]["context"]["initialization"]
    elif bal_init is not None:
        init_cand = bal_init["candidate"]["context"]["initialization"]

    if closing_msg is None:
        if op_type == "AllAccount":
            closing_msg = 3
        elif op_type == "BalanceRefresh":
            closing_msg = 2 + 1 + len(balance_continuations)
        else:
            closing_msg = 2

    closing = make_session_closing(message=closing_msg, code=closing_code, scoped_code=scoped_code)
    extras = [] if extra_responses is None else extra_responses

    read_session_attempt_vectors.append({
        "name": name,
        "initialization": init_cand,
        "operationType": op_type,
        "allAccount": all_acc,
        "balanceInitial": bal_init,
        "balanceContinuations": bal_conts,
        "closing": closing,
        "maximumBalancePages": maximum,
        "extraResponses": extras,
        "steps": steps,
    })

# 1. Clean all-account discovery session
read_session_attempt_trace("clean-all-account-discovery-session", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordAllAccount", "CandidateRecorded", "AwaitingAllAccountResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("allAccountResponse", "ExecutionReported", "OperationCompleted", stage="Closing", at=0, requests=2, handled=2, accounts=1),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=3, handled=2, accounts=1),
    session_step("closingResponse", "Closed", "Closed", stage="Terminal", at=0, requests=3, handled=3, accounts=1),
    session_step("closingResponse", "Terminal", "Closed", stage="Terminal", at=0, requests=3, handled=3, accounts=1),
    session_step("start", "Terminal", "Closed", stage="Terminal", at=0, requests=3, handled=3, accounts=1),
], op_type="AllAccount", all_account="clean-discovery")

# 2. Clean all-account unavailable session
read_session_attempt_trace("clean-all-account-unavailable-session", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordAllAccount", "CandidateRecorded", "AwaitingAllAccountResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("allAccountResponse", "UnavailableReported", "OperationCompleted", stage="Closing", at=0, requests=2, handled=2, accounts=0),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=3, handled=2, accounts=0),
    session_step("closingResponse", "Closed", "Closed", stage="Terminal", at=0, requests=3, handled=3, accounts=0),
], op_type="AllAccount", all_account="clean-unavailable-absent")

# 3. Clean balance refresh single-page session
read_session_attempt_trace("clean-balance-refresh-single-page-session", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordBalance", "CandidateRecorded", "AwaitingBalanceFirstReadResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("balanceResponse", "ExecutionReported", "OperationCompleted", stage="Closing", at=0, requests=2, handled=2, pages=1, balances=1),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=3, handled=2, pages=1, balances=1),
    session_step("closingResponse", "Closed", "Closed", stage="Terminal", at=0, requests=3, handled=3, pages=1, balances=1),
], op_type="BalanceRefresh", balance_initial="balance-seven")

# 4. Clean balance refresh two-page session
read_session_attempt_trace("clean-balance-refresh-two-page-session", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordBalance", "CandidateRecorded", "AwaitingBalanceFirstReadResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("balanceResponse", "PartialAccepted", "WaitingForBalanceContinuation", stage="Operation", at=0, requests=2, handled=2, pages=1, balances=1),
    session_step("recordBalanceContinuation", "CandidateRecorded", "AwaitingBalanceContinuationResponse", stage="Operation", at=0, requests=3, handled=2, pages=1, balances=1),
    session_step("balanceResponse", "ExecutionReported", "OperationCompleted", stage="Closing", at=0, requests=3, handled=3, pages=2, balances=2),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=4, handled=3, pages=2, balances=2),
    session_step("closingResponse", "Closed", "Closed", stage="Terminal", at=0, requests=4, handled=4, pages=2, balances=2),
], op_type="BalanceRefresh", balance_initial="partial-balance", balance_continuations=["balance-seven"])

# 5. Clean balance refresh unavailable session
read_session_attempt_trace("clean-balance-refresh-unavailable-session", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordBalance", "CandidateRecorded", "AwaitingBalanceFirstReadResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("balanceResponse", "UnavailableReported", "OperationCompleted", stage="Closing", at=0, requests=2, handled=2, pages=1, balances=0),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=3, handled=2, pages=1, balances=0),
    session_step("closingResponse", "Closed", "Closed", stage="Terminal", at=0, requests=3, handled=3, pages=1, balances=0),
], op_type="BalanceRefresh", balance_initial="balance-unavailable")

# 6. Clean session closing without operation
read_session_attempt_trace("clean-session-closing-without-operation", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=2, handled=1),
    session_step("closingResponse", "Closed", "Closed", stage="Terminal", at=0, requests=2, handled=2),
], op_type="None")

# 7. Session bank abort on close
read_session_attempt_trace("session-bank-abort-on-close", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordAllAccount", "CandidateRecorded", "AwaitingAllAccountResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("allAccountResponse", "ExecutionReported", "OperationCompleted", stage="Closing", at=0, requests=2, handled=2, accounts=1),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=3, handled=2, accounts=1),
    session_step("closingResponse", "AbortReported", "Aborted", stage="Terminal", at=0, requests=3, handled=3, accounts=1),
], op_type="AllAccount", all_account="clean-discovery", closing_code="9800", scoped_code=None)

# 8. Session initialization review
read_session_attempt_trace("session-initialization-review", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "RejectedForReview", "NeedsReview", stage="Terminal", at=0, requests=1, handled=1),
    session_step("recordClosing", "Terminal", "NeedsReview", stage="Terminal", at=0, requests=1, handled=1),
], init="foreign-profile", op_type="None")

# 9. Session all-account review
read_session_attempt_trace("session-all-account-review", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordAllAccount", "CandidateRecorded", "AwaitingAllAccountResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("allAccountResponse", "RejectedForReview", "NeedsReview", stage="Terminal", at=0, requests=2, handled=2),
], op_type="AllAccount", all_account="institution-mismatch")

# 10. Session balance read review
read_session_attempt_trace("session-balance-read-review", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordBalance", "CandidateRecorded", "AwaitingBalanceFirstReadResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("balanceResponse", "RejectedForReview", "NeedsReview", stage="Terminal", at=0, requests=2, handled=2),
], op_type="BalanceRefresh", balance_initial="foreign-iban")

# 11. Session closing review
read_session_attempt_trace("session-closing-review", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordAllAccount", "CandidateRecorded", "AwaitingAllAccountResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("allAccountResponse", "ExecutionReported", "OperationCompleted", stage="Closing", at=0, requests=2, handled=2, accounts=1),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=3, handled=2, accounts=1),
    session_step("closingResponse", "RejectedForReview", "NeedsReview", stage="Terminal", at=0, requests=3, handled=3, accounts=1),
], op_type="AllAccount", all_account="clean-discovery", closing_code="9050", scoped_code=None)

# 12. Foreign init response keeps state
read_session_attempt_trace("foreign-init-response-keeps-state", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "ContextMismatch", "AwaitingInitializationResponse", stage="Initialization", at=500, requests=1, handled=0, index=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=700, requests=1, handled=1),
], extra_responses=[pin_tan_initialization_vectors[5]])

# 13. Foreign operation response keeps state
read_session_attempt_trace("foreign-operation-response-keeps-state", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordAllAccount", "CandidateRecorded", "AwaitingAllAccountResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("allAccountResponse", "ContextMismatch", "AwaitingAllAccountResponse", stage="Operation", at=500, requests=2, handled=1, index=0),
    session_step("allAccountResponse", "ExecutionReported", "OperationCompleted", stage="Closing", at=700, requests=2, handled=2, accounts=1),
], op_type="AllAccount", all_account="clean-discovery", extra_responses=[all_account_response_by_name["wrong-dialogue"]])

# 14. Foreign closing response keeps state
foreign_closing_wire = make_session_closing(message=2, dialogue="FOREIGN")
read_session_attempt_trace("foreign-closing-response-keeps-state", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=2, handled=1),
    session_step("closingResponse", "ContextMismatch", "AwaitingClosingResponse", stage="Closing", at=500, requests=2, handled=1, index=0),
    session_step("closingResponse", "Closed", "Closed", stage="Terminal", at=700, requests=2, handled=2),
], extra_responses=[foreign_closing_wire])

# 15. Candidate dialogue mismatch rejected
read_session_attempt_trace("candidate-dialogue-mismatch-rejected", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordAllAccount", "ContextMismatch", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1, index=0),
    session_step("recordAllAccount", "CandidateRecorded", "AwaitingAllAccountResponse", stage="Operation", at=0, requests=2, handled=1),
], op_type="AllAccount", all_account="clean-discovery", extra_responses=[all_account_response_by_name["clean-escaped-dialogue"]])

# 16. Completed operation cannot restart
read_session_attempt_trace("completed-operation-restart-rejected", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordBalance", "CandidateRecorded", "AwaitingBalanceFirstReadResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("balanceResponse", "ExecutionReported", "OperationCompleted", stage="Closing", at=0, requests=2, handled=2, pages=1, balances=1),
    session_step("recordBalance", "WrongState", "OperationCompleted", stage="Closing", at=0, requests=2, handled=2, pages=1, balances=1),
], op_type="BalanceRefresh", balance_initial="balance-seven")

# 17. Response counter replay rejected
read_session_attempt_trace("response-counter-replay-rejected", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordBalance", "CandidateRecorded", "AwaitingBalanceFirstReadResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("balanceResponse", "PartialAccepted", "WaitingForBalanceContinuation", stage="Operation", at=0, requests=2, handled=2, pages=1, balances=1),
    session_step("balanceResponse", "ReplayRejected", "WaitingForBalanceContinuation", stage="Operation", at=0, requests=2, handled=2, pages=1, balances=1),
], op_type="BalanceRefresh", balance_initial="partial-balance", balance_continuations=["balance-seven"])

# 18. Page limit reached session
read_session_attempt_trace("page-limit-reached-session", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordBalance", "CandidateRecorded", "AwaitingBalanceFirstReadResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("balanceResponse", "PageLimitReached", "PageLimitReached", stage="Closing", at=0, requests=2, handled=2, pages=1, balances=1),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=3, handled=2, pages=1, balances=1),
    session_step("closingResponse", "Closed", "Closed", stage="Terminal", at=0, requests=3, handled=3, pages=1, balances=1),
], op_type="BalanceRefresh", balance_initial="partial-balance", maximum=1)

# 19. Deadline equality at init
read_session_attempt_trace("deadline-equality-at-init", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("snapshot", "Snapshot", "TimedOut", stage="Terminal", at=10000, requests=1, handled=0),
])

# 20. Deadline not extended by operation
read_session_attempt_trace("deadline-not-extended-by-operation", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=5000, requests=1, handled=1),
    session_step("recordAllAccount", "CandidateRecorded", "AwaitingAllAccountResponse", stage="Operation", at=9000, requests=2, handled=1),
    session_step("snapshot", "Snapshot", "TimedOut", stage="Terminal", at=10000, requests=2, handled=1),
], op_type="AllAccount", all_account="clean-discovery")

# 21. Cancellation during init
read_session_attempt_trace("cancellation-during-init", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("cancel", "Cancelled", "Cancelled", stage="Terminal", at=50, requests=1, handled=0),
    session_step("initResponse", "Terminal", "Cancelled", stage="Terminal", at=50, requests=1, handled=0),
])

# 22. Cancellation during operation
read_session_attempt_trace("cancellation-during-operation", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordAllAccount", "CandidateRecorded", "AwaitingAllAccountResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("cancel", "Cancelled", "Cancelled", stage="Terminal", at=100, requests=2, handled=1),
    session_step("allAccountResponse", "Terminal", "Cancelled", stage="Terminal", at=100, requests=2, handled=1),
], op_type="AllAccount", all_account="clean-discovery")

# 23. Cancellation during closing
read_session_attempt_trace("cancellation-during-closing", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordClosing", "CandidateRecorded", "AwaitingClosingResponse", stage="Closing", at=0, requests=2, handled=1),
    session_step("cancel", "Cancelled", "Cancelled", stage="Terminal", at=150, requests=2, handled=1),
    session_step("closingResponse", "Terminal", "Cancelled", stage="Terminal", at=150, requests=2, handled=1),
])

# 24. Stop before start
read_session_attempt_trace("stop-before-start", [
    session_step("stop", "Stopped", "Stopped", stage="Terminal", at=0, requests=0, handled=0),
    session_step("start", "Terminal", "Stopped", stage="Terminal", at=0, requests=0, handled=0),
])

# 25. Dispose during operation
read_session_attempt_trace("dispose-during-operation", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=0, requests=1, handled=1),
    session_step("recordBalance", "CandidateRecorded", "AwaitingBalanceFirstReadResponse", stage="Operation", at=0, requests=2, handled=1),
    session_step("dispose", "Stopped", "Stopped", stage="Terminal", at=0, requests=2, handled=1),
    session_step("balanceResponse", "Terminal", "Stopped", stage="Terminal", at=0, requests=2, handled=1),
], op_type="BalanceRefresh", balance_initial="balance-seven")

# 26. Clock regression during operation
read_session_attempt_trace("clock-regression-during-operation", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=100, requests=1, handled=0),
    session_step("initResponse", "DialogueOpen", "DialogueOpen", stage="Operation", at=200, requests=1, handled=1),
    session_step("snapshot", "Snapshot", "ClockInvalid", stage="Terminal", at=50, requests=1, handled=1),
], op_type="AllAccount", all_account="clean-discovery")

# 27. Second start does not replace candidate
read_session_attempt_trace("second-start-does-not-replace-candidate", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("start", "WrongState", "AwaitingInitializationResponse", stage="Initialization", at=100, requests=1, handled=0),
])

# 28. Stage out of order rejected
read_session_attempt_trace("stage-out-of-order-rejected", [
    session_step("start", "CandidateRecorded", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
    session_step("recordAllAccount", "WrongState", "AwaitingInitializationResponse", stage="Initialization", at=0, requests=1, handled=0),
], op_type="AllAccount", all_account="clean-discovery")

target.with_name("pin-tan-read-session-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": read_session_attempt_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(read_session_attempt_vectors)} public synthetic PIN/TAN read session attempt traces.")

pin_tan_sync_by_name = {v["name"]: v for v in pin_tan_synchronization_vectors}
pin_tan_closing_by_name = {v["name"]: v for v in pin_tan_closing_vectors}
pin_tan_closing_resp_by_name = {v["name"]: v for v in pin_tan_closing_response_vectors}

sync_session_attempt_vectors = []


def sync_session_step(action, transition, state, stage="Synchronization", at=0, requests=1, handled=0, evidence=False, index=None):
    return {
        "action": action,
        "transition": transition,
        "state": state,
        "stage": stage,
        "atMilliseconds": at,
        "requestsRecorded": requests,
        "responsesHandled": handled,
        "evidence": evidence,
        "index": index,
    }


def sync_session_attempt_trace(name, steps, sync_name="two-step-assigned-system", closing_name="two-step-system-assignment",
                               closing_resp_name="two-step-message-closure", extra_responses=None, wrong_candidate=False):
    sync_entry = pin_tan_sync_by_name[sync_name]
    closing_entry = pin_tan_closing_by_name[closing_name]
    closing_resp_entry = pin_tan_closing_resp_by_name[closing_resp_name]
    extras = [] if extra_responses is None else extra_responses
    sync_wire = closing_entry["synchronization"]["responseBase64"] if (closing_entry.get("synchronization") and closing_entry["synchronization"].get("name") == sync_name) else sync_entry["responseBase64"]

    sync_session_attempt_vectors.append({
        "name": name,
        "syncCandidate": signature_context_vectors[0] if wrong_candidate else sync_entry["context"],
        "recovery": {
            "previousDialogueId": sync_entry["previousDialogueId"],
            "lastSubmittedMessageNumber": sync_entry["lastSubmittedMessageNumber"],
        } if sync_entry.get("previousDialogueId") is not None else None,
        "syncResponse": {
            "wireBase64": sync_wire,
            "wrapped": sync_entry["wrapped"],
        },
        "closingCandidate": closing_entry,
        "closingResponse": {
            "wireBase64": closing_resp_entry["responseBase64"],
            "wrapped": closing_resp_entry["wrapped"],
        },
        "extraResponses": extras,
        "steps": steps,
    })


# 1. Clean system ID assignment session
sync_session_attempt_trace("clean-assignment-session", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "ReinitializationRequired", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
    sync_session_step("closeResponse", "Terminal", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2),
    sync_session_step("startSync", "Terminal", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2),
])

# 2. Clean message recovery session
sync_session_attempt_trace("clean-message-recovery-session", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "ReinitializationRequired", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
], sync_name="bounded-message-recovery", closing_name="message-recovery", closing_resp_name="message-recovery-closing")

# 3. One-step assigned system session
sync_session_attempt_trace("one-step-assigned-system-session", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "ReinitializationRequired", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
], sync_name="one-step-assigned-system", closing_name="one-step-system-assignment", closing_resp_name="one-step-message-closure")

# 4. Escaped system and dialogue session
sync_session_attempt_trace("escaped-system-dialogue-session", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "ReinitializationRequired", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
], sync_name="escaped-assigned-system", closing_name="escaped-system-dialogue-control-pin", closing_resp_name="escaped-dialogue-and-system")

# 5. Maximum message recovery session
sync_session_attempt_trace("maximum-message-recovery-session", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "ReinitializationRequired", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
], sync_name="maximum-message-recovery", closing_name="maximum-recovery-is-not-close-counter", closing_resp_name="maximum-recovery-closing")

# 6. Bank abort on synchronization
sync_session_attempt_trace("bank-abort-on-synchronization", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "AbortReported", "Aborted", stage="Terminal", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("syncResponse", "Terminal", "Aborted", stage="Terminal", at=0, requests=1, handled=1),
], sync_name="bank-abort")

# 7. Bank abort on closing
sync_session_attempt_trace("bank-abort-on-closing", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "AbortReported", "Aborted", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
    sync_session_step("closeResponse", "Terminal", "Aborted", stage="Terminal", at=0, requests=2, handled=2),
], closing_resp_name="message-abort")

# 8. Scoped review on synchronization
sync_session_attempt_trace("scoped-review-on-synchronization", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "RejectedForReview", "NeedsReview", stage="Terminal", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("syncResponse", "Terminal", "NeedsReview", stage="Terminal", at=0, requests=1, handled=1),
], sync_name="foreign-envelope-user")

# 9. Scoped review on closing
sync_session_attempt_trace("scoped-review-on-closing", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "RejectedForReview", "NeedsReview", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
    sync_session_step("closeResponse", "Terminal", "NeedsReview", stage="Terminal", at=0, requests=2, handled=2),
], closing_resp_name="foreign-user")

# 10. Foreign scope on synchronization keeps candidate
sync_session_attempt_trace("foreign-scope-on-sync-keeps-candidate", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "ContextMismatch", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0, index=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "ReinitializationRequired", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
], extra_responses=[{"wireBase64": pin_tan_synchronization_vectors[24]["responseBase64"], "wrapped": pin_tan_synchronization_vectors[24]["wrapped"]}])

# 11. Foreign scope on closing keeps candidate
sync_session_attempt_trace("foreign-scope-on-closing-keeps-candidate", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "ContextMismatch", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1, index=0),
    sync_session_step("closeResponse", "ReinitializationRequired", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
], extra_responses=[{"wireBase64": pin_tan_closing_resp_by_name["unknown-reference"]["responseBase64"], "wrapped": pin_tan_closing_resp_by_name["unknown-reference"]["wrapped"]}])

# 12. Closing candidate dialogue mismatch
sync_session_attempt_trace("closing-candidate-dialogue-mismatch", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "ContextMismatch", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, index=0),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("closeResponse", "ReinitializationRequired", "ReinitializationRequired", stage="Terminal", at=0, requests=2, handled=2, evidence=True),
], extra_responses=[pin_tan_closing_by_name["escaped-system-dialogue-control-pin"]])

# 13. Candidate counter replay rejected
sync_session_attempt_trace("candidate-counter-replay-rejected", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("recordClose", "ReplayRejected", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
])

# 14. Response counter replay rejected
sync_session_attempt_trace("response-counter-replay-rejected", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("syncResponse", "ReplayRejected", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1),
])

# 15. Deadline equality at synchronization
sync_session_attempt_trace("deadline-equality-at-synchronization", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("snapshot", "Snapshot", "TimedOut", stage="Terminal", at=10000, requests=1, handled=0),
])

# 16. Deadline not extended by closing
sync_session_attempt_trace("deadline-not-extended-by-closing", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=5000, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=8000, requests=2, handled=1),
    sync_session_step("snapshot", "Snapshot", "TimedOut", stage="Terminal", at=10000, requests=2, handled=1),
])

# 17. Cancellation during synchronization
sync_session_attempt_trace("cancellation-during-synchronization", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("cancel", "Cancelled", "Cancelled", stage="Terminal", at=50, requests=1, handled=0),
    sync_session_step("syncResponse", "Terminal", "Cancelled", stage="Terminal", at=50, requests=1, handled=0),
])

# 18. Cancellation during closing
sync_session_attempt_trace("cancellation-during-closing", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "CloseRequired", "AwaitingCloseRequest", stage="Closing", at=0, requests=1, handled=1, evidence=True),
    sync_session_step("recordClose", "CloseCandidateRecorded", "AwaitingCloseResponse", stage="Closing", at=0, requests=2, handled=1),
    sync_session_step("cancel", "Cancelled", "Cancelled", stage="Terminal", at=100, requests=2, handled=1),
    sync_session_step("closeResponse", "Terminal", "Cancelled", stage="Terminal", at=100, requests=2, handled=1),
])

# 19. Stop before start
sync_session_attempt_trace("stop-before-start", [
    sync_session_step("stop", "Stopped", "Stopped", stage="Terminal", at=0, requests=0, handled=0),
    sync_session_step("startSync", "Terminal", "Stopped", stage="Terminal", at=0, requests=0, handled=0),
])

# 20. Dispose during synchronization
sync_session_attempt_trace("dispose-during-synchronization", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("dispose", "Stopped", "Stopped", stage="Terminal", at=0, requests=1, handled=0),
    sync_session_step("syncResponse", "Terminal", "Stopped", stage="Terminal", at=0, requests=1, handled=0),
])

# 21. Clock regression during synchronization
sync_session_attempt_trace("clock-regression-during-synchronization", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=100, requests=1, handled=0),
    sync_session_step("snapshot", "Snapshot", "ClockInvalid", stage="Terminal", at=50, requests=1, handled=0),
])

# 22. Second start does not replace candidate
sync_session_attempt_trace("second-start-does-not-replace-candidate", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("startSync", "WrongState", "AwaitingSynchronizationResponse", stage="Synchronization", at=100, requests=1, handled=0),
])

# 23. Record close before sync rejected
sync_session_attempt_trace("record-close-before-sync-rejected", [
    sync_session_step("startSync", "CandidateRecorded", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
    sync_session_step("recordClose", "WrongState", "AwaitingSynchronizationResponse", stage="Synchronization", at=0, requests=1, handled=0),
])

# 24. Missing recovery on mode one rejected
sync_session_attempt_trace("missing-recovery-on-mode-one-rejected", [
    sync_session_step("startSync", "RejectedForReview", "NeedsReview", stage="Terminal", at=0, requests=0, handled=0),
], sync_name="missing-recovery-context")

target.with_name("pin-tan-synchronization-session-attempt-v1.json").write_text(
    json.dumps({"schemaVersion": 1, "vectors": sync_session_attempt_vectors}, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"Generated {len(sync_session_attempt_vectors)} public synthetic PIN/TAN synchronization session attempt traces.")








