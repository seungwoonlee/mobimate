using System;
using System.IO;
using ClosedXML.Excel;
using MobiMate;
using Xunit;

namespace MobiMate.Tests;

public class ExcelSheetSyncTests
{
    [Fact]
    public void CreateNewWorkbook_GeneratesProperLayout_AndFormulas()
    {
        // Act
        using var wb = ExcelSheetSyncService.CreateNewWorkbook();
        var ws = wb.Worksheet("캐릭터 육성");

        // Assert
        Assert.NotNull(ws);
        Assert.Equal("번호", ws.Cell(2, 2).GetString());
        Assert.Equal("캐릭터 이름", ws.Cell(2, 3).GetString());
        Assert.Equal("전투력", ws.Cell(2, 5).GetString());

        // 12개 캐릭터 존재 확인
        Assert.Equal("GALAXYZ", ws.Cell(3, 3).GetString());
        Assert.Equal("빅클라우드", ws.Cell(13, 3).GetString());
        Assert.Equal("빅콜라", ws.Cell(14, 3).GetString());

        // 통계 수식 확인
        Assert.Equal("AVERAGE(E3:E14)", ws.Cell(16, 5).FormulaA1);
        Assert.Equal("MAX(E3:E14)", ws.Cell(17, 5).FormulaA1);
    }

    [Fact]
    public void SyncCharacter_CreatesFileAndUpdatesData_WithIsolation()
    {
        // Arrange: W-09 준수 임시 디렉토리
        var tempDir = Path.Combine(Path.GetTempPath(), "MobiMate_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var testFilePath = Path.Combine(tempDir, "마비노기_테스트.xlsx");

        try
        {
            var service = new ExcelSheetSyncService(testFilePath);
            var charInfo = new CharacterInfo(
                Title: "말랑말랑한",
                RealmName: "아이라",
                Level: 100,
                JobName: "전사",
                CombatScore: new ScoreVal("전투력", 102815),
                LivingScore: new ScoreVal("생활력", 18526),
                AttractivenessScore: new ScoreVal("매력", 26999),
                DecorScore: null,
                HealthMax: null,
                AttackPower: null,
                DefencePower: null,
                ArcaneResistance: new ScoreVal("마도 저항", 4736),
                STR: null,
                DEX: null,
                INT: null,
                LUCK: null,
                WILL: null,
                PaladinStats: null,
                Vitals: null
            );

            var repo = new HomeworkRepository(tempDir);
            var record = repo.GetOrCreateRecord("아이라_전사", DateTime.Now);
            record.Items["raid_cavrak"].IsCompleted = true;
            record.Items["raid_airel"].IsCompleted = false;
            repo.SaveRecord(record);

            // Act: 빅클라우드 동기화
            var result = service.SyncCharacter("빅클라우드", charInfo, repo);

            // Assert
            Assert.True(result.success);
            Assert.True(File.Exists(testFilePath));
            Assert.Equal(13, result.updatedRow);

            // 엑셀 파일 열어서 값 실측 검증
            using var wb = new XLWorkbook(testFilePath);
            var ws = wb.Worksheet("캐릭터 육성");
            Assert.Equal(102815, ws.Cell(13, 5).GetDouble()); // 전투력
            Assert.Equal(18526, ws.Cell(13, 6).GetDouble());  // 생활력
            Assert.Equal(26999, ws.Cell(13, 7).GetDouble());  // 매력
            Assert.Equal(4736, ws.Cell(13, 8).GetDouble());   // 마도 저항
            Assert.Equal("O", ws.Cell(13, 9).GetString());     // 카브락 완료
            Assert.Equal("-", ws.Cell(13, 10).GetString());    // 에이렐 미완료
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void SyncCharacter_UpdatesConsecutiveCharacters_Accurately()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MobiMate_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var testFilePath = Path.Combine(tempDir, "마비노기_연속_테스트.xlsx");

        try
        {
            var service = new ExcelSheetSyncService(testFilePath);

            // 1. 빅클라우드
            var char1 = new CharacterInfo("칭호", "아이라", 100, "전사",
                new ScoreVal("전투력", 102815), new ScoreVal("생활력", 18526),
                null, null, null, null, null, null, null, null, null, null, null, null, null);
            service.SyncCharacter("빅클라우드", char1, null);

            // 2. 빅콜라
            var char2 = new CharacterInfo("칭호", "아이라", 100, "듀얼블레이드",
                new ScoreVal("전투력", 91879), new ScoreVal("생활력", 16851),
                null, null, null, null, null, null, null, null, null, null, null, null, null);
            service.SyncCharacter("빅콜라", char2, null);

            // Assert
            using var wb = new XLWorkbook(testFilePath);
            var ws = wb.Worksheet("캐릭터 육성");
            Assert.Equal(102815, ws.Cell(13, 5).GetDouble());
            Assert.Equal("전사", ws.Cell(13, 4).GetString());
            Assert.Equal(91879, ws.Cell(14, 5).GetDouble());
            Assert.Equal("듀얼블레이드", ws.Cell(14, 4).GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact(Skip = "실제 구글 드라이브 파일 1회성 생성 검증용 (W-09 회귀 격리 준수)")]
    public void GenerateRealGoogleDriveExcelFile()
    {
        var service = new ExcelSheetSyncService(); // 기본 경로: G:\내 드라이브\Games\Mabinogi\마비노기_모바일_캐릭터육성.xlsx
        var charInfo = new CharacterInfo(
            Title: "말랑말랑한",
            RealmName: "아이라",
            Level: 100,
            JobName: "전사",
            CombatScore: new ScoreVal("전투력", 102832),
            LivingScore: new ScoreVal("생활력", 18526),
            AttractivenessScore: new ScoreVal("매력", 26999),
            DecorScore: new ScoreVal("데코", 3895),
            HealthMax: null, AttackPower: null, DefencePower: null,
            ArcaneResistance: new ScoreVal("마도 저항", 4736),
            STR: null, DEX: null, INT: null, LUCK: null, WILL: null,
            PaladinStats: null, Vitals: null
        );
        var repo = new HomeworkRepository();
        var result = service.SyncCharacter("빅클라우드", charInfo, repo);
        Assert.True(result.success);

        // 빅콜라도 함께 기입
        var char2 = new CharacterInfo("이거밖에 안 되심?", "아이라", 100, "듀얼블레이드",
            new ScoreVal("전투력", 91879), new ScoreVal("생활력", 16851),
            new ScoreVal("매력", 20999), new ScoreVal("데코", 3895),
            null, null, null, new ScoreVal("마도 저항", 3644),
            null, null, null, null, null, null, null);
        var result2 = service.SyncCharacter("빅콜라", char2, repo);
        Assert.True(result2.success);
    }
}
