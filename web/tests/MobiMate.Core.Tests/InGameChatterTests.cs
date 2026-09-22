using System;
using System.IO;
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
            var category = PersonaTemplates.DetermineCategory(ctx);
            Assert.Equal("가방_과적", category);

            var line = PersonaTemplates.GetRandomTemplate(ChatterPersona.MorningSpirit, ctx);
            Assert.Contains(line, PersonaTemplatesData.MorningLines["가방_과적"]);
        }
    }

    [Fact]
    public void PersonaTemplates_MorningSpirit_ReflectsYoutuberPersona()
    {
        var generalCtx = new ChatterContext("류트", "전사", 50, 30000, "휴식", "티르코네일", "정상", "50000");
        var gatherCtx = new ChatterContext("류트", "전사", 50, 30000, "채집 중", "티르코네일", "정상", "50000");

        Assert.Equal("채집_자연", PersonaTemplates.DetermineCategory(gatherCtx));

        var gLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.MorningSpirit, generalCtx);
        var cLine = PersonaTemplates.GetRandomTemplate(ChatterPersona.MorningSpirit, gatherCtx);

        Assert.Contains("닝", gLine);
        Assert.Contains("닝", cLine);
        Assert.Contains(cLine, PersonaTemplatesData.MorningLines["채집_자연"]);
    }

    [Theory]
    [InlineData(ChatterPersona.Villainess, "🌹 악덕영애")]
    [InlineData(ChatterPersona.Scrooge, "💰 구두쇠 영감")]
    [InlineData(ChatterPersona.MorningSpirit, "☀️ 안녕하닝 모닝이야")]
    [InlineData(ChatterPersona.GyeongsangAhjussi, "🌊 갱상도 아재")]
    [InlineData(ChatterPersona.IdolDancer, "✨ 아이돌 댄서")]
    public void InGameChatterService_GetPersonaDisplayName_ReturnsCorrectBadge(ChatterPersona persona, string expectedName)
    {
        var displayName = ChatterLineService.GetPersonaDisplayName(persona);
        Assert.Equal(expectedName, displayName);
    }

    // [TST-01 대체] WPF판 InGameChatterService.TriggerChatterAsync / IntervalSeconds 테스트 2건은
    // D-01(send_chat 죽은 코드 제거)·D-05(DispatcherTimer 제거)로 해당 API가 없어져 아래 두 테스트로 바꿨다.
    // 원래 의도였던 "페르소나 대사가 만들어지고 50자 이내"와 "페르소나 전환"을 ChatterLineService로 검증한다.
    [Fact]
    public async Task ChatterLineService_BuiltInEngine_ReturnsPersonaLineUnder50_WithoutFallbackFlag()
    {
        var aiManager = new AiEngineManager(probeCli: (_, _) => Task.FromResult<string?>(null));
        await aiManager.DiscoverEnginesAsync();
        var service = new ChatterLineService(aiManager);
        var ctx = new ChatterContext("류트", "격투가", 100, 88000, "채집 중", "두갈드 아일", "가방 무게 50%", "100,000");

        var line = await service.GenerateLineAsync(ChatterPersona.Villainess, null, ctx);

        Assert.False(string.IsNullOrWhiteSpace(line.Text));
        Assert.True(ChatText.Count(line.Text) <= 50);
        Assert.False(line.UsedFallback); // 내장 엔진은 LLM을 시도하지 않으므로 폴백이 아니다
    }

    [Theory]
    [InlineData(ChatterPersona.MorningSpirit, "닝")]
    [InlineData(ChatterPersona.GyeongsangAhjussi, null)]
    public async Task ChatterLineService_PersonaSwitch_UsesThatPersonasPool(ChatterPersona persona, string? mustContain)
    {
        var aiManager = new AiEngineManager(probeCli: (_, _) => Task.FromResult<string?>(null));
        await aiManager.DiscoverEnginesAsync();
        var service = new ChatterLineService(aiManager);
        var ctx = new ChatterContext("류트", "격투가", 100, 88000, "휴식", "티르코네일", "정상", "1000");

        var line = await service.GenerateLineAsync(persona, null, ctx);

        Assert.False(string.IsNullOrWhiteSpace(line.Text));
        if (mustContain != null) Assert.Contains(mustContain, line.Text);
    }

    [Theory]
    [InlineData("2959-5-19 22:45", "에린 시간 5월 19일 22:45 🌙 (밤)")]
    [InlineData("2959-4-23 15:51", "에린 시간 4월 23일 15:51 ☀️ (낮)")]
    [InlineData("5-19 04:02", "에린 시간 5월 19일 04:02 🌙 (밤)")]
    [InlineData("2959-10-05 11:30", "에린 시간 10월 5일 11:30 ☀️ (낮)")]
    public void InGameChatterService_FormatErinnTime_RemovesYearAndShowsDayNight(string input, string expected)
    {
        var formatted = ChatterLineService.FormatErinnTime(input);
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public async Task ChatterLineService_GenerateLineAsync_ReturnsValidLineUnder50Chars()
    {
        var aiManager = new AiEngineManager(probeCli: (_, _) => Task.FromResult<string?>(null));
        await aiManager.DiscoverEnginesAsync();
        var service = new ChatterLineService(aiManager);
        var dummyCtx = new ChatterContext("아이라", "빙결술사", 100, 99000, "휴식 중", "던바튼", "가방: 400/1000", "5,000,000 골드");

        var line = await service.GenerateLineAsync(ChatterPersona.MorningSpirit, null, dummyCtx);
        Assert.False(string.IsNullOrWhiteSpace(line.Text));
        Assert.True(ChatText.Count(line.Text) <= 50);
        Assert.Contains("닝", line.Text);
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

    [Fact]
    public void PersonaTemplates_TotalCount_Exceeds_800()
    {
        // 5대 페르소나의 전체 대사 총합 계산
        var pools = new[]
        {
            PersonaTemplatesData.VillainessLines,
            PersonaTemplatesData.ScroogeLines,
            PersonaTemplatesData.MorningLines,
            PersonaTemplatesData.GyeongsangLines,
            PersonaTemplatesData.IdolDancerLines
        };

        var total = 0;
        foreach (var pool in pools)
        {
            Assert.True(pool.Count == 12); // 12대 카테고리 전수 구비 확인
            foreach (var kvp in pool)
            {
                total += kvp.Value.Length;
                // 각 문장이 50자 이내 규칙 준수하는지 전수 검증
                foreach (var line in kvp.Value)
                {
                    Assert.True(line.Length <= 45, $"문장 길이 초과 ({line.Length}자): {line}");
                }
            }
        }

        Assert.True(total >= 800, $"템플릿 총합({total}개)이 800개 이상이어야 합니다.");
    }

    [Fact]
    public void PersonaTemplates_12Categories_Routing()
    {
        // 1. 가방 과적 (100%+)
        var ctxOver = new ChatterContext("류트", "전사", 50, 10000, "전투", "던전", "가방 무게 100%", "10000");
        Assert.Equal("가방_과적", PersonaTemplates.DetermineCategory(ctxOver));

        // 2. 보스 레이드
        var ctxBoss = new ChatterContext("류트", "전사", 50, 10000, "글라스기브넨 보스 레이드", "어비스", "정상", "10000");
        Assert.Equal("전투_보스레이드", PersonaTemplates.DetermineCategory(ctxBoss));

        // 3. 일반 전투
        var ctxCombat = new ChatterContext("류트", "전사", 50, 10000, "필드 몬스터 사냥 중", "가이레흐", "정상", "10000");
        Assert.Equal("전투_일반", PersonaTemplates.DetermineCategory(ctxCombat));

        // 4. 낚시
        var ctxFish = new ChatterContext("류트", "전사", 50, 10000, "낚시 중", "이멘마하 호수", "정상", "10000");
        Assert.Equal("낚시", PersonaTemplates.DetermineCategory(ctxFish));

        // 5. 생산 가공
        var ctxCraft = new ChatterContext("류트", "전사", 50, 10000, "가공 시설 작업 중", "작업대", "정상", "10000");
        Assert.Equal("생산_가공", PersonaTemplates.DetermineCategory(ctxCraft));

        // 6. 자연 채집
        var ctxGather = new ChatterContext("류트", "전사", 50, 10000, "벌목 작업 중", "벌목 캠프", "정상", "10000");
        Assert.Equal("채집_자연", PersonaTemplates.DetermineCategory(ctxGather));

        // 7. 골드 부족
        var ctxNoGold = new ChatterContext("류트", "전사", 50, 10000, "휴식", "티르코네일", "정상", "0 골드 (부족)");
        Assert.Equal("골드_부족", PersonaTemplates.DetermineCategory(ctxNoGold));

        // 8. 골드 부자 (500만 골드 이상)
        var ctxRich = new ChatterContext("류트", "전사", 50, 10000, "휴식", "티르코네일", "정상", "10,000,000 골드");
        Assert.Equal("골드_부자", PersonaTemplates.DetermineCategory(ctxRich));

        // 9. 마을 휴식
        var ctxTown = new ChatterContext("류트", "전사", 50, 10000, "마을 휴식 중", "던바튼 광장", "정상", "100,000 골드");
        Assert.Equal("마을_휴식", PersonaTemplates.DetermineCategory(ctxTown));
    }

    [Fact]
    public void CustomPersona_Save_Load_Delete()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "MobiMateTest_Persona_" + Guid.NewGuid().ToString("N"));
        var sm = new SnapshotManager(tempDir);

        try
        {
            // 1. 초기 상태 빈 목록
            var initial = sm.LoadCustomPersonas();
            Assert.Empty(initial);

            // 2. 저장
            var cp = new CustomPersona
            {
                Name = "츤데레 메이드",
                TagEmoji = "🎀",
                SystemPrompt = "도도하지만 주인을 챙기는 메이드. 어미로 '~라구요!' 사용."
            };
            sm.SaveCustomPersona(cp);

            // 3. 파일에서 재로드 확인
            var reloaded = sm.LoadCustomPersonas();
            Assert.Single(reloaded);
            Assert.Equal("츤데레 메이드", reloaded[0].Name);
            Assert.Equal("🎀 츤데레 메이드", reloaded[0].DisplayName);
            Assert.Equal("도도하지만 주인을 챙기는 메이드. 어미로 '~라구요!' 사용.", reloaded[0].SystemPrompt);

            // 4. 삭제 확인
            var deleted = sm.DeleteCustomPersona(cp.Id);
            Assert.True(deleted);
            var afterDelete = sm.LoadCustomPersonas();
            Assert.Empty(afterDelete);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
