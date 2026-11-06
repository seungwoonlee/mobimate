using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

public sealed record ChatLogEntry(DateTimeOffset At, string Message, string? Behaviour, bool Ok, string? Error, string Source, string DeviceId);

public enum ChatSendStatus { Sent, Failed, Empty, Duplicate }

/// <summary>
/// 게임 채팅 (FR-GC): 이모지·소셜 액션 계획 → write_chat → (성공 시 150ms 뒤) 행동 전송.
/// 전송 로그는 서버 메모리에 최근 200건을 두고 모든 기기에 SSE "chat.logged"로 알린다.
/// 채팅 본문은 파일 로그에 쓰지 않는다 (NFR-15).
/// </summary>
public sealed class ChatService(IGameCli cli, SseHub hub, IChatStylist stylist, MobiMate.Web.Hosting.WebSettingsStore settings, SnapshotManager snapshots)
{
    public const int LogCapacity = 200;
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan BehaviourDelay = TimeSpan.FromMilliseconds(150);

    private readonly LinkedList<ChatLogEntry> _log = new();
    private readonly Dictionary<string, (string Text, DateTimeOffset At)> _lastByDevice = new();
    private readonly object _lock = new();

    /// <summary>현재 아무말 페르소나("Villainess" 또는 "custom:&lt;id&gt;")의 말투 설명</summary>
    private string ResolvePersonaDesc()
    {
        var value = settings.Current.ChatterPersona ?? "Villainess";
        CustomPersona? custom = null;
        ChatterPersona persona;
        if (value.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            persona = ChatterPersona.Custom;
            var id = value["custom:".Length..];
            custom = snapshots.LoadCustomPersonas().FirstOrDefault(p => p.Id == id);
        }
        else if (!Enum.TryParse(value, true, out persona)) persona = ChatterPersona.Villainess;
        return ChatterLineService.DescribePersona(persona, custom);
    }

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

    /// <param name="sendBehaviour">
    /// 소셜 액션(`/손인사1` 같은 행동 명령)을 두 번째 write_chat으로 보낼지. 게임은 write_chat마다 확인창을 띄우므로 행동 명령까지 보내면
    /// 한 문장에 확인창이 두 번 뜬다. 음성으로 말한 문장은 확인창이 한 번만 뜨도록 보내지 않는다(이모지는 문장에 그대로 붙는다).
    /// </param>
    public async Task<(ChatSendStatus Status, ChatLogEntry? Entry)> SendAsync(string? text, bool autoEmote, string source, string deviceId, CancellationToken ct, bool sendBehaviour = true)
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

        // 맥락 이모티콘·페르소나 말투 (AI). 실패하면 원문과 키워드 규칙을 그대로 쓴다.
        var cfg = settings.Current;
        var smart = autoEmote && cfg.ChatSmartEmote;
        var tone = cfg.ChatPersonaTone;
        var style = ChatStyleResult.Nothing;
        if (smart || tone)
        {
            try { style = await stylist.StyleAsync(clean, new ChatStyleRequest(tone, smart, tone ? ResolvePersonaDesc() : ""), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* 꾸미기 실패는 원문으로 */ }
        }
        var body = style.Text ?? clean;

        string final;
        string? behaviour = null;
        if (autoEmote)
        {
            var rule = ChatPlanService.BuildChatPlan(clean);
            ChatPlan plan;
            if (smart && style.EmojiDecided) plan = ChatPlanService.BuildChatPlanWithEmoji(body, style.EmojiNone ? null : style.Emoji);
            else plan = ChatPlanService.BuildChatPlanWithEmoji(body, rule.Emoji) with { BehaviourCommand = rule.BehaviourCommand };
            final = plan.FinalMessage;
            behaviour = plan.BehaviourCommand;
        }
        else final = ChatText.Truncate(body, ChatText.MaxLength);

        var (ok, error) = await cli.SendGameChatAsync(final, ct);
        if (ok && sendBehaviour && behaviour != null)
        {
            try
            {
                await Task.Delay(BehaviourDelay, ct);
                await cli.SendGameChatAsync(behaviour, ct);
            }
            catch (OperationCanceledException) { }
        }

        var entry = new ChatLogEntry(DateTimeOffset.UtcNow, final, ok && sendBehaviour ? behaviour : null, ok, ok ? null : error, source, deviceId);
        lock (_lock)
        {
            _log.AddLast(entry);
            while (_log.Count > LogCapacity) _log.RemoveFirst();
        }
        hub.Broadcast("chat.logged", entry);
        return (ok ? ChatSendStatus.Sent : ChatSendStatus.Failed, entry);
    }
}
