namespace MobiMate;

/// <summary>주변 플레이어 한 명의 관찰값. 게임이 이름을 주지 않아 서버·직업·레벨·전투력·칭호로 구분한다.</summary>
public sealed record WatchedPlayer(string Key, string Realm, string Job, int Level, long CombatScore, string? Title, bool InCombat, double Distance = 0, bool Playing = false)
{
    /// <summary>같은 사람을 이어서 알아보는 표식. 둘 이상이 같으면 구분할 수 없어 판정에서 뺀다.</summary>
    public static string KeyOf(NearPcItem p) => $"{p.RealmName}|{p.JobName}|{p.Level}|{p.CombatScore}|{p.Title}";

    public static WatchedPlayer From(NearPcItem p) =>
        new(KeyOf(p), p.RealmName, p.JobName, p.Level, p.CombatScore, p.Title, p.IsInCombat, p.Distance, p.Performance?.IsPlaying == true);
}

/// <summary>상황 파악을 못 하는 듯한 증상. 한 사람이 여럿에 걸리면 가장 짙은 것 하나를 보인다.</summary>
public enum AutoSuspicionReason
{
    /// <summary>다른 사람이 모두 전투 중인데 혼자 움직이지도 않고 누워 있는 듯하다(죽고 부활을 누르지 않은 모습). 가장 확실한 증상.</summary>
    Lying,
    /// <summary>다른 사람은 모두 전투 중인데 혼자 오래 전투하지 않는다.</summary>
    Idle,
    /// <summary>다른 사람이 전투 중인데 혼자 악기를 연주하고 있다.</summary>
    Playing,
    /// <summary>다른 사람들은 가까이 모여 있는데 혼자 멀리 떨어져 있다(따라오지 않는다).</summary>
    Far,
    /// <summary>함께 보이다가 주변(반경 30m)에서 오래 보이지 않는다(따라오지 않는다).</summary>
    Missing,
}

/// <param name="For">그 증상이 이어진 시간(1초 단위 카운터)</param>
/// <param name="Intensity">의심의 짙기 0..1. 카운터가 표시 기준을 넘는 순간 약하게 켜지고, 클수록 짙어져 만점 기준에서 1이 된다.</param>
public sealed record AutoSuspect(string Key, AutoSuspicionReason Reason, TimeSpan For, WatchedPlayer Player, double Intensity);

/// <summary>
/// 오토 의심 표식(순수, 2026-10-22 승운 요청). 던전·레이드 안에서 주변 플레이어를 넣으면 증상별로 초 단위 카운터를 쌓는다(흐른 시간만큼 더하므로 간격이 바뀌어도 초가 맞다).
/// 평소에는 기존 읽기 주기(캐릭터 감시 틱·화면의 주변 조회)로 넣다가, 어느 증상의 조건이 시작되면(<see cref="NeedsFastSampling"/>) 그때부터 1초마다 넣어 정확히 센다.
/// 오토는 화면을 보지 않아 상황 파악을 못 하므로 다른 멤버가 함께 움직이고 싸우는 동안 이런 모습이 길게 이어진다:
/// ① 누워 있음(<see cref="AutoSuspicionReason.Lying"/>): 부활을 직접 눌러야 하는데 혼자 전투하지 않고 거리도 변하지 않는다 — 가장 확실해 기준이 짧다.
/// ② 혼자 전투하지 않음(Idle) ③ 혼자 연주 중(Playing) ④ 혼자 멀리 떨어짐(Far) ⑤ 주변에서 사라짐(Missing, 반경 30m 밖).
/// 각 카운터가 표시 기준을 넘으면 켜지고 클수록 짙어지며, <b>해소 요인이 생기면 그 카운터는 바로 0</b>으로 돌아가 곧바로 풀린다.
/// 던전·레이드 밖이거나 관찰 사이가 <see cref="MaxGap"/>보다 길게 끊기면 모두 처음부터. 같은 표식의 사람이 둘 이상이면 구분할 수 없어 뺀다.
/// 기준 시간은 상수라 경험적으로 조정한다. 스레드 안전하지 않다(호출자가 잠근다).
/// </summary>
public sealed class AutoSuspicionTracker
{
    /// <summary>증상별 (표시 기준, 만점 기준). 카운터가 기준을 넘으면 켜지고 만점 기준에서 가장 짙다.</summary>
    public static readonly (TimeSpan Show, TimeSpan Full) LyingScale = (TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(90));
    public static readonly (TimeSpan Show, TimeSpan Full) IdleScale = (TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(150));
    public static readonly (TimeSpan Show, TimeSpan Full) PlayingScale = (TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120));
    public static readonly (TimeSpan Show, TimeSpan Full) FarScale = (TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(180));
    public static readonly (TimeSpan Show, TimeSpan Full) MissingScale = (TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(180));

    /// <summary>"누워 있음"으로 보는 정지 시간: 다른 사람의 거리는 변하는데 이 사람의 거리가 이만큼 계속 그대로여야 한다.</summary>
    public static readonly TimeSpan StaticNeeded = TimeSpan.FromSeconds(8);
    /// <summary>거리가 이 값(m)보다 덜 변하면 가만히 있는 것, 다른 사람이 이 값 이상 변하면 움직이는 중으로 본다.</summary>
    public const double StillMeters = 0.05, MovingMeters = 0.3;
    /// <summary>"멀리 떨어짐": 이 사람이 FarMeters 이상, 다른 사람 둘 이상이 NearMeters 이내. 목록은 반경 30m까지만 준다.</summary>
    public const double FarMeters = 20, NearMeters = 12;

    public static readonly TimeSpan MinSeenBeforeMissing = TimeSpan.FromSeconds(10);
    /// <summary>이보다 길게 끊기면 그 사이를 모르는 것이다. 모두 처음부터.(평소 관찰 간격인 감시 틱 4초보다 길어야 한다)</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(10);
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

    /// <summary>이어지는 동안 초를 쌓고, 끊기면 바로 0이 되는 카운터.</summary>
    private sealed class Counter
    {
        public double Seconds;
        public bool Active;

        public void Tick(bool condition, double dt)
        {
            if (!condition) { Seconds = 0; Active = false; return; }
            if (Active) Seconds += dt;   // 처음 참이 된 관찰은 쌓지 않는다
            Active = true;
        }
    }

    private sealed class Track
    {
        public WatchedPlayer Last = null!;
        public DateTimeOffset FirstSeen;      // 끊김 없이 보이기 시작한 때
        public DateTimeOffset LastSeen;
        public double? LastDistance;
        public double LastDelta;              // 직전 관찰 대비 거리 변화
        public readonly Counter Still = new(), Idle = new(), Lying = new(), Playing = new(), Far = new();
        public bool Absent;
        public double AbsentSeconds;          // 안 보이는 카운터

        /// <summary>어느 증상이든 조건이 시작되어 카운터가 도는 중이다.</summary>
        public bool Counting => Idle.Active || Lying.Active || Playing.Active || Far.Active || Absent;
    }

    private readonly Dictionary<string, Track> _tracks = new();
    private DateTimeOffset? _lastObserved;
    private int _visible;

    /// <summary>
    /// 누군가의 증상 조건이 시작되어 카운터가 도는 중이면 true — 그때부터 호출자가 1초 간격으로 관찰해 초 단위로 센다.
    /// 아무 조건도 없으면 false라 평소의 읽기 주기만 쓴다(게임 CLI를 계속 두드리지 않으려는 목적).
    /// </summary>
    public bool NeedsFastSampling => _tracks.Values.Any(t => t.Counting);

    /// <summary>지금 의심 중인 사람들(카운터가 표시 기준을 넘은 사람).</summary>
    public IReadOnlyList<AutoSuspect> Suspects { get; private set; } = Array.Empty<AutoSuspect>();

    /// <summary>같은 사람으로 보는 전투력 차이: 던전 안에서는 버프·음식으로 다른 사람의 전투력이 수시로 조금씩 바뀐다(실측 50,403 → 50,571 → 50,604).</summary>
    public const double ScoreDriftRatio = 0.03;
    public const long ScoreDriftMin = 1000;

    /// <summary>
    /// 표식(Key)에 전투력이 들어 있어, 전투력이 조금만 바뀌어도 "사라진 사람 + 새 사람"으로 둘이 된다(2026-10-22 승운: 같은 사람이 2명으로 보임).
    /// 이번 관찰에 처음 보이는 표식이 있고, 지금 안 보이는 기존 추적 중에 서버·직업·레벨·칭호가 같고 전투력 차이가 작은 사람이 있으면
    /// 같은 사람으로 보고 그 추적(카운터 포함)을 새 표식으로 옮긴다. 가장 가까운 전투력 하나만, 한 번에 한 사람씩 짝짓는다.
    /// </summary>
    private void Rekey(List<WatchedPlayer> list)
    {
        var present = list.Select(p => p.Key).ToHashSet();
        foreach (var p in list.Where(p => !_tracks.ContainsKey(p.Key)))
        {
            var match = _tracks
                .Where(kv => !present.Contains(kv.Key) && kv.Value.Last is { } w
                    && w.Realm == p.Realm && w.Job == p.Job && w.Level == p.Level && w.Title == p.Title
                    && Math.Abs(w.CombatScore - p.CombatScore) <= Math.Max(ScoreDriftMin, w.CombatScore * ScoreDriftRatio))
                .OrderBy(kv => Math.Abs(kv.Value.Last!.CombatScore - p.CombatScore))
                .Select(kv => kv.Key)
                .FirstOrDefault();
            if (match == null) continue;
            var t = _tracks[match];
            _tracks.Remove(match);
            _tracks[p.Key] = t;
            present.Add(match);   // 같은 옛 추적을 두 번 쓰지 않게
        }
    }

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
        if (_lastObserved is { } same && now <= same) return;   // 여러 곳(감시·화면)이 넣으므로 같은 시각이거나 거꾸로 온 관찰은 무시한다
        if (_lastObserved is { } prev0 && now - prev0 > MaxGap) Reset();
        var dt = _lastObserved is { } prev ? (now - prev).TotalSeconds : 0;   // 이번 관찰까지 흐른 초
        _lastObserved = now;

        // 표식이 같은 사람이 둘 이상이면 구분할 수 없다
        var dup = players.GroupBy(p => p.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var list = players.Where(p => !dup.Contains(p.Key)).ToList();
        foreach (var k in dup) _tracks.Remove(k);
        _visible = list.Count;
        Rekey(list);

        // 1) 거리 변화(움직임)부터 갱신한다: 정지 판정은 "다른 사람은 움직이는데 이 사람만 그대로"라서 모두의 변화가 필요하다
        foreach (var p in list)
        {
            if (!_tracks.TryGetValue(p.Key, out var t)) _tracks[p.Key] = t = new Track { FirstSeen = now };
            if (t.Absent) { t.Absent = false; t.AbsentSeconds = 0; t.FirstSeen = now; t.LastDistance = null; }   // 돌아왔다: 바로 풀린다
            t.LastDelta = t.LastDistance is { } d0 ? Math.Abs(p.Distance - d0) : 0;
            t.LastDistance = p.Distance;
            t.Last = p;
            t.LastSeen = now;
        }

        // 2) 증상별 카운터
        foreach (var p in list)
        {
            var t = _tracks[p.Key];
            var others = list.Where(o => o.Key != p.Key).ToList();
            var othersAllFighting = others.Count >= MinOthers && others.All(o => o.InCombat);
            var sceneMoving = others.Any(o => _tracks[o.Key].LastDelta >= MovingMeters);

            var idleCond = !p.InCombat && othersAllFighting;
            // 정지: 다른 사람이 움직이는 동안 이 사람의 거리가 그대로. 아무도 안 움직이면 알 수 없으니 그대로 둔다.
            if (t.LastDelta >= StillMeters) t.Still.Tick(false, dt);
            else if (sceneMoving) t.Still.Tick(true, dt);

            t.Idle.Tick(idleCond, dt);
            t.Lying.Tick(idleCond && t.Still.Seconds >= StaticNeeded.TotalSeconds, dt);
            t.Playing.Tick(p.Playing && !p.InCombat && othersAllFighting, dt);
            t.Far.Tick(others.Count(o => o.Distance <= NearMeters) >= MinOthers && p.Distance >= FarMeters, dt);
        }

        // 3) 사라진 사람
        var present = list.Select(p => p.Key).ToHashSet();
        foreach (var (key, t) in _tracks.ToList())
        {
            if (present.Contains(key) || dup.Contains(key)) continue;
            t.Idle.Tick(false, dt); t.Lying.Tick(false, dt); t.Playing.Tick(false, dt); t.Far.Tick(false, dt); t.Still.Tick(false, dt);
            if (!t.Absent)
            {
                if (t.LastSeen - t.FirstSeen < MinSeenBeforeMissing) { _tracks.Remove(key); continue; }   // 잠깐 스친 사람
                t.Absent = true;
                t.AbsentSeconds = 0;
            }
            else if (_visible >= MinOthers) t.AbsentSeconds += dt;   // 다른 사람이 충분히 보이는 동안만 쌓는다
            if (t.AbsentSeconds > ForgetAfter.TotalSeconds) _tracks.Remove(key);
        }

        // 4) 표시 기준을 넘은 증상 중 가장 짙은 것 하나
        var suspects = new List<AutoSuspect>();
        foreach (var (key, t) in _tracks)
        {
            var candidates = new List<(AutoSuspicionReason Reason, double Seconds, (TimeSpan Show, TimeSpan Full) Scale)>
            {
                (AutoSuspicionReason.Lying, t.Lying.Seconds, LyingScale),
                (AutoSuspicionReason.Playing, t.Playing.Seconds, PlayingScale),
                (AutoSuspicionReason.Idle, t.Idle.Seconds, IdleScale),
                (AutoSuspicionReason.Far, t.Far.Seconds, FarScale),
                (AutoSuspicionReason.Missing, t.Absent ? t.AbsentSeconds : 0, MissingScale),
            };
            AutoSuspect? best = null;
            foreach (var (reason, seconds, scale) in candidates)
            {
                var count = TimeSpan.FromSeconds(seconds);
                var i = IntensityOf(count, scale.Show, scale.Full);
                if (i <= 0) continue;
                if (best == null || i > best.Intensity) best = new AutoSuspect(key, reason, count, t.Last, i);   // 같으면 앞(더 확실한 증상)이 이긴다
            }
            if (best != null) suspects.Add(best);
        }
        Suspects = suspects;
    }
}
