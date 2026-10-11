using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace MobiMate.Tests;

public class SnapshotManagerTests : IDisposable
{
    private readonly string _testDir;

    public SnapshotManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "MobiMateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
            // 임시 디렉터리 정리 무해 처리
        }
    }

    [Fact]
    public void StoragePath_DefaultsToWebOnlyAppDataFolder()
    {
        // [TST-01 변경] 웹앱은 WPF판과 데이터를 분리해 %APPDATA%\MobiMateWeb 를 쓴다.
        // 실제 폴더를 만들지 않도록 인스턴스를 만들지 않고 경로 값만 확인한다.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.Equal(Path.Combine(appData, "MobiMateWeb"), SnapshotManager.DefaultStorageDirectory);
        Assert.Equal(Path.Combine(appData, "MobiMate"), SnapshotManager.WpfStorageDirectory);
        Assert.NotEqual(SnapshotManager.DefaultStorageDirectory, SnapshotManager.WpfStorageDirectory);
    }

    [Fact]
    public void ImportMissingFrom_CopiesOnlyMissingFiles_AndNeverTouchesSource()
    {
        var wpfDir = Path.Combine(_testDir, "wpf");
        var webDir = Path.Combine(_testDir, "web");
        Directory.CreateDirectory(wpfDir);
        var src = new SnapshotManager(wpfDir);
        src.SetCustomName("아이라", "격투가", "별명");
        src.SaveSnapshotsNow();
        var before = Directory.GetFiles(wpfDir).ToDictionary(f => f, File.ReadAllText);

        var web = new SnapshotManager(webDir);
        Assert.True(web.ImportMissingFrom(wpfDir) >= 1);
        Assert.Equal("별명", web.GetProfile("아이라", "격투가")?.CustomName);

        // 이미 있는 파일은 다시 가져오지 않는다 (파일별 규칙)
        Assert.Equal(0, web.ImportMissingFrom(wpfDir));

        // 원본은 바뀌지 않는다
        web.SetCustomName("아이라", "격투가", "웹에서 바꿈");
        web.SaveSnapshotsNow();
        foreach (var (file, text) in before) Assert.Equal(text, File.ReadAllText(file));
    }

    [Fact]
    public void LoadSnapshots_WhenFileDoesNotExist_StartsCleanlyWithoutError()
    {
        var manager = new SnapshotManager(_testDir);

        Assert.NotNull(manager);
        Assert.Null(manager.GetProfile("TestRealm", "Warrior"));
    }

    [Fact]
    public void LoadSnapshots_WhenFileIsZeroBytes_QuarantinesAndStartsCleanly()
    {
        var snapFile = Path.Combine(_testDir, "character_snapshots.json");
        File.WriteAllBytes(snapFile, Array.Empty<byte>()); // 0바이트 손상 파일

        var manager = new SnapshotManager(_testDir);

        Assert.NotNull(manager);
        // 손상 파일이 .corrupted.*.bak으로 백업 격리되었는지 확인
        var bakFiles = Directory.GetFiles(_testDir, "character_snapshots.json.corrupted.*.bak");
        Assert.Single(bakFiles);
    }

    [Fact]
    public void LoadSnapshots_WhenJsonIsCorrupted_QuarantinesAndStartsCleanly()
    {
        var snapFile = Path.Combine(_testDir, "character_snapshots.json");
        var dbFile = Path.Combine(_testDir, "character_history_db.json");

        // 깨진 JSON 문법 작성
        File.WriteAllText(snapFile, "{ broken_json : [[[");
        File.WriteAllText(dbFile, "NOT_EVEN_JSON");

        var manager = new SnapshotManager(_testDir);

        Assert.NotNull(manager);
        Assert.Null(manager.GetProfile("Realm", "Job"));

        // 두 파일 모두 .corrupted.*.bak으로 안전 격리되었는지 확인
        var snapBaks = Directory.GetFiles(_testDir, "character_snapshots.json.corrupted.*.bak");
        var dbBaks = Directory.GetFiles(_testDir, "character_history_db.json.corrupted.*.bak");
        Assert.Single(snapBaks);
        Assert.Single(dbBaks);
    }

    [Fact]
    public void SaveSnapshotsNow_And_Reload_PreservesDataCorrectly()
    {
        var manager1 = new SnapshotManager(_testDir);
        manager1.SetCustomName("류트", "전사", "슈퍼파이터");

        var info = new CharacterInfo(
            Title: "초보자",
            RealmName: "류트",
            Level: 50,
            JobName: "전사",
            CombatScore: new ScoreVal("전투력", 30000),
            LivingScore: null,
            AttractivenessScore: null,
            DecorScore: null,
            HealthMax: null,
            AttackPower: null,
            DefencePower: null,
            ArcaneResistance: null,
            STR: null,
            DEX: null,
            INT: null,
            LUCK: null,
            WILL: null,
            PaladinStats: null,
            Vitals: new VitalsInfo(1000, 1000, 500.0, 1000.0, 100, 100, 0)
        );

        var currencies = new System.Collections.Generic.List<CurrencyItem>
        {
            new("골드", 1000000),
            new("정령의 날개", 50)
        };

        manager1.UpdateSnapshot(info, currencies, null);
        manager1.SaveSnapshotsNow();

        // 새로 생성하여 로드 검증
        var manager2 = new SnapshotManager(_testDir);
        var profile = manager2.GetProfile("류트", "전사");
        Assert.NotNull(profile);
        Assert.Equal("슈퍼파이터", profile.CustomName);
        Assert.Single(profile.History);
        Assert.Equal(30000, profile.History[0].CombatScore);
        Assert.Equal(1000000, profile.History[0].Gold);
    }

    [Fact]
    public void SnapshotManager_CharacterProfile_And_History_CumulativeTracking()
    {
        // [TST-01 변경] WPF 원본은 new SnapshotManager()로 실제 %APPDATA%\MobiMate에 테스트 캐릭터를 기록했다.
        // 임시 폴더로 격리한다 (사용자 데이터 오염 방지).
        var tempDir = Path.Combine(Path.GetTempPath(), "mm-chatter-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sm = new SnapshotManager(tempDir);
            var uniqueRealm = "테스트_" + Guid.NewGuid().ToString("N")[..8];

            var ch1 = new CharacterInfo(
                Title: "용사",
                RealmName: uniqueRealm,
                Level: 100,
                JobName: "테스트직업",
                CombatScore: new ScoreVal("전투력", 80000),
                LivingScore: null,
                AttractivenessScore: null,
                DecorScore: null,
                HealthMax: null,
                AttackPower: null,
                DefencePower: null,
                ArcaneResistance: null,
                STR: null,
                DEX: null,
                INT: null,
                LUCK: null,
                WILL: null,
                PaladinStats: null,
                Vitals: new VitalsInfo(1000, 1000, 500.0, 1000.0, 100, 100, 0)
            );

            var currencies1 = new List<CurrencyItem>
            {
                new("골드", 1000000),
                new("정령의 날개", 500)
            };

            // 1. 첫 번째 스냅샷 업데이트
            sm.UpdateSnapshot(ch1, currencies1, null);

            var profile = sm.GetProfile(uniqueRealm, "테스트직업");
            Assert.NotNull(profile);
            Assert.Equal($"{uniqueRealm}_테스트직업", profile.CharacterKey);
            Assert.Single(profile.History);
            Assert.Equal(80000, profile.History[0].CombatScore);
            Assert.Equal(1000000, profile.History[0].Gold);

            // 2. 별칭(CustomName) 설정 및 표시명 확인
            sm.SetCustomName(uniqueRealm, "테스트직업", "나의본캐");
            Assert.Equal("나의본캐", profile.CustomName);
            Assert.Equal($"[{uniqueRealm}] 나의본캐 (테스트직업)", profile.DisplayName);

            // 3. 수치 변동 발생 시 누적 히스토리 레코드 추가 확인 (전투력 상승)
            var ch2 = new CharacterInfo(
                Title: "용사",
                RealmName: uniqueRealm,
                Level: 100,
                JobName: "테스트직업",
                CombatScore: new ScoreVal("전투력", 85000), // +5000
                LivingScore: null,
                AttractivenessScore: null,
                DecorScore: null,
                HealthMax: null,
                AttackPower: null,
                DefencePower: null,
                ArcaneResistance: null,
                STR: null,
                DEX: null,
                INT: null,
                LUCK: null,
                WILL: null,
                PaladinStats: null,
                Vitals: new VitalsInfo(1000, 1000, 500.0, 1000.0, 100, 100, 0)
            );

            sm.UpdateSnapshot(ch2, currencies1, null);
            Assert.Equal(2, profile.History.Count);
            Assert.Equal(85000, profile.History[1].CombatScore);
            sm.SaveSnapshotsNow();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }
}
