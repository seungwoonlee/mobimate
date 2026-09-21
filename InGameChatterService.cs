using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MobiMate;

/// <summary>
/// AI 및 상황 인식 기반 인게임 "아무말 대잔치" (페르소나 혼잣말) 백그라운드 오케스트레이터
/// </summary>
public class InGameChatterService
{
    private readonly GameCliService _cli;
    private readonly AiEngineManager _aiManager;
    private readonly Func<ChatterContext> _contextProvider;
    private readonly DispatcherTimer _timer;

    private bool _isBusy;
    private bool _isEnabled;
    private ChatterPersona _currentPersona = ChatterPersona.Villainess;
    private int _intervalSeconds = 10;
    private bool _sendToGameDirectly = true;

    /// <summary>
    /// 아무말 대사가 발송되었을 때 UI 피드 및 로그에 알리는 이벤트 (페르소나이름, 대사)
    /// </summary>
    public event Action<string, string>? OnChatterEmitted;

    /// <summary>
    /// 토스트 알림이 필요할 때 발송하는 이벤트 (메시지, 성공여부)
    /// </summary>
    public event Action<string, bool>? OnToastRequested;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            if (_isEnabled)
            {
                _timer.Interval = TimeSpan.FromSeconds(Math.Max(3, _intervalSeconds));
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    public ChatterPersona CurrentPersona
    {
        get => _currentPersona;
        set => _currentPersona = value;
    }

    public int IntervalSeconds
    {
        get => _intervalSeconds;
        set
        {
            _intervalSeconds = Math.Max(3, value);
            _timer.Interval = TimeSpan.FromSeconds(_intervalSeconds);
        }
    }

    public bool SendToGameDirectly
    {
        get => _sendToGameDirectly;
        set => _sendToGameDirectly = value;
    }

    public InGameChatterService(GameCliService cli, AiEngineManager aiManager, Func<ChatterContext> contextProvider)
    {
        _cli = cli ?? throw new ArgumentNullException(nameof(cli));
        _aiManager = aiManager ?? throw new ArgumentNullException(nameof(aiManager));
        _contextProvider = contextProvider ?? throw new ArgumentNullException(nameof(contextProvider));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_intervalSeconds) };
        _timer.Tick += async (s, e) => await TriggerChatterAsync(isManual: false);
    }

    /// <summary>
    /// 수동 즉시 발송 또는 타이머 틱에 의한 대사 생성 및 발송
    /// </summary>
    public async Task<bool> TriggerChatterAsync(bool isManual = false)
    {
        if (_isBusy) return false;
        _isBusy = true;

        try
        {
            var ctx = _contextProvider();
            var line = await GeneratePersonaLineAsync(_currentPersona, ctx);

            // 1. 개행/공백/따옴표 정제 (Sanitization)
            line = SanitizeLine(line);
            if (string.IsNullOrWhiteSpace(line))
            {
                line = PersonaTemplates.GetRandomTemplate(_currentPersona, ctx);
            }

            // 2. ChatPlanService 연동 (이모티콘, 소셜 액션 및 최대 50자 제한 준수)
            var plan = ChatPlanService.BuildChatPlan(line);
            var finalMsg = plan.FinalMessage;
            var personaTag = GetPersonaDisplayName(_currentPersona);

            // 3. 인게임 실제 전송 (옵션 체크 시)
            if (_sendToGameDirectly)
            {
                var body = $"{{\"message\":\"{EscapeJson(finalMsg)}\"}}";
                var (ok, _, err) = await _cli.RunRawAsync("send_chat", stdinJson: body, timeoutSeconds: 3);
                if (!ok && isManual)
                {
                    OnToastRequested?.Invoke($"인게임 채팅 전송 실패: {err}", false);
                }
            }

            // 4. 앱 내 피드 알림
            OnChatterEmitted?.Invoke(personaTag, finalMsg);

            return true;
        }
        catch (Exception ex)
        {
            if (isManual)
            {
                OnToastRequested?.Invoke($"아무말 생성 오류: {ex.Message}", false);
            }
            return false;
        }
        finally
        {
            _isBusy = false;
        }
    }

    /// <summary>
    /// LLM 실시간 질의 또는 내장 템플릿 풀 폴백
    /// </summary>
    private async Task<string> GeneratePersonaLineAsync(ChatterPersona persona, ChatterContext ctx)
    {
        var engine = _aiManager.CurrentEngine;

        // LLM 엔진(Ollama 또는 CLI)이 활성화되어 있는 경우 실시간 프롬프트 생성 시도
        if (engine != null && engine.Info.Type != AiEngineType.BuiltInGuide)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2.5));
                var prompt = BuildPersonaPrompt(persona, ctx);
                var res = await engine.GenerateResponseAsync(prompt, "", cts.Token);
                if (res.Success && !string.IsNullOrWhiteSpace(res.Reply))
                {
                    return res.Reply.Trim();
                }
            }
            catch
            {
                // 타임아웃 또는 LLM 에러 시 내장 템플릿으로 안전하게 폴백
            }
        }

        // 무료 내장 가이드 모드이거나 LLM 실패 시 내장 템플릿 풀 사용
        return PersonaTemplates.GetRandomTemplate(persona, ctx);
    }

    private static string BuildPersonaPrompt(ChatterPersona persona, ChatterContext ctx)
    {
        var personaDesc = persona switch
        {
            ChatterPersona.Villainess => "도도하고 오만한 츤데레 귀족 영애. 어미로 '~사와요', '~하나요?', '오호호!'를 쓰며 불평과 허당미를 보임.",
            ChatterPersona.Scrooge => "1골드도 아까워하는 구두쇠 할아버지. 수리비와 물가에 혀를 차며 어미로 '~구먼', '~여', '~제', '에헴'을 씀.",
            ChatterPersona.MorningSpirit => "초발랄 아침 요정 '모닝이야'. 모든 문장 끝에 '~닝', '~모닝!'을 붙이며 에너지가 넘침.",
            ChatterPersona.GyeongsangAhjussi => "억세고 투박하지만 정감 넘치는 부산/경상도 사투리를 쓰는 아재. 어미로 '~했나?', '~데이', '~뿌라', '~아이가', '마!'를 씀.",
            ChatterPersona.IdolDancer => "K-POP 무대를 사랑하는 열정 넘치는 아이돌 댄서. 비트, 리듬, 칼군무, 킬링 파트, 엔딩 요정 등 댄서 용어를 쓰며 텐션이 높음.",
            _ => "밀레시안 방랑자"
        };

        return $"너는 마비노기 모바일 게임 속 캐릭터 페르소나 '{personaDesc}'이다.\n" +
               $"[현재 상황] 직업:{ctx.Job}, 위치:{ctx.Location}, 행동:{ctx.Activity}, 가방:{ctx.WeightSummary}, 골드:{ctx.GoldSummary}\n" +
               $"위 상황에 맞춰 혼잣말 넋두리를 따옴표 없이 1문장(20자~35자 내외, 최대 40자 이하)으로만 한국어로 출력해라. 줄바꿈 절대 금지.";
    }

    private static string SanitizeLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // 줄바꿈, 탭 제거
        var clean = Regex.Replace(text, @"[\r\n\t]+", " ");
        // 앞뒤 따옴표 제거
        clean = clean.Trim('"', '\'', '“', '”', '`', ' ');
        // 42자 초과 시 컷
        if (clean.Length > 42)
        {
            clean = clean[..42].TrimEnd();
        }
        return clean;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");

    public static string GetPersonaDisplayName(ChatterPersona persona) => persona switch
    {
        ChatterPersona.Villainess => "🌹 악덕영애",
        ChatterPersona.Scrooge => "💰 구두쇠 영감",
        ChatterPersona.MorningSpirit => "☀️ 안녕하닝 모닝이야",
        ChatterPersona.GyeongsangAhjussi => "🌊 갱상도 아재",
        ChatterPersona.IdolDancer => "✨ 아이돌 댄서",
        _ => "혼잣말"
    };
}
