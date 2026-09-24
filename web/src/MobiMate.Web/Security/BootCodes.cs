using System.Security.Cryptography;

namespace MobiMate.Web.Security;

/// <summary>기동 코드 (SEC-06): 브라우저 자동 열기·트레이 "브라우저 열기"에 쓰는 1회용 60초 코드. 메모리에만 둔다.</summary>
public sealed class BootCodes
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);
    private readonly Dictionary<string, DateTimeOffset> _codes = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public string Issue(DateTimeOffset now)
    {
        var code = DeviceStore.Base64Url(RandomNumberGenerator.GetBytes(18));
        lock (_lock)
        {
            foreach (var k in _codes.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList()) _codes.Remove(k);
            _codes[code] = now + Lifetime;
        }
        return code;
    }

    /// <summary>한 번만 쓸 수 있다. 만료됐거나 이미 썼으면 false.</summary>
    public bool Consume(string? code, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(code)) return false;
        lock (_lock)
        {
            if (!_codes.Remove(code, out var exp)) return false;
            return exp > now;
        }
    }
}

public enum PairingResult { Ok, Invalid, Expired, Locked }

/// <summary>
/// 페어링 (SEC-07): 6자리 코드, 5분 만료, 1회용. 코드별 실패 5회면 코드 무효, IP별 실패 5회면 10분 잠금.
/// 새로 시작하면 이전 코드는 무효가 된다.
/// </summary>
public sealed class PairingService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(10);
    public const int MaxFailures = 5;

    private readonly object _lock = new();
    private string? _code;
    private DateTimeOffset _expiresAt;
    private int _codeFailures;
    private readonly Dictionary<string, (int Count, DateTimeOffset First, DateTimeOffset? LockedUntil)> _ipFailures = new();

    public (string Code, DateTimeOffset ExpiresAt) Start(DateTimeOffset now)
    {
        lock (_lock)
        {
            _code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            _expiresAt = now + CodeLifetime;
            _codeFailures = 0;
            return (_code, _expiresAt);
        }
    }

    public PairingResult Confirm(string? code, string ip, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_ipFailures.TryGetValue(ip, out var f) && f.LockedUntil is { } until && until > now) return PairingResult.Locked;

            var ok = _code != null && code != null && code.Length == 6 &&
                     CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(_code), System.Text.Encoding.ASCII.GetBytes(code));
            if (ok && _expiresAt <= now)
            {
                _code = null;
                return PairingResult.Expired;
            }
            if (ok)
            {
                _code = null;               // 1회용
                _ipFailures.Remove(ip);
                return PairingResult.Ok;
            }

            RecordFailure(ip, now);
            if (_code != null && ++_codeFailures >= MaxFailures) _code = null;   // 코드 무효
            return _ipFailures[ip].LockedUntil is { } u && u > now ? PairingResult.Locked : PairingResult.Invalid;
        }
    }

    private void RecordFailure(string ip, DateTimeOffset now)
    {
        var (count, first, _) = _ipFailures.TryGetValue(ip, out var f) && now - f.First < LockDuration ? f : (0, now, (DateTimeOffset?)null);
        count++;
        _ipFailures[ip] = (count, first, count >= MaxFailures ? now + LockDuration : null);
    }
}
