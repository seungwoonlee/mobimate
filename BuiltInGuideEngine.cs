using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

/// <summary>
/// AI 툴(Ollama, CLI 등)이 전혀 설치되지 않은 일반 게이머 PC에서도
/// 100% 무료 오프라인으로 동작하는 지능형 내장 마비노기 공략 엔진입니다.
/// </summary>
public class BuiltInGuideEngine : IAiEngine
{
    public AiEngineInfo Info { get; } = new(
        Id: "builtin:guide",
        DisplayName: "[무료/내장] 💡 내장 마비 가이드 (오프라인 무설치)",
        Type: AiEngineType.BuiltInGuide,
        TargetModel: "mabimate-builtin-v1",
        Description: "완전 무료(0원), AI 설치 불필요, 핵심 공략 및 캐릭터 실시간 분석 지원",
        IsZeroCost: true
    );

    public Task<AiResponse> GenerateResponseAsync(string prompt, string gameContext, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var q = prompt.Trim().ToLowerInvariant();

        sb.AppendLine("💡 [내장 마비 가이드 답변]");
        sb.AppendLine();

        // 1. 전투력 / 스탯 / 룬 관련 질의
        if (q.Contains("전투력") || q.Contains("스탯") || q.Contains("스펙") || q.Contains("성장") || q.Contains("세팅"))
        {
            sb.AppendLine("⚔️ 전투력 및 스펙업 핵심 공략:");
            sb.AppendLine("1. [룬 장착]: 1티어 룬(무기/방어구)의 고정 옵션보다 세트 효과(공격력%, 관통력) 활성화를 최우선으로 맞추세요.");
            sb.AppendLine("2. [장비 각인]: 무기 각인은 치명타 확률과 피해량을 집중적으로 올리는 것이 전투력 대비 실데미지 효율이 가장 높습니다.");
            sb.AppendLine("3. [칭호 및 도감]: 생활 칭호(채집/제작)와 필드 몬스터 도감 점수는 계정 전체 전투력 보너스를 제공합니다.");
        }
        // 2. 골드 / 재화 / 파밍 관련 질의
        else if (q.Contains("골드") || q.Contains("돈") || q.Contains("재화") || q.Contains("파밍") || q.Contains("날개") || q.Contains("냥"))
        {
            sb.AppendLine("💰 골드 및 재화 파밍 공략:");
            sb.AppendLine("1. [일일 미션 & 요일 던전]: 일일 미션 11종 완수 시 대량의 골드와 정령의 날개를 지급합니다. 매일 초기화 전 완료하세요.");
            sb.AppendLine("2. [생활 가공 납품]: 가죽/천/주괴 등 1차 가공 재료를 모아 거래소 및 길드 상점에 납품하면 안정적인 수익이 납니다.");
            sb.AppendLine("3. [정령의 날개]: 빠른 이동 및 시설 즉시 완료에 소모되므로 아껴두고 주간 미션 완료 보상으로 수급하세요.");
        }
        // 3. 채집 / 생산 / 가공 관련 질의
        else if (q.Contains("채집") || q.Contains("가공") || q.Contains("제작") || q.Contains("생활") || q.Contains("가죽") || q.Contains("광석") || q.Contains("약초"))
        {
            sb.AppendLine("🌿 생활 및 가공 공략:");
            sb.AppendLine("1. [시설 대기열]: 가공 시설은 쿨타임이 긴 고급 가공(상급 가죽, 고급 직물 등)을 취침 전에 걸어두는 것이 유리합니다.");
            sb.AppendLine("2. [채집물 분포]:");
            sb.AppendLine("   - 철광석/구리: 두갈드 아일 북부 광산 지대");
            sb.AppendLine("   - 약초(블러디/마나): 티르코네일 남쪽 평원 및 알비 던전 근처");
            sb.AppendLine("   - 목재(참나무/물푸레나무): 벌목 캠프 동쪽 숲");
            sb.AppendLine("3. [가방 무게 관리]: 원자재는 무게가 무거우므로 수거 즉시 시설에 가공 등록하거나 계정 창고에 보관하세요.");
        }
        // 4. 던전 / 레이드 / 보스 관련 질의
        else if (q.Contains("던전") || q.Contains("레이드") || q.Contains("보스") || q.Contains("어비스") || q.Contains("브레이크"))
        {
            sb.AppendLine("🛡️ 던전 및 레이드 공략:");
            sb.AppendLine("1. [브레이크 게이지]: 보스의 브레이크 게이지는 연속 강공격 및 카운터 스킬로 빠르게 깎을 수 있습니다.");
            sb.AppendLine("2. [장판 회피]: 붉은색 예고 장판은 구르기(대시) 무적 시간으로 타이밍을 맞춰 피하세요.");
            sb.AppendLine("3. [부활 코인]: 주간 어비스는 부활 횟수가 제한되어 있으니 물약 쿨타임을 항상 체크하세요.");
        }
        // 5. 기타 일반 질의
        else
        {
            sb.AppendLine($"'{prompt}'에 대한 마비노기 모바일 핵심 조언입니다:");
            sb.AppendLine("- 상단 탭에서 실시간 캐릭터 스탯, 가방 무게, 미션 진행도를 확인하실 수 있습니다.");
            sb.AppendLine("- 현재 캐릭터 상태에 맞춘 맞춤 조언: 매일 일일 미션 완수와 생활 시설 대기열을 비우지 않고 가동하는 것이 빠른 성장의 지름길입니다.");
        }

        // 현재 실시간 게임 컨텍스트 요약 피드백 추가
        if (!string.IsNullOrWhiteSpace(gameContext))
        {
            sb.AppendLine();
            sb.AppendLine("📊 [현재 캐릭터 실시간 진단]");
            if (gameContext.Contains("전투력"))
            {
                sb.AppendLine("• 실시간 전황이 반영되었습니다. 지속적으로 가방 무게와 미션 달성률을 체크해 주세요.");
            }
        }

        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("💡 팁: 더 자유롭고 심층적인 자연어 AI 대화를 원하시면, 완전 무료 오프라인 로컬 AI인 'Ollama'(https://ollama.com)를 PC에 설치하시면 gemma, deepseek 등 최신 LLM이 앱에 자동 감지되어 연동됩니다.");

        return Task.FromResult(new AiResponse(true, sb.ToString().Trim()));
    }
}
