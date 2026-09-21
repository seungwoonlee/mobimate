using System;
using System.Collections.Generic;
using System.Net.Http;
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
    [property: JsonPropertyName("done")] bool Done
);

public class OllamaService
{
    private static readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("http://localhost:11434/"),
        Timeout = TimeSpan.FromSeconds(20)
    };

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

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
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
            return (false, "", "Ollama 응답 대기 시간이 초과되었습니다 (15초).");
        }
        catch (Exception ex)
        {
            return (false, "", $"Ollama 통신 오류: {ex.Message}");
        }
    }
}
