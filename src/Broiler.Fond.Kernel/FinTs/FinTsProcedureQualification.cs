using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsProcedureQualificationStatus { Qualified, Rejected, CloseFailed, TransportFailed, Cancelled }
public sealed record FinTsProcedureQualificationResult(FinTsProcedureQualificationStatus Status, FinTsPinTanProcedureContext? Procedures = null);

/// <summary>Closed identified, unsigned parameter observation. No PIN, authentication claim or anonymous-permission promotion.</summary>
public static class FinTsProcedureQualification
{
    public static async Task<FinTsProcedureQualificationResult> ExecuteAsync(IFinTsTransport transport, BankConnection connection,
        FinTsProductRegistration registration, FinTsPinTanSignatureSelection selection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport); ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(registration); ArgumentNullException.ThrowIfNull(selection);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        var ct = lifetime.Token;
        string? dialogue = null;
        bool closeAttempted = false;
        var endpoint = connection.InstitutionConfiguration.FinTsEndpoint;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!connection.InstitutionConfiguration.IsEndpointConfirmed || connection.SystemStatus != FinTsCustomerSystemStatus.Required)
                return new(FinTsProcedureQualificationStatus.Rejected);
            // Version zero requests fresh BPD/UPD; a later response must identify the actual configured user.
            var input = new FinTsInitializationInput("280", connection.InstitutionConfiguration.BankCode!, connection.CustomerId ?? connection.UserId,
                "0", FinTsCustomerSystemStatus.Required, 0, 0, FinTsDialogueLanguage.Standard, registration.ProductIdentifier, registration.ProductVersion);
            byte[] wire = FinTsUnsignedInitializationWriter.Encode(input, ct);
            var request = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(wire, ct), ct);
            var correlation = new FinTsDialogueCorrelation();
            if (correlation.BeginRequest(request.Frame) != FinTsCorrelationStatus.RequestRecorded) return new(FinTsProcedureQualificationStatus.Rejected);
            var received = await transport.SendAsync(endpoint, wire, ct).ConfigureAwait(false);
            if (!received.IsSuccess) return new(FinTsProcedureQualificationStatus.TransportFailed);
            var response = FinTsReadExecutionPipeline.ParseWireResponse(received.Payload.Span, ct);
            if (correlation.AcceptResponse(response) != FinTsCorrelationStatus.Matched || response.PinTanEnvelope is not null)
                return new(FinTsProcedureQualificationStatus.Rejected);
            dialogue = response.Frame.Syntax.Segments[0].Fields[2].Elements[0].HeaderText();
            var parameters = FinTsParameterSet.Parse(response, ct);
            var procedures = new FinTsPinTanProcedureContext(request, FinTsTanParameterSet.Parse(parameters, ct), FinTsPermittedProcedureSet.Parse(response, ct));
            // Exact user/customer/bank scope, permitted selection and response semantics are checked by the production factory.
            _ = new FinTsProductionCandidateFactory(connection, registration, procedures, selection);
            if (parameters.User is not { IsDialogueScoped: false }) return new(FinTsProcedureQualificationStatus.Rejected);
            ct.ThrowIfCancellationRequested();
            closeAttempted = true;
            if (!await FinTsBankParameterBootstrap.CloseAsync(transport, endpoint, dialogue, ct).ConfigureAwait(false))
                return new(FinTsProcedureQualificationStatus.CloseFailed);
            ct.ThrowIfCancellationRequested();
            return new(FinTsProcedureQualificationStatus.Qualified, procedures);
        }
        catch (OperationCanceledException) { return new(FinTsProcedureQualificationStatus.Cancelled); }
        catch (FinTsTransportException) { return new(ct.IsCancellationRequested ? FinTsProcedureQualificationStatus.Cancelled : closeAttempted ? FinTsProcedureQualificationStatus.CloseFailed : FinTsProcedureQualificationStatus.TransportFailed); }
        catch (Exception ex) when (ex is FinTsFormatException or ArgumentException or InvalidOperationException)
        { return new(closeAttempted ? FinTsProcedureQualificationStatus.CloseFailed : FinTsProcedureQualificationStatus.Rejected); }
        finally
        {
            if (dialogue is not null && !closeAttempted)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await FinTsBankParameterBootstrap.CloseAsync(transport, endpoint, dialogue, cleanup.Token).ConfigureAwait(false); } catch (Exception) { }
            }
        }
    }
}
