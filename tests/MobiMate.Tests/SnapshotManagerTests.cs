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
    public void StoragePath_DefaultsToAppData_WhenNotSpecified()
    {
        var manager = new SnapshotManager();
        var expectedAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MobiMate");
        Assert.Equal(expectedAppData, manager.StorageDirectory);
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
}
