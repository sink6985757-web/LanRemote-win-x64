using System.Security.Cryptography;
using System.Text;

namespace LanRemote.Core;

public static class FileTransferIdentity
{
    public static Guid CreateSeed(string direction, string destination, IEnumerable<string> sourcePaths)
    {
        string canonical = string.Join(
            "\n",
            sourcePaths.Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{direction.ToUpperInvariant()}\n{Path.GetFullPath(destination).ToUpperInvariant()}\n{canonical.ToUpperInvariant()}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static Guid WithManifest(Guid seed, string manifestSha256)
    {
        if (seed == Guid.Empty || manifestSha256.Length != 64 || !manifestSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("續傳識別碼或 manifest SHA-256 無效。");
        }

        Span<byte> identity = stackalloc byte[48];
        seed.TryWriteBytes(identity[..16]);
        Convert.FromHexString(manifestSha256).CopyTo(identity[16..]);
        byte[] hash = SHA256.HashData(identity);
        return new Guid(hash.AsSpan(0, 16));
    }
}
