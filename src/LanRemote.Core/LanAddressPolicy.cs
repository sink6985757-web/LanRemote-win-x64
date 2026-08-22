using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanRemote.Core;

public static class LanAddressPolicy
{
    public static bool IsAllowed(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 169 && bytes[1] == 254);
    }

    public static IReadOnlyList<IPAddress> GetLocalPrivateAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(info => info.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && IsAllowed(address))
            .Distinct()
            .OrderBy(address => address.ToString(), StringComparer.Ordinal)
            .ToArray();
    }
}
