using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

/// <summary>
/// 로컬 LM Studio 서버(OpenAI 호환 API, 기본 http://127.0.0.1:1234/v1)와 통신한다.
/// LM Studio에서 "Local Server"를 켜 두어야 한다. 꺼져 있으면 모델 목록이 비어 엔진이 보이지 않는다.
/// </summary>
public class LmStudioService
{
    private static readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:1234/"), Timeout = TimeSpan.FromSeconds(10) };
    private static readonly HttpClient _streamHttp = new() { BaseAddress = new Uri("http://127.0.0.1:1234/"), Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    /// <summary>모델이 아직 메모리에 없으면 불러오는 데 시간이 걸리므로 첫 응답은 넉넉히 기다린다</summary>
    public static readonly TimeSpan FirstTokenTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>채팅에 쓸 수 있는 모델 이름들(임베딩 모델 제외). 서버가 꺼져 있으면 빈 목록.</summary>
    public virtual async Task<List<string>> GetModelsAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            var json = await _http.GetStringAsync("v1/models", cts.Token);
            return ParseModels(json);
        }
        catch { return new List<string>(); }
    }

    internal static List<string> ParseModels(string json)
    {
        var list = new List<string>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return list;
        foreach (var m in data.EnumerateArray())
        {
            var id = m.TryGetProperty("id", out var i) ? i.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (id.Contains("embed", StringComparison.OrdinalIgnoreCase)) continue;   // 임베딩 모델은 대화에 못 쓴다
            list.Add(id);
        }
        return list;
    }

    private static string Body(string model, string prompt, string context, bool stream) => JsonSerializer.Serialize(new
    {
        model,
        messages = new object[] { new { role = "system", content = context }, new { role = "user", content = prompt } },
        stream,
    });

    public async Task<(bool success, string reply, string error)> AskAsync(string model, string prompt, string context, CancellationToken ct = default)
    {
        try
        {
            using var content = new StringContent(Body(model, prompt, context, false), Encoding.UTF8, "application/json");
            using var timeout = new CancellationTokenSource(FirstTokenTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            using var res = await _streamHttp.PostAsync("v1/chat/completions", content, linked.Token);
            var json = await res.Content.ReadAsStringAsync(linked.Token);
            if (!res.IsSuccessStatusCode) return (false, "", $"LM Studio 응답 실패 (HTTP {(int)res.StatusCode}): {json}");
            using var doc = JsonDocument.Parse(json);
            var reply = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
            return string.IsNullOrEmpty(reply) ? (false, "", "LM Studio로부터 빈 응답이 반환되었습니다.") : (true, reply, "");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (false, "", $"LM Studio 응답 대기 시간이 초과되었습니다 ({(int)FirstTokenTimeout.TotalSeconds}초)."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return (false, "", $"LM Studio 통신 오류: {ex.Message}"); }
    }

    /// <summary>SSE(data: {...}) 스트림에서 delta.content를 낸다. 취소하면 요청을 끊는다.</summary>
    public async IAsyncEnumerable<string> StreamAsync(string model, string prompt, string context, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(Body(model, prompt, context, true), Encoding.UTF8, "application/json")
        };
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerCts.CancelAfter(FirstTokenTimeout);
        HttpResponseMessage res;
        try { res = await _streamHttp.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, headerCts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AiEngineException($"LM Studio 응답 대기 시간이 초과되었습니다 ({(int)FirstTokenTimeout.TotalSeconds}초)."); }
        catch (HttpRequestException ex) { throw new AiEngineException($"LM Studio에 연결하지 못했습니다(Local Server가 켜져 있나요?): {ex.Message}"); }

        using (res)
        {
            if (!res.IsSuccessStatusCode) throw new AiEngineException($"LM Studio 응답 실패 (HTTP {(int)res.StatusCode})");
            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var tokens = 0;
            while (true)
            {
                string? line;
                // 첫 토큰은 모델을 불러오는 시간이 있어 길게, 이후에는 30초 유휴 제한
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    idle.CancelAfter(tokens == 0 ? FirstTokenTimeout : StreamIdleTimeout);
                    try { line = await reader.ReadLineAsync(idle.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AiEngineException("LM Studio 응답 대기 시간이 초과되었습니다."); }
                    catch (IOException ex) { throw new AiEngineException($"LM Studio 연결이 끊겼습니다: {ex.Message}"); }
                }
                if (line == null) break;
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var payload = line[5..].Trim();
                if (payload == "[DONE]") break;
                var token = ParseDelta(payload);
                if (!string.IsNullOrEmpty(token)) { tokens++; yield return token; }
            }
            if (tokens == 0) throw new AiEngineException("LM Studio로부터 빈 응답이 반환되었습니다.");
        }
    }

    internal static string? ParseDelta(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("error", out var err)) throw new AiEngineException("LM Studio 오류: " + err.ToString());
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;
            return choices[0].TryGetProperty("delta", out var d) && d.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }
}

/// <summary>LM Studio에 올려 둔 모델 하나를 쓰는 엔진</summary>
public class LmStudioAiEngine : IAiEngine
{
    private readonly LmStudioService _service;
    public AiEngineInfo Info { get; }

    public LmStudioAiEngine(string model, LmStudioService service)
    {
        _service = service;
        Info = new AiEngineInfo(
            Id: $"lmstudio:{model}",
            DisplayName: $"[무료/로컬] 🧪 LM Studio: {model}",
            Type: AiEngineType.LmStudio,
            TargetModel: model,
            Description: "완전 무료(0원), 로컬 PC의 LM Studio 서버(Local Server를 켜 두어야 합니다)",
            IsZeroCost: true);
    }

    public async Task<AiResponse> GenerateResponseAsync(string prompt, string gameContext, CancellationToken ct = default)
    {
        var (ok, reply, err) = await _service.AskAsync(Info.TargetModel, prompt, gameContext, ct);
        return new AiResponse(ok, reply, err);
    }

    public IAsyncEnumerable<string> StreamAsync(string prompt, string gameContext, CancellationToken ct = default)
        => _service.StreamAsync(Info.TargetModel, prompt, gameContext, ct);
}
