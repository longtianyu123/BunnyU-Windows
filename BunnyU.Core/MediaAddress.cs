using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BunnyU.Core;

public static class MediaAddress
{
    // App protocol: Base64(16-byte IV + AES-256-CBC/PKCS7 payload), UTF-8 key.
    // This resolves addresses supplied by the API; access/trial checks remain separate.
    private const string ProtocolKey = "7F7BA3C95B3689637B69183F8E6AC147";
    public static string ResolveDownload(JsonElement chapter,JsonElement sound)
    {
        foreach(var source in new[]{chapter,sound})
            foreach(var key in new[]{"url_mp3_normal","url_mp3_higher","url_flac","url"})
            {
                var url=ResponseParser.Text(source,key);
                if(IsDirectAudio(url))return url;
            }
        var payload=ResponseParser.Text(sound,"urlBody");
        if(payload.Length>0)
            try
            {
                var plain=DecodePayload(payload);
                if(IsDirectAudio(plain))return plain;
                using var doc=JsonDocument.Parse(plain);
                foreach(var key in new[]{"url_mp3_normal","url_mp3_higher","url_flac","url"})
                {var url=ResponseParser.Text(doc.RootElement,key);if(IsDirectAudio(url))return url;}
            }
            catch(Exception ex)when(ex is FormatException or CryptographicException or JsonException or DecoderFallbackException){}
        return "";
    }
    public static bool IsDirectAudio(string? url)=>IsHttp(url)&&!new Uri(url!).AbsolutePath.EndsWith(".m3u8",StringComparison.OrdinalIgnoreCase)&&!new Uri(url!).AbsolutePath.EndsWith(".m3u",StringComparison.OrdinalIgnoreCase);
    public static string Resolve(JsonElement chapter, JsonElement sound)
    {
        foreach (var source in new[] { chapter, sound })
            foreach (var key in new[] { "url_m3u8_normal", "url_mp3_normal", "url", "url_mp3_higher", "url_flac" })
                if (IsHttp(ResponseParser.Text(source, key))) return ResponseParser.Text(source, key);
        var payload = ResponseParser.Text(sound, "urlBody");
        if (payload.Length == 0) return "";
        try
        {
            var plain = DecodePayload(payload);
            if (IsHttp(plain)) return plain;
            using var json = JsonDocument.Parse(plain);
            foreach (var key in new[] { "url_mp3_normal", "url_mp3_higher", "url_flac" })
            {
                var url = ResponseParser.Text(json.RootElement, key);
                if (IsHttp(url)) return url;
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or DecoderFallbackException) { }
        return "";
    }
    public static string DecodePayload(string payload)
    {
        var bytes = Convert.FromBase64String(payload);
        if (bytes.Length < 32 || (bytes.Length - 16) % 16 != 0) throw new CryptographicException("Invalid media address envelope");
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(ProtocolKey);
        return new UTF8Encoding(false, true).GetString(aes.DecryptCbc(bytes.AsSpan(16), bytes.AsSpan(0, 16), PaddingMode.PKCS7));
    }
    public static bool IsHttp(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && !string.IsNullOrEmpty(uri.Host);
}
