using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrossConnect.Core.Pairing;

/// <summary>
/// Stores trusted device data and local identity in AppData\Roaming\CrossConnect.
///
/// Security:
/// - The file is encrypted with Windows DPAPI (Data Protection API) before writing.
/// - DPAPI ties encryption to the current Windows user account; the file cannot be
///   decrypted on another machine or by another user account.
/// - DataProtectionScope.CurrentUser is used so no admin rights are required.
///
/// File layout (after DPAPI decryption):
/// {
///   "identity": { ... },
///   "trustedDevices": [ ... ]
/// }
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class TrustedDeviceStore : ITrustedDeviceStore
{
    private static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "CrossConnect");

    private static readonly string StoreFile  = Path.Combine(AppDataDir, "trusted_devices.dpapi");
    private static readonly string IdentityFile = Path.Combine(AppDataDir, "identity.dpapi");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _lock = new();

    // Lazily loaded in-memory cache.
    private DeviceIdentity?        _identity;
    private List<TrustedDevice>?   _devices;

    // ── ITrustedDeviceStore ───────────────────────────────────────────────────

    public DeviceIdentity GetLocalIdentity()
    {
        lock (_lock)
        {
            if (_identity is not null) return _identity;

            _identity = LoadIdentity();
            return _identity;
        }
    }

    public IReadOnlyList<TrustedDevice> GetAll()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _devices!.AsReadOnly();
        }
    }

    public bool IsTrusted(string deviceId)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _devices!.Any(d => d.DeviceId == deviceId);
        }
    }

    public void AddOrUpdate(TrustedDevice device)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var existing = _devices!.FirstOrDefault(d => d.DeviceId == device.DeviceId);
            if (existing is not null)
            {
                existing.DeviceName = device.DeviceName;
                existing.LastSeenAt = device.LastSeenAt;
            }
            else
            {
                _devices!.Add(device);
            }
            Persist();
        }
    }

    public void Remove(string deviceId)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _devices!.RemoveAll(d => d.DeviceId == deviceId);
            Persist();
        }
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void EnsureLoaded()
    {
        if (_devices is not null) return;
        _devices = LoadDevices();
    }

    private DeviceIdentity LoadIdentity()
    {
        EnsureDirectory();
        if (!File.Exists(IdentityFile))
        {
            var newIdentity = new DeviceIdentity();
            SaveEncrypted(IdentityFile, JsonSerializer.Serialize(newIdentity, JsonOptions));
            return newIdentity;
        }

        try
        {
            string json = LoadDecrypted(IdentityFile);
            return JsonSerializer.Deserialize<DeviceIdentity>(json, JsonOptions)
                   ?? new DeviceIdentity();
        }
        catch
        {
            // Corrupted file — regenerate identity.
            var newIdentity = new DeviceIdentity();
            SaveEncrypted(IdentityFile, JsonSerializer.Serialize(newIdentity, JsonOptions));
            return newIdentity;
        }
    }

    private List<TrustedDevice> LoadDevices()
    {
        EnsureDirectory();
        if (!File.Exists(StoreFile)) return new List<TrustedDevice>();

        try
        {
            string json = LoadDecrypted(StoreFile);
            return JsonSerializer.Deserialize<List<TrustedDevice>>(json, JsonOptions)
                   ?? new List<TrustedDevice>();
        }
        catch
        {
            return new List<TrustedDevice>();
        }
    }

    private void Persist()
    {
        EnsureDirectory();
        string json = JsonSerializer.Serialize(_devices, JsonOptions);
        SaveEncrypted(StoreFile, json);
    }

    private static void EnsureDirectory() => Directory.CreateDirectory(AppDataDir);

    // ── DPAPI helpers ─────────────────────────────────────────────────────────

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void SaveEncrypted(string path, string plaintext)
    {
        byte[] plainBytes     = Encoding.UTF8.GetBytes(plaintext);
        byte[] encryptedBytes = ProtectedData.Protect(
            plainBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encryptedBytes);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string LoadDecrypted(string path)
    {
        byte[] encryptedBytes = File.ReadAllBytes(path);
        byte[] plainBytes     = ProtectedData.Unprotect(
            encryptedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
