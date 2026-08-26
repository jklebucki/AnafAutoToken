using System.Net;
using System.Net.Sockets;

namespace AnafAutoToken.Shared.Networking;

/// <summary>
/// Lista sieci, którym wolno wołać API workera. Endpointy nie mają uwierzytelnienia,
/// a <c>/api/tokens/current</c> zwraca tokeny jawnym tekstem, więc filtr po adresie
/// źródłowym jest tu jedyną barierą wewnątrz aplikacji.
/// </summary>
public sealed class NetworkAccessPolicy
{
    private readonly List<(IPAddress Network, int PrefixLength)> _allowed;

    private NetworkAccessPolicy(
        List<(IPAddress, int)> allowed,
        IReadOnlyList<string> allowedNetworks,
        IReadOnlyList<string> invalidEntries)
    {
        _allowed = allowed;
        AllowedNetworks = allowedNetworks;
        InvalidEntries = invalidEntries;
    }

    /// <summary>Poprawnie odczytane wpisy, w formie z konfiguracji.</summary>
    public IReadOnlyList<string> AllowedNetworks { get; }

    /// <summary>Wpisy, których nie dało się sparsować - do zgłoszenia w logu.</summary>
    public IReadOnlyList<string> InvalidEntries { get; }

    /// <summary>
    /// Brak skonfigurowanych sieci oznacza dostęp wyłącznie z tej maszyny. Pusta lista nie
    /// może znaczyć "wpuszczaj wszystkich" - po zmianie nasłuchu na wszystkie interfejsy
    /// takie zachowanie otwierałoby tokeny dla całej sieci przy pierwszej instalacji.
    /// </summary>
    public bool AllowsOnlyLoopback => _allowed.Count == 0;

    public static NetworkAccessPolicy Create(IEnumerable<string?>? networks)
    {
        var allowed = new List<(IPAddress, int)>();
        var accepted = new List<string>();
        var invalid = new List<string>();

        foreach (var entry in networks ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var trimmed = entry.Trim();

            if (TryParseCidr(trimmed, out var network, out var prefixLength))
            {
                allowed.Add((network, prefixLength));
                accepted.Add(trimmed);
            }
            else
            {
                invalid.Add(trimmed);
            }
        }

        return new NetworkAccessPolicy(allowed, accepted, invalid);
    }

    public bool IsAllowed(IPAddress? address)
    {
        if (address is null)
        {
            // Bez adresu źródłowego nie ma jak potwierdzić uprawnienia.
            return false;
        }

        // Gniazdo dual-stack podaje adresy IPv4 w formie ::ffff:a.b.c.d.
        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        // Menedżer i narzędzia diagnostyczne wołają API z tej samej maszyny - pętla
        // zwrotna jest dozwolona zawsze, niezależnie od konfiguracji.
        if (IPAddress.IsLoopback(candidate))
        {
            return true;
        }

        foreach (var (network, prefixLength) in _allowed)
        {
            if (IsInSubnet(candidate, network, prefixLength))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseCidr(string value, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;

        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);

        if (!IPAddress.TryParse(parts[0], out var parsed))
        {
            return false;
        }

        var maxPrefixLength = parsed.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;

        if (parts.Length == 1)
        {
            // Sam adres bez maski to pojedynczy host.
            network = parsed;
            prefixLength = maxPrefixLength;
            return true;
        }

        if (!int.TryParse(parts[1], out var parsedPrefix) || parsedPrefix < 0 || parsedPrefix > maxPrefixLength)
        {
            return false;
        }

        network = parsed;
        prefixLength = parsedPrefix;
        return true;
    }

    private static bool IsInSubnet(IPAddress address, IPAddress network, int prefixLength)
    {
        if (address.AddressFamily != network.AddressFamily)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var index = 0; index < fullBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xFF << (8 - remainingBits));

        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }
}
