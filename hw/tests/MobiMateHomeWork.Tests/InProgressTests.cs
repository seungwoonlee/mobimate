namespace MobiMateHomeWork.Tests;

/// <summary>상단 캐릭터 정보의 "진행 중" 표시: 지금 있는 지역이 레이드·어비스·필드 보스·요일 던전인지 (웹앱 InProgressTests).</summary>
public class InProgressTests
{
    private static InProgressInfo? Of(string? space) =>
        CharacterSession.InProgressOf(HomeworkCatalog.LoadEmbedded(), new EnvironmentInfo("1채널", null, null, space));

    [Theory]
    [InlineData("먼 바다의 바위 협곡", "레이드", "카브락")]
    [InlineData("먼 바다의 춤추는 바람", "레이드", "에이렐")]
    [InlineData("먼 바다의 빛바랜 환영", "레이드", "화이트 서큐버스")]
    [InlineData("허상의 정박지", "어비스", "허상의 정박지")]
    [InlineData("원념이 메아리치는 곳", "필드 보스", "앙그르바한")]   // 실측 2026-10-05
    [InlineData("광기의  동굴", "어비스", "광기의 동굴")]
    [InlineData("흩어진 물길", "어비스", "흩어진 물길")]
    public void KnownAreas_ShowKindAndTitle(string space, string kind, string title)
    {
        var r = Of(space);
        Assert.NotNull(r);
        Assert.Equal(kind, r!.Kind);
        Assert.Equal(title, r.Title);
    }

    [Theory]
    [InlineData("찬란한 유적 V")]      // 실측 2026-10-04 (일요일)
    [InlineData("찬란한 유적  III")]   // 단계(로마 숫자)가 달라도 같은 던전
    [InlineData("빛나는 동굴 V ")]     // 실측 2026-10-05 (월요일, 끝에 공백이 붙어 온다)
    public void DayDungeonAreas_MatchByPrefix(string space)
    {
        Assert.Equal("요일 던전", Of(space)!.Kind);
    }

    [Theory]
    [InlineData("콜헨")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherAreas_ShowNothing(string? space) => Assert.Null(Of(space));

    [Fact]
    public async Task HeaderAndCharacterCard_ShowTheAreaTheCharacterIsIn()
    {
        await using var h = await AppHarness.StartAsync();
        h.Game.SetMap("먼 바다의 바위 협곡");
        await h.RecordAsync();

        var header = h.Log.Headers[^1];
        Assert.Equal("레이드", header.InProgress!.Kind);
        Assert.Equal("카브락", header.InProgress.Title);
        Assert.Equal("먼 바다의 바위 협곡", header.Map);
        var card = h.Session.Characters().Card("아이라_격투가");
        Assert.Equal(new[] { "raid_cavrak" }, card.InProgressIds);

        h.Game.SetMap("콜헨");
        await h.RecordAsync();
        Assert.Null(h.Log.Headers[^1].InProgress);
        Assert.Empty(h.Session.Characters().Card("아이라_격투가").InProgressIds);
    }
}
