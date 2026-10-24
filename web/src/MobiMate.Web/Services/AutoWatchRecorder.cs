using System.Text.Json;

namespace MobiMate.Web.Services;

/// <summary>
/// 던전·레이드·어비스 안에서 주변 플레이어 관찰을 날짜별 파일(logs/watch-yyyyMMdd.jsonl)에 계속 남긴다(2026-10-25 승운: 방금 돈 레이드에 오토가 둘이었는데 못 잡았다).
/// 판이 끝난 뒤에 "이 사람이 오토였다"는 표본과 함께 기준을 다시 돌려 볼 수 있어야 해서, 사람이 따로 기록기를 켜지 않아도 늘 남긴다.
/// 한 줄 = 한 번의 관찰: 시각, 맵, 내 위치, 주변 사람들의 관찰값, 그때 앱이 켜 둔 의심 표식. 용량은 하루에 판 시간 × 약 1KB/초이고 14일이 지나면 지운다.
/// 파일을 못 써도 오토 감시 자체는 멈추지 않는다(오류는 알리기만 한다).
/// </summary>
public sealed class AutoWatchRecorder
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);
    public const long MaxFileBytes = 300L * 1024 * 1024;

    private readonly string _dir;
    private readonly Action<string, Exception?>? _onError;
    private readonly object _lock = new();
    private DateTime _prunedOn = DateTime.MinValue;
    private bool _failed;

    public AutoWatchRecorder(string dir, Action<string, Exception?>? onError = null) { _dir = dir; _onError = onError; }

    public string FileFor(DateTimeOffset when) => Path.Combine(_dir, $"watch-{when.ToLocalTime():yyyyMMdd}.jsonl");

    public void Record(DateTimeOffset now, string? space, WorldPos? me, IReadOnlyList<NearPcItem> players, IReadOnlyList<AutoSuspect> suspects)
    {
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                t = now.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff"),
                space,
                me = me == null ? null : new { me.X, me.Y },
                p = players.Select(p => new
                {
                    k = WatchedPlayer.KeyOf(p), job = p.JobName, lv = p.Level, cs = p.CombatScore, title = p.Title, realm = p.RealmName,
                    c = p.IsInCombat ? 1 : 0, d = p.Distance, pl = p.Performance?.IsPlaying == true ? 1 : 0,
                    rb = p.IsRobeWeared ? 1 : 0, hd = p.IsHoodOn ? 1 : 0, wh = p.IsWeaponHidden ? 1 : 0, cl = p.ClothesCount,
                }),
                s = suspects.Select(s => new { k = s.Key, r = s.Reason.ToString(), sec = Math.Round(s.For.TotalSeconds, 1), i = Math.Round(s.Intensity, 2) }),
            });
            lock (_lock)
            {
                Directory.CreateDirectory(_dir);
                var path = FileFor(now);
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes) return;
                File.AppendAllText(path, line + "\n");
                Prune(now);
            }
        }
        catch (Exception ex)
        {
            if (_failed) return;   // 같은 오류를 계속 알리지 않는다
            _failed = true;
            _onError?.Invoke("오토 감시 기록을 쓰지 못했습니다(감시는 계속합니다)", ex);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        if (_prunedOn == now.LocalDateTime.Date) return;
        _prunedOn = now.LocalDateTime.Date;
        foreach (var f in Directory.EnumerateFiles(_dir, "watch-*.jsonl"))
            if (File.GetLastWriteTimeUtc(f) < now.UtcDateTime - KeepFor) { try { File.Delete(f); } catch { } }
    }
}
