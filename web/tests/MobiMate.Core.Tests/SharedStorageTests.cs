using System.Diagnostics;
using System.Text.Json;
using MobiMate.Web.Hosting;

namespace MobiMate.Tests;

/// <summary>
/// 공유 저장 패치: 웹앱과 숙제 앱이 같은 저장 폴더를 동시에 써도 갱신을 잃지 않는다.
/// 시간 의존 단언은 고정 대기 대신 조건 대기(타임아웃)로 쓴다.
/// </summary>
public class SharedStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-shared-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.FromHours(9)).ToUniversalTime();   // 수요일
    private readonly HomeworkCatalog _catalog = HomeworkCatalog.LoadEmbedded();
    private const string Main = "아이라_격투가";
    private const string Cavrak = "원정의 증거: 카브락 레이드";

    public SharedStorageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string HwFile => Path.Combine(_dir, HomeworkStore.FileName);
    private HomeworkService NewService() => new(_catalog, new HomeworkStore(_dir), () => _now);
    private string ManualId => _catalog.Items.First(d => d.Active && d.Share == HomeworkShare.Character).Id;
    private string OtherManualId => _catalog.Items.Where(d => d.Active && d.Share == HomeworkShare.Character).Skip(1).First().Id;

    private static HomeworkObservation Obs(params (string Name, long Amount)[] c) =>
        new(Currencies: c.Select(x => new CurrencyItem(x.Name, x.Amount)).Append(new CurrencyItem("골드", 1000)).ToList());

    private static void WaitUntil(Func<bool> cond, int ms = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!cond())
        {
            if (sw.ElapsedMilliseconds > ms) throw new TimeoutException("조건을 기다리다 시간이 지났습니다.");
            Thread.Sleep(10);
        }
    }

    // ---------------- a. 두 프로세스 동시 쓰기 ----------------

    [Fact]
    public async Task TwoProcesses_WritingTheSameFolder_LoseNoUpdates()
    {
        const int PerProcess = 600;   // 프로세스당 600회 × 2 = 1,200회 (숙제 체크·캐릭터 기록·계정 지정을 번갈아)
        var exe = HarnessExe();
        var runs = new[] { "A", "B" }.Select(role => Task.Run(() =>
        {
            using var p = Process.Start(new ProcessStartInfo(exe, $"\"{_dir}\" {role} {PerProcess}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            })!;
            var err = p.StandardError.ReadToEnd();
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(180_000)) { try { p.Kill(true); } catch { } throw new TimeoutException($"하네스 {role}가 끝나지 않았습니다."); }
            return (role, p.ExitCode, output, err);
        })).ToArray();
        var results = await Task.WhenAll(runs);
        foreach (var r in results) Assert.True(r.ExitCode == 0, $"{r.role}: exit {r.ExitCode}\n{r.output}\n{r.err}");

        const int PerKind = PerProcess / 3;   // 종류별 200회씩
        var homework = new HomeworkStore(_dir).Load();
        foreach (var role in new[] { "A", "B" })
        {
            for (var i = 0; i < PerProcess; i += 3)
                Assert.True(homework.Characters.TryGetValue($"hw_{role}_{i}", out var l) && l.Items.GetValueOrDefault(ManualId)?.Completed == true, $"숙제 체크 유실: hw_{role}_{i}");
        }
        Assert.Equal(2 * PerKind, homework.Characters.Count);

        var snapshots = new SnapshotManager(_dir);
        var keys = snapshots.GetAllProfiles().Select(p => p.CharacterKey).ToHashSet();
        Assert.Equal(2 * PerKind, keys.Count);
        foreach (var role in new[] { "A", "B" })
            for (var i = 1; i < PerProcess; i += 3)
                Assert.Contains($"류트_{role}{i}", keys);
        Assert.Equal(2 * PerKind, JsonDocument.Parse(File.ReadAllText(snapshots.SnapshotsFilePath)).RootElement.GetArrayLength());   // 스냅숏 파일도 함께

        var assign = new AccountStore(_dir).Snapshot().Assign;
        Assert.Equal(2 * PerKind, assign.Count);
        foreach (var role in new[] { "A", "B" })
            for (var i = 2; i < PerProcess; i += 3)
                Assert.Equal(role, assign[$"acct_{role}_{i}"].Account);
    }

    private static string HarnessExe()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var tfm = Path.GetFileName(baseDir);
        var config = Path.GetFileName(Path.GetDirectoryName(baseDir)!);
        var dir = new DirectoryInfo(baseDir);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "SharedStorageHarness"))) dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("web/tools/SharedStorageHarness 폴더를 찾지 못했습니다.");
        var exe = Path.Combine(dir.FullName, "tools", "SharedStorageHarness", "bin", config, tfm, "SharedStorageHarness.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("하네스가 빌드되지 않았습니다. 솔루션 전체를 빌드하세요.", exe);
        return exe;
    }

    // ---------------- b. 없는/교체 중인 파일 ----------------

    [Fact]
    public void MissingFile_DoesNotOverwriteWithEmptyRecords_AndRecoversWhenItReturns()
    {
        var svc = NewService();
        svc.Set("c1", ManualId, completed: true);
        var backup = File.ReadAllBytes(HwFile);

        File.Delete(HwFile);   // File.Replace 도중처럼 대상이 없다
        var ex = Assert.Throws<IOException>(() => svc.Set("c2", ManualId, completed: true));
        Assert.Contains("읽을 수 없어", ex.Message);
        Assert.False(File.Exists(HwFile));   // 빈 기록(또는 메모리)으로 다시 만들어 덮어쓰지 않았다
        Assert.Null(svc.Evaluate("c2", Obs((Cavrak, 3))));   // 판정도 건너뛴다

        File.WriteAllBytes(HwFile, backup);   // 파일이 돌아왔다
        svc.Set("c2", ManualId, completed: true);
        var saved = new HomeworkStore(_dir).Load();
        Assert.True(saved.Characters["c1"].Items[ManualId].Completed);
        Assert.True(saved.Characters["c2"].Items[ManualId].Completed);
    }

    [Fact]
    public void FileReplacedWhileReading_RetriesAndKeepsBothUpdates()
    {
        var svc = NewService();
        svc.Set("c1", ManualId, completed: true);
        var other = NewService();
        other.Set("c2", ManualId, completed: true);   // 다른 인스턴스가 고쳐서 svc의 기억이 낡았다
        var current = File.ReadAllBytes(HwFile);
        File.Delete(HwFile);

        // 교체 중이던 파일이 재시도 구간(50ms×3) 안에 돌아온다
        var restorer = new Thread(() => { Thread.Sleep(60); File.WriteAllBytes(HwFile, current); });
        restorer.Start();
        svc.Set("c3", ManualId, completed: true);
        restorer.Join();

        var saved = new HomeworkStore(_dir).Load();
        Assert.Equal(new[] { "c1", "c2", "c3" }, saved.Characters.Keys.OrderBy(k => k).ToArray());
    }

    [Fact]
    public void MissingSnapshotFiles_KeepMemory_AndAreNotOverwrittenWithEmpty()
    {
        var m = new SnapshotManager(_dir);
        m.UpdateSnapshot(Info("류트", "전사", 50), null, null, "류트_전사");
        var dbBytes = File.ReadAllBytes(m.CharacterDbFilePath);
        File.Delete(m.CharacterDbFilePath);

        m.UpdateSnapshot(Info("류트", "전사", 51), null, null, "류트_전사");   // 레벨이 바뀌어 저장하려 하지만 파일이 없다 = 교체 중
        Assert.False(File.Exists(m.CharacterDbFilePath));
        Assert.Single(m.GetAllProfiles());   // 메모리는 그대로

        File.WriteAllBytes(m.CharacterDbFilePath, dbBytes);
        m.UpdateSnapshot(Info("류트", "전사", 52), null, null, "류트_전사");
        Assert.Single(new SnapshotManager(_dir).GetAllProfiles());
    }

    // ---------------- c. 잠긴 파일 ----------------

    [Fact]
    public void LockedFile_KeepsMemory_AndNeverOverwritesWithEmpty()
    {
        var svc = NewService();
        svc.Set("c1", ManualId, completed: true);
        NewService().Set("c2", ManualId, completed: true);   // 파일이 바뀌어 svc는 다시 읽어야 한다

        byte[] before;
        using (new FileStream(HwFile, FileMode.Open, FileAccess.Read, FileShare.None))   // 다른 프로그램이 쥐고 있다
        {
            Assert.Throws<IOException>(() => svc.Set("c3", ManualId, completed: true));   // 수동 조작은 실패시킨다
            Assert.Null(svc.Evaluate("c3", Obs((Cavrak, 3))));   // 판정은 건너뛴다
            Assert.NotNull(svc.GetBoard("c1"));   // 화면은 메모리로 계속 보여 준다
        }
        before = File.ReadAllBytes(HwFile);
        Assert.Equal(new[] { "c1", "c2" }, new HomeworkStore(_dir).Load().Characters.Keys.OrderBy(k => k).ToArray());

        svc.Set("c3", ManualId, completed: true);   // 풀리면 파일을 다시 읽고 합쳐서 저장
        Assert.Equal(new[] { "c1", "c2", "c3" }, new HomeworkStore(_dir).Load().Characters.Keys.OrderBy(k => k).ToArray());
        Assert.NotEqual(before.Length, File.ReadAllBytes(HwFile).Length);
    }

    [Fact]
    public void LockedSnapshotFiles_DoNotClearMemory_AndDoNotWriteEmptyLists()
    {
        var a = new SnapshotManager(_dir);
        a.UpdateSnapshot(Info("류트", "전사", 50), null, null, "류트_전사");
        var b = new SnapshotManager(_dir);   // 다른 프로세스 역할
        b.UpdateSnapshot(Info("류트", "궁수", 40), null, null, "류트_궁수");

        using (new FileStream(a.CharacterDbFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // a는 다시 읽어야 하지만 읽지 못한다: 메모리를 비우지 않고, 레벨이 바뀌어도 저장하지 않는다
            a.UpdateSnapshot(Info("류트", "전사", 51), null, null, "류트_전사");
            Assert.NotEmpty(a.GetAllProfiles());
            Assert.NotNull(a.GetProfileByKey("류트_전사"));
        }
        var onDisk = new SnapshotManager(_dir).GetAllProfiles().Select(p => p.CharacterKey).OrderBy(k => k).ToArray();
        Assert.Equal(new[] { "류트_궁수", "류트_전사" }, onDisk);   // 빈 목록으로 덮어쓰지 않았다

        a.UpdateSnapshot(Info("류트", "전사", 52), null, null, "류트_전사");   // 풀린 뒤에는 합쳐서 저장
        var merged = new SnapshotManager(_dir);
        Assert.Equal(2, merged.GetAllProfiles().Count);
        Assert.Equal(52, merged.GetProfileByKey("류트_전사")!.History.Last().Level);
    }

    // ---------------- d. 다른 인스턴스가 저장한 관찰 시각·기준값 ----------------

    // 증표가 늘면 "제안"만 하는 일반 경로를 보는 전용 카탈로그 (실제 카탈로그의 카브락은 TokenAuto)
    private readonly HomeworkCatalog _raidCatalog = HomeworkCatalog.Parse("""
    { "version": 1, "sharedPools": {}, "items": [
      { "id": "raid_cavrak", "category": "raid", "period": "weekly", "share": "character", "mode": "manual", "title": "카브락", "subtitle": "", "icon": "raid", "goal": 1, "tokenCurrency": "원정의 증거: 카브락 레이드" }
    ] }
    """);

    [Fact]
    public void ObservationTimesAndBaselines_AreSharedThroughTheFile()
    {
        var web = new HomeworkService(_raidCatalog, new HomeworkStore(_dir), () => _now);
        var app = new HomeworkService(_raidCatalog, new HomeworkStore(_dir), () => _now);   // 같은 폴더의 두 번째 서비스(숙제 앱 역할)
        web.Evaluate(Main, Obs((Cavrak, 3)));   // 기준값만 잡는다

        _now += TimeSpan.FromHours(1);
        app.Evaluate(Main, Obs((Cavrak, 3)));   // 개수는 같고 관찰 시각만 바뀐다: 그래도 저장되어야 한다
        Assert.Equal(_now, new HomeworkStore(_dir).Load().Characters[Main].RaidTokensObservedUtc);

        _now += TimeSpan.FromHours(1);
        app.Evaluate(Main, Obs((Cavrak, 4)));   // 증표가 늘어난 것을 app이 본다 (3 → 4)
        // web은 증표를 새로 관찰하지 않았지만 app이 저장한 기준값·제안을 보고 판정한다
        var card = web.GetBoard(Main).Cards.Single(c => c.Id == "raid_cavrak");
        Assert.Equal(new HomeworkSuggestion("raidTokenIncreased", Cavrak, 3, 4), card.Suggestion);   // 다른 인스턴스가 본 증표 증가를 이 인스턴스가 안다

        _now += TimeSpan.FromHours(1);
        web.Evaluate(Main, Obs((Cavrak, 4)));   // 기준값이 4로 공유되어 있으므로 추가 증가(4 → 4)가 아니다
        var saved = new HomeworkStore(_dir).Load().Characters[Main];
        Assert.Equal(4, saved.RaidTokens!.Values.Single());
        Assert.Equal(_now, saved.RaidTokensObservedUtc);   // web이 본 시각도 저장
        Assert.Equal(new HomeworkSuggestion("raidTokenIncreased", Cavrak, 3, 4), web.GetBoard(Main).Cards.Single(c => c.Id == "raid_cavrak").Suggestion);
    }

    // ---------------- e. 변경 없으면 저장하지 않음 ----------------

    [Fact]
    public void Homework_NoChange_DoesNotWrite_ButObservationTimeChangeDoes()
    {
        var svc = NewService();
        svc.Evaluate(Main, Obs((Cavrak, 3)));
        var t1 = File.GetLastWriteTimeUtc(HwFile).Ticks;

        WaitForClockToMove(HwFile);
        svc.Evaluate(Main, Obs((Cavrak, 3)));   // 같은 시각·같은 값
        Assert.Equal(t1, File.GetLastWriteTimeUtc(HwFile).Ticks);

        _now += TimeSpan.FromMinutes(1);   // 시각만 바뀐 관찰은 저장한다
        svc.Evaluate(Main, Obs((Cavrak, 3)));
        Assert.True(File.GetLastWriteTimeUtc(HwFile).Ticks > t1);
    }

    [Fact]
    public void Snapshots_NoChange_DoesNotWriteEitherFile()
    {
        var m = new SnapshotManager(_dir);
        var info = Info("류트", "전사", 50);
        var cur = new List<CurrencyItem> { new("골드", 1_000_000) };
        m.UpdateSnapshot(info, cur, null, "류트_전사");
        var t1 = File.GetLastWriteTimeUtc(m.SnapshotsFilePath).Ticks;
        var t2 = File.GetLastWriteTimeUtc(m.CharacterDbFilePath).Ticks;

        WaitForClockToMove(m.SnapshotsFilePath);
        for (var i = 0; i < 5; i++) m.UpdateSnapshot(info, cur, null, "류트_전사");   // 4초 주기 감시처럼 같은 값을 반복
        Assert.Equal(t1, File.GetLastWriteTimeUtc(m.SnapshotsFilePath).Ticks);
        Assert.Equal(t2, File.GetLastWriteTimeUtc(m.CharacterDbFilePath).Ticks);

        m.UpdateSnapshot(Info("류트", "전사", 51), cur, null, "류트_전사");   // 값이 바뀌면 저장
        Assert.True(File.GetLastWriteTimeUtc(m.SnapshotsFilePath).Ticks > t1);
        Assert.True(File.GetLastWriteTimeUtc(m.CharacterDbFilePath).Ticks > t2);
    }

    /// <summary>파일시스템 시각 해상도 때문에 "쓰지 않았다"를 잘못 통과하지 않도록, 시계가 충분히 움직일 때까지 기다린다.</summary>
    private static void WaitForClockToMove(string path)
    {
        var last = File.GetLastWriteTimeUtc(path);
        WaitUntil(() => DateTime.UtcNow - last > TimeSpan.FromMilliseconds(100), 5000);
    }

    // ---------------- f. 뮤텍스 ----------------

    [Fact]
    public void AbandonedMutex_IsTreatedAsAcquired()
    {
        var name = SharedStorageLock.MutexNameFor(_dir);
        var holderReady = new ManualResetEventSlim();
        Mutex? held = null;
        var t = new Thread(() =>
        {
            held = new Mutex(false, name);
            held.WaitOne();   // 쥔 채 스레드가 끝난다 = 버려진 뮤텍스
            holderReady.Set();
        });
        t.Start();
        Assert.True(holderReady.Wait(5000));
        t.Join();

        var ran = false;
        SharedStorageLock.For(_dir).Run(() => ran = true);   // AbandonedMutexException을 획득으로 처리
        Assert.True(ran);
        SharedStorageLock.For(_dir).Run(() => ran = false);   // 이어서 정상적으로 다시 잡을 수 있다
        Assert.False(ran);
        GC.KeepAlive(held);
    }

    [Fact]
    public void MutexName_DependsOnFolder_NotOnCaseOrTrailingSlash()
    {
        var a = SharedStorageLock.MutexNameFor(_dir);
        Assert.StartsWith("Local\\MobiMate.Data.", a);
        Assert.Equal(a, SharedStorageLock.MutexNameFor(_dir.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.NotEqual(a, SharedStorageLock.MutexNameFor(_dir + "-other"));
    }

    [Fact]
    public void Lock_IsReentrant_AndExcludesOtherThreads()
    {
        var gate = SharedStorageLock.For(_dir);
        var inside = 0;
        var maxInside = 0;
        var threads = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
        {
            for (var i = 0; i < 50; i++)
                gate.Run(() => gate.Run(() =>   // 중첩 호출
                {
                    var n = Interlocked.Increment(ref inside);
                    maxInside = Math.Max(maxInside, n);
                    Thread.SpinWait(500);
                    Interlocked.Decrement(ref inside);
                }));
        })).ToArray();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
        Assert.Equal(1, maxInside);
    }

    // ---------------- g. 외부 변경 감지 ----------------

    [Fact]
    public void RefreshExternal_DetectsOtherInstanceWrites_ButNotOwnWrites()
    {
        var web = NewService();
        web.Set("c1", ManualId, completed: true);
        Assert.False(web.RefreshExternal());   // 자기 쓰기는 바뀐 것이 아니다

        NewService().Set("c2", OtherManualId, completed: true);   // 다른 인스턴스(숙제 앱 역할)가 썼다
        Assert.True(web.RefreshExternal());
        Assert.False(web.RefreshExternal());   // 한 번 따라잡으면 다시 알리지 않는다
        Assert.True(web.GetBoard("c2").Cards.Single(c => c.Id == OtherManualId).IsDone);

        var m1 = new SnapshotManager(_dir);
        var m2 = new SnapshotManager(_dir);
        m1.UpdateSnapshot(Info("류트", "전사", 50), null, null, "류트_전사");
        Assert.False(m1.RefreshExternal());
        m2.UpdateSnapshot(Info("류트", "궁수", 40), null, null, "류트_궁수");
        Assert.True(m1.RefreshExternal());
        Assert.NotNull(m1.GetProfileByKey("류트_궁수"));
    }

    [Fact]
    public void Watcher_SignalsOnSharedFilesOnly_AndPollingIsAFallback()
    {
        var signals = 0;
        using (var w = new SharedStorageWatcher(_dir, () => Interlocked.Increment(ref signals), TimeSpan.FromMilliseconds(50), TimeSpan.FromHours(1)))
        {
            // 공유 파일이 아닌 것은 무시한다 (임시 파일·백업·다른 파일)
            File.WriteAllText(Path.Combine(_dir, "homework_records.json.tmp.abc123"), "{}");
            File.WriteAllText(Path.Combine(_dir, "homework_records.json.bak"), "{}");
            File.WriteAllText(Path.Combine(_dir, "web_settings.json"), "{}");
            Thread.Sleep(400);   // 음성 확인: 이벤트가 온다면 이 안에 와야 한다
            Assert.Equal(0, Volatile.Read(ref signals));

            File.WriteAllText(Path.Combine(_dir, "accounts.json"), "{}");
            WaitUntil(() => Volatile.Read(ref signals) >= 1);
        }

        Assert.True(SharedStorageWatcher.IsSharedFile("Character_Snapshots.json"));
        Assert.False(SharedStorageWatcher.IsSharedFile("character_snapshots.json.tmp.1"));
        Assert.False(SharedStorageWatcher.IsSharedFile("currency_changes.jsonl"));

        // 감시 없이 폴링만으로도 신호가 온다
        var polled = 0;
        using var p = new SharedStorageWatcher(Path.Combine(_dir, "nowhere-yet"), () => Interlocked.Increment(ref polled), poll: TimeSpan.FromMilliseconds(50));
        WaitUntil(() => Volatile.Read(ref polled) >= 2);
    }

    [Fact]
    public void CurrencyDeltaLog_AppendFailure_IsReportedNotSwallowed()
    {
        var blocker = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocker, "파일이 폴더 자리를 차지한다");   // 로그 폴더를 만들 수 없다
        var errors = new List<string>();
        var log = new CurrencyDeltaLog(Path.Combine(blocker, "c.jsonl"), onError: (msg, ex) => errors.Add(msg + "|" + ex.GetType().Name));
        log.Observe("k", new List<CurrencyItem> { new("골드", 5) });
        Assert.NotNull(log.Observe("k", new List<CurrencyItem> { new("골드", 9) }));   // 앱 동작에는 영향 없다
        Assert.Single(errors);
    }

    private static CharacterInfo Info(string realm, string job, int level) =>
        new("초보자", realm, level, job, new ScoreVal("전투력", 30000), null, null, null, null, null, null, null,
            null, null, null, null, null, null, new VitalsInfo(1000, 1000, 500.0, 1000.0, 100, 100, 0));
}
