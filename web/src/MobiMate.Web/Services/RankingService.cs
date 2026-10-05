using System.Security.Cryptography;
using System.Text;
using MobiMate.Web.Hosting;

namespace MobiMate.Web.Services;

/// <summary>저장 항목 하나: 종류별 순위. Rank가 null이면 "순위 없음"(그 이름이 랭킹에 없다)을 확인한 것이다.</summary>
public sealed class RankEntryData
{
    public int? Rank { get; set; }
    public long? Score { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Source { get; set; } = "bookmarklet";   // bookmarklet | manual
}

/// <summary>캐릭터 하나의 랭킹 기록. Name·ServerId는 조회한 때의 값이라, 별칭이 바뀌면 이 기록은 옛 이름의 순위다.</summary>
public sealed class RankRecord
{
    public string Name { get; set; } = "";
    public int ServerId { get; set; }
    public Dictionary<string, RankEntryData> Entries { get; set; } = new();   // 키: combat · attract · living · total
}

public sealed class RankingData
{
    /// <summary>북마크릿 전용 토큰. 같은 PC의 다른 웹 페이지가 함부로 순위를 쓰지 못하게 한다.</summary>
    public string Token { get; set; } = "";
    public Dictionary<string, RankRecord> Records { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record RankTarget(string Key, string Name, int ServerId, string ServerName, string Job, long Combat);
public sealed record RankImportItem(string? Key, string? Name, int Kind, string? Html);
public sealed record RankImportRequest(string? Token, List<RankImportItem>? Items);
public sealed record RankImportResult(int Accepted, int Rejected, List<string> Problems);

public sealed record RankEntryView(int? Rank, long? Score, string Tier, DateTimeOffset At, string Source, bool Stale);
public sealed record RankView(string Key, string? Name, string? ServerName, bool HasName, bool Supported, bool NameChanged, Dictionary<string, RankEntryView> Entries);
public sealed record RankBadge(int Rank, string Tier, bool Stale, DateTimeOffset At);

/// <summary>
/// 서버 랭킹 저장·조회 (v0.3). 앱이 넥슨 랭킹 페이지를 스스로 부르지 않는다(자동 접속 차단 장치가 있어 우회하지 않는다):
/// 값은 사용자가 자기 브라우저에서 북마크릿을 눌렀을 때, 또는 화면에서 직접 적었을 때만 들어온다.
/// 순위는 별칭(사용자가 입력한 캐릭터 이름)과 서버로 찾은 값이라, 별칭이나 서버가 바뀌면 그 기록은 쓰지 않는다.
/// </summary>
public sealed class RankingService
{
    public const string FileName = "rankings.json";
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(6);
    public const string RankingUrl = "https://mabinogimobile.nexon.com/Ranking/List?t=1";

    private readonly JsonFileStore _store = new();
    private readonly string _path;
    private readonly SnapshotManager _snapshots;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _lock = new();
    private RankingData _data;

    public RankingService(string storageDir, SnapshotManager snapshots, Func<DateTimeOffset>? now = null)
    {
        _path = Path.Combine(storageDir, FileName);
        _snapshots = snapshots;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _data = _store.Load<RankingData>(_path) ?? new RankingData();
        _data.Records = new Dictionary<string, RankRecord>(_data.Records ?? new(), StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(_data.Token))
        {
            _data.Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
            _store.Save(_path, _data);
        }
    }

    public static string KeyOf(RankKind k) => k switch { RankKind.Combat => "combat", RankKind.Attract => "attract", RankKind.Living => "living", _ => "total" };

    public static RankKind? KindOf(int t) => Enum.IsDefined(typeof(RankKind), t) ? (RankKind)t : null;

    public bool TokenOk(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        lock (_lock) return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(_data.Token));
    }

    public string Token { get { lock (_lock) return _data.Token; } }

    /// <summary>순위를 가져올 캐릭터: 별칭이 있고 서버를 아는 캐릭터. 같은 서버에서 같은 별칭을 쓰는 캐릭터가 여럿이면 처음 하나만 (같은 서버 같은 이름은 한 캐릭터다).</summary>
    public List<RankTarget> Targets()
    {
        var result = new List<RankTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in _snapshots.GetAllProfiles()
                     .Where(p => !(p.RealmName == "에린" && p.JobName == "밀레시안") && p.History.Any(h => h.Level > 0))
                     .OrderBy(p => p.CharacterKey, StringComparer.Ordinal))
        {
            var name = p.CustomName?.Trim();
            if (string.IsNullOrEmpty(name) || RankingServers.IdOf(p.RealmName) is not { } sid) continue;
            if (!seen.Add($"{sid}|{name}")) continue;
            result.Add(new RankTarget(p.CharacterKey, name, sid, p.RealmName, p.JobName, p.History.LastOrDefault()?.CombatScore ?? 0));
        }
        return result;
    }

    /// <summary>북마크릿이 보낸 결과를 저장한다. 조회 대상이 아니거나 이름·서버가 어긋나거나 형식이 다른 항목은 건너뛴다.</summary>
    public RankImportResult Import(IEnumerable<RankImportItem> items)
    {
        var targets = Targets().ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        int accepted = 0, rejected = 0;
        lock (_lock)
        {
            var next = Clone(_data);
            foreach (var it in items)
            {
                string? why = null;
                RankTarget? t = null;
                if (it.Key == null || !targets.TryGetValue(it.Key, out t)) why = "조회 대상이 아닌 캐릭터";
                else if (KindOf(it.Kind) is not { } kind) why = "알 수 없는 종류";
                else if (it.Name != null && it.Name != t.Name) why = $"{t.Name}: 조회한 이름이 지금 별칭과 다름";
                else if (RankingParser.Parse(it.Html, kind) is not { } reading) why = $"{t.Name}: 응답 형식을 읽지 못함";
                else if (reading.Found && !SameCharacter(reading, t)) why = $"{t.Name}: 같은 이름의 다른 캐릭터로 보임";
                else
                {
                    Put(next, t, kind, reading.Found ? reading.Rank : null, reading.Found ? reading.Score : null, "bookmarklet");
                    accepted++;
                    continue;
                }
                rejected++;
                if (problems.Count < 10) problems.Add(why!);
            }
            if (accepted > 0 && !_store.Save(_path, next)) return new RankImportResult(0, accepted + rejected, new List<string> { "저장하지 못했습니다" });
            if (accepted > 0) _data = next;
        }
        return new RankImportResult(accepted, rejected, problems);
    }

    /// <summary>화면에서 직접 적은 순위. rank가 null이면 "순위 없음"으로 기록한다.</summary>
    public (bool Ok, string? Error) SetManual(string key, int kindValue, int? rank, long? score)
    {
        if (KindOf(kindValue) is not { } kind) return (false, "알 수 없는 종류입니다.");
        if (rank is <= 0) return (false, "순위는 1 이상이어야 합니다.");
        if (score is < 0) return (false, "점수가 올바르지 않습니다.");
        var t = Targets().FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        if (t == null) return (false, "캐릭터 이름(별칭)이 있는 캐릭터만 입력할 수 있습니다.");
        lock (_lock)
        {
            var next = Clone(_data);
            Put(next, t, kind, rank, score, "manual");
            if (!_store.Save(_path, next)) return (false, "저장하지 못했습니다.");
            _data = next;
        }
        return (true, null);
    }

    /// <summary>입력한 순위 하나를 지운다.</summary>
    public bool Clear(string key, int kindValue)
    {
        if (KindOf(kindValue) is not { } kind) return false;
        lock (_lock)
        {
            if (!_data.Records.TryGetValue(key, out var rec) || !rec.Entries.ContainsKey(KeyOf(kind))) return true;
            var next = Clone(_data);
            next.Records[key].Entries.Remove(KeyOf(kind));
            if (next.Records[key].Entries.Count == 0) next.Records.Remove(key);
            if (!_store.Save(_path, next)) return false;
            _data = next;
            return true;
        }
    }

    /// <summary>캐릭터 한 명의 랭킹 보기. 지금 별칭·서버와 다른 이름으로 조회한 값이면 NameChanged이고 순위는 비운다.</summary>
    public RankView ViewOf(string key)
    {
        var profile = _snapshots.GetProfileByKey(key);
        var name = string.IsNullOrWhiteSpace(profile?.CustomName) ? null : profile!.CustomName.Trim();
        var sid = RankingServers.IdOf(profile?.RealmName);
        RankRecord? rec;
        lock (_lock) _data.Records.TryGetValue(key, out rec);
        var entries = new Dictionary<string, RankEntryView>();
        var changed = false;
        if (rec != null)
        {
            changed = rec.Name != name || rec.ServerId != sid;
            if (!changed)
                foreach (var (k, e) in rec.Entries)
                    entries[k] = new RankEntryView(e.Rank, e.Score, RankTiers.Name(RankTiers.Of(e.Rank)), e.AtUtc, e.Source, _now() - e.AtUtc > FreshFor);
        }
        return new RankView(key, name, sid is { } s ? RankingServers.NameOf(s) : null, name != null, sid != null, changed, entries);
    }

    /// <summary>전투력 서버 순위 뱃지. 순위가 없거나 별칭이 바뀌었거나 10000위 밖이면 null.</summary>
    public RankBadge? BadgeOf(string key)
    {
        var v = ViewOf(key);
        if (!v.Entries.TryGetValue("combat", out var e) || e.Rank is not { } rank || RankTiers.Of(rank) == RankTier.None) return null;
        return new RankBadge(rank, RankTiers.Name(RankTiers.Of(rank)), e.Stale, e.At);
    }

    private void Put(RankingData data, RankTarget t, RankKind kind, int? rank, long? score, string source)
    {
        if (!data.Records.TryGetValue(t.Key, out var rec) || rec.Name != t.Name || rec.ServerId != t.ServerId)
            data.Records[t.Key] = rec = new RankRecord { Name = t.Name, ServerId = t.ServerId };   // 이름·서버가 바뀌었으면 옛 값은 버린다
        rec.Entries[KeyOf(kind)] = new RankEntryData { Rank = rank, Score = score, AtUtc = _now(), Source = source };
    }

    /// <summary>동명이인 걸러내기: 응답의 클래스가 이 캐릭터의 직업과 다르면서, 전투력을 알 때 20% 넘게 차이 나면 다른 캐릭터다.</summary>
    private static bool SameCharacter(RankReading r, RankTarget t)
    {
        var cls = r.ClassName?.Replace(" ", "");
        if (cls == null || cls == t.Job.Replace(" ", "")) return true;
        if (r.CombatScore is not { } cs || t.Combat <= 0) return true;
        return Math.Abs(cs - t.Combat) <= t.Combat * 0.2;
    }

    private static RankingData Clone(RankingData d) => new()
    {
        Token = d.Token,
        Records = new Dictionary<string, RankRecord>(d.Records.ToDictionary(x => x.Key, x => new RankRecord
        {
            Name = x.Value.Name, ServerId = x.Value.ServerId,
            Entries = x.Value.Entries.ToDictionary(e => e.Key, e => new RankEntryData { Rank = e.Value.Rank, Score = e.Value.Score, AtUtc = e.Value.AtUtc, Source = e.Value.Source }),
        }), StringComparer.OrdinalIgnoreCase),
    };
}
