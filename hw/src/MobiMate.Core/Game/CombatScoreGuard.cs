namespace MobiMate;

/// <summary>
/// 전투력 재확인 (FR-DT-10). 게임 로딩 순간(펫 등)에 전투력이 실제보다 낮게(3,000쯤) 읽힐 때가 있다.
/// 새로 읽은 값이 마지막으로 받아들인 값보다 낮으면 바로 믿지 않는다: 이전 값을 그대로 보여 주고 잠시 뒤 다시 읽는다.
/// 다시 읽어도 계속 낮으면 실제 하락(장비 교체 등)으로 보고 받아들인다. 캐릭터(키)마다 따로 센다.
/// </summary>
public sealed class CombatScoreGuard
{
    private readonly object _lock = new();
    private readonly Dictionary<string, long> _known = new();
    private readonly Dictionary<string, long> _confirmedLow = new();
    private readonly HashSet<string> _rechecking = new();

    /// <summary>
    /// 읽은 값을 판정한다. <paramref name="saved"/>는 서버를 다시 켠 직후처럼 아직 받아들인 값이 없을 때 쓰는 저장된 마지막 값이다.
    /// 받아들이면 <see cref="Verdict.Suspect"/>가 아니고 <see cref="Verdict.Value"/>가 보여 줄 값이다.
    /// 의심스러우면 Value는 이전(알려진) 값이다.
    /// </summary>
    public Verdict Evaluate(string key, long observed, long? saved = null)
    {
        lock (_lock)
        {
            var known = _known.TryGetValue(key, out var k) ? k : saved;
            if (known is null || observed >= known || (_confirmedLow.TryGetValue(key, out var low) && observed == low))
            {
                _known[key] = observed;
                _confirmedLow.Remove(key);
                return new Verdict(observed, false);
            }
            return new Verdict(known.Value, true);
        }
    }

    /// <summary>다시 읽어도 계속 낮았다: 이 값을 실제 값으로 받아들이게 한다.</summary>
    public void ConfirmLow(string key, long value)
    {
        lock (_lock) _confirmedLow[key] = value;
    }

    /// <summary>같은 캐릭터를 동시에 두 번 재확인하지 않게 한다. 시작할 수 있으면 true.</summary>
    public bool TryBeginRecheck(string key)
    {
        lock (_lock) return _rechecking.Add(key);
    }

    public void EndRecheck(string key)
    {
        lock (_lock) _rechecking.Remove(key);
    }

    /// <param name="Value">화면에 보여 줄 전투력</param>
    /// <param name="Suspect">새로 읽은 값이 의심스러워 이전 값을 대신 보여 주는가</param>
    public readonly record struct Verdict(long Value, bool Suspect);
}
