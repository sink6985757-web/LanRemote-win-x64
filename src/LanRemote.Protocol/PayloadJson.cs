using System.Text.Json;

namespace LanRemote.Protocol;

public static class PayloadJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T Deserialize<T>(ReadOnlySpan<byte> payload)
    {
        T? value = JsonSerializer.Deserialize<T>(payload, Options);
        return value ?? throw new ProtocolException($"Unable to deserialize {typeof(T).Name}.");
    }
}
