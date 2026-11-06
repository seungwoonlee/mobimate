using MobiMate.Web.Voice;

namespace MobiMate.Web.Tests;

/// <summary>누적 학습 (FR-VC-08): 낱말 빈도, 고친 쌍, 치환, 개인정보(문장 미저장), 정리</summary>
public class VocabularyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-vocab-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private VoiceVocabulary Make() => new(_dir, () => _now);

    private string FileText() => File.ReadAllText(Path.Combine(_dir, "stt", "vocab.json"));

    // ── 고친 쌍 뽑기 ──

    [Fact]
    public void ExtractPairs_FindsSubstitutions_AndAttachesANeighbourToOneSyllableHunks()
    {
        // P0 실측 오인식: 힐러→일러, 분→번
        var pairs = VoiceVocabulary.ExtractPairs("일러한번만더구합니다", "힐러한분만더구합니다");
        Assert.Equal(new[] { ("일러", "힐러"), ("번만", "분만") }, pairs);
    }

    [Fact]
    public void ExtractPairs_RealMisrecognitionsFromTheFirstMeasurement()
    {
        var pairs = VoiceVocabulary.ExtractPairs("보습패턴조심하세요바닥동판나옵니다", "보스패턴조심하세요바닥장판나옵니다");
        Assert.Equal(new[] { ("습패", "스패"), ("동판", "장판") }, pairs);
    }

    [Fact]
    public void ExtractPairs_AdjacentChangedSyllables_FormOneFragment()
    {
        // 이어진 두 글자가 모두 달라지면 한 덩어리 "사용→샤오"
        Assert.Equal(new[] { ("사용", "샤오") }, VoiceVocabulary.ExtractPairs("오늘미사용합니다", "오늘미샤오합니다"));
    }

    [Theory]
    [InlineData("저는잠깐자리비겠습니다", "저는잠깐자리비우겠습니다")]     // 삽입만
    [InlineData("저는잠깐자리비우겠습니다", "저는잠깐자리비겠습니다")]     // 삭제만
    [InlineData("지금레이드들어갑니다", "지금레이드들어갑니다")]           // 안 고침
    public void ExtractPairs_IgnoresInsertionsDeletionsAndUnchanged(string original, string sent) =>
        Assert.Empty(VoiceVocabulary.ExtractPairs(original, sent));

    [Fact]
    public void ExtractPairs_IgnoresASentenceThatWasRewritten()
    {
        // 편집 거리가 8을 넘으면 고친 것이 아니라 다시 쓴 것이다: 엉뚱한 쌍을 배우지 않는다
        Assert.Empty(VoiceVocabulary.ExtractPairs("지금레이드들어갑니다준비해주세요", "오늘은쉬겠습니다내일봐요"));
    }

    [Fact]
    public void ExtractPairs_IgnoresTooManySeparateEdits()
    {
        Assert.Empty(VoiceVocabulary.ExtractPairs("가나다라마바사아자차", "가너다러마버사어자처"));   // 5곳을 고쳤다
    }

    [Fact]
    public void ExtractPairs_IgnoresHunksLongerThanSixSyllables()
    {
        Assert.Empty(VoiceVocabulary.ExtractPairs("가나다라마바사아자차", "가아야어오우유이으에차"));
    }

    // ── 배우기와 활성화 ──

    [Fact]
    public void APairBecomesActiveOnlyAfterTwoConfirmations()
    {
        var v = Make();
        v.Learn("힐러 한 분만 더 구합니다", "일러한번만더구합니다");
        var once = v.Apply("일러한번만더구합니다");
        Assert.Equal("일러한번만더구합니다", once.Text);   // 1회: 아직 적용하지 않는다
        Assert.Empty(once.Applied);

        v.Learn("힐러 한 분만 더 구합니다", "일러한번만더구합니다");
        var (text, applied) = v.Apply("일러한번만더구합니다");
        Assert.Equal("힐러한분만더구합니다", text);
        Assert.Contains("일러→힐러", applied);
        Assert.Contains("번만→분만", applied);
    }

    [Fact]
    public void LearnedSubstitution_AppliesToOtherSentencesToo()
    {
        var v = Make();
        for (var i = 0; i < 2; i++) v.Learn("힐러 구합니다", "일러구합니다");
        Assert.Equal("힐러님어디계세요", v.Apply("일러님어디계세요").Text);
    }

    [Fact]
    public void Apply_IsSinglePass_NeverChainsOneSubstitutionIntoAnother()
    {
        var v = Make();
        for (var i = 0; i < 2; i++)
        {
            v.Learn("다라", "가나");   // 가나 → 다라
            v.Learn("마바", "다라");   // 다라 → 마바
        }
        Assert.Equal("다라", v.Apply("가나").Text);     // 바뀐 "다라"를 다시 "마바"로 바꾸지 않는다
        Assert.Equal("마바", v.Apply("다라").Text);
        Assert.Equal("다라마바", v.Apply("가나다라").Text);   // 한 번에 각각 한 번씩
    }

    [Fact]
    public void Apply_PrefersTheLongestMatch()
    {
        var v = Make();
        for (var i = 0; i < 2; i++)
        {
            v.Learn("다라", "가나");        // 가나 → 다라
            v.Learn("마바사", "가나다");    // 가나다 → 마바사
        }
        Assert.Equal("마바사", v.Apply("가나다").Text);   // 더 긴 "가나다"가 먼저
    }

    [Fact]
    public void ASingleSyllableRecognition_IsNeverApplied()
    {
        var v = Make();
        // 한 글자만 다른 쌍도 이웃을 붙여 2음절로 배우므로 from은 항상 2음절 이상이다
        for (var i = 0; i < 3; i++) v.Learn("보스 나옵니다", "보습나옵니다");
        var pairs = v.View().Pairs;
        Assert.All(pairs, p => Assert.True(p.From.Length >= VoiceVocabulary.MinApplySyllables));
    }

    [Fact]
    public void RevertingASubstitutionTwiceSwitchesThePairOff()
    {
        var v = Make();
        for (var i = 0; i < 2; i++) v.Learn("힐러 구합니다", "일러구합니다");   // 활성화
        Assert.Contains("힐러", v.Apply("일러구합니다").Text);

        // 서버가 "힐러"로 바꿔 줬는데 사용자가 "일러"로 되돌려 보낸다
        v.Learn("일러 구합니다", "일러구합니다");
        Assert.Contains("힐러", v.Apply("일러구합니다").Text);          // 1회 되돌림: 아직 켜져 있다
        v.Learn("일러 구합니다", "일러구합니다");
        Assert.Equal("일러구합니다", v.Apply("일러구합니다").Text);      // 2회: 자동으로 꺼졌다
        Assert.True(v.View().Pairs.Single().Off);
    }

    [Fact]
    public void ManuallySwitchingAPairOnAgainResetsItsRevertCount()
    {
        var v = Make();
        for (var i = 0; i < 2; i++) v.Learn("힐러 구합니다", "일러구합니다");
        for (var i = 0; i < 2; i++) v.Learn("일러 구합니다", "일러구합니다");
        Assert.True(v.View().Pairs.Single().Off);

        Assert.True(v.SetPairOff("일러", "힐러", false));
        Assert.Equal(0, v.View().Pairs.Single().Reverts);
        Assert.Contains("힐러", v.Apply("일러구합니다").Text);
    }

    [Fact]
    public void LearningOff_LearnsNothing_AndAppliesNothing()
    {
        var v = Make();
        for (var i = 0; i < 2; i++) v.Learn("힐러 구합니다", "일러구합니다");
        v.SetLearning(false);
        Assert.Equal("일러구합니다", v.Apply("일러구합니다").Text);   // 꺼지면 이미 배운 치환도 하지 않는다
        v.Learn("새로운 낱말입니다", null);
        Assert.DoesNotContain(v.View().Words, w => w.Word == "새로운");
        Assert.Empty(v.HotwordCandidates(10));

        v.SetLearning(true);
        Assert.Contains("힐러", v.Apply("일러구합니다").Text);   // 배운 것은 지워지지 않았다
    }

    // ── 낱말 ──

    [Fact]
    public void Words_AreCounted_ExcludingDigitsEmojiShortAndLongTokens()
    {
        var w = VoiceVocabulary.Words("힐러 한 분만 😊 더 구합니다! 010-1234-5678 길드원12 a https://x.y/z 가나다라마바사아자").ToList();
        Assert.Contains("힐러", w);
        Assert.Contains("분만", w);
        Assert.Contains("구합니다", w);          // 문장부호는 떼고
        Assert.DoesNotContain("한", w);           // 한 글자
        Assert.DoesNotContain("더", w);
        Assert.DoesNotContain("a", w);
        Assert.DoesNotContain(w, x => x.Any(char.IsDigit));   // 숫자가 섞인 낱말은 통째로 제외
        Assert.DoesNotContain("가나다라마바사아자", w);          // 9자 이상
        Assert.DoesNotContain(w, x => x.Contains("http"));
    }

    [Fact]
    public void HotwordCandidates_NeedThreeUses_AndAreOrderedByCount()
    {
        var v = Make();
        for (var i = 0; i < 5; i++) v.Learn("힐러 구합니다");
        for (var i = 0; i < 3; i++) v.Learn("장판 조심");
        v.Learn("한번만 쓴 낱말");
        Assert.Equal(new[] { "구합니다", "힐러", "장판", "조심" }, v.HotwordCandidates(10));   // 같은 횟수면 가나다순
        Assert.Equal(new[] { "구합니다", "힐러" }, v.HotwordCandidates(2));
    }

    // ── 개인정보 ──

    /// <summary>
    /// vocab.json에 저장된 "낱말"(Words의 키)과 쌍(Pairs의 From·To)을 읽는다. (파일에는 한글이 \uXXXX로 저장되므로 원문 문자열 비교로는 아무것도 잡지 못한다 —
    /// 처음에는 그런 헛통과 단언이었다.) 속성 이름·날짜 같은 구조 값은 제외한다.
    /// </summary>
    private List<string> StoredFragments()
    {
        var root = System.Text.Json.JsonDocument.Parse(FileText()).RootElement;
        var found = new List<string>();
        foreach (var w in root.GetProperty("Words").EnumerateObject()) found.Add(w.Name);
        foreach (var p in root.GetProperty("Pairs").EnumerateArray())
        {
            found.Add(p.GetProperty("From").GetString()!);
            found.Add(p.GetProperty("To").GetString()!);
        }
        return found;
    }

    [Fact]
    public void TheFile_NeverContainsTheSentenceItself()
    {
        var v = Make();
        v.Learn("힐러 한 분만 더 구합니다 파티장님 고맙습니다", "일러한번만더구합니다파티장님고맙습니다");
        var all = StoredFragments();

        Assert.Contains("힐러", all);                      // 낱말과 쌍은 있다 (검사가 헛돌지 않는다는 증거)
        Assert.Contains("파티장님", all);
        Assert.Contains("일러", all);                      // 고친 쌍의 인식 쪽
        Assert.All(all, s =>
        {
            Assert.DoesNotContain(" ", s);                  // 공백이 든 값이 없다 = 문장이 아니다
            Assert.True(s.Length <= 8, $"낱말·쌍보다 긴 값이 저장됨: {s}");
        });
        Assert.DoesNotContain("일러한번만더구합니다파티장님고맙습니다", all);   // 인식 원문
        Assert.DoesNotContain("힐러한분만더구합니다파티장님고맙습니다", all);   // 보낸 글(공백 뺀 것)
    }

    [Fact]
    public void ThePairsHoldNoSurroundingContext_OnlyTheChangedFragmentWithOneNeighbour()
    {
        var v = Make();
        v.Learn("길동님 힐러 불러요", "길동님일러불러요");
        var p = v.View().Pairs.Single();
        Assert.Equal(("일러", "힐러"), (p.From, p.To));       // 이웃 하나까지만
        Assert.DoesNotContain("길동", p.From + p.To);          // 앞뒤 낱말(이름일 수 있음)은 쌍에 들어가지 않는다
        Assert.DoesNotContain("불러", p.From + p.To);
    }

    // ── 정리·저장 ──

    [Fact]
    public void EntriesUnusedFor90Days_AreDropped()
    {
        var v = Make();
        v.Learn("오래된 낱말입니다");
        _now = _now.AddDays(89);
        v.Learn("석달쯤 지난 말입니다");
        Assert.Contains(v.View().Words, w => w.Word == "오래된");        // 89일: 아직 남는다
        _now = _now.AddDays(2);                                           // 처음 것은 91일
        v.Learn("새로운 말입니다");
        var words = v.View().Words.Select(w => w.Word).ToList();
        Assert.DoesNotContain("오래된", words);
        Assert.DoesNotContain("낱말입니다", words);
        Assert.Contains("새로운", words);
        Assert.Contains("석달쯤", words);                                 // 2일 전 것은 남는다
    }

    [Fact]
    public void PersistsAcrossRestarts()
    {
        var v = Make();
        for (var i = 0; i < 2; i++) v.Learn("힐러 구합니다", "일러구합니다");
        var again = Make();
        Assert.Contains("힐러", again.Apply("일러구합니다").Text);
        Assert.Contains(again.View().Words, w => w.Word == "힐러" && w.Count == 2);
    }

    [Fact]
    public void ACorruptFile_IsQuarantined_AndTheUserIsTold()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "stt"));
        File.WriteAllText(Path.Combine(_dir, "stt", "vocab.json"), "{ 이건 json이 아님");
        var v = Make();
        Assert.NotNull(v.LoadWarning);
        Assert.Contains("손상", v.LoadWarning);
        Assert.Empty(v.View().Words);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_dir, "stt"), "*.corrupted.*"));   // 지우지 않고 따로 보관
        v.Learn("다시 시작합니다");   // 이후 정상 동작
        Assert.Contains(v.View().Words, w => w.Word == "시작합니다");
    }

    [Fact]
    public void ClearKeepsTheLearningSwitch_AndRemoveWorks()
    {
        var v = Make();
        v.SetLearning(true);
        v.Learn("힐러 구합니다", "일러구합니다");
        Assert.True(v.RemoveWord("힐러"));
        Assert.False(v.RemoveWord("힐러"));
        v.Learn("힐러 구합니다", "일러구합니다");
        Assert.True(v.RemovePair("일러", "힐러"));
        v.Clear();
        Assert.Empty(v.View().Words);
        Assert.Empty(v.View().Pairs);
        Assert.True(v.LearningOn);
    }

    [Fact]
    public void WordList_IsCapped()
    {
        var v = Make();
        // 서로 다른 2음절 낱말을 한 문장에 100개씩 넣어 MaxWords보다 많이 배우게 한다
        for (var batch = 0; batch < (VoiceVocabulary.MaxWords + 100) / 100; batch++)
        {
            var words = Enumerable.Range(0, 100).Select(j => $"{(char)('가' + batch)}{(char)('가' + j)}");
            v.Learn(string.Join(" ", words));
        }
        Assert.True(v.View(maxWords: 100_000).Words.Count <= VoiceVocabulary.MaxWords);
    }

    [Fact]
    public void LearnedPostProcessor_AppliesTheLearnedSubstitutions()
    {
        var v = Make();
        for (var i = 0; i < 2; i++) v.Learn("힐러 구합니다", "일러구합니다");
        var settings = new MobiMate.Web.Hosting.WebSettingsStore(_dir);
        var r = new LearnedPostProcessor(v, new SpacingProvider(v), settings).Process("  일러구합니다  ");
        Assert.Equal("힐러구합니다", r.Text);
        Assert.Equal(new[] { "일러→힐러" }, r.Applied);
    }

    // ── 리뷰 반영: 데이터 보호·상한·잔존 파일 ──

    [Fact]
    public void AFileThatCannotBeRead_IsNeverOverwritten_AndTheUserIsTold()
    {
        // 잠겨 있어서 읽지 못한 것을 "비어 있다"로 보고 빈 내용을 저장하면 원본(과 백업)이 사라진다
        var seed = Make();
        for (var i = 0; i < 2; i++) seed.Learn("힐러 구합니다", "일러구합니다");
        var path = Path.Combine(_dir, "stt", "vocab.json");
        var original = File.ReadAllText(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))   // 다른 프로그램이 쓰는 중
        {
            var v = Make();
            Assert.NotNull(v.LoadWarning);
            Assert.Contains("저장하지 않습니다", v.LoadWarning);
            v.Learn("새로 배운 낱말입니다");   // 예외 없이 동작하되 저장은 하지 않는다
            v.SetLearning(false);
        }
        Assert.Equal(original, File.ReadAllText(path));   // 원본이 그대로다
        Assert.Contains("힐러", Make().Apply("일러구합니다").Text);   // 잠금이 풀리면 배운 것이 그대로 살아 있다
    }

    [Fact]
    public void ValidJsonWithNullEntries_DoesNotStopStartup()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "stt"));
        File.WriteAllText(Path.Combine(_dir, "stt", "vocab.json"),
            "{\"Version\":1,\"LearningOn\":true,\"Words\":{\"널값\":null,\"\":{\"Count\":1,\"Last\":\"2026-11-06T00:00:00Z\"}},\"Pairs\":[null,{\"From\":\"\",\"To\":\"가나\",\"Count\":2}]}");
        var v = Make();   // 예외 없이 시작
        Assert.Empty(v.View().Words);
        Assert.Empty(v.View().Pairs);
        v.Learn("정상으로 돌아왔습니다");
        Assert.Contains(v.View().Words, w => w.Word == "정상으로");
    }

    [Fact]
    public void HugeInputs_AreClipped_SoMemoryAndTimeStayBounded()
    {
        var v = Make();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        v.Learn(new string('나', 50_000), new string('가', 50_000));   // 편집 거리 표가 5만×5만이면 수 GB
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"오래 걸렸다: {sw.Elapsed}");
        Assert.All(v.View().Words, w => Assert.True(w.Word.Length <= 8));
        Assert.True(new FileInfo(Path.Combine(_dir, "stt", "vocab.json")).Length < 50_000);
    }

    [Fact]
    public void ThePairList_IsCapped()
    {
        var v = Make();
        for (var i = 0; i < VoiceVocabulary.MaxPairs + 40; i++)
        {
            string Syll(int n) => new(new[] { (char)(0xAC00 + n), (char)(0xAC00 + n + 1) });
            v.Learn(Syll(5000 + 2 * i), Syll(2 * i));   // 서로 다른 두 음절 맞바꿈 쌍
        }
        Assert.True(v.View().Pairs.Count <= VoiceVocabulary.MaxPairs);
    }

    [Theory]
    [InlineData("word")]
    [InlineData("pair")]
    [InlineData("clear")]
    public void DeletingLearnedContent_AlsoRemovesBackupQuarantineAndHotwordFiles(string how)
    {
        // 저장소가 직전 내용을 vocab.json.bak으로 남기고, 핫워드 파일에는 낱말이 평문으로 있다. 지웠는데 남아 있으면 안 된다.
        var v = Make();
        for (var i = 0; i < 2; i++) v.Learn("힐러 구합니다", "일러구합니다");
        var dir = Path.Combine(_dir, "stt");
        File.WriteAllText(Path.Combine(dir, "hotwords.txt"), "힐 러");
        File.WriteAllText(Path.Combine(dir, "vocab.json.corrupted.20260101_000000.bak"), "옛 내용 힐러");
        Assert.True(File.Exists(Path.Combine(dir, "vocab.json.bak")), "시험 전제: 백업이 있어야 한다");

        Assert.True(how switch { "word" => v.RemoveWord("힐러"), "pair" => v.RemovePair("일러", "힐러"), _ => Do(v.Clear) });

        Assert.False(File.Exists(Path.Combine(dir, "vocab.json.bak")));
        Assert.False(File.Exists(Path.Combine(dir, "hotwords.txt")));
        Assert.Empty(Directory.GetFiles(dir, "vocab.json.corrupted.*"));
        static bool Do(Action a) { a(); return true; }
    }

    [Fact]
    public void ChangingTheLearnedPairs_IsPickedUpByTheCachedMatcher()
    {
        // Apply는 활성 쌍의 정규식을 캐시한다: 학습·켜기·끄기·지우기 뒤에는 새 내용이 바로 반영되어야 한다
        var v = Make();
        for (var i = 0; i < 2; i++) v.Learn("힐러 구합니다", "일러구합니다");
        Assert.Equal("힐러구합니다", v.Apply("일러구합니다").Text);
        v.SetPairOff("일러", "힐러", true);
        Assert.Equal("일러구합니다", v.Apply("일러구합니다").Text);
        v.SetPairOff("일러", "힐러", false);
        Assert.Equal("힐러구합니다", v.Apply("일러구합니다").Text);
        v.RemovePair("일러", "힐러");
        Assert.Equal("일러구합니다", v.Apply("일러구합니다").Text);
    }
}
