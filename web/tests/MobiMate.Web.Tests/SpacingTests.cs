using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MobiMate.Web.Voice;
using Xunit.Abstractions;

namespace MobiMate.Web.Tests;

/// <summary>띄어쓰기 복원 (FR-VC-09): 동작 규칙과, 보류한 문장으로 잰 정확도</summary>
public class SpacingTests(ITestOutputHelper output)
{
    /// <summary>띄어쓰기가 된 게임 문장 말뭉치 (대사 풀 약 900문장)</summary>
    private static List<string> Corpus()
    {
        var all = new List<string>();
        foreach (var dict in new[] { PersonaTemplatesData.VillainessLines, PersonaTemplatesData.ScroogeLines, PersonaTemplatesData.MorningLines,
                                     PersonaTemplatesData.GyeongsangLines, PersonaTemplatesData.IdolDancerLines, NeutralPersonaLines.Lines })
            foreach (var lines in dict.Values) all.AddRange(lines);
        return all;
    }

    private static string Compact(string s) => string.Concat(s.Where(ch => !char.IsWhiteSpace(ch)));

    /// <summary>글자 사이 경계(공백 위치)를 "공백 뺀 글자열의 인덱스" 집합으로</summary>
    private static HashSet<int> Boundaries(string spaced)
    {
        var set = new HashSet<int>();
        var count = 0;
        foreach (var ch in spaced)
        {
            if (char.IsWhiteSpace(ch)) { if (count > 0) set.Add(count); }
            else count++;
        }
        return set;
    }

    private record Score(int Tp, int Fp, int Fn, int Exact, int Lines)
    {
        public double Precision => Tp + Fp == 0 ? 1 : (double)Tp / (Tp + Fp);
        public double Recall => Tp + Fn == 0 ? 1 : (double)Tp / (Tp + Fn);
        public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);
        public override string ToString() => $"경계 정밀도 {Precision:P1} · 재현율 {Recall:P1} · F1 {F1:P1} · 문장 완전 일치 {Exact}/{Lines} ({(double)Exact / Lines:P1}) · 틀린 공백 {Fp}개 · 놓친 공백 {Fn}개";
    }

    private static Score Evaluate(SpacingRestorer r, IEnumerable<string> gold)
    {
        int tp = 0, fp = 0, fn = 0, exact = 0, n = 0;
        foreach (var g in gold)
        {
            var gold1 = string.Join(" ", g.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            var compact = Compact(gold1);
            var predicted = r.Restore(compact);
            var gb = Boundaries(gold1);
            var pb = Boundaries(predicted);
            tp += pb.Count(gb.Contains);
            fp += pb.Count(b => !gb.Contains(b));
            fn += gb.Count(b => !pb.Contains(b));
            if (predicted == gold1) exact++;
            n++;
        }
        return new Score(tp, fp, fn, exact, n);
    }

    /// <summary>말뭉치의 5분의 1을 보류하고 나머지로 사전을 만든다 (보류한 문장의 낱말은 사전에 없다)</summary>
    private static (SpacingRestorer Restorer, List<string> HeldOut) Split(int fold = 0, IEnumerable<string>? curated = null, int glue = SpacingRestorer.DefaultGlue)
    {
        var all = Corpus();
        var held = all.Where((_, i) => i % 5 == fold).ToList();
        var train = all.Where((_, i) => i % 5 != fold).ToList();
        return (SpacingRestorer.Build(train, curated ?? Array.Empty<string>(), null, glue), held);
    }

    private static Score CrossValidate(IEnumerable<string>? curated, int glue)
    {
        var scores = Enumerable.Range(0, 5).Select(f => { var (r, held) = Split(f, curated, glue); return Evaluate(r, held); }).ToList();
        return new Score(scores.Sum(s => s.Tp), scores.Sum(s => s.Fp), scores.Sum(s => s.Fn), scores.Sum(s => s.Exact), scores.Sum(s => s.Lines));
    }

    [Fact]
    public void Measure_GlueLength_Comparison()
    {
        foreach (var glue in new[] { 0, 1, 2, 3, 4 })
            output.WriteLine($"[붙임 {glue}음절 이하, 말뭉치 + 채팅 낱말] " + CrossValidate(SpacingRestorer.Curated, glue));
        Assert.True(true);
    }

    [Fact]
    public void Measure_HeldOutCorpus_WithoutAnyCuratedWords()
    {
        // 사전이 말뭉치 낱말뿐일 때 처음 보는 문장에 얼마나 일반화되는가
        var scores = Enumerable.Range(0, 5).Select(f => { var (r, held) = Split(f); return Evaluate(r, held); }).ToList();
        var total = new Score(scores.Sum(s => s.Tp), scores.Sum(s => s.Fp), scores.Sum(s => s.Fn), scores.Sum(s => s.Exact), scores.Sum(s => s.Lines));
        output.WriteLine("[보류 5겹 교차, 말뭉치 낱말만] " + total);
        Assert.True(total.Lines > 500, "말뭉치가 비어 있다");
    }

    [Fact]
    public void Measure_HeldOutCorpus_WithTheCuratedChatWords()
    {
        var scores = Enumerable.Range(0, 5).Select(f => { var (r, held) = Split(f, SpacingRestorer.Curated); return Evaluate(r, held); }).ToList();
        var total = new Score(scores.Sum(s => s.Tp), scores.Sum(s => s.Fp), scores.Sum(s => s.Fn), scores.Sum(s => s.Exact), scores.Sum(s => s.Lines));
        output.WriteLine("[보류 5겹 교차, 말뭉치 + 정리한 채팅 낱말] " + total);
        Assert.True(total.Lines > 500);
    }

    [Fact]
    public void Measure_TheEightRealisticChatSentences()
    {
        // P0에서 읽은 문장들. 정리한 채팅 낱말에 같은 낱말이 들어 있어 낙관적인 수치다(어휘 범위 확인용).
        var gold = new[]
        {
            "지금 레이드 들어갑니다 준비해 주세요", "힐러 한 분만 더 구합니다", "저는 잠깐 자리 비우겠습니다", "보스 패턴 조심하세요 바닥 장판 나옵니다",
            "수고하셨습니다 다음에 또 같이 해요", "파티 초대 부탁드립니다", "포션이 부족해서 마을에 다녀올게요", "다음 층으로 이동하겠습니다",
        };
        var r = SpacingRestorer.Build(Corpus(), SpacingRestorer.Curated);
        output.WriteLine("[사전 낱말 수 " + r.WordCount + "] " + Evaluate(r, gold));
        foreach (var g in gold) output.WriteLine($"  정답: {g}\n  복원: {r.Restore(Compact(g))}");
        Assert.Equal(8, gold.Length);
    }

    // ── 동작 규칙 ──

    private static SpacingRestorer Small(params string[] words) => SpacingRestorer.Build(Array.Empty<string>(), words);

    [Fact]
    public void KnownWords_GetSpaces() =>
        Assert.Equal("파티 초대 부탁드립니다", Small("파티", "초대", "부탁드립니다").Restore("파티초대부탁드립니다"));

    [Fact]
    public void AnUnknownStretch_IsNeverSplit_EvenWhenKnownWordsSitInsideIt()
    {
        // "초대문의"를 모르면 쪼개지 않고 한 덩어리로 둔다 (틀린 곳에 공백을 넣는 것보다 붙어 있는 편이 낫다)
        Assert.Equal("파티 초대문의 부탁드립니다", Small("파티", "부탁드립니다").Restore("파티초대문의부탁드립니다"));
    }

    [Fact]
    public void AShortUnknownFragmentAfterAWord_StaysAttached_LikeAParticle()
    {
        var r = Small("포션", "부족해서", "마을");
        Assert.Equal("포션이 부족해서 마을에", r.Restore("포션이부족해서마을에"));
        Assert.Equal("포션 이 부족해서", SpacingRestorer.Build(Array.Empty<string>(), new[] { "포션", "부족해서" }, null, glueSyllables: 0).Restore("포션이부족해서"));   // 붙이는 규칙을 끄면 떨어진다
    }

    [Fact]
    public void ADecoratedHangulRun_IsOnlySplitInsideTheHangul_DigitsAndLatinStayPut()
    {
        Assert.Equal("3층 이동5", Small("층", "이동").Restore("3층이동5"));
        Assert.Equal("ABC 파티", Small("파티").Restore("ABC 파티"));
        Assert.Equal("파티!!", Small("파티").Restore("파티!!"));
    }

    [Fact]
    public void AlreadySpacedText_IsNotGluedTogether()
    {
        var r = Small("파티", "초대");
        Assert.Equal("파티 초대", r.Restore("파티 초대"));
        Assert.Equal("파티 초대 파티", r.Restore("파티 초대파티"));   // 공백이 있는 곳은 그대로, 붙은 곳만 처리
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("가")]
    public void EmptyOrSingleSyllable_IsReturnedAsIs(string text) => Assert.Equal(text, Small("가나").Restore(text));

    [Fact]
    public void WithAnEmptyDictionary_NothingChanges() =>
        Assert.Equal("지금레이드", SpacingRestorer.Build(Array.Empty<string>(), Array.Empty<string>()).Restore("지금레이드"));

    [Fact]
    public void OneSyllableWords_OnlyComeFromTheCuratedList_NotFromTheCorpus()
    {
        // 말뭉치에서 한 글자까지 뽑으면 조사("이", "가")가 낱말이 되어 엉뚱한 곳에서 쪼개진다
        var r = SpacingRestorer.Build(new[] { "이 가 은 는 파티 초대" }, Array.Empty<string>());
        Assert.Equal("파티 초대", r.Restore("파티초대"));
        Assert.Equal(2, r.WordCount);   // 파티, 초대만
    }

    [Fact]
    public void LearnedWords_ChangeTheResult_SoItImprovesWithUse()
    {
        var before = SpacingRestorer.Build(Array.Empty<string>(), new[] { "고맙습니다" });
        Assert.Equal("쏘냐님뿌뿌단 고맙습니다", before.Restore("쏘냐님뿌뿌단고맙습니다"));   // 모르는 두 낱말은 붙어 있다

        var after = SpacingRestorer.Build(Array.Empty<string>(), new[] { "고맙습니다" }, new[] { new KeyValuePair<string, int>("쏘냐님", 3), new KeyValuePair<string, int>("뿌뿌단", 2) });
        Assert.Equal("쏘냐님 뿌뿌단 고맙습니다", after.Restore("쏘냐님뿌뿌단고맙습니다"));
    }

    [Fact]
    public void TheProvider_RebuildsTheDictionary_WhenTheUserSendsNewWords()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mm-spc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vocab = new VoiceVocabulary(dir);
            var provider = new SpacingProvider(vocab);
            var first = provider.Get();
            Assert.Same(first, provider.Get());                       // 바뀐 게 없으면 다시 만들지 않는다
            Assert.Equal("쏘냐님뿌뿌단 고맙습니다", first.Restore("쏘냐님뿌뿌단고맙습니다"));

            vocab.Learn("쏘냐님 뿌뿌단 고맙습니다");
            var second = provider.Get();
            Assert.NotSame(first, second);
            Assert.Equal("쏘냐님 뿌뿌단 고맙습니다", second.Restore("쏘냐님뿌뿌단고맙습니다"));

            vocab.SetLearning(false);                                  // 학습을 끄면 내 낱말은 사전에서 빠진다
            Assert.Equal("쏘냐님뿌뿌단 고맙습니다", provider.Get().Restore("쏘냐님뿌뿌단고맙습니다"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── 정확도 하한 (측정값이 나빠지면 알려 준다) ──

    [Fact]
    public void Accuracy_OnUnseenSentences_DoesNotRegress()
    {
        // 측정(2026-11-06, 보류 5겹 교차, 정리한 채팅 낱말 포함, 2음절 붙임): 정밀도 83.2% · 재현율 56.9% · F1 67.5%
        var s = CrossValidate(SpacingRestorer.Curated, SpacingRestorer.DefaultGlue);
        output.WriteLine("[기본 설정] " + s);
        Assert.True(s.Precision >= 0.80, $"정밀도가 내려갔다: {s.Precision:P1}");   // 틀린 공백이 늘면 안 된다
        Assert.True(s.F1 >= 0.60, $"F1이 내려갔다: {s.F1:P1}");
    }

    [Fact]
    public void Accuracy_OnTheEightChatSentences_IsExact()
    {
        var gold = new[]
        {
            "지금 레이드 들어갑니다 준비해 주세요", "힐러 한 분만 더 구합니다", "저는 잠깐 자리 비우겠습니다", "보스 패턴 조심하세요 바닥 장판 나옵니다",
            "수고하셨습니다 다음에 또 같이 해요", "파티 초대 부탁드립니다", "포션이 부족해서 마을에 다녀올게요", "다음 층으로 이동하겠습니다",
        };
        var s = Evaluate(SpacingRestorer.Build(Corpus(), SpacingRestorer.Curated), gold);
        Assert.Equal(8, s.Exact);   // 어휘 범위 안의 문장은 모두 맞는다 (낙관적 수치라 하한으로만 쓴다)
    }

    // ── 앱에 연결 ──

    private static HttpRequestMessage Req(HttpMethod m, string url, object? body = null)
    {
        var r = new HttpRequestMessage(m, url) { Content = new StringContent(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, "application/json") };
        r.Headers.Add("X-MobiMate-Req", "1");
        return r;
    }

    private static async Task<SttFinal> Listen(TestHost host, HttpClient c, string utterance)
    {
        var stt = host.Services.GetRequiredService<SttService>();
        host.Engines.Utterances = new[] { utterance };
        var previous = stt.Last()?.SessionId;
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req(HttpMethod.Post, "/api/stt/start"))).StatusCode);
        host.Audio.Current!.PushVoice(4);
        var until = DateTime.UtcNow.AddSeconds(4);
        while (DateTime.UtcNow < until && !(stt.Last() is { } l && l.SessionId != previous)) await Task.Delay(15);
        var last = stt.Last()!;
        while (DateTime.UtcNow < until && stt.Snapshot().State != SttState.Idle) await Task.Delay(15);
        return last;
    }

    private static Task<HttpResponseMessage> PostChat(HttpClient c, object body)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/chat/game") { Content = System.Net.Http.Json.JsonContent.Create(body) };
        r.Headers.Add("X-MobiMate-Req", "1");   // 학습은 이 PC 화면에서 온 요청(헤더 포함)만 받는다
        return c.SendAsync(r);
    }

    [Fact]
    public void TwoOneSyllableWords_AreNotSplitOutOfARealWord()
    {
        // "분명"이 "분 명"으로 나뉘면 안 된다 (한 글자 낱말 "분", "명"이 사전에 있어도)
        Assert.Equal("분명", Small("분", "명").Restore("분명"));
        Assert.Equal("한명", Small("한", "명").Restore("한명"));   // 보수적: 숫자+단위도 붙여 둔다 (놓치는 쪽이 틀린 공백보다 낫다)
    }

    [Fact]
    public void AOneSyllableWordBetweenKnownWords_IsStillSplit()
    {
        Assert.Equal("한 분만 더 구합니다", Small("한", "분만", "더", "구합니다").Restore("한분만더구합니다"));
    }

    [Fact]
    public async Task InTheApp_SpacingIsOffByDefault_AndOnlyChangesTheFinalText_NotTheOriginal()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        Assert.False((await TestHost.Data(await c.GetAsync("/api/stt/settings"))).GetProperty("spacing").GetBoolean());
        Assert.Equal("지금레이드들어갑니다", (await Listen(host, c, "지금레이드들어갑니다")).Text);   // 기본 끔: 붙은 채로

        var on = await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { spacing = true }));
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        var fin = await Listen(host, c, "지금레이드들어갑니다");
        Assert.Equal("지금 레이드 들어갑니다", fin.Text);
        Assert.Equal("지금레이드들어갑니다", fin.Original);   // 학습 비교용 원문은 후처리 전 그대로
    }

    [Fact]
    public async Task InTheApp_ALearnedSubstitutionAndSpacing_WorkTogether()
    {
        using var host = new TestHost();
        var c = await host.LocalAsync();
        await c.SendAsync(Req(HttpMethod.Put, "/api/stt/settings", new { spacing = true }));
        for (var i = 0; i < 2; i++)
        {
            await PostChat(c, new { text = "힐러 구합니다", autoEmote = false, source = "음성", sttOriginal = "일러구합니다" });
            await Task.Delay(1100);
        }
        var fin = await Listen(host, c, "일러구합니다");
        Assert.Equal("힐러 구합니다", fin.Text);   // 치환(일러→힐러)을 먼저, 그 뒤에 띄어쓰기
    }
}
