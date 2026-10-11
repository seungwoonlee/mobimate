namespace MobiMate.Tests;

[Collection(GlobalStateCollection.Name)]
public class ChatTextTests
{
    [Fact]
    public void Count_TreatsEmojiAsOneCodePoint_ByDefault()
    {
        Assert.Equal(ChatCountMode.CodePoint, ChatText.Mode);
        Assert.Equal(3, ChatText.Count("안녕😊"));
        Assert.Equal(0, ChatText.Count(null));
    }

    [Fact]
    public void Truncate_NeverSplitsSurrogatePair()
    {
        var s = new string('가', 49) + "😊😊";
        var cut = ChatText.Truncate(s, 50);
        Assert.Equal(50, ChatText.Count(cut));
        Assert.False(char.IsHighSurrogate(cut[^1]));
        Assert.EndsWith("😊", cut);
    }

    [Fact]
    public void Utf16Mode_CountsEmojiAsTwo_AndTruncatesBeforeHalfEmoji()
    {
        var prev = ChatText.Mode;
        try
        {
            ChatText.Mode = ChatCountMode.Utf16;
            Assert.Equal(4, ChatText.Count("안녕😊"));
            var cut = ChatText.Truncate(new string('가', 49) + "😊", 50);
            Assert.Equal(new string('가', 49), cut); // 이모지 절반(1칸)은 넣지 않는다
        }
        finally
        {
            ChatText.Mode = prev;
        }
    }

    [Fact]
    public void Sanitize_ReplacesNewlinesAndTrims()
    {
        Assert.Equal("가 나 다", ChatText.Sanitize("  가\r\n나\n다  "));
    }

    [Fact]
    public void ChatPlan_BodyLimitLeavesRoomForEmoji_SoNothingIsSilentlyLost()
    {
        var plan = ChatPlanService.BuildChatPlan(new string('가', 60) + " 안녕");
        Assert.Equal(50, ChatText.Count(plan.FinalMessage));
        Assert.EndsWith(" 😊", plan.FinalMessage);
    }
}

public class CommandIntentParserTests
{
    private static readonly string[] Gatherables = { "사과", "철광석", "구리 광석", "나무 장작", "통나무" };

    [Theory]
    [InlineData("정지")]
    [InlineData("멈춰!")]
    [InlineData("긴급 정지해줘")]
    [InlineData("그만해")]
    [InlineData(" 스톱 ")]
    public void Stop_OnlyForImperativeShortCommands(string input)
    {
        var intent = CommandIntentParser.Parse(input, Gatherables);
        Assert.Equal(IntentKind.Stop, intent.Kind);
        Assert.True(intent.ExecutesImmediately);
    }

    [Theory]
    [InlineData("정지 기능 알려줘")]
    [InlineData("멈춰 있는 캐릭터는 어떻게 해?")]
    [InlineData("그만두는 법")]
    public void Stop_QuestionsAboutStopping_GoToLlm(string input)
    {
        Assert.Equal(IntentKind.None, CommandIntentParser.Parse(input, Gatherables).Kind);
    }

    [Theory]
    [InlineData("오늘 숙제 뭐 남았어?", IntentKind.CheckDailyMissions)]
    [InlineData("일일미션 확인", IntentKind.CheckDailyMissions)]
    [InlineData("가방 정리 좀", IntentKind.InventoryDiet)]
    [InlineData("무게 줄여줘", IntentKind.InventoryDiet)]
    [InlineData("가공물 수거해", IntentKind.CollectWorks)]
    public void NonStopIntents_AreRecognized(string input, IntentKind kind)
    {
        var intent = CommandIntentParser.Parse(input, Gatherables);
        Assert.Equal(kind, intent.Kind);
        Assert.False(intent.ExecutesImmediately);
    }

    [Fact]
    public void Gather_PicksLongestName_AndCount()
    {
        var intent = CommandIntentParser.Parse("구리 광석 30개 캐줘", Gatherables);
        Assert.Equal(IntentKind.Gather, intent.Kind);
        Assert.Equal("구리 광석", intent.ItemName);
        Assert.Equal(30, intent.Count);
        Assert.False(intent.ExecutesImmediately); // 확인 카드를 거친다
    }

    [Theory]
    [InlineData("Lv.100 캐릭인데 사과 20개 캐줘", 20)]
    [InlineData("사과를 15 채집", 15)]
    [InlineData("사과 좀 캐줘", null)]
    public void Gather_CountPrefersUnit_AndIgnoresLevel(string input, int? expected)
    {
        var intent = CommandIntentParser.Parse(input, Gatherables);
        Assert.Equal(IntentKind.Gather, intent.Kind);
        Assert.Equal("사과", intent.ItemName);
        Assert.Equal(expected, intent.Count);
    }

    [Fact]
    public void Gather_WithoutCount_LeavesCountNull()
    {
        var intent = CommandIntentParser.Parse("사과 채집해줘", Gatherables);
        Assert.Equal("사과", intent.ItemName);
        Assert.Null(intent.Count);
    }

    [Theory]
    [InlineData("사과 파이 채집해줘")]         // 상세설계 §8 오탐 사례: "사과"가 아니라 "사과 파이"
    [InlineData("사과파이 캐줘")]
    [InlineData("사과도끼 채집해")]            // 붙여 쓴 조사처럼 보이지만 다른 낱말
    [InlineData("사과 파이 맛있어?")]          // 채집 동사가 없다
    [InlineData("은 광석 채집해줘")]           // 채집 목록에 없다
    [InlineData("채집 명당 알려줘")]           // 아이템명이 없다
    public void Gather_NotTriggered_WithoutVerbOrKnownItem(string input)
    {
        Assert.Equal(IntentKind.None, CommandIntentParser.Parse(input, Gatherables).Kind);
    }
}

public class JsonFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-store-" + Guid.NewGuid().ToString("N"));

    public JsonFileStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void FirstSave_UsesMove_ThenReplaceKeepsOneBackup()
    {
        var store = new JsonFileStore();
        var path = Path.Combine(_dir, "a.json");

        Assert.True(store.Save(path, new List<int> { 1 }));
        Assert.False(File.Exists(path + ".bak"));

        Assert.True(store.Save(path, new List<int> { 1, 2 }));
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(new List<int> { 1, 2 }, store.Load<List<int>>(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp.*"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ 깨진 json")]
    public void CorruptedFile_IsQuarantined_AndLoadReturnsNull(string content)
    {
        var store = new JsonFileStore();
        var path = Path.Combine(_dir, "b.json");
        File.WriteAllText(path, content);

        Assert.Null(store.Load<List<int>>(path));
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_dir, "b.json.corrupted.*.bak"));
    }

    [Fact]
    public void ConcurrentSaves_NeverLeaveBrokenJson()
    {
        var store = new JsonFileStore();
        var path = Path.Combine(_dir, "c.json");
        Parallel.For(0, 200, i => store.Save(path, Enumerable.Range(0, i % 20).ToList()));
        Assert.NotNull(store.Load<List<int>>(path));
    }
}

public class GameStateCacheTests
{
    [Fact]
    public void Contexts_UseLatestData_AndDescribeActivity()
    {
        var now = DateTimeOffset.Parse("2026-09-23T12:00:00+09:00");
        var cache = new GameStateCache(() => now);
        Assert.True(cache.IsHeaderStale);

        var ch = new CharacterInfo("칭호", "아이라", 100, "격투가", new ScoreVal("전투력", 88737), null, null, null, null, null, null, null,
            null, null, null, null, null, null, new VitalsInfo(1, 1, 1040, 1030, 0, 100, 0));
        var act = new ActivityInfo(false, false, false, true, false, false, false, false, true, "Gathering", true);
        cache.UpdateHeader(ch, act, new EnvironmentInfo("던바튼 광장", "Sunny", "2959-4-23 15:51"));
        cache.UpdateCurrencies(new List<CurrencyItem> { new("골드", 6_000_000) });

        Assert.False(cache.IsHeaderStale);
        var ctx = cache.BuildChatterContext();
        Assert.Equal("채집 중", ctx.Activity);
        Assert.Equal("던바튼 광장", ctx.Location);
        Assert.Contains("101.0%", ctx.WeightSummary);
        Assert.Equal("6,000,000 골드", ctx.GoldSummary);
        Assert.Equal("가방_과적", PersonaTemplates.DetermineCategory(ctx)); // 100% 이상은 과적 우선

        var ai = cache.BuildAiContext();
        Assert.Contains("격투가 (Lv.100)", ai);
        Assert.Contains("88,737점", ai);

        now = now.AddSeconds(31);
        Assert.True(cache.IsHeaderStale);
    }
}

public class AiEngineManagerTests
{
    [Fact]
    public async Task PaidCliEngine_IsListed_ButNeverRestoredAutomatically()
    {
        var manager = new AiEngineManager(probeCli: (cmd, _) => Task.FromResult<string?>(cmd == "claude" ? @"C:\fake\claude.exe" : null));
        await manager.DiscoverEnginesAsync(preferredEngineId: "cli:claude");

        Assert.Contains(manager.AvailableEngines, e => e.Info.Id == "cli:claude" && e.Info.CostTier == AiCostTier.Paid);
        Assert.Equal("builtin:guide", manager.CurrentEngine!.Info.Id); // FR-AI-02: 유료 엔진은 자동 복원 안 함
        Assert.True(manager.SetCurrentEngine("cli:claude"));           // 사용자가 직접 고르면 선택된다
        Assert.False(manager.SetCurrentEngine("없는:엔진"));
    }

    [Fact]
    public async Task FailingLlm_CustomPersona_FallsBackToNeutralLines_WithFlag()
    {
        // D-03: 커스텀 페르소나 + LLM 실패 → 악덕영애가 아닌 중립 대사, UsedFallback=true
        var manager = new AiEngineManager(probeCli: (cmd, _) => Task.FromResult<string?>(cmd == "claude" ? @"Z:\없는\claude.exe" : null));
        await manager.DiscoverEnginesAsync();
        manager.SetCurrentEngine("cli:claude");
        var service = new ChatterLineService(manager);
        var custom = new CustomPersona { Name = "츤데레 메이드", SystemPrompt = "흥!" };
        var ctx = new ChatterContext("아이라", "격투가", 100, 1, "대기 중", "알 수 없음", "정상", "1,000 골드", "2959-4-23 15:51");

        var line = await service.GenerateLineAsync(ChatterPersona.Custom, custom, ctx);

        Assert.True(line.UsedFallback);
        var allNeutral = NeutralPersonaLines.Lines.Values.SelectMany(x => x).ToList();
        Assert.Contains(allNeutral, n => line.Text.StartsWith(n));
        var villainess = PersonaTemplatesData.VillainessLines.Values.SelectMany(x => x);
        Assert.DoesNotContain(villainess, v => line.Text.StartsWith(v));
    }

    [Fact]
    public void CostTier_FollowsEngineType()
    {
        Assert.Equal(AiCostTier.Builtin, new BuiltInGuideEngine().Info.CostTier);
        Assert.Equal(AiCostTier.Paid, new CliAgentEngine("codex", "codex.exe", "Codex").Info.CostTier);
    }

    [Fact]
    public async Task DefaultStream_YieldsWholeReplyAsOneChunk()
    {
        IAiEngine engine = new BuiltInGuideEngine();
        var chunks = new List<string>();
        await foreach (var c in engine.StreamAsync("전투력 올리는 법", "")) chunks.Add(c);
        Assert.Single(chunks);
        Assert.False(string.IsNullOrWhiteSpace(chunks[0]));
    }
}
