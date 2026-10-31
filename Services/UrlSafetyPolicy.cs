using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace MediaForge.Services;

/// <summary>
/// Política de proteção contra SSRF para a conversão de URLs.
/// Valida o esquema/anfitrião e, sobretudo, verifica os endereços IP reais no momento da ligação
/// (também em redireccionamentos), bloqueando redes locais, privadas e reservadas.
/// </summary>
public sealed class UrlSafetyPolicy
{
    public const int MaximumUrlLength = 2048;
    public const string BlockedMessage = "O endereço de destino pertence a uma rede local, privada ou reservada e foi bloqueado.";

    private const int MaximumRedirections = 5;
    private static readonly string[] BlockedHostSuffixes = [".localhost", ".local", ".localdomain", ".internal"];

    /// <summary>Política predefinida: bloqueia localhost, redes privadas e endereços reservados.</summary>
    public static UrlSafetyPolicy Default { get; } = new(allowPrivateNetwork: false);

    /// <summary>Apenas para testes automatizados com servidores em loopback. Nunca usar na aplicação.</summary>
    public static UrlSafetyPolicy AllowPrivateNetwork { get; } = new(allowPrivateNetwork: true);

    private UrlSafetyPolicy(bool allowPrivateNetwork)
    {
        AllowsPrivateNetwork = allowPrivateNetwork;
    }

    public bool AllowsPrivateNetwork { get; }

    public Uri ValidateUrl(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0
            || text.Length > MaximumUrlLength
            || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException("O URL deve ser uma ligação HTTP ou HTTPS válida.", nameof(value));
        }

        if (!AllowsPrivateNetwork && IsBlockedHost(uri))
        {
            throw new ArgumentException(BlockedMessage, nameof(value));
        }
        return uri;
    }

    public static bool IsBlockedHost(Uri uri)
    {
        var host = uri.Host.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0)
        {
            return true;
        }
        if (IPAddress.TryParse(host, out var address))
        {
            return IsBlockedAddress(address);
        }
        return host == "localhost" || BlockedHostSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal));
    }

    public static bool IsBlockedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                                  // 0.0.0.0/8
                || b[0] == 10                                 // 10.0.0.0/8
                || (b[0] == 100 && b[1] is >= 64 and <= 127)  // 100.64.0.0/10 (CGNAT)
                || (b[0] == 169 && b[1] == 254)               // 169.254.0.0/16 (link-local, metadados cloud)
                || (b[0] == 172 && b[1] is >= 16 and <= 31)   // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)               // 192.168.0.0/16
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)    // 192.0.0.0/24
                || (b[0] == 198 && b[1] is 18 or 19)          // 198.18.0.0/15
                || b[0] >= 224;                               // multicast e reservados
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast;
        }
        return true;
    }

    /// <summary>
    /// Cria um HttpClient cuja ligação TCP só é aberta depois de todos os endereços resolvidos serem validados.
    /// Isto cobre redireccionamentos e DNS rebinding, ao contrário de uma validação única antes do pedido.
    /// O proxy do sistema é desativado para que o destino real seja sempre o endereço verificado.
    /// </summary>
    public HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaximumRedirections,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectAsync
        };
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MediaForge", "1.0"));
        return client;
    }

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0)
        {
            throw new HttpRequestException("Não foi possível resolver o endereço do website.");
        }
        if (!AllowsPrivateNetwork && addresses.Any(IsBlockedAddress))
        {
            throw new HttpRequestException(BlockedMessage);
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
