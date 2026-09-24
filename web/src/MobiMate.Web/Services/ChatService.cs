using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

public sealed record ChatLogEntry(DateTimeOffset At, string Message, string? Behaviour, bool Ok, string? Error, string Source, string DeviceId);

public enum ChatSendStatus { Sent, Failed, Empty, Duplicate }

/// <summary>
/// 게임 채팅 (FR-GC): 이모지·소셜 액션 계획 → write_chat → (성공 시 150ms 뒤) 행동 전송.
/// 전송 로그는 서버 메모리에 최근 200건을 두고 모든 기기에 SSE "chat.logged"로 알린다.
/// 채팅 본문은 파일 로그에 쓰지 않는다 (NFR-15).
/// </summary>
public sealed class ChatService(IGameCli cli, SseHub hub)
{
    public const int LogCapacity = 200;
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan BehaviourDelay = TimeSpan.FromMilliseconds(150);

    private readonly LinkedList<ChatLogEntry> _log = new();
    private readonly Dictionary<string, (string Text, DateTimeOffset At)> _lastByDevice = new();
    private readonly object _lock = new();

    public IReadOnlyList<ChatLogEntry> Log()
    {
        lock (_lock) return _log.ToList();
    }

    public static object Preview(string? text, bool autoEmote)
    {
        var clean = ChatText.Sanitize(text);
        if (clean.Length == 0) return new { final = "", emoji = (string?)null, behaviour = (string?)null, count = 0, max = ChatText.MaxLength };
        if (!autoEmote)
        {
            var plain = ChatText.Truncate(clean, ChatText.MaxLength);
            return new { final = plain, emoji = (string?)null, behaviour = (string?)null, count = ChatText.Count(plain), max = ChatText.MaxLength };
        }
        var plan = ChatPlanService.BuildChatPlan(clean);
        return new { final = plan.FinalMessage, emoji = (string?)plan.Emoji, behaviour = plan.BehaviourCommand, count = ChatText.Count(plan.FinalMessage), max = ChatText.MaxLength };
    }

    public async Task<(ChatSendStatus Status, ChatLogEntry? Entry)> SendAsync(string? text, bool autoEmote, string source, string deviceId, CancellationToken ct)
    {
        var clean = ChatText.Sanitize(text);
        if (clean.Length == 0) return (ChatSendStatus.Empty, null);

        var now = DateTimeOffset.UtcNow;
        lock (_lock)
        {
            if (_lastByDevice.TryGetValue(deviceId, out var last) && last.Text == clean && now - last.At < DuplicateWindow)
                return (ChatSendStatus.Duplicate, null);
            _lastByDevice[deviceId] = (clean, now);
        }

        string final;
        string? behaviour = null;
        if (autoEmote)
        {
            var plan = ChatPlanService.BuildChatPlan(clean);
            final = plan.FinalMessage;
            behaviour = plan.BehaviourCommand;
        }
        else final = ChatText.Truncate(clean, ChatText.MaxLength);

        var (ok, error) = await cli.SendGameChatAsync(final, ct);
        if (ok && behaviour != null)
        {
            try
            {
                await Task.Delay(BehaviourDelay, ct);
                await cli.SendGameChatAsync(behaviour, ct);
            }
            catch (OperationCanceledException) { }
        }

        var entry = new ChatLogEntry(DateTimeOffset.UtcNow, final, ok ? behaviour : null, ok, ok ? null : error, source, deviceId);
        lock (_lock)
        {
            _log.AddLast(entry);
            while (_log.Count > LogCapacity) _log.RemoveFirst();
        }
        hub.Broadcast("chat.logged", entry);
        return (ok ? ChatSendStatus.Sent : ChatSendStatus.Failed, entry);
    }
}
