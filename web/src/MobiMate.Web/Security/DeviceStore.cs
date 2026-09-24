using System.Security.Cryptography;
using System.Text;

namespace MobiMate.Web.Security;

public sealed class DeviceRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = DeviceKinds.Local;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>기기 토큰의 SHA-256. 토큰 원문은 저장하지 않는다 (SEC-05).</summary>
    public string Hash { get; set; } = "";
}

public static class DeviceKinds
{
    public const string Local = "local";
    public const string Lan = "lan";
}

/// <summary>
/// 기기 토큰 저장소 (SEC-05·07, 상세설계 §3.3). devices.json에 토큰 해시와 CSRF 비밀값만 둔다.
/// LAN 기기는 30일 뒤 만료(SEC-10), 로컬 기기는 만료 없음.
/// </summary>
public sealed class DeviceStore
{
    public const string FileName = "devices.json";
    public static readonly TimeSpan LanLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    private readonly JsonFileStore _store = new();
    private readonly string _path;
    private readonly object _lock = new();
    private readonly DeviceFile _file;
    private readonly Dictionary<string, DeviceRecord> _byHash;

    public DeviceStore(string storageDir)
    {
        _path = Path.Combine(storageDir, FileName);
        // 백신·백업 프로그램이 잠깐 잠근 경우를 넘기려고 몇 번 다시 읽는다. 빈 목록으로 덮어쓰면 모든 기기가 풀리므로 끝내 못 읽으면 기동을 멈춘다.
        var status = _store.TryLoad<DeviceFile>(_path, out var loaded);
        for (var i = 0; status == LoadStatus.Failed && i < 5; i++)
        {
            Thread.Sleep(200);
            status = _store.TryLoad(_path, out loaded);
        }
        if (status == LoadStatus.Failed) throw new IOException($"기기 목록을 읽을 수 없습니다: {_path}");
        _file = loaded ?? new DeviceFile();
        if (string.IsNullOrEmpty(_file.Secret))
        {
            _file.Secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _store.Save(_path, _file);
        }
        _byHash = _file.Devices.ToDictionary(d => d.Hash, StringComparer.Ordinal);
    }

    /// <summary>새 기기를 등록하고 토큰 원문을 돌려준다(쿠키로만 전달, 저장하지 않음).</summary>
    public (DeviceRecord Device, string Token) Issue(string name, string kind, DateTimeOffset now)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var device = new DeviceRecord
        {
            Id = Base64Url(RandomNumberGenerator.GetBytes(9)),
            Name = string.IsNullOrWhiteSpace(name) ? "알 수 없는 기기" : name.Trim()[..Math.Min(name.Trim().Length, 60)],
            Kind = kind,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = kind == DeviceKinds.Lan ? now + LanLifetime : null,
            Hash = HashToken(token),
        };
        lock (_lock)
        {
            _file.Devices.Add(device);
            _byHash[device.Hash] = device;
            Save();
        }
        return (device, token);
    }

    /// <summary>토큰으로 기기를 찾는다. 만료됐으면 지우고 null.</summary>
    public DeviceRecord? Authenticate(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200) return null;
        var hash = HashToken(token);
        lock (_lock)
        {
            if (!_byHash.TryGetValue(hash, out var d)) return null;
            if (d.ExpiresAt is { } exp && exp <= now)
            {
                RemoveLocked(d);
                Save();
                return null;
            }
            if (now - d.LastSeenAt >= TouchInterval)
            {
                d.LastSeenAt = now;
                Save();
            }
            return d;
        }
    }

    public bool Exists(string id)
    {
        lock (_lock) return _file.Devices.Any(d => d.Id == id);
    }

    public IReadOnlyList<DeviceRecord> List()
    {
        lock (_lock) return _file.Devices.OrderByDescending(d => d.LastSeenAt).ToList();
    }

    public bool Revoke(string id)
    {
        lock (_lock)
        {
            var d = _file.Devices.FirstOrDefault(x => x.Id == id);
            if (d == null) return false;
            RemoveLocked(d);
            Save();
            return true;
        }
    }

    /// <summary>만료된 기기를 지우고 지운 ID를 돌려준다 (정리 작업, 상세설계 §3.3).</summary>
    public IReadOnlyList<string> SweepExpired(DateTimeOffset now)
    {
        lock (_lock)
        {
            var expired = _file.Devices.Where(d => d.ExpiresAt is { } e && e <= now).ToList();
            foreach (var d in expired) RemoveLocked(d);
            if (expired.Count > 0) Save();
            return expired.Select(d => d.Id).ToList();
        }
    }

    /// <summary>기기별 CSRF 값 = HMAC(서버 비밀값, 기기 ID) (SEC-04).</summary>
    public string CsrfFor(string deviceId)
    {
        using var h = new HMACSHA256(Convert.FromBase64String(_file.Secret));
        return Base64Url(h.ComputeHash(Encoding.UTF8.GetBytes(deviceId)));
    }

    public bool CheckCsrf(string deviceId, string? value) =>
        value != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(CsrfFor(deviceId)), Encoding.UTF8.GetBytes(value));

    private void RemoveLocked(DeviceRecord d)
    {
        _file.Devices.Remove(d);
        _byHash.Remove(d.Hash);
    }

    private void Save() => _store.Save(_path, _file);

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class DeviceFile
    {
        public string Secret { get; set; } = "";
        public List<DeviceRecord> Devices { get; set; } = new();
    }
}
