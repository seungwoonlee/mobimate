namespace MobiMateHomeWork.Services;

public enum CliFailureKind { Missing, Timeout, Parse, Failed }

public sealed record CliFailure(CliFailureKind Kind, string Message);

/// <summary>CLI 조회 결과: 값 또는 실패.</summary>
public sealed record CliData<T>(T? Value, CliFailure? Failure, DateTimeOffset FetchedAt)
{
    public bool Ok => Failure == null;
}
