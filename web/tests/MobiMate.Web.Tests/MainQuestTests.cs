using System.Text.Json;
using MobiMate.Web.Services;

namespace MobiMate.Web.Tests;

/// <summary>상단 캐릭터 정보에 보이는 메인 퀘스트 고르기</summary>
public class MainQuestTests
{
    private static QuestItem Q(string title, string source, params (string desc, bool done)[] objs) =>
        new(title, source, source, objs.Select(o => new QuestObjective(o.desc, o.done)).ToList());

    private static JsonElement Pick(params QuestItem[] quests) =>
        JsonSerializer.SerializeToElement(GameViews.MainQuestOf(quests));

    [Fact]
    public void PicksTheMainSourceQuest_WithItsFirstUnfinishedObjective_AndStripsTags()
    {
        var r = Pick(Q("사이드 일", "pinned_sub", ("<color=orange>가</color>", false)),
                     Q("<b>잿빛 하늘</b>", "main", ("<color=orange>마을</color> 도착", true), ("<color=orange>퍼거스</color>와 대화", false)));
        Assert.Equal("잿빛 하늘", r.GetProperty("title").GetString());
        Assert.Equal("퍼거스와 대화", r.GetProperty("objective").GetString());
    }

    [Fact]
    public void WeeklyGoalQuest_EvenWithMainSource_IsNotTheMainStory()
    {
        Assert.Equal(JsonValueKind.Null, Pick(Q("[주간 목표] 모험가 길드의 정기 의뢰 (2)", "main", ("<color=orange>던전</color> 클리어 5/5", true))).ValueKind);
    }

    [Fact]
    public void NoQuests_IsNull() => Assert.Null(GameViews.MainQuestOf(null));
}
