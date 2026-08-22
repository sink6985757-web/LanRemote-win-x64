using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanRemote.Core;

public static class PairingCode
{
    public static string Compute(
        X509Certificate2 certificate,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (clientNonce.Length != 32 || serverNonce.Length != 32)
        {
            throw new ArgumentException("Pairing nonces must contain exactly 32 bytes.");
        }

        byte[] certificateHash = SHA256.HashData(certificate.RawData);
        byte[] material = new byte[certificateHash.Length + clientNonce.Length + serverNonce.Length];
        certificateHash.CopyTo(material, 0);
        clientNonce.CopyTo(material.AsSpan(certificateHash.Length));
        serverNonce.CopyTo(material.AsSpan(certificateHash.Length + clientNonce.Length));
        byte[] digest = SHA256.HashData(material);
        uint numeric = BinaryPrimitives.ReadUInt32BigEndian(digest) % 1_000_000;
        CryptographicOperations.ZeroMemory(material);
        return numeric.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
