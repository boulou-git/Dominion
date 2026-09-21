using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

/// <summary>
/// Encodes replicated game-state JSON as binary payload so Photon does not hit Protocol18's
/// 32,767-byte limit for a single string. Supports raw UTF-8 and gzip-compressed UTF-8.
/// </summary>
public static class NetworkGameStatePhotonPayload
{
    private const byte RawUtf8Format = 1;
    private const byte GzipUtf8Format = 2;
    // Application safety bound, not a Photon transport guarantee. Keep encode/decode
    // symmetric so a compressed payload cannot allocate unbounded memory.
    public const int MaxDecodedBytes = 8 * 1024 * 1024;

    public static byte[] Encode(string json)
    {
        if (json == null) return null;
        if (Encoding.UTF8.GetByteCount(json) > MaxDecodedBytes) return null;

        byte[] utf8 = Encoding.UTF8.GetBytes(json);
        if (utf8.Length < 1024)
            return WithHeader(RawUtf8Format, utf8);

        using (MemoryStream output = new MemoryStream())
        {
            output.WriteByte(GzipUtf8Format);
            using (GZipStream gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                gzip.Write(utf8, 0, utf8.Length);
            return output.ToArray();
        }
    }

    public static bool TryDecode(object value, out string json)
    {
        json = null;

        // Backward compatibility with rooms created before the binary payload migration.
        if (value is string legacyJson)
        {
            if (Encoding.UTF8.GetByteCount(legacyJson) > MaxDecodedBytes) return false;
            json = legacyJson;
            return true;
        }

        byte[] payload = value as byte[];
        if (payload == null || payload.Length == 0)
            return false;

        try
        {
            byte format = payload[0];
            switch (format)
            {
                case RawUtf8Format:
                    if (payload.Length - 1 > MaxDecodedBytes) return false;
                    json = Encoding.UTF8.GetString(payload, 1, payload.Length - 1);
                    return true;

                case GzipUtf8Format:
                    using (MemoryStream input = new MemoryStream(payload, 1, payload.Length - 1, false))
                    using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                    using (MemoryStream output = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192];
                        int read;
                        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (output.Length + read > MaxDecodedBytes) return false;
                            output.Write(buffer, 0, read);
                        }
                        json = Encoding.UTF8.GetString(output.ToArray());
                        return true;
                    }

                default:
                    Debug.LogError("Unsupported replicated game-state payload format: " + format);
                    return false;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("Failed to decode replicated game-state payload: " + exception.Message);
            json = null;
            return false;
        }
    }

    private static byte[] WithHeader(byte format, byte[] body)
    {
        byte[] payload = new byte[body.Length + 1];
        payload[0] = format;
        Buffer.BlockCopy(body, 0, payload, 1, body.Length);
        return payload;
    }
}
