using System.Text.Json;

namespace MobiMate.Tests;

/// <summary>
/// get_activity 호환(2026-10-22 CLI 점검): 게임 업데이트(10-15) 뒤 IsGathering·IsPlayingInstrument·IsSitting·CanStopCurrentAction 등이 없어지고
/// Mode·Performance·Interaction 묶음이 생겼다. 새 응답에서도 활동 설명과 "정지 가능"이 맞아야 한다.
/// </summary>
public class ActivityCompatTests
{
    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };

    // 2026-10-22에 받은 실제 응답의 모양(옛 IsGathering 등이 없다)
    private static ActivityInfo Parse(string extra = "", string mode = "\"MainButtonState\":\"Compass\",\"SitState\":\"None\"", string perf = "\"IsPlaying\":false", bool combat = false, bool dead = false) =>
        JsonSerializer.Deserialize<ActivityInfo>($$"""
        {"IsAutoPlaying":false,"CanStartAutoPlay":true,"IsAutoTraveling":false,"IsDead":{{dead.ToString().ToLowerInvariant()}},"IsReviving":false,"IsInCombat":{{combat.ToString().ToLowerInvariant()}},
         "Dungeon":{"State":"NotInDungeon","IsBossBattleInProgress":false},
         "Performance":{ {{perf}} },
         "Interaction":{"HasTarget":false,"AvailableInteractionType":"None","LastRunningInteractionType":"None","TargetKind":"None"},
         "Mode":{ {{mode}} } {{extra}} }
        """, Read)!;

    [Fact]
    public void NewShape_Deserializes_AndIdleIsWaiting()
    {
        var a = Parse();
        Assert.Equal("Compass", a.Mode!.MainButtonState);
        Assert.False(a.CanStop());
        Assert.Equal("대기 중", GameStateCache.DescribeActivity(a));
    }

    [Fact]
    public void StopButtonShowing_MeansStoppable_AndIsDescribedAsWork()
    {
        var a = Parse(mode: "\"MainButtonState\":\"Stop\",\"SitState\":\"None\"");
        Assert.True(a.CanStop());
        Assert.Equal("작업 중", GameStateCache.DescribeActivity(a));
    }

    [Fact]
    public void Performance_IsPlaying_IsDescribedAsPlaying()
    {
        var a = Parse(perf: "\"IsPlaying\":true,\"InstrumentName\":\"하모니카\"", mode: "\"MainButtonState\":\"Stop\",\"SitState\":\"None\"");
        Assert.True(a.IsPlayingMusic());
        Assert.Equal("연주 중", GameStateCache.DescribeActivity(a));
    }

    [Fact]
    public void SitStateSitting_IsRestingOnAChair()
    {
        var a = Parse(mode: "\"MainButtonState\":\"Stop\",\"SitState\":\"Sitting\"");
        Assert.True(a.IsSittingOnChair());
        Assert.Equal("휴식 중", GameStateCache.DescribeActivity(a));
    }

    [Fact]
    public void RunningGatheringInteraction_IsDescribedAsGathering()
    {
        var json = """{"IsInCombat":false,"IsDead":false,"Interaction":{"HasTarget":true,"AvailableInteractionType":"Gathering","LastRunningInteractionType":"Gathering","TargetKind":"Prop"},"Mode":{"MainButtonState":"Stop","SitState":"None"}}""";
        var a = JsonSerializer.Deserialize<ActivityInfo>(json, Read)!;
        Assert.Equal("채집 중", GameStateCache.DescribeActivity(a));
    }

    [Fact]
    public void DeadAndReviving_AreReported()
    {
        Assert.Equal("쓰러짐", GameStateCache.DescribeActivity(Parse(dead: true)));
        var reviving = JsonSerializer.Deserialize<ActivityInfo>("""{"IsDead":false,"IsReviving":true,"IsInCombat":false}""", Read)!;
        Assert.Equal("부활 중", GameStateCache.DescribeActivity(reviving));
    }

    [Fact]
    public void OldShape_StillWorks()
    {
        var old = JsonSerializer.Deserialize<ActivityInfo>("""{"IsGathering":true,"IsInCombat":false,"CanStopCurrentAction":true}""", Read)!;
        Assert.Equal("채집 중", GameStateCache.DescribeActivity(old));
        Assert.True(old.CanStop());
    }
}
