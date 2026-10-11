using System;

namespace MobiMate;

/// <summary>
/// 사용자가 직접 정의한 커스텀 AI 페르소나 정보
/// </summary>
public class CustomPersona
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string TagEmoji { get; set; } = "🎭";
    public string SystemPrompt { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayName => $"{TagEmoji} {Name}";
}
