using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// 가짜 MabinogiMobile_CLI.exe (TST-02).
// 사용법은 진짜 CLI와 같다: MabinogiMobile_CLI.exe <command> [args...] [jsonBody]
//
// 환경변수로 동작을 바꾼다 (명령 이름은 대문자로):
//   FAKECLI_DELAY_<CMD>=ms        응답 전 지연 (execute_gathering은 이 시간 동안 정지 신호를 기다린다)
//   FAKECLI_FAIL_<CMD>=exitCode   stderr에 오류를 쓰고 해당 코드로 종료
//   FAKECLI_BADJSON_<CMD>=1       깨진 JSON을 출력
//   FAKECLI_STATE=disconnected    status가 실패한다 (게임 미연결)
//   FAKECLI_LOG=path              호출 기록(탭 구분: 시각·pid·phase·command·args)을 덧붙인다
//   FAKECLI_STOPFILE=path         stop_action이 이 파일을 만들고, 실행 중인 execute_gathering이 이를 보고 "stopped"로 끝난다
//   FAKECLI_SAMPLES_DIR=dir       내장 샘플 대신 <dir>/<command>.json을 쓴다

Console.OutputEncoding = new UTF8Encoding(false);
var json = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: MabinogiMobile_CLI <command> [args...]");
    return 2;
}

var cmd = args[0];
var rest = args.Skip(1).ToArray();
var key = cmd.ToUpperInvariant();

Log("start");
try
{
    if (Env($"FAKECLI_FAIL_{key}") is { } fail && int.TryParse(fail, out var code))
    {
        await Delay();
        Console.Error.WriteLine($"fake failure: {cmd}");
        return code;
    }

    if (cmd == "status" && string.Equals(Env("FAKECLI_STATE"), "disconnected", StringComparison.OrdinalIgnoreCase))
    {
        await Delay();
        Console.Error.WriteLine("게임 클라이언트에 연결할 수 없습니다.");
        return 1;
    }

    switch (cmd)
    {
        case "execute_gathering":
            return await Gather();

        case "stop_action":
            await Delay();
            if (Env("FAKECLI_STOPFILE") is { } stopFile) File.WriteAllText(stopFile, DateTime.UtcNow.ToString("O"));
            Console.WriteLine("""{"result":"stopped"}""");
            return 0;

        case "write_chat":
            await Delay();
            if (rest.Length == 0 || string.IsNullOrWhiteSpace(rest[0]))
            {
                Console.Error.WriteLine("채팅 내용이 없습니다.");
                return 1;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { result = "sent", message = rest[0] }, json));
            return 0;

        case "complete_altering_work":
        case "execute_crafting":
        case "execute_altering":
        case "play_music_score":
        case "change_instrument":
        case "stand_up":
            await Delay();
            Console.WriteLine(JsonSerializer.Serialize(new { result = "ok", command = cmd, body = rest.LastOrDefault() }, json));
            return 0;
    }

    var sample = LoadSample(cmd);
    if (sample == null)
    {
        Console.Error.WriteLine($"알 수 없는 명령: {cmd}");
        return 2;
    }

    await Delay();
    Console.WriteLine(Env($"FAKECLI_BADJSON_{key}") == "1" ? sample[..Math.Max(1, sample.Length / 2)] : sample);
    return 0;
}
finally
{
    Log("end");
}

async Task<int> Gather()
{
    string? name = null;
    int? count = null;
    try
    {
        var body = JsonNode.Parse(rest.LastOrDefault() ?? "{}");
        name = body?["displayName"]?.GetValue<string>();
        count = body?["count"]?.GetValue<int>();
    }
    catch (JsonException) { }

    if (string.IsNullOrWhiteSpace(name))
    {
        Console.Error.WriteLine("displayName이 필요합니다.");
        return 1;
    }

    var stopFile = Env("FAKECLI_STOPFILE");
    var total = DelayMs();
    var waited = 0;
    while (waited < total)
    {
        if (stopFile != null && File.Exists(stopFile))
        {
            try { File.Delete(stopFile); } catch { }
            Console.WriteLine(JsonSerializer.Serialize(new { result = "stopped", gained = Math.Min(count ?? 5, 1 + waited / 500) }, json));
            return 0;
        }
        await Task.Delay(25);
        waited += 25;
    }

    Console.WriteLine(JsonSerializer.Serialize(new { result = "completed", gained = count ?? 5, displayName = name }, json));
    return 0;
}

string? LoadSample(string command)
{
    if (Env("FAKECLI_SAMPLES_DIR") is { } dir)
    {
        var path = Path.Combine(dir, command + ".json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
    using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Samples.{command}.json");
    if (s == null) return null;
    using var r = new StreamReader(s, Encoding.UTF8);
    return r.ReadToEnd();
}

int DelayMs() => int.TryParse(Env($"FAKECLI_DELAY_{key}"), out var ms) ? ms : 0;
Task Delay() => DelayMs() > 0 ? Task.Delay(DelayMs()) : Task.CompletedTask;

string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

void Log(string phase)
{
    if (Env("FAKECLI_LOG") is not { } logPath) return;
    // 시각은 재시도 전에 찍는다. 다른 가짜 CLI가 쓰는 중이면(쓰기끼리는 배타) 최대 2초 재시도하고,
    // 그래도 못 쓰면 조용히 버리지 않고 stderr에 남긴다. 테스트 쪽 판독기는 쓰기 공유로 열어 쓰기를 막지 않는다.
    var line = string.Join('\t', DateTime.UtcNow.ToString("O"), Environment.ProcessId, phase, cmd, string.Join('\u001f', rest)) + "\n";
    for (var i = 0; i < 200; i++)
    {
        try
        {
            File.AppendAllText(logPath, line, new UTF8Encoding(false));
            return;
        }
        catch (IOException) { Thread.Sleep(10); }
    }
    Console.Error.WriteLine($"FAKECLI_LOG 기록 실패: {logPath}");
}
