using System.Text.Json;
using System.Text.Json.Serialization;

namespace MobiMate.Web.Infrastructure;

/// <summary>응답 형식 (요구사양서 §7): 성공 { data, fetchedAt }, 실패 { error: { code, message } }.</summary>
public static class ApiResults
{
    public static readonly JsonSerializerOptions Json = CreateJson();

    public static JsonSerializerOptions CreateJson()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return o;
    }

    public static IResult Ok<T>(T data, DateTimeOffset? fetchedAt = null) =>
        Results.Json(new { data, fetchedAt = fetchedAt ?? DateTimeOffset.UtcNow }, Json);

    public static IResult Accepted<T>(T data) => Results.Json(new { data, fetchedAt = DateTimeOffset.UtcNow }, Json, statusCode: StatusCodes.Status202Accepted);

    public static IResult Error(int status, string code, string message) =>
        Results.Json(new { error = new { code, message } }, Json, statusCode: status);

    /// <summary>CLI 실패를 오류 코드로 옮긴다 (상세설계 §3.8).</summary>
    public static IResult CliError(CliFailure f) => f.Kind switch
    {
        CliFailureKind.Missing => Error(StatusCodes.Status503ServiceUnavailable, "CLI_MISSING", f.Message),
        CliFailureKind.Timeout => Error(StatusCodes.Status504GatewayTimeout, "CLI_TIMEOUT", f.Message),
        CliFailureKind.Parse => Error(StatusCodes.Status502BadGateway, "PARSE_ERROR", f.Message),
        _ => Error(StatusCodes.Status502BadGateway, "CLI_FAILED", f.Message),
    };

    /// <summary>미들웨어에서 직접 응답할 때 쓴다.</summary>
    public static Task Write(HttpContext ctx, int status, string code, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = new { code, message } }, Json));
    }
}

public enum CliFailureKind { Missing, Timeout, Parse, Failed }

public sealed record CliFailure(CliFailureKind Kind, string Message);

/// <summary>CLI 조회 결과: 값 또는 실패.</summary>
public sealed record CliData<T>(T? Value, CliFailure? Failure, DateTimeOffset FetchedAt)
{
    public bool Ok => Failure == null;
}
