using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsTransportAndActivationTests
{
    private const string PinSentinel = "SECRET_USER_PIN_12345";
    private const string TanSentinel = "SECRET_CHALLENGE_TAN_99999";

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        var connId = new ConnectionId(501);
        var instId = new InstitutionId(601);
        var acctId = new AccountId(701);
        var now = new DateTimeOffset(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);

        var institutionConfig = ManualInstitutionConfiguration.Create(
            "Test Bank",
            "https://fints.example.com/banking",
            bankCode: "10020030",
            bic: "TESTDEFFXXX")
            .ConfirmEndpoint("https://fints.example.com/banking");

        var connection = BankConnection.Create(connId, instId, institutionConfig, "user1")
            .WithSynchronization("SYS-12345", 1, now);

        // ====================================================================
        // 1. AccountCapability domain entity tests
        // ====================================================================
        {
            // Boundary checks
            VerifyThrows<ArgumentException>(() =>
                new AccountCapability(new ConnectionId(0), acctId, "HKSAL", 7, 1, 1, AccountCapabilityStatus.Active, now),
                "Zero ConnectionId throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                new AccountCapability(connId, new AccountId(0), "HKSAL", 7, 1, 1, AccountCapabilityStatus.Active, now),
                "Zero AccountId throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                new AccountCapability(connId, acctId, "", 7, 1, 1, AccountCapabilityStatus.Active, now),
                "Empty OperationCode throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                new AccountCapability(connId, acctId, "   ", 7, 1, 1, AccountCapabilityStatus.Active, now),
                "Whitespace OperationCode throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                new AccountCapability(connId, acctId, "HK", 7, 1, 1, AccountCapabilityStatus.Active, now),
                "Too short OperationCode throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                new AccountCapability(connId, acctId, "HKSALXYZ", 7, 1, 1, AccountCapabilityStatus.Active, now),
                "Too long OperationCode throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                new AccountCapability(connId, acctId, "hksal", 7, 1, 1, AccountCapabilityStatus.Active, now),
                "Lowercase OperationCode throws ArgumentException.");

            VerifyThrows<ArgumentOutOfRangeException>(() =>
                new AccountCapability(connId, acctId, "HKSAL", 0, 1, 1, AccountCapabilityStatus.Active, now),
                "Zero Version throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentOutOfRangeException>(() =>
                new AccountCapability(connId, acctId, "HKSAL", 1000, 1, 1, AccountCapabilityStatus.Active, now),
                "Version > 999 throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentOutOfRangeException>(() =>
                new AccountCapability(connId, acctId, "HKSAL", 7, -1, 1, AccountCapabilityStatus.Active, now),
                "Negative MinimumSignatures throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentOutOfRangeException>(() =>
                new AccountCapability(connId, acctId, "HKSAL", 7, 10, 1, AccountCapabilityStatus.Active, now),
                "MinimumSignatures > 9 throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentOutOfRangeException>(() =>
                new AccountCapability(connId, acctId, "HKSAL", 7, 1, -1, AccountCapabilityStatus.Active, now),
                "Negative MaximumOrders throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentOutOfRangeException>(() =>
                new AccountCapability(connId, acctId, "HKSAL", 7, 1, 1000, AccountCapabilityStatus.Active, now),
                "MaximumOrders > 999 throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentOutOfRangeException>(() =>
                new AccountCapability(connId, acctId, "HKSAL", 7, 1, 1, (AccountCapabilityStatus)99, now),
                "Undefined AccountCapabilityStatus throws ArgumentOutOfRangeException.");

            // Valid instantiation & properties
            var cap = new AccountCapability(connId, acctId, "HKSAL", 7, 1, 9, AccountCapabilityStatus.Active, now);
            Verify(cap.ConnectionId == connId && cap.AccountId == acctId && cap.OperationCode == "HKSAL" &&
                   cap.Version == 7 && cap.MinimumSignatures == 1 && cap.MaximumOrders == 9 &&
                   cap.Status == AccountCapabilityStatus.Active && cap.ActivatedAt == now,
                   "AccountCapability properties correctly initialized.");

            // Connection-level capability (null AccountId)
            var connCap = new AccountCapability(connId, null, "HKSYN", 3, 0, 1, AccountCapabilityStatus.Active, now);
            Verify(connCap.AccountId is null && connCap.OperationCode == "HKSYN" && connCap.Version == 3,
                   "Connection-level AccountCapability has null AccountId.");

            // WithStatus immutability
            var suspended = cap.WithStatus(AccountCapabilityStatus.Suspended);
            Verify(suspended.Status == AccountCapabilityStatus.Suspended && cap.Status == AccountCapabilityStatus.Active,
                   "WithStatus returns a new instance without mutating the original.");
            Verify(ReferenceEquals(cap.WithStatus(AccountCapabilityStatus.Active), cap),
                   "WithStatus with same status returns the same instance.");

            // Value equality
            var capCopy = new AccountCapability(connId, acctId, "HKSAL", 7, 1, 9, AccountCapabilityStatus.Active, now);
            Verify(cap == capCopy && cap.Equals(capCopy) && cap.GetHashCode() == capCopy.GetHashCode(),
                   "Identical AccountCapability instances satisfy value equality.");

            var diffStatus = cap.WithStatus(AccountCapabilityStatus.Deprecated);
            Verify(cap != diffStatus && !cap.Equals(diffStatus), "AccountCapability differs when status differs.");

            var diffVersion = new AccountCapability(connId, acctId, "HKSAL", 6, 1, 9, AccountCapabilityStatus.Active, now);
            Verify(cap != diffVersion, "AccountCapability differs when version differs.");

            Verify(!cap.Equals(null) && !cap.Equals("other"), "Equals returns false for null or foreign types.");

            // ToString formatting
            Verify(cap is not null && cap.ToString()!.Contains("HKSAL:v7 [Active]", StringComparison.Ordinal),
                   "ToString formats operational code, version, and status.");
            Verify(connCap is not null && connCap.ToString()!.Contains("Connection", StringComparison.Ordinal),
                   "ToString formats Connection target when AccountId is null.");
        }

        // ====================================================================
        // 2. FinTsSecurityProfile tests
        // ====================================================================
        {
            VerifyThrows<ArgumentOutOfRangeException>(() => new FinTsSecurityProfile(0, "999"),
                "ProfileVersion 0 throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentOutOfRangeException>(() => new FinTsSecurityProfile(3, "999"),
                "ProfileVersion 3 throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentException>(() => new FinTsSecurityProfile(1, ""),
                "Empty security function throws ArgumentException.");

            VerifyThrows<ArgumentException>(() => new FinTsSecurityProfile(1, "abc"),
                "Non-numeric security function throws ArgumentException.");

            VerifyThrows<ArgumentException>(() => new FinTsSecurityProfile(1, "899"),
                "Security function < 900 throws ArgumentException.");

            VerifyThrows<ArgumentException>(() => new FinTsSecurityProfile(1, "1000"),
                "Security function > 999 throws ArgumentException.");

            VerifyThrows<ArgumentOutOfRangeException>(() => new FinTsSecurityProfile(1, "999", supplierRole: 3),
                "Invalid supplier role throws ArgumentOutOfRangeException.");

            var profile1 = new FinTsSecurityProfile(1, "999");
            Verify(profile1.ProfileVersion == 1 && profile1.SecurityFunction == "999" &&
                   profile1.HashAlgorithmCode == 1 && profile1.SupplierRole == 1,
                   "PIN:1 profile correctly configured for single-step SHA-1.");

            var profile2 = new FinTsSecurityProfile(2, "920");
            Verify(profile2.ProfileVersion == 2 && profile2.SecurityFunction == "920" &&
                   profile2.HashAlgorithmCode == 2 && profile2.SupplierRole == 1,
                   "PIN:2 profile correctly configured for two-step SHA-256.");

            // Profile equality
            var profile2Copy = new FinTsSecurityProfile(2, "920");
            Verify(profile2 == profile2Copy && profile2.Equals(profile2Copy) && profile2.GetHashCode() == profile2Copy.GetHashCode(),
                   "FinTsSecurityProfile satisfies value equality.");
            Verify(profile1 != profile2, "Distinct security profiles are not equal.");
            Verify((profile1.ToString() ?? string.Empty).Contains("PIN:1", StringComparison.Ordinal), "ToString includes profile name and version.");

            // Header validation checks with real synthetic vector
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-signature-header-v1.json")!)
            using (var doc = System.Text.Json.JsonDocument.Parse(stream))
            {
                var v = doc.RootElement.GetProperty("vectors").EnumerateArray().First();
                byte[] bytes = Convert.FromBase64String(v.GetProperty("wireBase64").GetString()!);
                var syntax = FinTsSyntax.ParseSegments(bytes);
                var header = FinTsPinTanSignatureHeader.Parse(syntax.Segments.Single());

                VerifyThrows<ArgumentNullException>(() => profile1.ValidateHeader(null!, out _),
                    "ValidateHeader throws ArgumentNullException on null header.");

                Verify(profile1.ValidateHeader(header, out string? issue) && issue is null,
                    "Negotiated PIN:1 profile accepts compatible HNSHK header.");

                Verify(!profile2.ValidateHeader(header, out string? mismatchIssue) && mismatchIssue is not null,
                    "Mismatched PIN:2 profile rejects PIN:1 HNSHK header.");
            }

            // Envelope validation checks with real synthetic vector
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-v1.json")!)
            using (var doc = System.Text.Json.JsonDocument.Parse(stream))
            {
                var v = doc.RootElement.GetProperty("vectors").EnumerateArray().First();
                byte[] bytes = Convert.FromBase64String(v.GetProperty("wireBase64").GetString()!);
                var frame = FinTsMessageFrame.Parse(bytes);
                var envelope = FinTsPinTanEnvelope.Parse(frame);

                VerifyThrows<ArgumentNullException>(() => profile2.ValidateEnvelope(null!, out _),
                    "ValidateEnvelope throws ArgumentNullException on null envelope.");

                Verify(profile1.ValidateEnvelope(envelope, out string? envIssue) && envIssue is null,
                    "Negotiated PIN:1 profile accepts compatible PIN:1 response envelope.");

                Verify(!profile2.ValidateEnvelope(envelope, out string? envMismatchIssue) && envMismatchIssue is not null,
                    "Mismatched PIN:2 profile rejects PIN:1 response envelope.");
            }
        }

        // ====================================================================
        // 3. FinTsCapabilityActivation tests
        // ====================================================================
        {
            const string bankWire = "HIBPA:3+1+280:10020030+Test Bank+1+1+300";
            const string userWire = "HIUPA:4+user1+1+1";
            const string acctWire = "HIUPD:6+PUBLIC-ACCOUNT::280:10020030+DE89370400440532013000+C+1+EUR+Owner++++HKSAL:1+HKSPA:1";
            const string sal6Wire = "HISALS:6+9+1+4";
            const string sal7Wire = "HISALS:7+9+1+4";
            const string sal8Wire = "HISALS:8+9+1+4+J";
            const string spa1Wire = "HISPAS:1+1+1+0+J:N:J:urn?:synthetic:";

            var readParams = BuildReadParameters(bankWire, userWire, acctWire, sal6Wire, sal7Wire, sal8Wire, spa1Wire);

            // Rejection on disabled connection
            var disabledConn = connection.WithDisabled();
            var disabledResult = FinTsCapabilityActivation.Activate(disabledConn, readParams, now);
            Verify(disabledResult.Status == FinTsCapabilityActivationStatus.RejectedDisabledConnection && !disabledResult.IsSuccess,
                   "Disabled connection rejects capability activation.");

            // Rejection on unsynchronized connection
            var unconfiguredConn = BankConnection.Create(connId, instId, institutionConfig, "user1");
            var unconfiguredResult = FinTsCapabilityActivation.Activate(unconfiguredConn, readParams, now);
            Verify(unconfiguredResult.Status == FinTsCapabilityActivationStatus.RejectedUnsynchronizedConnection && !unconfiguredResult.IsSuccess,
                   "Unsynchronized connection rejects capability activation.");

            // Rejection on missing bank parameters
            var noBankParams = BuildReadParameters(userWire, acctWire, sal7Wire);
            var noBankResult = FinTsCapabilityActivation.Activate(connection, noBankParams, now);
            Verify(noBankResult.Status == FinTsCapabilityActivationStatus.RejectedMissingParameters,
                   "Missing HIBPA in BPD rejects capability activation.");

            // Rejection on BPD without protocol 300
            const string oldProtoBank = "HIBPA:3+1+280:10020030+Test Bank+1+1+220";
            var oldProtoParams = BuildReadParameters(oldProtoBank, userWire, acctWire, sal7Wire);
            var oldProtoResult = FinTsCapabilityActivation.Activate(connection, oldProtoParams, now);
            Verify(oldProtoResult.Status == FinTsCapabilityActivationStatus.RejectedMissingParameters,
                   "BPD lacking protocol 300 rejects capability activation.");

            // Rejection on bank code mismatch
            const string wrongBankWire = "HIBPA:3+1+280:99999999+Wrong Bank+1+1+300";
            var wrongBankParams = BuildReadParameters(wrongBankWire, userWire, acctWire, sal7Wire);
            var wrongBankResult = FinTsCapabilityActivation.Activate(connection, wrongBankParams, now);
            Verify(wrongBankResult.Status == FinTsCapabilityActivationStatus.RejectedInstitutionMismatch,
                   "Bank code mismatch rejects capability activation.");

            // Clean activation
            var activeResult = FinTsCapabilityActivation.Activate(
                connection,
                readParams,
                now,
                account => acctId);

            Verify(activeResult.IsSuccess && activeResult.Status == FinTsCapabilityActivationStatus.Activated,
                   "Valid BPD/UPD parameters cleanly activate capabilities.");

            // Standard connection capabilities
            var syncCap = activeResult.Capabilities.FirstOrDefault(c => c.OperationCode == "HKSYN");
            var endCap = activeResult.Capabilities.FirstOrDefault(c => c.OperationCode == "HKEND");
            Verify(syncCap is not null && syncCap.Status == AccountCapabilityStatus.Active && syncCap.AccountId is null,
                   "HKSYN connection capability activated.");
            Verify(endCap is not null && endCap.Status == AccountCapabilityStatus.Active && endCap.AccountId is null,
                   "HKEND connection capability activated.");

            // Account-level capabilities
            var sal7 = activeResult.Capabilities.FirstOrDefault(c => c.OperationCode == "HKSAL" && c.Version == 7);
            Verify(sal7 is not null && sal7.Status == AccountCapabilityStatus.Active && sal7.AccountId == acctId,
                   "HKSAL v7 activated for resolved account ID.");

            var spa1 = activeResult.Capabilities.FirstOrDefault(c => c.OperationCode == "HKSPA" && c.Version == 1);
            Verify(spa1 is not null && spa1.Status == AccountCapabilityStatus.Active && spa1.AccountId == acctId,
                   "HKSPA v1 activated for resolved account ID.");

            // Account permission suspension: zero orders
            const string zeroOrderSal = "HISALS:7+0+1+4";
            var zeroOrderParams = BuildReadParameters(bankWire, userWire, acctWire, zeroOrderSal);
            var zeroOrderResult = FinTsCapabilityActivation.Activate(connection, zeroOrderParams, now);
            var zeroSalCap = zeroOrderResult.Capabilities.FirstOrDefault(c => c.OperationCode == "HKSAL" && c.Version == 7);
            Verify(zeroSalCap is not null && zeroSalCap.Status == AccountCapabilityStatus.Suspended,
                   "HKSAL with zero MaximumOrders is suspended.");

            // Account permission suspension: unlisted blocked
            const string blockedUser = "HIUPA:4+user1+1+0"; // unlisted blocked
            const string emptyPermAcct = "HIUPD:6+PUBLIC-ACCOUNT::280:10020030+DE89370400440532013000+C+1+EUR+Owner++++HKXYZ:1";
            var blockedParams = BuildReadParameters(bankWire, blockedUser, emptyPermAcct, sal7Wire);
            var blockedResult = FinTsCapabilityActivation.Activate(connection, blockedParams, now);
            var blockedSalCap = blockedResult.Capabilities.FirstOrDefault(c => c.OperationCode == "HKSAL" && c.Version == 7);
            Verify(blockedSalCap is not null && blockedSalCap.Status == AccountCapabilityStatus.Suspended,
                   "Unlisted blocked operation produces Suspended capability status.");

            // Account permission suspension: signature conflict
            const string highSigSal = "HISALS:7+9+3+4"; // min signatures 3
            const string lowSigAcct = "HIUPD:6+PUBLIC-ACCOUNT::280:10020030+DE89370400440532013000+C+1+EUR+Owner++++HKSAL:1"; // required 1
            var sigConflictParams = BuildReadParameters(bankWire, userWire, lowSigAcct, highSigSal);
            var sigConflictResult = FinTsCapabilityActivation.Activate(connection, sigConflictParams, now);
            var sigConflictCap = sigConflictResult.Capabilities.FirstOrDefault(c => c.OperationCode == "HKSAL" && c.Version == 7);
            Verify(sigConflictCap is not null && sigConflictCap.Status == AccountCapabilityStatus.Suspended,
                   "Signature requirement conflict produces Suspended capability status.");

            // HKSPA suspension when single account request not allowed
            const string noSingleSpa = "HISPAS:1+1+1+0+N:N:J:urn?:synthetic:"; // single account = N
            var noSingleParams = BuildReadParameters(bankWire, userWire, acctWire, noSingleSpa);
            var noSingleResult = FinTsCapabilityActivation.Activate(connection, noSingleParams, now);
            var noSingleCap = noSingleResult.Capabilities.FirstOrDefault(c => c.OperationCode == "HKSPA" && c.Version == 1);
            Verify(noSingleCap is not null && noSingleCap.Status == AccountCapabilityStatus.Suspended,
                   "HKSPA without SingleAccountRequestAllowed produces Suspended capability status.");

            // Missing IBAN on HKSAL v7/v8
            const string noIbanAcct = "HIUPD:6+PUBLIC-ACCOUNT::280:10020030++C+1+EUR+Owner++++HKSAL:1";
            var noIbanParams = BuildReadParameters(bankWire, userWire, noIbanAcct, sal7Wire);
            var noIbanResult = FinTsCapabilityActivation.Activate(connection, noIbanParams, now);
            var noIbanCap = noIbanResult.Capabilities.FirstOrDefault(c => c.OperationCode == "HKSAL" && c.Version == 7);
            Verify(noIbanCap is not null && noIbanCap.Status == AccountCapabilityStatus.Suspended,
                   "HKSAL v7 without IBAN produces Suspended capability status.");

            // Concurrency: 16-thread atomicity
            bool concurrencyPassed = true;
            Parallel.For(0, 16, i =>
            {
                var res = FinTsCapabilityActivation.Activate(connection, readParams, now);
                if (!res.IsSuccess || res.Capabilities.Count != activeResult.Capabilities.Count)
                {
                    concurrencyPassed = false;
                }
            });
            Verify(concurrencyPassed, "16 concurrent activations produce identical, atomic results.");
        }

        // ====================================================================
        // 4. FinTsHttpsTransport tests
        // ====================================================================
        {
            var validEndpoint = new Uri("https://fints.example.com/banking");

            // Construction boundary checks
            VerifyThrows<ArgumentOutOfRangeException>(() => new FinTsHttpsTransport(maximumRequestBodyBytes: 0),
                "Zero maximumRequestBodyBytes throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentOutOfRangeException>(() => new FinTsHttpsTransport(maximumResponseBodyBytes: 0),
                "Zero maximumResponseBodyBytes throws ArgumentOutOfRangeException.");

            VerifyThrows<ArgumentOutOfRangeException>(() => new FinTsHttpsTransport(timeout: TimeSpan.Zero),
                "Zero timeout throws ArgumentOutOfRangeException.");

            // Endpoint validation
            using var testTransport = new FinTsHttpsTransport(new MockHttpHandler((req, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("HNHBK:1:3+000000000025+300+0+1'HNHBS:2:1+1'"u8.ToArray())
                })));

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await testTransport.SendAsync(new Uri("http://fints.example.com/banking"), "data"u8.ToArray()),
                ex => ex.Issue == FinTsTransportIssue.InvalidEndpoint,
                "Insecure HTTP endpoint throws FinTsTransportException with InvalidEndpoint.");

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await testTransport.SendAsync(new Uri("ftp://fints.example.com/banking"), "data"u8.ToArray()),
                ex => ex.Issue == FinTsTransportIssue.InvalidEndpoint,
                "FTP endpoint throws FinTsTransportException with InvalidEndpoint.");

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await testTransport.SendAsync(new Uri("https://user:pass@fints.example.com/banking"), "data"u8.ToArray()),
                ex => ex.Issue == FinTsTransportIssue.InvalidEndpoint,
                "Endpoint with UserInfo throws FinTsTransportException with InvalidEndpoint.");

            // Payload bounds
            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await testTransport.SendAsync(validEndpoint, ReadOnlyMemory<byte>.Empty),
                ex => ex.Issue == FinTsTransportIssue.EmptyPayload,
                "Empty request payload throws FinTsTransportException with EmptyPayload.");

            byte[] tooLargeRequest = new byte[FinTsHttpsTransport.DefaultMaximumRequestBodyBytes + 1];
            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await testTransport.SendAsync(validEndpoint, tooLargeRequest),
                ex => ex.Issue == FinTsTransportIssue.PayloadTooLarge,
                "Request payload exceeding maximum bound throws FinTsTransportException with PayloadTooLarge.");

            // Clean 200 OK POST execution
            HttpRequestMessage? capturedRequest = null;
            byte[]? capturedBody = null;

            using var echoTransport = new FinTsHttpsTransport(new MockHttpHandler(async (req, ct) =>
            {
                capturedRequest = req;
                capturedBody = await req.Content!.ReadAsByteArrayAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent("RESPONSE_PAYLOAD"u8.ToArray())
                    {
                        Headers = { ContentType = new MediaTypeHeaderValue("text/plain") }
                    }
                };
            }));

            byte[] requestBytes = "HNHBK:1:3+000000000025+300+0+1'HNHBS:2:1+1'"u8.ToArray();
            var response = echoTransport.SendAsync(validEndpoint, requestBytes).AsTask().GetAwaiter().GetResult();

            Verify(response.IsSuccess && response.StatusCode == 200, "Clean transport returns 200 OK response.");
            Verify(response.Payload.Span.SequenceEqual("RESPONSE_PAYLOAD"u8), "Response payload matches server bytes.");
            Verify(response.ContentType == "text/plain", "Response ContentType matches server header.");
            Verify(capturedRequest is not null && capturedRequest.Method == HttpMethod.Post, "Transport transmits via HTTP POST.");
            Verify(capturedRequest!.RequestUri == validEndpoint, "Transport sends to exact configured endpoint.");
            Verify(capturedBody is not null && capturedBody.SequenceEqual(requestBytes), "Transport sends exact request payload bytes.");

            // Non-200 HTTP response mapping
            using var errorTransport = new FinTsHttpsTransport(new MockHttpHandler((req, ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await errorTransport.SendAsync(validEndpoint, requestBytes),
                ex => ex.Issue == FinTsTransportIssue.HttpError && ex.StatusCode == 500,
                "HTTP 500 maps to FinTsTransportException with HttpError and StatusCode 500.");

            // Advertised Content-Length exceeding maximum bound
            using var oversizeAdvertisedTransport = new FinTsHttpsTransport(
                new MockHttpHandler((req, ct) =>
                {
                    var msg = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[10])
                    };
                    msg.Content.Headers.ContentLength = FinTsHttpsTransport.DefaultMaximumResponseBodyBytes + 100;
                    return Task.FromResult(msg);
                }));

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await oversizeAdvertisedTransport.SendAsync(validEndpoint, requestBytes),
                ex => ex.Issue == FinTsTransportIssue.PayloadTooLarge,
                "Advertised Content-Length exceeding bound throws PayloadTooLarge.");

            // Streaming body exceeding maximum bound
            using var streamLimitTransport = new FinTsHttpsTransport(
                new MockHttpHandler((req, ct) =>
                {
                    byte[] bigBody = new byte[2000];
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(bigBody)
                    });
                }),
                maximumResponseBodyBytes: 1000); // 1000 byte limit

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await streamLimitTransport.SendAsync(validEndpoint, requestBytes),
                ex => ex.Issue == FinTsTransportIssue.PayloadTooLarge,
                "Streamed body exceeding maximum bound throws PayloadTooLarge.");

            // Timeout mapping
            using var timeoutTransport = new FinTsHttpsTransport(
                new MockHttpHandler(async (req, ct) =>
                {
                    await Task.Delay(200, ct);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }),
                timeout: TimeSpan.FromMilliseconds(50));

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await timeoutTransport.SendAsync(validEndpoint, requestBytes),
                ex => ex.Issue == FinTsTransportIssue.Timeout,
                "Transport timeout maps to FinTsTransportException with Timeout.");

            // TLS handshake failure mapping
            using var tlsFailTransport = new FinTsHttpsTransport(new MockHttpHandler((req, ct) =>
                throw new HttpRequestException("The SSL connection could not be established.", new AuthenticationException("Certificate invalid."))));

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await tlsFailTransport.SendAsync(validEndpoint, requestBytes),
                ex => ex.Issue == FinTsTransportIssue.TlsFailure,
                "SSL/TLS failure maps to FinTsTransportException with TlsFailure.");

            // Generic network failure mapping
            using var netFailTransport = new FinTsHttpsTransport(new MockHttpHandler((req, ct) =>
                throw new HttpRequestException("Connection refused.")));

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await netFailTransport.SendAsync(validEndpoint, requestBytes),
                ex => ex.Issue == FinTsTransportIssue.NetworkFailure,
                "Generic network error maps to FinTsTransportException with NetworkFailure.");

            // Caller cancellation
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await testTransport.SendAsync(validEndpoint, requestBytes, cts.Token),
                ex => ex.Issue == FinTsTransportIssue.Cancelled,
                "Caller cancellation token maps to FinTsTransportException with Cancelled.");

            // ================================================================
            // SECRET-SAFE INVARIANT VERIFICATION
            // Ensure no credentials or sentinels leak into exception diagnostics
            // ================================================================
            byte[] sensitiveRequest = Encoding.Latin1.GetBytes($"HNHBK:1:3+000000000050+300+0+1'HNSHA:2:2+{PinSentinel}+{TanSentinel}'HNHBS:3:1+1'");

            Action<FinTsTransportException, string> verifySecretSafe = (ex, scenario) =>
            {
                string exString = ex.ToString();
                string exMsg = ex.Message;
                Verify(!exString.Contains(PinSentinel, StringComparison.Ordinal) &&
                       !exMsg.Contains(PinSentinel, StringComparison.Ordinal),
                       $"Exception in scenario '{scenario}' does not leak PIN sentinel in Message or ToString.");
                Verify(!exString.Contains(TanSentinel, StringComparison.Ordinal) &&
                       !exMsg.Contains(TanSentinel, StringComparison.Ordinal),
                       $"Exception in scenario '{scenario}' does not leak TAN sentinel in Message or ToString.");
            };

            // Test across multiple failure scenarios
            try { netFailTransport.SendAsync(validEndpoint, sensitiveRequest).AsTask().GetAwaiter().GetResult(); }
            catch (FinTsTransportException ex) { verifySecretSafe(ex, "NetworkFailure"); }

            try { tlsFailTransport.SendAsync(validEndpoint, sensitiveRequest).AsTask().GetAwaiter().GetResult(); }
            catch (FinTsTransportException ex) { verifySecretSafe(ex, "TlsFailure"); }

            try { errorTransport.SendAsync(validEndpoint, sensitiveRequest).AsTask().GetAwaiter().GetResult(); }
            catch (FinTsTransportException ex) { verifySecretSafe(ex, "HttpError"); }
        }

        // ====================================================================
        // 5. FinTsSimulatedTransport tests
        // ====================================================================
        {
            var simulated = new FinTsSimulatedTransport();
            var endpoint = new Uri("https://fints.example.com/banking");

            simulated.EnqueueResponse("SIMULATED_REPLY"u8.ToArray(), 200);

            var resp = simulated.SendAsync(endpoint, "REQUEST_1"u8.ToArray()).AsTask().GetAwaiter().GetResult();
            Verify(resp.IsSuccess && resp.Payload.Span.SequenceEqual("SIMULATED_REPLY"u8),
                   "Simulated transport dequeues and returns configured response.");
            Verify(simulated.TransmittedCount == 1, "Simulated transport tracks transmission count.");

            simulated.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.TlsFailure, "Simulated TLS failure.", endpoint));
            VerifyThrowsAsync<FinTsTransportException>(async () =>
                await simulated.SendAsync(endpoint, "REQUEST_2"u8.ToArray()),
                ex => ex.Issue == FinTsTransportIssue.TlsFailure,
                "Simulated transport throws configured failure exception.");

            simulated.Dispose();
        }

        Console.WriteLine($"FinTS transport, security profiles, and capability activation: {count} executable checks completed.");
    }

    private static void VerifyThrows<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected {typeof(TException).Name} but no exception was thrown.");
        }
        catch (TException)
        {
            // Expected
        }
    }

    private static void VerifyThrowsAsync<TException>(Func<Task> action, Func<TException, bool>? predicate, string message) where TException : Exception
    {
        try
        {
            action().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Expected {typeof(TException).Name} but no exception was thrown.");
        }
        catch (TException ex)
        {
            if (predicate is not null && !predicate(ex))
            {
                throw new InvalidOperationException($"Exception thrown did not satisfy predicate: {ex.Message}");
            }
        }
    }

    private static FinTsReadParameterSet BuildReadParameters(params string[] segments)
    {
        var body = new StringBuilder("HIRMG:2:2+0010::received'");
        int number = 3;
        foreach (string seg in segments)
        {
            int colon = seg.IndexOf(':');
            body.Append(seg.AsSpan(0, colon)).Append(':').Append((number++).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(seg.AsSpan(colon)).Append('\'');
        }
        string template = "HNHBK:1:3+000000000000+300+D+1+D:1'" + body + $"HNHBS:{number}:1+1'";
        string wire = template.Replace("000000000000", Encoding.Latin1.GetByteCount(template).ToString("D12", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire)));
        var paramSet = FinTsParameterSet.Parse(response);
        return FinTsReadParameterSet.Parse(paramSet);
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public MockHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
