namespace MobiMateHomeWork.Services;

/// <summary>무게 읽기 검증 결과.</summary>
public enum WeightVerdict { Ok, NotFinite, Negative, TooLarge, CurrentOverMax, MaxOverSaved, CurrentOverSavedMax }

/// <summary>
/// 가방 무게 읽기 검증(순수 함수). 맵 이동(로딩) 때 게임이 비정상적으로 큰 값을 잠깐 돌려주는 일이 있어,
/// 비정상이면 화면에 쓰지 않고 마지막 정상 값을 보여 준다. 기준: NaN·무한·음수 / 절대값 100,000 초과 /
/// 최대를 알 때 현재 &gt; 최대×3 / 최대가 저장된 최대의 3배 초과 / 최대가 0 이하인데 현재가 저장된 최대의 3배 초과.
/// </summary>
public static class WeightReading
{
    public const double AbsoluteLimit = 100_000;
    public const double Factor = 3;

    public static WeightVerdict Evaluate(double current, double max, double? savedMax)
    {
        if (!double.IsFinite(current) || !double.IsFinite(max)) return WeightVerdict.NotFinite;
        if (current < 0 || max < 0) return WeightVerdict.Negative;
        if (current > AbsoluteLimit || max > AbsoluteLimit) return WeightVerdict.TooLarge;
        var saved = savedMax is > 0 ? savedMax : null;
        if (max > 0)
        {
            if (current > max * Factor) return WeightVerdict.CurrentOverMax;
            if (saved != null && max > saved * Factor) return WeightVerdict.MaxOverSaved;
        }
        else if (saved != null && current > saved * Factor) return WeightVerdict.CurrentOverSavedMax;
        return WeightVerdict.Ok;
    }
}

/// <summary>무게 읽기 결과: 화면에 쓸 값(비정상이면 마지막 정상 값, 없으면 null)과 읽기가 정상이었는지.</summary>
public sealed record WeightResult(WeightView? View, bool Abnormal);

/// <summary>
/// 캐릭터별 마지막 정상 실시간 무게를 메모리에 들고, 비정상 읽기를 걸러 낸다. 비정상 값은 원래 값(현재·최대·캐릭터·맵)을
/// Information 로그로 남기되 같은 캐릭터당 1분에 한 번만 남긴다(실제 패턴을 보려고).
/// </summary>
public sealed class WeightGuard(ILogger<WeightGuard>? log = null, Func<DateTimeOffset>? now = null)
{
    private static readonly TimeSpan LogEvery = TimeSpan.FromMinutes(1);
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.UtcNow);
    private readonly object _lock = new();
    private readonly Dictionary<string, WeightView> _lastGood = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastLogged = new(StringComparer.OrdinalIgnoreCase);

    public WeightResult Resolve(string key, string? map, VitalsInfo? v, double? savedMax)
    {
        if (v == null) return new WeightResult(null, false);
        var verdict = WeightReading.Evaluate(v.WeightCurrent, v.WeightMax, savedMax);
        lock (_lock)
        {
            if (verdict == WeightVerdict.Ok)
            {
                var view = WeightView.Of(v.WeightCurrent, v.WeightMax > 0 ? v.WeightMax : savedMax);
                _lastGood[key] = view;
                return new WeightResult(view, false);
            }
            var t = _now();
            if (!_lastLogged.TryGetValue(key, out var last) || t - last >= LogEvery)
            {
                _lastLogged[key] = t;
                log?.LogInformation("비정상 가방 무게 읽기({Verdict}): 현재 {Current} 최대 {Max} 저장된 최대 {Saved} 캐릭터 {Key} 맵 {Map}",
                    verdict, v.WeightCurrent, v.WeightMax, savedMax, key, map ?? "(없음)");
            }
            return new WeightResult(_lastGood.GetValueOrDefault(key), true);
        }
    }
}
