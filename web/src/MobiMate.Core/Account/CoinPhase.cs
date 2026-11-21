namespace MobiMate;

/// <summary>
/// 은동전(30분)·마족 공물(12시간)이 +1 되는 "시각의 위상"을 관찰로 좁혀 간다 (2026-11-21).
/// 충전은 캐릭터마다 고정된 위상(예: 매시 10분·40분)에서 일어나고, 접속하지 않아도 이어진다(기록으로 확인).
/// 그래서 마지막으로 본 시각부터 몇 번 +1 됐는지는 "경과 시간 ÷ 주기"가 아니라 그 위상을 지난 횟수로 세야 정확하다.
///
/// 위상은 [0, 주기) 초 범위의 "가능한 값의 집합"으로 둔다. 가까운 두 관찰 사이에 +1이 n번 있었다면 그 사이 구간에 위상이 n번 들어 있어야 한다는 식으로
/// 집합을 줄인다(+1이 없었다면 그 구간은 위상이 아니다). 상한에 닿았거나 소비한 구간은 정보가 없어 쓰지 않는다.
/// 서로 맞지 않는 관찰이 나오면 바로 믿지 않고, 같은 결론이 두 번 이어질 때만 바꾼다(한 번의 읽기 오류로 망가지지 않게).
/// </summary>
public sealed class CoinPhase
{
    public int StepSeconds { get; }
    /// <summary>상한으로 보는 최소값(은동전 100, 마족 공물 10). 이 이상이면 충전이 멈췄을 수 있어 그 구간은 쓰지 않는다.</summary>
    public long MinCap { get; }
    /// <summary>두 관찰의 간격이 이보다 길면 그 사이 소비·상한이 섞였을 수 있어 쓰지 않는다.</summary>
    public static readonly TimeSpan MaxGap = TimeSpan.FromMinutes(5);

    private bool[] _ok;               // 가능한 위상(초) 표시
    private bool[]? _alt;             // 기존 결론과 충돌한 관찰이 가리키는 다른 후보
    private int _altVotes;
    public long? LastHeld { get; private set; }
    public DateTime? LastObsUtc { get; private set; }

    public CoinPhase(int stepSeconds, long minCap)
    {
        StepSeconds = stepSeconds;
        MinCap = minCap;
        _ok = new bool[stepSeconds];
        Array.Fill(_ok, true);
    }

    public static CoinPhase Silver() => new(30 * 60, 100);
    public static CoinPhase Tribute() => new(12 * 3600, 10);

    private static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    private static long Epoch(DateTime utc) => (long)Math.Floor((utc - DateTime.UnixEpoch).TotalSeconds);

    /// <summary>방금 읽은 보유량을 알린다 (시각은 읽은 순간).</summary>
    public void Observe(DateTime nowUtc, long held)
    {
        var prevHeld = LastHeld; var prevAt = LastObsUtc;
        LastHeld = held; LastObsUtc = nowUtc;
        if (prevHeld is not { } h0 || prevAt is not { } t0 || nowUtc <= t0 || nowUtc - t0 > MaxGap) return;

        if (held < h0)
        {
            // 소비. 상한이던 것을 쓰면 충전 시계가 그때부터 새로 시작할 수 있어 위상을 모른다고 본다.
            if (h0 >= MinCap) { Array.Fill(_ok, true); _alt = null; _altVotes = 0; }
            return;
        }
        if (h0 >= MinCap) return;   // 충전이 멈춘 상태일 수 있다

        var n = held - h0;
        if (n > (nowUtc - t0).TotalSeconds / StepSeconds + 1) return;   // 그 시간에 그만큼 늘 수 없다: 다른 캐릭터 값이 섞였거나 읽기 오류
        var exact = held < MinCap;   // 상한에 닿지 않았다면 정확히 n번
        var c = Constraint(Epoch(t0), Epoch(nowUtc), n, exact);
        Apply(c);
    }

    private bool[] Constraint(long t0, long t1, long n, bool exact)
    {
        var s = StepSeconds;
        var r = new bool[s];
        for (var p = 0; p < s; p++)
        {
            var cnt = FloorDiv(t1 - p, s) - FloorDiv(t0 - p, s);
            r[p] = exact ? cnt == n : cnt >= n;
        }
        return r;
    }

    private void Apply(bool[] c)
    {
        var next = new bool[StepSeconds];
        var any = false;
        for (var i = 0; i < next.Length; i++) { next[i] = _ok[i] && c[i]; any |= next[i]; }
        if (any)
        {
            _ok = next; _alt = null; _altVotes = 0;
            return;
        }
        // 기존 결론과 맞지 않는다: 같은 결론이 두 번 이어지면 그쪽으로 바꾼다
        if (!c.Any(x => x)) return;
        if (_alt != null)
        {
            var both = new bool[StepSeconds]; var anyAlt = false;
            for (var i = 0; i < both.Length; i++) { both[i] = _alt[i] && c[i]; anyAlt |= both[i]; }
            if (anyAlt) { _alt = both; _altVotes++; }
            else { _alt = c; _altVotes = 1; }
        }
        else { _alt = c; _altVotes = 1; }
        if (_altVotes >= 2) { _ok = _alt!; _alt = null; _altVotes = 0; }
    }

    /// <summary>
    /// 가능한 위상 중 가장 긴 연속 구간의 가운데(초)와 폭(초). 정보가 없으면(전부 가능하거나 구간이 주기의 절반 이상) null.
    /// 구간은 주기 끝에서 처음으로 이어질 수 있다(예: 1790초~10초).
    /// </summary>
    public (double Phase, int Width)? Estimate()
    {
        var s = StepSeconds;
        var total = _ok.Count(x => x);
        if (total == s || total == 0) return null;
        // 시작점: 불가능→가능으로 바뀌는 곳
        int bestStart = -1, bestLen = 0;
        for (var i = 0; i < s; i++)
        {
            if (!_ok[i] || _ok[(i + s - 1) % s]) continue;
            var len = 0;
            while (len < s && _ok[(i + len) % s]) len++;
            if (len > bestLen) { bestLen = len; bestStart = i; }
        }
        if (bestStart < 0 || bestLen >= s / 2) return null;
        return ((bestStart + bestLen / 2.0) % s, bestLen);
    }

    /// <summary>
    /// 처음 만들 때 과거의 +1 감지 시각들로 위상을 잡는다. 감지는 읽는 주기만큼 늦을 수 있어(<paramref name="lagSeconds"/>) 그 구간을 후보로 보고,
    /// 가장 많이 겹치는 곳을 고른다. 일치하는 비율이 낮으면(위상이 자주 바뀐 캐릭터) 잡지 않는다.
    /// </summary>
    public bool Bootstrap(IReadOnlyCollection<DateTime> tickDetectedUtc, int lagSeconds = 75)
    {
        if (tickDetectedUtc.Count < 5) return false;
        var s = StepSeconds;
        var votes = new int[s];
        foreach (var t in tickDetectedUtc)
        {
            var end = (int)(((Epoch(t) % s) + s) % s);
            for (var k = 0; k <= lagSeconds; k++) votes[((end - k) % s + s) % s]++;
        }
        var max = votes.Max();
        if (max < Math.Max(5, tickDetectedUtc.Count * 0.5)) return false;
        var thr = (int)Math.Ceiling(max * 0.9);
        for (var i = 0; i < s; i++) _ok[i] = votes[i] >= thr;
        return true;
    }

    // ── 저장·복원 (가능한 위상을 [시작, 끝) 구간 목록으로) ──
    public List<int[]> ToIntervals()
    {
        var list = new List<int[]>();
        var s = StepSeconds;
        for (var i = 0; i < s; i++)
        {
            if (!_ok[i]) continue;
            var j = i; while (j < s && _ok[j]) j++;
            list.Add([i, j]); i = j;
        }
        return list;
    }

    public void LoadIntervals(IEnumerable<int[]>? intervals, long? lastHeld, DateTime? lastObsUtc)
    {
        if (intervals != null)
        {
            var arr = new bool[StepSeconds]; var any = false;
            foreach (var iv in intervals)
                if (iv.Length == 2) for (var i = Math.Max(0, iv[0]); i < Math.Min(StepSeconds, iv[1]); i++) { arr[i] = true; any = true; }
            if (any) _ok = arr;
        }
        LastHeld = lastHeld; LastObsUtc = lastObsUtc;
    }

    /// <summary>
    /// 마지막으로 본 때(t0) 이후 지금(now)까지 +1이 몇 번 있었나. 위상을 모르면 null(경과 시간 ÷ 주기로 대신한다).
    /// </summary>
    public static long TicksBetween(double phaseSeconds, int stepSeconds, DateTime t0Utc, DateTime nowUtc)
    {
        if (nowUtc <= t0Utc) return 0;
        var p = phaseSeconds;
        var a = Math.Floor((Epoch(nowUtc) - p) / stepSeconds);
        var b = Math.Floor((Epoch(t0Utc) - p) / stepSeconds);
        return (long)(a - b);
    }

    /// <summary>지금 이후 첫 +1 시각</summary>
    public static DateTime NextTick(double phaseSeconds, int stepSeconds, DateTime nowUtc)
    {
        var k = Math.Floor((Epoch(nowUtc) - phaseSeconds) / stepSeconds) + 1;
        return DateTime.UnixEpoch.AddSeconds(k * stepSeconds + phaseSeconds);
    }
}
