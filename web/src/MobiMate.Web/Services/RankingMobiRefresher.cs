namespace MobiMate.Web.Services;

public sealed record MobiRefreshResult(
    bool Started, int WaitSeconds, int Updated, List<string> NotFound, List<string> Mismatch, string? Stopped, int Total);

/// <summary>
/// "모비라이프에서 갱신" 버튼 한 번의 일(2026-10-22 승운 결정: 자동 조회 없음, 누를 때 1회).
/// 별칭이 있는 캐릭터마다 이름으로 한 번씩만 조회하고(재시도 없음), 간격을 두며, 403·429·연속 실패가 나오면 바로 멈춘다.
/// 한 번 시작하면 쿨다운(10분) 동안 다시 시작하지 않고, 동시에 두 번 돌지도 않는다.
/// </summary>
public sealed class RankingMobiRefresher
{
    public const int MaxTargets = 20;
    private readonly RankingService _ranking;
    private readonly MobiRankClient _client;
    private readonly ILogger<RankingMobiRefresher> _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _gap;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RankingMobiRefresher(RankingService ranking, MobiRankClient client, ILogger<RankingMobiRefresher> log,
        TimeSpan? gap = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _ranking = ranking; _client = client; _log = log;
        _gap = gap ?? TimeSpan.FromSeconds(1.5);
        _delay = delay ?? Task.Delay;
    }

    public async Task<MobiRefreshResult> RunAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) return new MobiRefreshResult(false, 0, 0, new(), new(), "이미 조회 중입니다.", 0);
        try
        {
            if (!_ranking.TryBeginMobiRefresh(out var wait))
                return new MobiRefreshResult(false, (int)Math.Ceiling(Math.Max(wait.TotalSeconds, 1)), 0, new(), new(), null, 0);

            var targets = _ranking.Targets().Take(MaxTargets).ToList();
            int updated = 0, failures = 0;
            var notFound = new List<string>();
            var mismatch = new List<string>();
            string? stopped = null;
            for (var i = 0; i < targets.Count; i++)
            {
                if (i > 0) await _delay(_gap, ct);
                var t = targets[i];
                var r = await _client.SearchAsync(t.Name, ct);
                if (r.Status == MobiStatus.Blocked) { stopped = $"모비라이프가 이 앱의 접속을 거절했습니다({r.Detail}). 사이트가 프로그램의 접속을 막는 것으로 보여 우회하지 않고 멈췄습니다."; break; }
                if (r.Status == MobiStatus.Failed)
                {
                    if (++failures >= 2) { stopped = $"모비라이프에 연속으로 접속하지 못했습니다({r.Detail}). 더 조회하지 않았습니다."; break; }
                    notFound.Add(t.Name);
                    continue;
                }
                failures = 0;
                switch (_ranking.ApplyMobi(t, r.Rows))
                {
                    case MobiApply.Updated: updated++; break;
                    case MobiApply.Mismatch: mismatch.Add(t.Name); break;
                    case MobiApply.SaveFailed: stopped = "저장하지 못했습니다."; i = targets.Count; break;
                    default: notFound.Add(t.Name); break;
                }
            }
            _log.LogInformation("모비라이프 순위 조회: {Updated}/{Total}명 갱신, 못 찾음 {NotFound}, 다른 캐릭터로 보임 {Mismatch}{Stopped}",
                updated, targets.Count, notFound.Count, mismatch.Count, stopped == null ? "" : " / " + stopped);
            return new MobiRefreshResult(true, 0, updated, notFound, mismatch, stopped, targets.Count);
        }
        finally { _gate.Release(); }
    }
}
