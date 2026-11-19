using System.Text.Json;

namespace MobiMate.Web.Hosting;

/// <summary>
/// 캐릭터 지문 저장소 (identity_fingerprints.json) + 비교 기록(identity_shadow.jsonl). 캐릭터 식별 개선 1단계.
/// 웹앱 전용 파일이라 숙제 앱과 공유하는 기록의 형식은 바꾸지 않는다. 내용이 바뀌었을 때만 저장하고, 비교 기록은 캐릭터가 바뀌었거나 새 기록이 생겼을 때만 한 줄 남긴다.
/// </summary>
public sealed class IdentityFingerprintStore
{
    public const string FileName = "identity_fingerprints.json";
    public const string ShadowFileName = "identity_shadow.jsonl";
    private static readonly TimeSpan MinSaveInterval = TimeSpan.FromMinutes(2);

    private readonly object _lock = new();
    private readonly string _path, _shadowPath;
    private readonly JsonFileStore _store = new();
    private readonly Func<DateTime> _now;
    private Dictionary<string, IdentityFingerprint> _data = new(StringComparer.Ordinal);
    private DateTime _lastSave = DateTime.MinValue;
    private string? _lastKey;

    public IdentityFingerprintStore(string storageDir, Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        _path = Path.Combine(storageDir, FileName);
        _shadowPath = Path.Combine(storageDir, ShadowFileName);
        if (_store.TryLoad<Dictionary<string, IdentityFingerprint>>(_path, out var loaded) == LoadStatus.Loaded && loaded != null)
            _data = new Dictionary<string, IdentityFingerprint>(loaded, StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, IdentityFingerprint> Snapshot() { lock (_lock) return new Dictionary<string, IdentityFingerprint>(_data); }

    public IdentityFingerprint? Get(string key) { lock (_lock) return _data.GetValueOrDefault(key); }

    /// <summary>
    /// 방금 읽은 지문을 그 캐릭터 기록(<paramref name="fp"/>.Key)의 최신 지문으로 둔다.
    /// 캐릭터가 바뀌었거나 이 기록의 지문이 처음이면, 같은 서버의 다른 기록과 비교한 결과를 비교 기록에 남긴다(판정에는 쓰지 않는다).
    /// </summary>
    public void Observe(IdentityFingerprint fp)
    {
        if (string.IsNullOrEmpty(fp.Key)) return;
        lock (_lock)
        {
            var first = !_data.ContainsKey(fp.Key);
            var switched = _lastKey != null && _lastKey != fp.Key;
            if (first || switched || _lastKey == null) WriteShadow(fp, first);
            _lastKey = fp.Key;

            var prev = _data.GetValueOrDefault(fp.Key);
            // 생활 스킬을 못 읽은 지문은 이미 읽은 값을 지우지 않는다
            if (!fp.HasSkills && prev is { HasSkills: true }) { fp.Skills = prev.Skills; }
            if (prev != null && prev.SameContent(fp)) return;
            if (!first && _now() - _lastSave < MinSaveInterval) { _data[fp.Key] = fp; return; }   // 저장은 2분에 한 번만(메모리는 항상 최신)
            _data[fp.Key] = fp;
            _store.Save(_path, _data);
            _lastSave = _now();
        }
    }

    private void WriteShadow(IdentityFingerprint now, bool first)
    {
        try
        {
            var others = _data.Values.Where(o => o.Key != now.Key && o.Realm == now.Realm).ToList();
            var rows = others.Select(o => new
            {
                key = o.Key, job = o.Job, level = o.Level, combat = o.Combat, deca = o.Deca, mcash = o.MCash,
                match = FingerprintComparer.Compare(o, now),
            }).ToList();
            var line = JsonSerializer.Serialize(new
            {
                at = _now(), key = now.Key, newRecord = first, realm = now.Realm, job = now.Job, level = now.Level, combat = now.Combat, deca = now.Deca, mcash = now.MCash,
                skills = now.Skills.Count, charStorage = now.CharStorage.Count, accountStorage = now.AccountStorage.Count, vs = rows,
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            File.AppendAllText(_shadowPath, line + Environment.NewLine);
        }
        catch (IOException) { }   // 비교 기록은 덤이다: 실패해도 앱 동작에는 영향이 없다
    }
}
