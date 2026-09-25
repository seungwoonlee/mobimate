using System.Text.RegularExpressions;

namespace MobiMate;

/// <param name="Text">ChatPlanService(이모지·50자)까지 거친 최종 대사</param>
/// <param name="UsedFallback">LLM 엔진을 시도했지만 실패·시간 초과로 내장 대사 풀을 쓴 경우 true (D-03 토스트 판단용)</param>
/// <param name="Text">자동 이모티콘·행동까지 붙인 최종 문장 (WPF판과 같은 결과)</param>
/// <param name="Raw">이모지를 붙이기 전 대사. 입력창에 채울 때 쓴다: 보낼 때 이모지가 다시 붙으므로 두 번 붙지 않게 (FR-CH-03)</param>
public sealed record ChatterLine(string Text, bool UsedFallback, string Raw = "");

/// <summary>
/// "아무말 대잔치" 한마디 생성기 (FR-CH-02).
/// WPF판 InGameChatterService에서 타이머·자동 전송(send_chat)을 걷어내고 생성만 남겼다 (D-01, D-05).
/// 생성된 대사는 호출자가 입력창에 채우며, 전송은 사용자가 따로 한다.
/// </summary>
public sealed class ChatterLineService
{
    public static readonly TimeSpan LlmTimeout = TimeSpan.FromSeconds(2.5);
    private const int MaxLineLength = 42;

    private readonly AiEngineManager _aiManager;

    public ChatterLineService(AiEngineManager aiManager)
    {
        _aiManager = aiManager ?? throw new ArgumentNullException(nameof(aiManager));
    }

    public async Task<ChatterLine> GenerateLineAsync(
        ChatterPersona persona, CustomPersona? custom, ChatterContext ctx, CancellationToken ct = default)
    {
        var usedFallback = false;
        string? line = null;
        var engine = _aiManager.CurrentEngine;

        if (engine != null && engine.Info.Type != AiEngineType.BuiltInGuide)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(LlmTimeout);
                var res = await engine.GenerateResponseAsync(BuildPersonaPrompt(persona, custom, ctx), "", cts.Token);
                if (res.Success) line = SanitizeLine(res.Reply);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // LLM 시간 초과 → 내장 풀로 폴백
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // LLM 오류 → 내장 풀로 폴백
            }
            if (string.IsNullOrWhiteSpace(line)) usedFallback = true;
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            line = SanitizeLine(PersonaTemplates.GetRandomTemplate(persona, ctx));
        }

        // 원문은 이모지 접미사(공백 + 이모지 1자) 자리를 남겨 자른다: 보낼 때 붙어도 50자를 넘지 않게
        var raw = ChatText.Truncate(ChatText.Sanitize(line), ChatText.MaxLength - 2);
        return new ChatterLine(ChatPlanService.BuildChatPlan(line).FinalMessage, usedFallback, raw);
    }

    internal static string BuildPersonaPrompt(ChatterPersona persona, CustomPersona? custom, ChatterContext ctx)
    {
        var personaDesc = persona == ChatterPersona.Custom && custom != null
            ? $"{custom.Name}: {custom.SystemPrompt}"
            : persona switch
            {
                ChatterPersona.Villainess => "도도하고 오만한 츤데레 귀족 영애. 어미로 '~사와요', '~하나요?', '오호호!'를 쓰며 불평과 허당미를 보임.",
                ChatterPersona.Scrooge => "1골드도 아까워하는 구두쇠 할아버지. 수리비와 물가에 혀를 차며 어미로 '~구먼', '~여', '~제', '에헴'을 씀.",
                ChatterPersona.MorningSpirit => "마비노기 모바일 전문 게임 유튜버 '모닝이'. 영상 인삿말 '안녕하닝 모닝이야!'로 시작하거나 어미로 '~닝', '~하닝?', '~이닝', '형들'을 쓰며 무소과금 공략과 팁을 공유하는 밝고 친근한 어조.",
                ChatterPersona.GyeongsangAhjussi => "억세고 투박하지만 정감 넘치는 부산/경상도 사투리를 쓰는 아재. 어미로 '~했나?', '~데이', '~뿌라', '~아이가', '마!'를 씀.",
                ChatterPersona.IdolDancer => "K-POP 무대를 사랑하는 열정 넘치는 아이돌 댄서. 비트, 리듬, 칼군무, 킬링 파트, 엔딩 요정 등 댄서 용어를 쓰며 텐션이 높음.",
                _ => "밀레시안 방랑자"
            };

        var erinnStr = string.IsNullOrWhiteSpace(ctx.ErinnTime) ? "" : $", 시간:{ctx.ErinnTime}";

        return $"너는 마비노기 모바일 게임 속 캐릭터 페르소나 '{personaDesc}'이다.\n" +
               $"[현재 상황] 직업:{ctx.Job}, 위치:{ctx.Location}, 행동:{ctx.Activity}, 가방:{ctx.WeightSummary}, 골드:{ctx.GoldSummary}{erinnStr}\n" +
               "위 상황에 맞춰 혼잣말 넋두리를 따옴표 없이 1문장(20자~35자 내외, 최대 40자 이하)으로만 한국어로 출력해라. 줄바꿈 절대 금지.";
    }

    internal static string SanitizeLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var clean = Regex.Replace(text, @"[\r\n\t]+", " ");
        clean = clean.Trim('"', '\'', '“', '”', '`', ' ');
        return ChatText.Truncate(clean, MaxLineLength);
    }

    /// <summary>에린 시간 포맷. 형식은 DisplayFormat.ErinnTime(연도 포함, v1.1.0)을 따른다.</summary>
    public static string FormatErinnTime(string? erinnNow) => DisplayFormat.ErinnTime(erinnNow);

    public static string GetPersonaDisplayName(ChatterPersona persona, CustomPersona? custom = null) => persona switch
    {
        ChatterPersona.Custom when custom != null => custom.DisplayName,
        ChatterPersona.Villainess => "🌹 악덕영애",
        ChatterPersona.Scrooge => "💰 구두쇠 영감",
        ChatterPersona.MorningSpirit => "☀️ 안녕하닝 모닝이야",
        ChatterPersona.GyeongsangAhjussi => "🌊 갱상도 아재",
        ChatterPersona.IdolDancer => "✨ 아이돌 댄서",
        ChatterPersona.Custom => "🎭 커스텀 페르소나",
        _ => "혼잣말"
    };
}
