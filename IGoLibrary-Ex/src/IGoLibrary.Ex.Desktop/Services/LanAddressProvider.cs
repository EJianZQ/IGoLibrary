using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class LanAddressProvider : ILanAddressProvider
{
    private static readonly string[] VirtualAdapterKeywords =
    [
        "virtual",
        "vmware",
        "hyper-v",
        "vethernet",
        "docker",
        "wsl",
        "vpn",
        "tap",
        "tun",
        "virtualbox",
        "zerotier",
        "tailscale",
        "npcap"
    ];

    public IPAddress? GetPrimaryLanAddress()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(static adapter =>
                adapter.OperationalStatus == OperationalStatus.Up &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .SelectMany(CreateCandidates);

        return SelectPrimaryLanAddress(candidates);
    }

    internal static IPAddress? SelectPrimaryLanAddress(IEnumerable<LanAddressCandidate> candidates)
    {
        return candidates
            .Where(static candidate =>
                candidate.Address.AddressFamily == AddressFamily.InterNetwork &&
                IsPrivateIPv4(candidate.Address))
            .OrderBy(static candidate => candidate.IsLikelyVirtual)
            .ThenByDescending(static candidate => candidate.HasGateway)
            .ThenByDescending(static candidate => GetInterfacePriority(candidate.InterfaceType))
            .ThenByDescending(static candidate => candidate.Speed)
            .Select(static candidate => candidate.Address)
            .FirstOrDefault();
    }

    private static IEnumerable<LanAddressCandidate> CreateCandidates(NetworkInterface adapter)
    {
        IPInterfaceProperties properties;
        try
        {
            properties = adapter.GetIPProperties();
        }
        catch (NetworkInformationException)
        {
            yield break;
        }

        var hasGateway = properties.GatewayAddresses
            .Any(static gateway => IsUsableIPv4Gateway(gateway.Address));
        var isLikelyVirtual = IsLikelyVirtualAdapter(adapter);
        var speed = adapter.Speed;

        foreach (var address in properties.UnicastAddresses)
        {
            yield return new LanAddressCandidate(
                address.Address,
                adapter.NetworkInterfaceType,
                hasGateway,
                isLikelyVirtual,
                speed);
        }
    }

    private static int GetInterfacePriority(NetworkInterfaceType interfaceType)
    {
        return interfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => 4,
            NetworkInterfaceType.Ethernet => 3,
            NetworkInterfaceType.GigabitEthernet => 3,
            NetworkInterfaceType.FastEthernetFx => 3,
            NetworkInterfaceType.FastEthernetT => 3,
            NetworkInterfaceType.Ppp => 1,
            _ => 0
        };
    }

    private static bool IsLikelyVirtualAdapter(NetworkInterface adapter)
    {
        return ContainsVirtualKeyword(adapter.Name) ||
               ContainsVirtualKeyword(adapter.Description);
    }

    private static bool ContainsVirtualKeyword(string text)
    {
        return VirtualAdapterKeywords.Any(keyword =>
            text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUsableIPv4Gateway(IPAddress address)
    {
        return address.AddressFamily == AddressFamily.InterNetwork &&
               !IPAddress.Any.Equals(address) &&
               !IPAddress.Loopback.Equals(address);
    }

    private static bool IsPrivateIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes is [10, _, _, _] ||
               bytes is [172, >= 16 and <= 31, _, _] ||
               bytes is [192, 168, _, _];
    }
}

internal sealed record LanAddressCandidate(
    IPAddress Address,
    NetworkInterfaceType InterfaceType,
    bool HasGateway,
    bool IsLikelyVirtual,
    long Speed);
