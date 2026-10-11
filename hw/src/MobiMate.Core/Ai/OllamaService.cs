using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

public record OllamaTagItem([property: JsonPropertyName("name")] string Name);
public record OllamaTagsResponse([property: JsonPropertyName("models")] List<OllamaTagItem>? Models);

public record OllamaChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);

public record OllamaChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<OllamaChatMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream
);

public record OllamaChatResponse(
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("message")] OllamaChatMessage? Message,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("error")] string? Error = null
);

public class OllamaService
{
    private static readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("http://localhost:11434/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

    // 스트리밍은 응답이 길어질 수 있어 HttpClient 자체 타임아웃을 끄고 CTS로 시간을 제어한다.
    private static readonly HttpClient _streamHttp = new()
    {
        BaseAddress = new Uri("http://localhost:11434/"),
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    public static readonly TimeSpan StreamIdleTimeout = TimeSpan.FromSeconds(30);

    public bool IsOnline { get; private set; }

    public async Task<List<string>> GetInstalledModelsAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var res = await _http.GetAsync("api/tags", cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                IsOnline = false;
                return new List<string>();
            }

            var json = await res.Content.ReadAsStringAsync(cts.Token);
            var parsed = JsonSerializer.Deserialize<OllamaTagsResponse>(json);
            if (parsed?.Models == null || parsed.Models.Count == 0)
            {
                IsOnline = false;
                return new List<string>();
            }

            IsOnline = true;
            var list = new List<string>();
            foreach (var m in parsed.Models)
            {
                if (!string.IsNullOrWhiteSpace(m.Name))
                {
                    list.Add(m.Name);
                }
            }
            return list;
        }
        catch
        {
            IsOnline = false;
            return new List<string>();
        }
    }

    public async Task<(bool success, string reply, string error)> AskAiAsync(
        string modelName,
        string userPrompt,
        string systemContext,
        CancellationToken ct = default)
    {
        try
        {
            var req = new OllamaChatRequest(
                Model: modelName,
                Messages: new List<OllamaChatMessage>
                {
                    new("system", systemContext),
                    new("user", userPrompt)
                },
                Stream: false
            );

            var jsonContent = JsonSerializer.Serialize(req);
            using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var res = await _http.PostAsync("api/chat", content, linkedCts.Token);
            var resJson = await res.Content.ReadAsStringAsync(linkedCts.Token);

            if (!res.IsSuccessStatusCode)
            {
                return (false, "", $"Ollama API 응답 실패 (HTTP {(int)res.StatusCode}): {resJson}");
            }

            var chatRes = JsonSerializer.Deserialize<OllamaChatResponse>(resJson);
            var reply = chatRes?.Message?.Content?.Trim();

            if (string.IsNullOrEmpty(reply))
            {
                return (false, "", "Ollama로부터 빈 응답이 반환되었습니다.");
            }

            return (true, reply, "");
        }
        catch (OperationCanceledException)
        {
            return (false, "", "Ollama 응답 대기 시간이 초과되었습니다 (30초).");
        }
        catch (Exception ex)
        {
            return (false, "", $"Ollama 통신 오류: {ex.Message}");
        }
    }

    /// <summary>
    /// /api/chat stream:true 응답(NDJSON)을 줄 단위로 읽어 토큰을 낸다 (FR-AI-04).
    /// 첫 토큰까지 30초를 넘기면 시간 초과로 끝낸다. 취소하면 HTTP 요청을 끊는다 (FR-AI-08).
    /// </summary>
    public async IAsyncEnumerable<string> AskAiStreamAsync(
        string modelName, string userPrompt, string systemContext,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var req = new OllamaChatRequest(
            Model: modelName,
            Messages: new List<OllamaChatMessage> { new("system", systemContext), new("user", userPrompt) },
            Stream: true);

        using var msg = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json")
        };

        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerCts.CancelAfter(StreamIdleTimeout);

        HttpResponseMessage res;
        try
        {
            res = await _streamHttp.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiEngineException("Ollama 응답 대기 시간이 초과되었습니다 (30초).");
        }
        catch (HttpRequestException ex)
        {
            throw new AiEngineException($"Ollama 통신 오류: {ex.Message}");
        }

        using (res)
        {
            if (!res.IsSuccessStatusCode)
            {
                throw new AiEngineException($"Ollama API 응답 실패 (HTTP {(int)res.StatusCode})");
            }

            Stream stream;
            try
            {
                stream = await res.Content.ReadAsStreamAsync(ct);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                throw new AiEngineException($"Ollama 연결이 끊겼습니다: {ex.Message}");
            }

            await using var _ = stream;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var tokens = 0;
            while (true)
            {
                // 줄마다 유휴 타임아웃(30초)을 새로 건다. 사용자 취소(ct)는 그대로 전파하고, 시간 초과·끊김은 AiEngineException으로 바꾼다.
                string? line;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    idle.CancelAfter(StreamIdleTimeout);
                    try
                    {
                        line = await reader.ReadLineAsync(idle.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new AiEngineException("Ollama 응답 대기 시간이 초과되었습니다 (30초).");
                    }
                    catch (IOException ex)
                    {
                        throw new AiEngineException($"Ollama 연결이 끊겼습니다: {ex.Message}");
                    }
                }

                if (line == null) break;
                if (line.Length == 0) continue;

                OllamaChatResponse? chunk;
                try { chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line); }
                catch (JsonException) { continue; }

                if (!string.IsNullOrEmpty(chunk?.Error))
                {
                    throw new AiEngineException($"Ollama 오류: {chunk!.Error}");
                }

                var token = chunk?.Message?.Content;
                if (!string.IsNullOrEmpty(token))
                {
                    tokens++;
                    yield return token;
                }
                if (chunk?.Done == true) break;
            }

            if (tokens == 0)
            {
                throw new AiEngineException("Ollama로부터 빈 응답이 반환되었습니다.");
            }
        }
    }
}
