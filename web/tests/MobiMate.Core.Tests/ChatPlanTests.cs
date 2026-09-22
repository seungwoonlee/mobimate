using Xunit;

namespace MobiMate.Tests;

public class ChatPlanTests
{
    [Theory]
    [InlineData("안녕하세요 반갑습니다", "😊", "/손인사1")]
    [InlineData("오늘 정말 감사해요", "😍", "/하트")]
    [InlineData("레벨업 ㅊㅋ드립니다", "🥳", "/축하해")]
    [InlineData("실수해서 죄송합니다", "😓", "/사과1")]
    [InlineData("오늘 레이드 수고하셨습니다", "😉", "/손인사1")]
    [InlineData("다들 화이팅 힘내세요", "🥳", "/응원댄스")]
    [InlineData("ㅋㅋㅋㅋ 너무 웃기다", "🤣", "/웃기1")]
    [InlineData("아이템 놓쳐서 슬프네요", "😢", "/울기1")]
    [InlineData("오늘 딜량 대박 최고", "😎", "/최고")]
    public void BuildChatPlan_MatchesEmotionAndBehaviour(string input, string expectedEmoji, string expectedBehaviour)
    {
        var plan = ChatPlanService.BuildChatPlan(input);

        Assert.Equal(expectedEmoji, plan.Emoji);
        Assert.Equal(expectedBehaviour, plan.BehaviourCommand);
        Assert.EndsWith(expectedEmoji, plan.FinalMessage);
        Assert.True(plan.FinalMessage.Length <= 50);
    }

    [Fact]
    public void BuildChatPlan_HandlesNegationAndExclusions()
    {
        // 1. 부정어 방어: "안 미안해"는 사과가 아님
        var plan1 = ChatPlanService.BuildChatPlan("난 전혀 안 미안한데?");
        Assert.NotEqual("/사과1", plan1.BehaviourCommand);

        // 2. 사과나무는 과일/채집물이므로 사과(Apology)가 아님
        var plan2 = ChatPlanService.BuildChatPlan("여기 사과나무 채집하러 가자");
        Assert.NotEqual("/사과1", plan2.BehaviourCommand);

        var plan3 = ChatPlanService.BuildChatPlan("사과파이 만들어야지");
        Assert.NotEqual("/사과1", plan3.BehaviourCommand);
    }

    [Fact]
    public void BuildChatPlan_RespectsMaxLength_AndDoesNotBreakSurrogatePair()
    {
        // 50자를 초과하는 긴 문자열 입력 시
        var longInput = new string('가', 60);
        var plan = ChatPlanService.BuildChatPlan(longInput);

        // [TST-01 변경] 글자 수 규칙이 ChatText 단일 규칙으로 바뀌었다 (FR-GC-02). 기본 모드는 코드포인트.
        Assert.True(ChatText.Count(plan.FinalMessage) <= 50);
        Assert.EndsWith(ChatPlanService.DefaultEmoji, plan.FinalMessage);
        Assert.False(char.IsHighSurrogate(plan.FinalMessage[^1]));
    }

    [Fact]
    public void GetPreviewHint_ReturnsExpectedString()
    {
        var hint1 = ChatPlanService.GetPreviewHint("안녕하세요");
        Assert.Equal("😊 /손인사1", hint1);

        var hint2 = ChatPlanService.GetPreviewHint("그냥 일반 대화입니다");
        Assert.Equal("😊", hint2);
    }
}
