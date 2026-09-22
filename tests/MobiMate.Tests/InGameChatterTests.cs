using System;
using System.Threading.Tasks;
using Xunit;

namespace MobiMate.Tests;

public class InGameChatterTests
{
    [Fact]
    public void PersonaTemplates_GeneratesValidLines_ForAllPersonasAndCategories()
    {
        var testContexts = new[]
        {
            new ChatterContext("류트", "전사", 80, 50000, "벌목 작업 중", "벌목 캠프", "가방: 450/1000", "50,000 골드"),
            new ChatterContext("만돌린", "궁수", 95, 85000, "마을 휴식", "티르코네일", "가방 무게 105% 초과!", "0 골드 (부족)"),
            new ChatterContext("하프", "격투가", 100, 120000, "어비스 보스 전투 중", "글라스기브넨", "가방: 300/1000", "1,000,000 골드"),
            new ChatterContext("골렘", "마법사", 60, 40000, "광장 휴식", "던바튼", "정상 (50%)", "200,000 골드")
        };

        var personas = new[]
        {
            ChatterPersona.Villainess,
            ChatterPersona.Scrooge,
            ChatterPersona.MorningSpirit,
            ChatterPersona.GyeongsangAhjussi,
            ChatterPersona.IdolDancer
        };

        foreach (var persona in personas)
        {
            foreach (var ctx in testContexts)
            {
                var line = PersonaTemplates.GetRandomTemplate(persona, ctx);
                Assert.False(string.IsNullOrWhiteSpace(line));
                Assert.True(line.Length <= 50, $"대사 길이가 50자를 초과했습니다: '{line}' ({line.Length}자)");

                // ChatPlanService를 거친 최종 메시지도 50자 이내여야 함
                var plan = ChatPlanService.BuildChatPlan(line);
                Assert.True(plan.FinalMessage.Length <= 50, $"최종 메시지가 50자를 초과했습니다: '{plan.FinalMessage}' ({plan.FinalMessage.Length}자)");
            }
        }
    }

    [Fact]
    public void PersonaTemplates_CharacterStyles_MatchExpectedTone()
    {
        var ctx = new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "정상", "10000");

        // 악덕영애: 오호호, 사와요, 귀족 등
        var vLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.Villainess, ctx);
        Assert.False(string.IsNullOrWhiteSpace(vLine));

        // 구두쇠 영감: 골드, 돈, 아까워, 에헴 등
        var sLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.Scrooge, ctx);
        Assert.False(string.IsNullOrWhiteSpace(sLine));

        // 모닝이야: 모닝, 닝, 안녕하닝, 형들 등 유튜버 특유 말투
        var mLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.MorningSpirit, ctx);
        Assert.Contains("닝", mLine);

        // 갱상도 아재: 마!, 행님, 쥑이네 등
        var gLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.GyeongsangAhjussi, ctx);
        Assert.False(string.IsNullOrWhiteSpace(gLine));

        // 아이돌 댄서: 비트, 댄스, 무대 등
        var iLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.IdolDancer, ctx);
        Assert.False(string.IsNullOrWhiteSpace(iLine));
    }

    [Fact]
    public void PersonaTemplates_WeightCategory_OnlyTriggersWhen100PercentOrAbove()
    {
        // 1. 100% 미만: 가방무거움 카테고리로 판정되지 않아야 함
        var underContexts = new[]
        {
            new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "가방 무게 80%", "10000"),
            new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "가방 무게 95%", "10000"),
            new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "가방: 450/1000", "10000"),
            new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "정상 (48kg)", "10000")
        };

        foreach (var ctx in underContexts)
        {
            var line = PersonaTemplates.GetRandomTemplate(ChatterPersona.Villainess, ctx);
            // 악덕영애의 가방무거움 대사는 "짐꾼", "짐더미", "걸을 수가 없사와요", "터질 지경" 포함
            Assert.DoesNotContain("짐꾼", line);
            Assert.DoesNotContain("짐더미", line);
            Assert.DoesNotContain("터질 지경", line);
        }

        // 2. 100% 이상/초과: 정상적으로 가방무거움 대사 트리거
        var overContexts = new[]
        {
            new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "가방 무게 100%", "10000"),
            new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "가방 무게 105%", "10000"),
            new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "100% 초과", "10000")
        };

        foreach (var ctx in overContexts)
        {
            var line = PersonaTemplates.GetRandomTemplate(ChatterPersona.MorningSpirit, ctx);
            // 모닝이의 100% 초과 가방 대사는 "100%", "기어다니닝", "다이어트" 포함
            Assert.True(line.Contains("100%") || line.Contains("기어다니닝") || line.Contains("다이어트"));
        }
    }

    [Fact]
    public void PersonaTemplates_MorningSpirit_ReflectsYoutuberPersona()
    {
        var generalCtx = new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "정상", "50000");
        var gatherCtx = new ChatterContext("류트", "전사", 50, 30000, "채집 중", "티르코네일", "정상", "50000");

        var gLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.MorningSpirit, generalCtx);
        var cLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.MorningSpirit, gatherCtx);

        Assert.Contains("닝", gLine);
        Assert.Contains("닝", cLine);
        Assert.True(cLine.Contains("쌀먹") || cLine.Contains("형들") || cLine.Contains("꿀팁") || cLine.Contains("대성공"));
    }

    [Theory]
    [InlineData(ChatterPersona.Villainess, "🌹 악덕영애")]
    [InlineData(ChatterPersona.Scrooge, "💰 구두쇠 영감")]
    [InlineData(ChatterPersona.MorningSpirit, "☀️ 안녕하닝 모닝이야")]
    [InlineData(ChatterPersona.GyeongsangAhjussi, "🌊 갱상도 아재")]
    [InlineData(ChatterPersona.IdolDancer, "✨ 아이돌 댄서")]
    public void InGameChatterService_GetPersonaDisplayName_ReturnsCorrectBadge(ChatterPersona persona, string expectedName)
    {
        var displayName = InGameChatterService.GetPersonaDisplayName(persona);
        Assert.Equal(expectedName, displayName);
    }

    [Fact]
    public async Task InGameChatterService_TriggerChatterAsync_EmitsChatterEvent()
    {
        var cli = new GameCliService();
        var aiManager = new AiEngineManager();
        var dummyCtx = new ChatterContext("류트", "격투가", 100, 88000, "채집 중", "두갈드 아일", "가방 무게 50%", "100,000");

        var service = new InGameChatterService(cli, aiManager, () => dummyCtx)
        {
            SendToGameDirectly = false // 테스트 중에는 인게임 CLI 호출 비활성화
        };

        Assert.False(service.IsEnabled);
        Assert.Equal(10, service.IntervalSeconds);
        Assert.Equal(ChatterPersona.Villainess, service.CurrentPersona);

        string? emittedPersona = null;
        string? emittedMsg = null;

        service.OnChatterEmitted += (persona, msg) =>
        {
            emittedPersona = persona;
            emittedMsg = msg;
        };

        var ok = await service.TriggerChatterAsync(isManual: true);
        Assert.True(ok);
        Assert.NotNull(emittedPersona);
        Assert.NotNull(emittedMsg);
        Assert.Contains("악덕영애", emittedPersona);
        Assert.True(emittedMsg.Length <= 50);
    }

    [Fact]
    public void InGameChatterService_IntervalAndPersona_CanBeUpdated()
    {
        var cli = new GameCliService();
        var aiManager = new AiEngineManager();
        var dummyCtx = new ChatterContext("류트", "격투가", 100, 88000, "휴식", "티르코네일", "정상", "1000");

        var service = new InGameChatterService(cli, aiManager, () => dummyCtx)
        {
            IntervalSeconds = 20,
            CurrentPersona = ChatterPersona.MorningSpirit
        };

        Assert.Equal(20, service.IntervalSeconds);
        Assert.Equal(ChatterPersona.MorningSpirit, service.CurrentPersona);

        // 최소 3초 보정
        service.IntervalSeconds = 1;
        Assert.Equal(3, service.IntervalSeconds);
    }

    [Theory]
    [InlineData("2959-5-19 22:45", "에린 시간 5월 19일 22:45 🌙 (밤)")]
    [InlineData("2959-4-23 15:51", "에린 시간 4월 23일 15:51 ☀️ (낮)")]
    [InlineData("5-19 04:02", "에린 시간 5월 19일 04:02 🌙 (밤)")]
    [InlineData("2959-10-05 11:30", "에린 시간 10월 5일 11:30 ☀️ (낮)")]
    public void InGameChatterService_FormatErinnTime_RemovesYearAndShowsDayNight(string input, string expected)
    {
        var formatted = InGameChatterService.FormatErinnTime(input);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public async Task InGameChatterService_GenerateChatterLineAsync_ReturnsValidLineUnder50Chars()
    {
        var cli = new GameCliService();
        var aiManager = new AiEngineManager();
        var dummyCtx = new ChatterContext("아이라", "빙결술사", 100, 99000, "휴식 중", "던바튼", "가방: 400/1000", "5,000,000 골드");

        var service = new InGameChatterService(cli, aiManager, () => dummyCtx)
        {
            CurrentPersona = ChatterPersona.MorningSpirit
        };

        var line = await service.GenerateChatterLineAsync();
        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.True(line.Length <= 50);
        Assert.Contains("닝", line);
    }

    [Fact]
    public void SnapshotManager_CharacterProfile_And_History_CumulativeTracking()
    {
        var sm = new SnapshotManager();
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
    }
}
