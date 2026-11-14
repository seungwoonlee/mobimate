using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

public enum AiEngineType
{
    Ollama,         // 로컬 Ollama HTTP (완전 무료 로컬 LLM)
    BuiltInGuide,   // 내장 마비 가이드 (100% 무료 무설치 오프라인 룰베이스/공략 DB)
    CliAgent        // agy, claude, codex 등 시스템 설치형 CLI 에이전트
}

public record AiEngineInfo(
    string Id,              // 고유 식별자 (예: "ollama:gemma4:12b", "cli:agy", "builtin:guide")
    string DisplayName,     // 화면 표시명 (예: "[무료/로컬] 🦙 Ollama: gemma4:12b")
    AiEngineType Type,      // 엔진 타입
    string TargetModel,     // 모델명 또는 실행 커맨드
    string Description,     // 설명 및 비용 안내
    bool IsZeroCost         // 100% 무료 여부 (기본값 자동 선택 후보 판단)
);

public record AiResponse(bool Success, string Reply, string? ErrorMessage = null);

public interface IAiEngine
{
    AiEngineInfo Info { get; }
    Task<AiResponse> GenerateResponseAsync(string prompt, string gameContext, CancellationToken ct = default);
}
