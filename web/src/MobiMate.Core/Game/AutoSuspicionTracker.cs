namespace MobiMate;

/// <summary>주변 플레이어 한 명의 관찰값. 게임이 이름을 주지 않아 서버·직업·레벨·전투력·칭호로 구분한다.</summary>
public sealed record WatchedPlayer(string Key, string Realm, string Job, int Level, long CombatScore, string? Title, bool InCombat)
{
    /// <summary>같은 사람을 이어서 알아보는 표식. 둘 이상이 같으면 구분할 수 없어 판정에서 뺀다.</summary>
    public static string KeyOf(NearPcItem p) => $"{p.RealmName}|{p.JobName}|{p.Level}|{p.CombatScore}|{p.Title}";

    public static WatchedPlayer From(NearPcItem p) => new(KeyOf(p), p.RealmName, p.JobName, p.Level, p.CombatScore, p.Title, p.IsInCombat);
}

public enum AutoSuspicionReason
{
    /// <summary>다른 사람은 모두 전투 중인데 혼자 오래 전투하지 않는다.</summary>
    Idle,
    /// <summary>한동안 함께 보이다가 던전·레이드 안인데 주변에서 오래 보이지 않는다.</summary>
    Missing,
}

/// <param name="For">조건이 이어진 시간(초 카운터)</param>
/// <param name="Intensity">의심의 짙기 0..1. 카운터가 표시 기준을 넘는 순간 약하게 켜지고, 클수록 짙어져 만점 기준에서 1이 된다.</param>
public sealed record AutoSuspect(string Key, AutoSuspicionReason Reason, TimeSpan For, WatchedPlayer Player, double Intensity);

/// <summary>
/// 오토 의심 표식(순수, 2026-10-22 승운 요청). 던전·레이드 안에서 주변 플레이어를 몇 초마다 넣으면 <b>초 단위 카운터</b>를 쌓는다.
/// 조건이 시작되었다고 바로 표시하지 않고, 카운터가 표시 기준(<see cref="IdleShowFrom"/> 등)을 넘으면 의심이 켜지며 카운터가 클수록 짙어진다
/// (<see cref="AutoSuspect.Intensity"/>). 해소 요인이 생기면 카운터를 <b>바로 0으로</b> 돌려 곧바로 풀린다.
/// ① 혼자 전투하지 않음: 다른 사람이 둘 이상이고 <b>모두</b> 전투 중인데 이 사람만 전투 중이 아닌 동안 쌓는다. 이 사람이 전투하거나 다른 누군가 전투를 멈추면 0.
/// ② 안 보임: <see cref="MinSeenBeforeMissing"/> 넘게 함께 보이던 사람이 주변 목록에서 사라진 동안 쌓는다(그동안 다른 사람이 둘 이상 보일 때만). 다시 보이면 0.
/// 던전·레이드 밖이거나 관찰 사이가 <see cref="MaxGap"/>보다 길게 끊기면 모두 처음부터. 같은 표식의 사람이 둘 이상이면 구분할 수 없어 뺀다.
/// 기준 시간은 상수라 실측에 맞춰 조정한다. 스레드 안전하지 않다(호출자가 잠근다).
/// </summary>
public sealed class AutoSuspicionTracker
{
    /// <summary>혼자 전투하지 않는 카운터가 이 초를 넘으면 표시를 켠다 / 이 초에서 가장 짙다.</summary>
    public static readonly TimeSpan IdleShowFrom = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan IdleFull = TimeSpan.FromSeconds(150);
    /// <summary>안 보이는 카운터의 표시 기준 / 가장 짙은 기준.</summary>
    public static readonly TimeSpan MissingShowFrom = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MissingFull = TimeSpan.FromSeconds(240);

    public static readonly TimeSpan MinSeenBeforeMissing = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);
    /// <summary>"안 보임"으로 남겨 두는 최대 시간. 이보다 오래 안 보이면 떠난 것으로 보고 지운다.</summary>
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(30);
    public const int MinOthers = 2;

    /// <summary>카운터가 <paramref name="count"/>일 때의 짙기: 표시 기준 이전은 0, 기준에서 약하게(0.15) 시작해 만점 기준에서 1.</summary>
    public static double IntensityOf(TimeSpan count, TimeSpan showFrom, TimeSpan full)
    {
        if (count < showFrom) return 0;
        if (count >= full) return 1;
        return 0.15 + 0.85 * (count - showFrom).TotalSeconds / (full - showFrom).TotalSeconds;
    }

    private sealed class Track
    {
        public WatchedPlayer Last = null!;
        public DateTimeOffset FirstSeen;      // 끊김 없이 보이기 시작한 때
        public DateTimeOffset LastSeen;
        public double IdleSeconds;            // 혼자 전투하지 않는 카운터
        public bool IdleActive;               // 직전 관찰에서도 조건이 참이었다
        public bool Absent;
        public double AbsentSeconds;          // 안 보이는 카운터
    }

    private readonly Dictionary<string, Track> _tracks = new();
    private DateTimeOffset? _lastObserved;
    private int _visible;

    /// <summary>지금 의심 중인 사람들(카운터가 표시 기준을 넘은 사람).</summary>
    public IReadOnlyList<AutoSuspect> Suspects { get; private set; } = Array.Empty<AutoSuspect>();

    public void Reset()
    {
        _tracks.Clear();
        _lastObserved = null;
        _visible = 0;
        Suspects = Array.Empty<AutoSuspect>();
    }

    /// <param name="inInstance">던전·레이드 안인가. 밖이면 처음부터 다시 한다.</param>
    public void Observe(DateTimeOffset now, bool inInstance, IReadOnlyList<WatchedPlayer> players)
    {
        if (!inInstance) { Reset(); return; }
        if (_lastObserved is { } prev0 && now - prev0 > MaxGap) Reset();
        var elapsed = _lastObserved is { } prev ? (now - prev).TotalSeconds : 0;   // 이번 관찰까지 흐른 초
        _lastObserved = now;

        // 표식이 같은 사람이 둘 이상이면 구분할 수 없다
        var dup = players.GroupBy(p => p.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var list = players.Where(p => !dup.Contains(p.Key)).ToList();
        foreach (var k in dup) _tracks.Remove(k);
        _visible = list.Count;

        var present = list.Select(p => p.Key).ToHashSet();
        foreach (var p in list)
        {
            if (!_tracks.TryGetValue(p.Key, out var t)) _tracks[p.Key] = t = new Track { FirstSeen = now };
            if (t.Absent) { t.Absent = false; t.AbsentSeconds = 0; t.FirstSeen = now; t.IdleSeconds = 0; t.IdleActive = false; }   // 돌아왔다: 바로 풀린다
            t.Last = p;
            t.LastSeen = now;

            var others = list.Where(o => o.Key != p.Key).ToList();
            var othersAllFighting = others.Count >= MinOthers && others.All(o => o.InCombat);
            if (!p.InCombat && othersAllFighting)
            {
                if (t.IdleActive) t.IdleSeconds += elapsed;   // 조건이 이어지는 동안 초를 쌓는다 (처음 한 번은 쌓지 않는다)
                t.IdleActive = true;
            }
            else { t.IdleSeconds = 0; t.IdleActive = false; }   // 해소 요인: 바로 0
        }

        foreach (var (key, t) in _tracks.ToList())
        {
            if (present.Contains(key) || dup.Contains(key)) continue;
            t.IdleSeconds = 0; t.IdleActive = false;
            if (!t.Absent)
            {
                if (t.LastSeen - t.FirstSeen < MinSeenBeforeMissing) { _tracks.Remove(key); continue; }   // 잠깐 스친 사람
                t.Absent = true;
                t.AbsentSeconds = 0;
            }
            else if (_visible >= MinOthers) t.AbsentSeconds += elapsed;   // 다른 사람이 충분히 보이는 동안만 쌓는다
            if (t.AbsentSeconds > ForgetAfter.TotalSeconds) _tracks.Remove(key);
        }

        var suspects = new List<AutoSuspect>();
        foreach (var (key, t) in _tracks)
        {
            var (count, from, full) = t.Absent
                ? (TimeSpan.FromSeconds(t.AbsentSeconds), MissingShowFrom, MissingFull)
                : (TimeSpan.FromSeconds(t.IdleSeconds), IdleShowFrom, IdleFull);
            if (count < from) continue;
            suspects.Add(new AutoSuspect(key, t.Absent ? AutoSuspicionReason.Missing : AutoSuspicionReason.Idle, count, t.Last, IntensityOf(count, from, full)));
        }
        Suspects = suspects;
    }
}
