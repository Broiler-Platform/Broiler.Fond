using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Presentation.Services;

/// <summary>BR-02 composition boundary: a transport cannot be reused for an unreviewed endpoint.</summary>
internal sealed class ConfirmedBankTransport : IFinTsTransport
{
    private readonly Uri _endpoint;
    private readonly Func<bool> _isCurrent;
    private readonly FinTsHttpsTransport _transport;

    internal ConfirmedBankTransport(ConnectionSetupContext context, BankProductConfiguration product,
        Func<bool> isCurrent, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        if (!product.IsConfigured) throw new InvalidOperationException(product.Error);
        var connection = BankConnectionSetup.ToDomain(context.Connection, context.Institution);
        if (!connection.InstitutionConfiguration.IsEndpointConfirmed)
            throw new InvalidOperationException("Confirm the bank endpoint before creating a bank transport.");
        _endpoint = connection.InstitutionConfiguration.FinTsEndpoint;
        _isCurrent = isCurrent;
        _transport = new FinTsHttpsTransport(handler, timeout: timeout);
    }

    public async ValueTask<FinTsTransportResponse> SendAsync(Uri endpoint, ReadOnlyMemory<byte> requestPayload, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isCurrent() || !endpoint.IsAbsoluteUri || endpoint.AbsoluteUri != _endpoint.AbsoluteUri)
            throw new InvalidOperationException("The reviewed bank endpoint changed. Review it again before sending credentials.");
        var response = await _transport.SendAsync(_endpoint, requestPayload, cancellationToken).ConfigureAwait(false);
        if (!_isCurrent()) throw new InvalidOperationException("Bank settings changed during the request; discard its result.");
        return response;
    }

    public void Dispose() => _transport.Dispose();
}
