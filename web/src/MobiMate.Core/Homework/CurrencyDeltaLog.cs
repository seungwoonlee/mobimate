using System.Text.Json;

namespace MobiMate;

/// <summary>
/// 재화 변화 기록 (실측용). 같은 캐릭터를 이어서 관찰하는 동안 재화 수량이 바뀐 것을 한 줄씩 남긴다:
/// 시각, 캐릭터, 위치, 바뀐 재화의 (이름, 전, 후). 레이드를 클리어하고 새로고침한 뒤 이 파일을 보면
/// 어떤 재화가 얼마나 늘었는지(예: 원정의 증거 종류별 증가량)를 난이도별로 알 수 있다.
/// 파일은 %APPDATA%\MobiMateWeb\currency_changes.jsonl. 쓰기에 실패해도 앱 동작에는 영향이 없다.
/// </summary>
public sealed class CurrencyDeltaLog
{
    private readonly string _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private string? _lastKey;
    private Dictionary<string, long>? _last;

    public CurrencyDeltaLog(string path, Func<DateTimeOffset>? now = null)
    {
        _path = path;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>이번 관찰을 반영한다. 바뀐 재화가 있으면 기록한 변화 목록을 돌려주고, 없으면 null.</summary>
    public IReadOnlyList<CurrencyChange>? Observe(string characterKey, IReadOnlyList<CurrencyItem>? currencies, string? place = null)
    {
        if (currencies is not { Count: > 0 }) return null;   // 조회 실패·빈 목록은 비교하지 않는다
        var cur = currencies.GroupBy(c => c.DisplayName).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));
        lock (_lock)
        {
            var prev = _lastKey != null && _lastKey.Equals(characterKey, StringComparison.OrdinalIgnoreCase) ? _last : null;
            _lastKey = characterKey;
            _last = cur;
            if (prev == null) return null;   // 첫 관찰·캐릭터 전환 직후: 기준값만 잡는다

            var changes = new List<CurrencyChange>();
            foreach (var (name, after) in cur)
            {
                // 이전 목록에 없던 재화는 새로 생긴 것(0 → n)으로 본다
                var before = prev.GetValueOrDefault(name);
                if (before != after) changes.Add(new CurrencyChange(name, before, after));
            }
            if (changes.Count == 0) return null;
            Append(characterKey, place, changes);
            return changes;
        }
    }

    private void Append(string key, string? place, List<CurrencyChange> changes)
    {
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                at = _now().ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                character = key,
                place,
                changes = changes.Select(c => new { name = c.Name, before = c.Before, after = c.After, diff = c.After - c.Before }),
            }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch { /* 실측 보조 기록이라 실패해도 무시 */ }
    }
}

public sealed record CurrencyChange(string Name, long Before, long After);
