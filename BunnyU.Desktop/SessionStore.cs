using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using BunnyU.Core;

namespace BunnyU.Desktop;

public sealed class SessionStore
{
    private readonly string _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BunnyU.Windows");
    private string SessionPath => Path.Combine(_folder,"session.dat");
    public string DeviceId { get; }
    public SessionStore()
    {
        Directory.CreateDirectory(_folder);
        var idFile = Path.Combine(_folder,"device-id.txt");
        var existing = File.Exists(idFile) ? File.ReadAllText(idFile).Trim() : "";
        DeviceId = existing.Length == 16 && existing.All(Uri.IsHexDigit) ? existing : Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        if (DeviceId != existing) File.WriteAllText(idFile,DeviceId);
    }
    public UserSession? Load()
    {
        try
        {
            if (!File.Exists(SessionPath)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(SessionPath), null, DataProtectionScope.CurrentUser);
            try { var session=JsonSerializer.Deserialize<UserSession>(bytes);return session is { UserId: > 0 } && !string.IsNullOrWhiteSpace(session.Token) && session.Mobile is not null ? session:null; }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException) { return null; }
    }
    public void Save(UserSession session)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            var encrypted=ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser);
            var temp=SessionPath+".tmp";File.WriteAllBytes(temp,encrypted);File.Move(temp,SessionPath,true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Clear() { if(File.Exists(SessionPath)) File.Delete(SessionPath); }
}
