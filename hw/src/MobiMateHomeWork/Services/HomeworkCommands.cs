namespace MobiMateHomeWork.Services;

public enum CommandError
{
    None,
    /// <summary>요청 값이 맞지 않다.</summary>
    Validation,
    /// <summary>기록에 없는 캐릭터.</summary>
    UnknownCharacter,
    /// <summary>접속 중인 캐릭터를 아직 모른다(대상 키를 안 줬는데 게임 연결이 없다).</summary>
    NoCharacter,
    /// <summary>카탈로그에 없는 숙제.</summary>
    UnknownHomework,
    /// <summary>공유 저장 파일을 읽거나 쓰지 못했다(잠김 등). 잠시 뒤 다시 시도.</summary>
    StorageUnavailable,
}

public sealed record CommandResult(CommandError Error, string? Message = null)
{
    public bool Ok => Error == CommandError.None;
    public static readonly CommandResult Success = new(CommandError.None);
}

public sealed record HomeworkCommandResult(CommandError Error, string? Message, HomeworkChange? Change)
{
    public bool Ok => Error == CommandError.None;
    public static HomeworkCommandResult Fail(CommandError e, string message) => new(e, message, null);
}

/// <summary>화면이 부르는 쓰기 동작. 화면 모델 테스트에서 가짜로 바꿔 끼울 수 있게 인터페이스로 둔다.</summary>
public interface IHomeworkActions
{
    HomeworkCommandResult SetHomework(string id, bool? completed, int? count, string? characterKey = null);
    CommandResult RenameCharacter(string characterKey, string? nickname);
}

/// <summary>
/// 화면이 부르는 쓰기 동작 (웹앱 PUT /homework/{id}, PUT /profile/nickname 처리).
/// </summary>
public sealed class HomeworkCommands(HomeworkWatcher homework, CharacterSession session, GameStateCache state, SnapshotManager snapshots, AppEvents events) : IHomeworkActions
{
    public const int MaxNicknameLength = 20;

    /// <summary>
    /// 숙제 수동 설정. 목표 상태를 지정한다(뒤집기가 아니라서 두 앱에서 동시에 눌러도 결과가 같다).
    /// <paramref name="characterKey"/>가 없으면 지금 접속 중인 캐릭터, 있으면 기록에 있는 캐릭터여야 한다.
    /// 접속 중 캐릭터를 키로 지정해도 같은 키로 처리된다(현재 캐릭터의 키는 헤더가 정한 이름을 쓴다).
    /// </summary>
    public HomeworkCommandResult SetHomework(string id, bool? completed, int? count, string? characterKey = null)
    {
        if (completed == null && count == null) return HomeworkCommandResult.Fail(CommandError.Validation, "completed 또는 count가 필요합니다.");
        string key;
        if (!string.IsNullOrWhiteSpace(characterKey))
        {
            if (!session.IsKnownCharacter(characterKey) && !session.IsCurrentCharacter(characterKey)) return HomeworkCommandResult.Fail(CommandError.UnknownCharacter, "알 수 없는 캐릭터입니다.");
            key = characterKey;
        }
        else
        {
            if (state.Character == null) return HomeworkCommandResult.Fail(CommandError.NoCharacter, "아직 캐릭터 정보를 받지 못했습니다. 게임 연결을 확인하세요.");
            key = state.CurrentKey ?? CharacterSession.CharacterKey(state.Character);
        }
        try
        {
            var change = homework.Service.Set(key, id, completed, count);
            homework.Notify(change);
            return new HomeworkCommandResult(CommandError.None, null, change);
        }
        catch (KeyNotFoundException) { return HomeworkCommandResult.Fail(CommandError.UnknownHomework, $"알 수 없는 숙제: {id}"); }
        catch (ArgumentException ex) { return HomeworkCommandResult.Fail(CommandError.Validation, ex.Message); }
        catch (IOException ex) { return HomeworkCommandResult.Fail(CommandError.StorageUnavailable, ex.Message); }
    }

    /// <summary>
    /// 캐릭터 이름(닉네임) 바꾸기. 웹앱 원본은 지금 접속 중인 캐릭터만 바꿨지만 여기서는 기록에 있는 아무 캐릭터나 바꾼다.
    /// 빈 문자열이면 닉네임을 지운다.
    /// </summary>
    public CommandResult RenameCharacter(string characterKey, string? nickname)
    {
        if (string.IsNullOrWhiteSpace(characterKey)) return new CommandResult(CommandError.Validation, "캐릭터 키가 필요합니다.");
        var nick = (nickname ?? "").Trim();
        if (nick.Length > MaxNicknameLength) return new CommandResult(CommandError.Validation, $"별칭은 {MaxNicknameLength}자 이하입니다.");

        var profile = snapshots.GetProfileByKey(characterKey);
        string realm, job;
        if (profile != null) (realm, job) = (profile.RealmName, profile.JobName);
        else if (session.IsCurrentCharacter(characterKey) && state.Character is { } c) (realm, job) = (c.RealmName ?? "에린", c.JobName ?? "밀레시안");
        else return new CommandResult(CommandError.UnknownCharacter, "알 수 없는 캐릭터입니다.");

        try { snapshots.SetCustomName(characterKey, realm, job, nick); }
        catch (IOException ex) { return new CommandResult(CommandError.StorageUnavailable, ex.Message); }
        events.RaiseCharactersChanged();
        return CommandResult.Success;
    }
}
