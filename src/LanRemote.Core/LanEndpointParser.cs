using System.Net;

namespace LanRemote.Core;

public static class LanEndpointParser
{
    public static IPEndPoint Parse(string value, int defaultPort = 45873)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("請輸入區網 IPv4 位址。");
        }

        string[] parts = value.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || !IPAddress.TryParse(parts[0], out IPAddress? address))
        {
            throw new FormatException("位址格式應為 192.168.x.x:連接埠。");
        }

        int port = defaultPort;
        if (parts.Length == 2 && (!int.TryParse(parts[1], out port) || port is < 1024 or > 65535))
        {
            throw new FormatException("連接埠必須介於 1024 與 65535。");
        }

        if (!LanAddressPolicy.IsAllowed(address))
        {
            throw new FormatException("MVP 只允許 loopback 或 RFC1918／link-local IPv4 位址。");
        }

        return new IPEndPoint(address, port);
    }
}
