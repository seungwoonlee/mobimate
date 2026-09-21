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
            new ChatterContext("만돌린", "궁수", 95, 85000, "마을 휴식", "티르코네일", "가방 무게 95% 초과!", "0 골드 (부족)"),
            new ChatterContext("하프", "격투가", 100, 120000, "어비스 보스 전투 중", "글라스기브넨", "가방: 300/1000", "1,000,000 골드")
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

        // 모닝이야: 모닝, 닝 등
        var mLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.MorningSpirit, ctx);
        Assert.Contains("닝", mLine);

        // 갱상도 아재: 마!, 행님, 쥑이네 등
        var gLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.GyeongsangAhjussi, ctx);
        Assert.False(string.IsNullOrWhiteSpace(gLine));

        // 아이돌 댄서: 비트, 댄스, 무대 등
        var iLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.IdolDancer, ctx);
        Assert.False(string.IsNullOrWhiteSpace(iLine));
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
}
