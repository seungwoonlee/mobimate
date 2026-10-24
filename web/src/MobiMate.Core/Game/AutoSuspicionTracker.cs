namespace MobiMate;

/// <summary>주변 플레이어 한 명의 관찰값. 게임이 이름을 주지 않아 서버·직업·레벨·전투력·칭호로 구분한다.</summary>
public sealed record WatchedPlayer(string Key, string Realm, string Job, int Level, long CombatScore, string? Title, bool InCombat, double Distance = 0, bool Playing = false,
    bool Robe = false, bool Hood = false, bool WeaponHidden = false, int Clothes = 0)
{
    /// <summary>같은 사람을 이어서 알아보는 표식. 둘 이상이 같으면 구분할 수 없어 판정에서 뺀다.</summary>
    public static string KeyOf(NearPcItem p) => $"{p.RealmName}|{p.JobName}|{p.Level}|{p.CombatScore}|{p.Title}";

    public static WatchedPlayer From(NearPcItem p) =>
        new(KeyOf(p), p.RealmName, p.JobName, p.Level, p.CombatScore, p.Title, p.IsInCombat, p.Distance, p.Performance?.IsPlaying == true,
            p.IsRobeWeared, p.IsHoodOn, p.IsWeaponHidden, p.ClothesCount);

    /// <summary>화면을 직접 만져야 바뀌는 상태(로브·후드·무기 숨김·연주·옷)가 지난 관찰과 달라졌는가 — 사람이 조작했다는 가장 확실한 증거.</summary>
    public bool ToggledSince(WatchedPlayer before) => Robe != before.Robe || Hood != before.Hood || WeaponHidden != before.WeaponHidden || Playing != before.Playing || Clothes != before.Clothes;
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
    /// <summary>한 자리에 오래 고정(2026-10-24 승운: 죽으면 위치가 확실히 고정이다 — 죽어도 "전투 중"으로 읽히는 경우에도 위치로 잡는다). 삼각측량 위치가 3m 안에서 이어진 시간.</summary>
    Frozen,
    /// <summary>상대 의심 점수(시범, 2026-10-24): 파티 안에서 뒤처지는 모습 — 전투가 시작될 때 가장 늦게 합류하는 일이 반복되거나, 한 자리 최장 시간이 파티에서 두드러진다. 확정이 아니라 의심 단계다.</summary>
    Relative,
}

/// <param name="For">그 증상이 이어진 시간(1초 단위 카운터)</param>
/// <param name="Intensity">의심의 짙기 0..1. 카운터가 표시 기준을 넘는 순간 약하게 켜지고, 클수록 짙어져 만점 기준에서 1이 된다.</param>
public sealed record AutoSuspect(string Key, AutoSuspicionReason Reason, TimeSpan For, WatchedPlayer Player, double Intensity);

/// <summary>
/// 오토(방치) 의심 표식(순수, 2026-10-22 승운 요청, 2026-10-24 모델 변경).
/// <b>모델 변경</b>: 처음 보인 순간부터 모두 의심으로 보고, 사람다운 행동(전투·상태 조작·혼자 움직임)이 보이면 "무활동 초"를 0으로 되돌린다.
/// 무활동이 표시 기준(기본 30초, 관찰로 조정)을 넘어서야 표시하므로 초반 30초는 속으로만 관찰한다. 다른 사람 대부분이 전투 중인 때만 초를 센다(대기·전환 구간은 멈춘다).
/// 아래는 옛 설명이다. 던전·레이드 안에서 주변 플레이어를 넣으면 증상별로 초 단위 카운터를 쌓는다(흐른 시간만큼 더하므로 간격이 바뀌어도 초가 맞다).
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
    /// <summary>다른 사람 <b>전원</b>이 전투 중인데 혼자 안 하는 시간(2026-10-24 승운: 10초만 돼도 사실상 오토). 대부분(75%) 기준보다 훨씬 짧다.</summary>
    public static readonly (TimeSpan Show, TimeSpan Full) IdleAllScale = (TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));
    public static readonly (TimeSpan Show, TimeSpan Full) PlayingScale = (TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120));
    /// <summary>한 자리 고정 기준: 추정 위치가 StayRadius(m) 안에서 이어진 시간이 Show를 넘으면 표시, Full에서 가장 짙다. 정보 없는 틈이 StayGap(초)보다 길면 끊는다.
    /// 기록 6판(2026-10-24)의 정상 플레이어 최장은 약 57초(레이드 1번째의 사제·격투가 83~84초는 라벨 없음), 확인된 방치·사망은 75~139초.
    /// 승운 요청(2026-10-24): "확정이 아니라 의심이니 더 강하게" — 70초에서 45초로 내렸다(정상 판에서도 가끔 옅게 켜진다. 의심하고 있다는 것을 알려 주는 용도).</summary>
    public static readonly (TimeSpan Show, TimeSpan Full) FrozenScale = (TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(120));
    public const double StayRadius = 3.0, StayGap = 20.0;
    /// <summary>상대 의심 점수 기준(시범): 전투 시작 판정 — 전원이 가만하던 시간 QuietBeforeOnset 뒤 누군가 전투를 시작하면 시작점, OnsetWindow 동안 각자 첫 전투까지 걸린 초를 잰다.
    /// 가장 늦은 사람이 2등보다 LateGap초 이상 늦으면 "늦게 합류" 한 번. 한 자리 최장(RelStayWindow초 안)이 2등의 RelStayRatio배 이상이고 RelStayMin초 이상이면 두드러짐.</summary>
    public const double QuietBeforeOnset = 8, OnsetWindow = 30, LateGap = 3, RelStayWindow = 120, RelStayMin = 25, RelStayRatio = 1.3, RelShow = 0.3, RelAloneShow = 0.5;
    public const int OnsetMinPlayers = 3;
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
    /// <summary>제자리 판정에 쓰는 최근 표본 기간(초). 평소 4초 간격에서도 표본이 모이도록 넉넉하게 둔다.</summary>
    public const double SampleWindowSeconds = 15;

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
        public readonly Counter Still = new(), Lying = new(), Playing = new(), Far = new();
        public double QuietAllSeconds;        // 그중 다른 사람 전원이 전투 중이던 시간(같은 시계, 더 확실한 증거)
        public double QuietSeconds;           // 사람다운 행동 없이 흐른 초(다른 사람 대부분이 전투 중인 동안만 쌓는다)
        public bool QuietActive;
        public readonly List<RangeSample> Samples = new();   // 최근 표본(내 위치·거리): 삼각측량으로 제자리 판정
        public bool? Stationary;                              // true 제자리 / false 움직임 / null 모름
        public (double X, double Y)? StayAnchor;              // 고정으로 보는 위치(삼각측량)
        public double? StayStart, StayLast;                   // 그 자리에 머문 구간(초)
        public readonly List<(double T, double Weight)> LateEvents = new();   // 전투 시작 때 가장 늦게 합류한 일(가중치 0..1)
        public int OnsetsSeen;                                                // 그 사람이 함께 있었던 전투 시작 횟수
        public readonly List<(double T, double Stay)> StayHist = new();       // 최근 한 자리 머문 시간 기록(상대 비교용)
        public double StayMax => StayHist.Count == 0 ? 0 : StayHist.Max(q => q.Stay);
        public double StaySeconds => StayStart is { } a && StayLast is { } b ? b - a : 0;
        public bool Absent;
        public double AbsentSeconds;          // 안 보이는 카운터

        /// <summary>어느 증상이든 조건이 시작되어 카운터가 도는 중이다.</summary>
        public bool Counting => QuietActive || Lying.Active || Playing.Active || Far.Active || Absent;
    }

    private sealed class Onset
    {
        public double Start;
        public readonly HashSet<string> Present = new();
        public readonly Dictionary<string, double> First = new();
    }

    private double _groupQuiet;
    private Onset? _onset;

    private readonly Dictionary<string, Track> _tracks = new();
    private DateTimeOffset? _lastObserved;
    private int _visible;

    /// <summary>
    /// 누군가의 증상 조건이 시작되어 카운터가 도는 중이면 true — 그때부터 호출자가 1초 간격으로 관찰해 초 단위로 센다.
    /// 아무 조건도 없으면 false라 평소의 읽기 주기만 쓴다(게임 CLI를 계속 두드리지 않으려는 목적).
    /// </summary>
    public bool NeedsFastSampling => _tracks.Values.Any(t => t.Counting);

    /// <summary>그 사람이 지금 같은 자리에 머문 시간(초). 모르면 0. 기준을 맞출 때 쓴다.</summary>
    public double StaySecondsOf(string key) => _tracks.TryGetValue(key, out var t) ? t.StaySeconds : 0;

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

    /// <summary>
    /// 전투 시작점 추적: 보이는 사람이 OnsetMinPlayers명 이상이고 전원이 QuietBeforeOnset초 가만하다가 누군가 전투를 시작하면 시작점이다.
    /// OnsetWindow초 동안 각자 처음 전투한 때를 재고, 끝나면 가장 늦은 사람이 2등보다 LateGap초 이상 늦으면 그 사람에게 "늦게 합류" 한 번을 준다.
    /// </summary>
    private void TrackOnsets(DateTimeOffset now, double dt, List<WatchedPlayer> list)
    {
        var ts = now.ToUnixTimeMilliseconds() / 1000.0;
        var fighting = list.Count(p => p.InCombat);
        if (_onset is { } on)
        {
            foreach (var p in list) if (p.InCombat && on.Present.Contains(p.Key) && !on.First.ContainsKey(p.Key)) on.First[p.Key] = ts - on.Start;
            if (ts - on.Start >= OnsetWindow) FinishOnset(on, ts);
        }
        else if (fighting == 0) _groupQuiet += dt;
        else
        {
            if (_groupQuiet >= QuietBeforeOnset && list.Count >= OnsetMinPlayers)
            {
                var n = new Onset { Start = ts };
                foreach (var p in list) { n.Present.Add(p.Key); if (p.InCombat) n.First[p.Key] = 0; }
                _onset = n;
            }
            _groupQuiet = 0;
        }
    }

    private void FinishOnset(Onset o, double ts)
    {
        _onset = null; _groupQuiet = 0;
        if (o.Present.Count < OnsetMinPlayers) return;
        var lags = o.Present.Select(k => (Key: k, Lag: o.First.TryGetValue(k, out var f) ? f : OnsetWindow)).OrderByDescending(x => x.Lag).ToList();
        foreach (var k in o.Present) if (_tracks.TryGetValue(k, out var t)) t.OnsetsSeen++;
        var top = lags[0]; var second = lags[1].Lag;
        if (top.Lag >= LateGap && top.Lag - second >= LateGap && _tracks.TryGetValue(top.Key, out var tt))
            tt.LateEvents.Add((ts, Math.Min(1, (top.Lag - second) / 10)));
    }

    /// <summary>한 자리 고정 구간 갱신: 삼각측량 위치가 같은 자리(3m)에 이어지면 머문 시간이 늘고, 움직임이 확인되거나 정보 없는 틈이 길면 끊긴다.</summary>
    private static void UpdateStay(Track t, Stationary.Result an, double ts)
    {
        if (an.Still == false) { t.StayAnchor = null; t.StayStart = null; t.StayLast = null; return; }
        if (an.Still == true)
        {
            if (an.X is { } x && an.Y is { } y)
            {
                if (t.StayAnchor is not { } a || Math.Sqrt((x - a.X) * (x - a.X) + (y - a.Y) * (y - a.Y)) > StayRadius)
                { t.StayAnchor = (x, y); t.StayStart = t.Samples.Count > 0 ? t.Samples[0].T : ts; }
                t.StayLast = ts;
            }
            else if (t.StayAnchor != null) t.StayLast = ts;   // 내가 제자리인데 거리가 그대로: 같은 자리에 있다는 확인
        }
        if (t.StayLast is { } last && ts - last > StayGap) { t.StayAnchor = null; t.StayStart = null; t.StayLast = null; }
    }

    public void Reset()
    {
        _tracks.Clear();
        _groupQuiet = 0; _onset = null;
        _lastObserved = null;
        _visible = 0;
        Suspects = Array.Empty<AutoSuspect>();
    }

    /// <param name="inInstance">던전·레이드 안인가. 밖이면 처음부터 다시 한다.</param>
    /// <param name="me">이 관찰과 같은 때의 내 위치. 있으면 삼각측량으로 제자리 여부를 판정한다(없으면 거리 변화만으로 어림한다).</param>
    public void Observe(DateTimeOffset now, bool inInstance, IReadOnlyList<WatchedPlayer> players, WorldPos? me = null)
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

        // 파티가 통째로 바뀌었다(다른 방·다시 들어옴): 어비스 파티는 나 포함 4명이라 한 번에 보이는 사람은 최대 3명이다.
        // 추적하던 사람(2명 이상)이 아무도 안 보이고 보이는 사람이 전부 새 얼굴이면 이전 파티원을 "안 보임"으로 남기지 않고 처음부터 다시 한다.
        if (_tracks.Values.Count(t => !t.Absent) >= 2 && list.Count >= 2 && !list.Any(p => _tracks.ContainsKey(p.Key))) _tracks.Clear();

        // 1) 거리 변화(움직임)부터 갱신한다: 정지 판정은 "다른 사람은 움직이는데 이 사람만 그대로"라서 모두의 변화가 필요하다
        var toggled = new HashSet<string>();
        foreach (var p in list)
        {
            if (!_tracks.TryGetValue(p.Key, out var t)) _tracks[p.Key] = t = new Track { FirstSeen = now };
            else if (!t.Absent && t.Last != null && p.ToggledSince(t.Last)) toggled.Add(p.Key);
            if (t.Absent) { t.Absent = false; t.AbsentSeconds = 0; t.FirstSeen = now; t.LastDistance = null; }   // 돌아왔다: 바로 풀린다
            t.LastDelta = t.LastDistance is { } d0 ? Math.Abs(p.Distance - d0) : 0;
            t.LastDistance = p.Distance;
            t.Last = p;
            t.LastSeen = now;
            if (me != null && p.Distance > 0.01)
            {
                var ts = now.ToUnixTimeMilliseconds() / 1000.0;
                t.Samples.Add(new RangeSample(ts, me.X, me.Y, p.Distance));
                t.Samples.RemoveAll(q => ts - q.T > SampleWindowSeconds);
                var an = Stationary.Analyze(t.Samples);
                t.Stationary = an.Still;
                UpdateStay(t, an, ts);
                t.StayHist.Add((ts, t.StaySeconds)); t.StayHist.RemoveAll(q => ts - q.T > RelStayWindow);
            }
            if (toggled.Contains(p.Key)) { t.StayAnchor = null; t.StayStart = null; t.StayLast = null; }   // 화면 조작은 사람 행동: 고정 구간을 끊는다   // 화면 조작은 사람 행동: 활동량 후보에서 빼고 처음부터 다시 본다
        }

        // 2) 무활동 시계와 증상별 카운터
        foreach (var p in list)
        {
            var t = _tracks[p.Key];
            var others = list.Where(o => o.Key != p.Key).ToList();
            // 다른 사람 대부분(75% 이상, 최소 2명)이 전투 중일 때만 "다들 하는데 혼자 안 한다"를 말할 수 있다. 보스 대기·전환처럼 모두 가만한 때는 시계를 멈춘다.
            var othersFighting = others.Count >= MinOthers && others.Count(o => o.InCombat) >= Math.Max(MinOthers, (int)Math.Ceiling(others.Count * 0.75));
            var sceneMoving = others.Any(o => _tracks[o.Key].LastDelta >= MovingMeters);

            // 사람다운 행동: 전투 중 / 화면을 만져야 바뀌는 상태 변화 / 혼자 움직임(내가 움직이면 모두의 거리가 변하므로, 가만히 있는 다른 사람이 있을 때만 인정한다)
            // 움직임은 사람다운 행동으로 치지 않는다(2026-10-24 승운: 자동전투도 목표를 향해 움직인다. 실측: 오토 화염술사가 44초 비전투로 걸어 다녀도 풀려 버렸다).
            // 확실한 것만 푼다: 전투 중이거나, 화면을 직접 만져야 바뀌는 상태가 바뀐 경우.
            var human = p.InCombat || toggled.Contains(p.Key);
            var allFighting = others.Count >= MinOthers && others.All(o => o.InCombat);
            if (human) { t.QuietSeconds = 0; t.QuietAllSeconds = 0; t.QuietActive = false; }
            else if (othersFighting)
            {
                if (t.QuietActive) { t.QuietSeconds += dt; if (allFighting) t.QuietAllSeconds += dt; }   // 처음 참이 된 관찰은 쌓지 않는다
                t.QuietActive = true;
            }   // 다들 가만한 구간이면 그대로 둔다(쌓지도, 지우지도 않는다)

            var idleCond = !p.InCombat && othersFighting;
            // 정지: 다른 사람이 움직이는 동안 이 사람의 거리가 그대로. 아무도 안 움직이면 알 수 없으니 그대로 둔다.
            if (t.LastDelta >= StillMeters) t.Still.Tick(false, dt);
            else if (sceneMoving) t.Still.Tick(true, dt);

            // 누워 있음 = 제자리 + 전투 안 함 + 다른 사람은 전투 중. 내 위치를 알면 삼각측량(내가 움직여도 맞다), 모르면 "다른 사람은 움직이는데 이 사람 거리만 그대로"로 어림한다.
            var stillNow = me != null ? t.Stationary == true : t.Still.Seconds >= StaticNeeded.TotalSeconds;
            t.Lying.Tick(idleCond && stillNow && !toggled.Contains(p.Key), dt);
            t.Playing.Tick(p.Playing && idleCond, dt);
            t.Far.Tick(others.Count(o => o.Distance <= NearMeters) >= MinOthers && p.Distance >= FarMeters, dt);
        }

        // 2.6) 전투 시작 때 합류 지연(가장 늦게 합류한 사람) 기록
        TrackOnsets(now, dt, list);

        // 2.7) 상대 의심 점수: 합류 지연이 반복되거나, 한 자리 최장 시간이 파티에서 두드러지는 사람
        var relative = new Dictionary<string, double>();
        if (me != null && list.Count >= OnsetMinPlayers)
        {
            var ranked = list.Select(p => (p.Key, Stay: _tracks[p.Key].StayMax)).OrderByDescending(x => x.Stay).ToList();
            var second = ranked.Count > 1 ? ranked[1].Stay : 0;
            foreach (var p in list)
            {
                var t = _tracks[p.Key];
                double late = 0, stay = 0;
                if (t.OnsetsSeen >= 2)
                {
                    var ratio = (double)t.LateEvents.Count / t.OnsetsSeen;
                    if (ratio >= 0.5) late = 0.25 + 0.5 * Math.Min(1, (ratio - 0.5) / 0.5);
                }
                else if (t.OnsetsSeen == 1 && t.LateEvents.Count == 1) late = 0.15;
                if (ranked[0].Key == p.Key && ranked[0].Stay >= RelStayMin && ranked[0].Stay >= RelStayRatio * Math.Max(second, 1))
                    stay = 0.3 + 0.5 * Math.Min(1, (ranked[0].Stay / Math.Max(second, 1) - RelStayRatio) / 0.7);
                // 한 신호만으로는 정상 판에서도 늘 누군가 두드러져 오탐이 많았다(기록 10판): 늦게 합류한 적이 있고 한 자리 최장도 두드러질 때만 합쳐서 본다
                var score = late + stay;
                if ((late > 0 && stay > 0 && score >= RelShow) || score >= RelAloneShow) relative[p.Key] = Math.Min(1, score);
            }
        }

        // 3) 사라진 사람
        var present = list.Select(p => p.Key).ToHashSet();
        foreach (var (key, t) in _tracks.ToList())
        {
            if (present.Contains(key) || dup.Contains(key)) continue;
            t.Samples.Clear(); t.Stationary = null; t.StayAnchor = null; t.StayStart = null; t.StayLast = null;
            t.QuietSeconds = 0; t.QuietAllSeconds = 0; t.QuietActive = false; t.Lying.Tick(false, dt); t.Playing.Tick(false, dt); t.Far.Tick(false, dt); t.Still.Tick(false, dt);
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
                (AutoSuspicionReason.Idle, t.QuietSeconds, IdleScale),
                (AutoSuspicionReason.Idle, t.QuietAllSeconds, IdleAllScale),
                (AutoSuspicionReason.Frozen, t.StaySeconds, FrozenScale),
                (AutoSuspicionReason.Far, t.Far.Seconds, FarScale),
                (AutoSuspicionReason.Missing, t.Absent ? t.AbsentSeconds : 0, MissingScale),
            };
            // 표시 기준을 넘은 증상 중 더 확실한 이유를 앞세운다(누워 있음 > 혼자 연주 > 혼자 전투 안 함 > 멀리 > 안 보임). 짙기는 그중 가장 센 값.
            AutoSuspect? best = null;
            var maxI = 0.0;
            foreach (var (reason, seconds, scale) in candidates)
            {
                var count = TimeSpan.FromSeconds(seconds);
                var i = IntensityOf(count, scale.Show, scale.Full);
                if (i <= 0) continue;
                maxI = Math.Max(maxI, i);
                if (best == null) best = new AutoSuspect(key, reason, count, t.Last, i);   // 후보는 확실한 순서로 늘어서 있다
            }
            if (relative.TryGetValue(key, out var relI))
            {
                maxI = Math.Max(maxI, relI);
                best ??= new AutoSuspect(key, AutoSuspicionReason.Relative, TimeSpan.Zero, t.Last, relI);   // 다른 이유가 없을 때만 이 이유로 보인다
            }
            if (best != null) best = best with { Intensity = maxI };
            if (best != null) suspects.Add(best);
        }
        Suspects = suspects;
    }
}
