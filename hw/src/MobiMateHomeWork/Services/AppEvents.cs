namespace MobiMateHomeWork.Services;

/// <summary>게임 연결 상태가 바뀌었다.</summary>
public sealed record StatusChange(GameConnection State, DateTimeOffset Since);

/// <summary>사용자에게 알려야 하는 경고의 종류.</summary>
public enum AppWarningKind
{
    /// <summary>공유 저장 패치 이전의 웹앱이 같이 떠 있다 → 기록이 서로 덮어써질 수 있다.</summary>
    LegacyWebApp,
    /// <summary>공유 뮤텍스를 만들지 못해 두 프로그램 동시 쓰기 보호가 꺼졌다.</summary>
    StorageLockDegraded,
}

public sealed record AppWarning(AppWarningKind Kind, string Message);

/// <summary>
/// 서비스 → 화면 알림 묶음 (웹앱의 SSE <c>hub.Broadcast</c> 자리). 이벤트는 서비스 스레드에서 올라온다: 구독자가 UI 스레드로 넘긴다.
/// 구독자의 예외가 서비스를 멈추지 않도록 하나씩 감싸서 부른다.
/// </summary>
public sealed class AppEvents
{
    private readonly object _lock = new();
    private readonly List<AppWarning> _warnings = [];

    public event Action<StatusChange>? StatusChanged;
    public event Action<HeaderView>? HeaderChanged;
    public event Action? CharactersChanged;
    public event Action<HomeworkChange>? HomeworkChanged;
    public event Action<AppWarning>? WarningRaised;
    public event Action<AppWarningKind>? WarningCleared;

    /// <summary>지금까지 올라온 경고(늦게 만들어진 창도 볼 수 있게). 같은 종류는 한 번만 담는다.</summary>
    public IReadOnlyList<AppWarning> Warnings
    {
        get { lock (_lock) return [.. _warnings]; }
    }

    public void RaiseStatusChanged(StatusChange c) => Invoke(StatusChanged, h => h(c));
    public void RaiseHeaderChanged(HeaderView v) => Invoke(HeaderChanged, h => h(v));
    public void RaiseCharactersChanged() => Invoke(CharactersChanged, h => h());
    public void RaiseHomeworkChanged(HomeworkChange c) => Invoke(HomeworkChanged, h => h(c));

    public void RaiseWarning(AppWarning w)
    {
        lock (_lock)
        {
            if (_warnings.Any(x => x.Kind == w.Kind)) return;
            _warnings.Add(w);
        }
        Invoke(WarningRaised, h => h(w));
    }

    /// <summary>경고가 사라졌다(예: 옛 웹앱이 종료됨).</summary>
    public void ClearWarning(AppWarningKind kind)
    {
        bool removed;
        lock (_lock) removed = _warnings.RemoveAll(x => x.Kind == kind) > 0;
        if (removed) Invoke(WarningCleared, h => h(kind));
    }

    private static void Invoke<T>(T? evt, Action<T> call) where T : Delegate
    {
        if (evt == null) return;
        foreach (var d in evt.GetInvocationList())
        {
            try { call((T)d); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"이벤트 구독자 오류: {ex.Message}"); }
        }
    }
}
