using System.Text.Json;
using System.Text.Json.Nodes;

namespace MobiMateHomeWork.Tests;

/// <summary>게임에서 읽히는 캐릭터 한 명의 모습 (get_my_info·get_currencies·get_current_environment 샘플을 이 값으로 덮는다).</summary>
public sealed record GameChar(
    string Realm, string Job, int Level, long Combat, long Living, string Title,
    long Gold, long Deca, long MCash, string Map, double WeightCurrent = 500, double WeightMax = 1000, long Silver = 50, long Tribute = 5);

/// <summary>
/// 가짜 게임: 빌드된 가짜 CLI(tools/FakeCli)와 그 응답 샘플 폴더. 샘플은 임시 폴더로 복사해 두고 테스트가 시나리오에 맞게 고친다.
/// 가짜 CLI는 환경변수(FAKECLI_*)로 제어하므로 프로세스 전역이다 — 이 클래스를 쓰는 테스트는 순서대로 실행된다(AssemblyInfo).
/// </summary>
public sealed class FakeGame : IDisposable
{
    private static readonly string[] Vars =
    {
        "FAKECLI_LOG", "FAKECLI_STOPFILE", "FAKECLI_STATE", "FAKECLI_SAMPLES_DIR", "FAKECLI_GATHER_SCRIPT",
        "FAKECLI_DELAY_GET_MY_INFO", "FAKECLI_DELAY_GET_CURRENCIES", "FAKECLI_FAIL_GET_MY_INFO", "FAKECLI_FAIL_GET_CURRENCIES",
        "FAKECLI_BADJSON_GET_MY_INFO", "FAKECLI_FAIL_STATUS",
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "mm-hwgame-" + Guid.NewGuid().ToString("N"));
    public string SamplesDir => Path.Combine(Dir, "samples");
    public string LogPath => Path.Combine(Dir, "calls.log");
    public string ExePath { get; } = Locate();

    // 빌드 직후 첫 실행은 .NET 콜드 스타트로 느리다: 테스트 전에 한 번 띄워 디스크 캐시·JIT를 데운다.
    private static readonly Lazy<bool> WarmUp = new(() =>
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Locate(), "status")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        p.WaitForExit(15_000);
        return true;
    });

    public FakeGame()
    {
        _ = WarmUp.Value;
        Directory.CreateDirectory(SamplesDir);
        foreach (var f in Directory.GetFiles(FindToolsDir("FakeCli", "Samples"), "*.json"))
            File.Copy(f, Path.Combine(SamplesDir, Path.GetFileName(f)));
        foreach (var v in Vars) Environment.SetEnvironmentVariable(v, null);
        Environment.SetEnvironmentVariable("FAKECLI_SAMPLES_DIR", SamplesDir);
        Environment.SetEnvironmentVariable("FAKECLI_LOG", LogPath);
    }

    /// <summary>샘플 JSON을 읽어 고친 뒤 되돌려 쓴다. 가짜 CLI가 읽는 중이면 잠시 뒤 다시 시도한다(읽는 쪽은 통째로 읽으므로 반쯤 쓴 파일을 보지 않게 임시 파일로 바꿔치기한다).</summary>
    public void Edit(string command, Action<JsonNode> edit)
    {
        var path = Path.Combine(SamplesDir, command + ".json");
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        edit(node);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, node.ToJsonString(WriteOptions));
        for (var i = 0; ; i++)
        {
            try { File.Move(tmp, path, overwrite: true); return; }
            catch (IOException) when (i < 100) { Thread.Sleep(20); }
        }
    }

    public void Disconnect() => Environment.SetEnvironmentVariable("FAKECLI_STATE", "disconnected");
    public void Reconnect() => Environment.SetEnvironmentVariable("FAKECLI_STATE", null);
    public void Set(string name, string? value) => Environment.SetEnvironmentVariable(name, value);

    public void SetCombat(long combat) => Edit("get_my_info", n => n["CombatScore"]!["Value"] = combat);
    public void SetJob(string job) => Edit("get_my_info", n => n["EnabledCombatJobDisplayName"] = job);
    public void SetMap(string map) => Edit("get_current_environment", n => n["GameSpaceDisplayName"] = map);

    public void SetVitals(double weightCurrent, double weightMax) => Edit("get_my_info", n =>
    {
        n["Vitals"]!["InventoryWeightCurrent"] = weightCurrent;
        n["Vitals"]!["InventoryWeightMax"] = weightMax;
    });

    /// <summary>캐릭터 선택창에서 읽히는 정보: 서버·직업이 비고 레벨 0, 전투력 0.</summary>
    public void ShowCharacterSelect() => Edit("get_my_info", n =>
    {
        n["RealmName"] = "";
        n["EnabledCombatJobDisplayName"] = "";
        n["Level"] = 0;
        n["CombatScore"]!["Value"] = 0;
    });

    /// <summary>캐릭터 정보(get_my_info)와 재화(get_currencies)·맵(get_current_environment)을 한꺼번에 이 캐릭터로 바꾼다.</summary>
    public void SetCharacter(GameChar c)
    {
        SetCharacterInfo(c);
        SetWallet(c);
        SetMap(c.Map);
    }

    /// <summary>캐릭터 정보만 바꾼다(재화는 그대로). 게임이 재화를 캐릭터 정보보다 먼저/늦게 알려 주는 순간을 흉내 낼 때 쓴다.</summary>
    public void SetCharacterInfo(GameChar c) => Edit("get_my_info", n =>
    {
        n["RealmName"] = c.Realm;
        n["EnabledCombatJobDisplayName"] = c.Job;
        n["Level"] = c.Level;
        n["Title"] = c.Title;
        n["CombatScore"]!["Value"] = c.Combat;
        n["LivingScore"]!["Value"] = c.Living;
        n["Vitals"]!["InventoryWeightCurrent"] = c.WeightCurrent;
        n["Vitals"]!["InventoryWeightMax"] = c.WeightMax;
    });

    /// <summary>재화만 바꾼다(캐릭터 정보는 그대로).</summary>
    public void SetWallet(GameChar c) => Edit("get_currencies", n =>
    {
        var list = n.AsArray();
        void Put(string[] names, long amount)
        {
            foreach (var item in list)
                if (names.Contains(item!["DisplayName"]!.GetValue<string>())) { item["Amount"] = amount; return; }
            list.Add(new JsonObject { ["DisplayName"] = names[0], ["Amount"] = amount });
        }
        Put(new[] { "골드" }, c.Gold);
        Put(new[] { "데카" }, c.Deca);
        Put(new[] { "M캐시", "M캐쉬" }, c.MCash);
        Put(new[] { "은동전" }, c.Silver);
        Put(new[] { "마족 공물", "마족공물" }, c.Tribute);
    });

    /// <summary>샘플 그대로의 접속 캐릭터 (아이라 · 격투가 · 데카 10,285 · M캐시 5,619 · 은동전 120 · 마족 공물 9).</summary>
    public const string SampleKey = "아이라_격투가";

    public static string FindToolsDir(params string[] sub)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "tools", "FakeCli"))) d = d.Parent;
        if (d == null) throw new InvalidOperationException("hw/tools 폴더를 찾지 못했습니다.");
        return Path.Combine(new[] { d.FullName, "tools" }.Concat(sub).ToArray());
    }

    private static string Locate()
    {
        // tests/MobiMateHomeWork.Tests/bin/<Config>/net8.0-windows → tools/FakeCli/bin/<Config>/net8.0
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var config = Path.GetFileName(Path.GetDirectoryName(baseDir)!);
        var exe = Path.Combine(FindToolsDir("FakeCli"), "bin", config, "net8.0", "MabinogiMobile_CLI.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("가짜 CLI가 빌드되지 않았습니다. 솔루션 전체를 빌드하세요.", exe);
        return exe;
    }

    public void Dispose()
    {
        foreach (var v in Vars) Environment.SetEnvironmentVariable(v, null);
        try { Directory.Delete(Dir, recursive: true); } catch { }
    }
}
