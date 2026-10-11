using System.Text.Json;

namespace MobiMateHomeWork.Tests;

/// <summary>
/// homework_app.json (숙제 앱 전용 값): 캐릭터별 마지막 맵·가방 최대 무게 기억(H2a 위험 지점 c)과
/// 잠긴 파일로 시작했을 때 기본값을 덮어쓰지 않는 규칙(위험 지점 h).
/// </summary>
public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-hwset-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, AppSettingsStore.FileName);

    public AppSettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private AppSettingsStore Open() => new(_dir, null, retries: 0, retryDelay: TimeSpan.Zero);

    // ── RememberCharacter ──

    [Fact]
    public void Remember_StoresMapAndWeightMax_AndTrimsTheMap()
    {
        var s = Open();
        Assert.True(s.RememberCharacter("바람_도적", "  콜헨 ", 2000));
        var info = s.CharacterInfo("바람_도적")!;
        Assert.Equal("콜헨", info.LastMap);
        Assert.Equal(2000, info.WeightMax);
        Assert.Equal("콜헨", Open().CharacterInfo("바람_도적")!.LastMap);   // 파일에 남았다(다시 열어도 보인다)
        s.RememberCharacter("Ab_Cd", "던바튼", null);
        Assert.Equal("던바튼", s.CharacterInfo("ab_cd")!.LastMap);   // 키는 대소문자 구분 없음
    }

    [Fact]
    public void Remember_EmptyMapAndZeroWeight_NeverEraseTheKnownValues()
    {
        var s = Open();
        s.RememberCharacter("a_b", "콜헨", 2000);
        var stamp = FileStamp.Of(FilePath);

        Assert.True(s.RememberCharacter("a_b", null, null));        // 잠깐 비어 읽힌 값
        Assert.True(s.RememberCharacter("a_b", "", 0));
        Assert.True(s.RememberCharacter("a_b", "   ", -5));
        var info = s.CharacterInfo("a_b")!;
        Assert.Equal("콜헨", info.LastMap);
        Assert.Equal(2000, info.WeightMax);
        Assert.Equal(stamp, FileStamp.Of(FilePath));                // 파일도 건드리지 않았다
    }

    [Fact]
    public void Remember_AnEmptyValueForAnUnknownCharacter_CreatesNothing()
    {
        var s = Open();
        Assert.True(s.RememberCharacter("a_b", "", 0));
        Assert.Null(s.CharacterInfo("a_b"));
        Assert.False(File.Exists(FilePath));                        // 저장할 것이 없으면 파일도 만들지 않는다
    }

    [Fact]
    public void Remember_OnlyOneValue_KeepsTheOther()
    {
        var s = Open();
        s.RememberCharacter("a_b", "콜헨", 2000);
        s.RememberCharacter("a_b", "던바튼", null);                  // 맵만 바뀜
        Assert.Equal(("던바튼", 2000), (s.CharacterInfo("a_b")!.LastMap, s.CharacterInfo("a_b")!.WeightMax));
        s.RememberCharacter("a_b", null, 2500);                     // 무게만 바뀜
        Assert.Equal(("던바튼", 2500), (s.CharacterInfo("a_b")!.LastMap, s.CharacterInfo("a_b")!.WeightMax));
    }

    [Fact]
    public void Remember_SavesOnlyWhenSomethingChanged()
    {
        var s = Open();
        s.RememberCharacter("a_b", "콜헨", 2000);
        var first = FileStamp.Of(FilePath);
        Assert.True(first.Exists);

        Assert.True(s.RememberCharacter("a_b", "콜헨", 2000));      // 같은 값
        Assert.True(s.RememberCharacter("a_b", " 콜헨 ", 2000));    // 앞뒤 공백만 다름 → 같은 값
        Assert.Equal(first, FileStamp.Of(FilePath));                // 저장하지 않았다(파일 고유 번호·시각이 그대로)

        s.RememberCharacter("a_b", "던바튼", 2000);
        Assert.NotEqual(first, FileStamp.Of(FilePath));             // 바뀌면 저장
    }

    [Fact]
    public void Remember_KeepsEachCharactersValuesApart()
    {
        var s = Open();
        s.RememberCharacter("a_b", "콜헨", 1000);
        s.RememberCharacter("c_d", "던바튼", 2000);
        Assert.Equal("콜헨", s.CharacterInfo("a_b")!.LastMap);
        Assert.Equal(2000, s.CharacterInfo("c_d")!.WeightMax);
        Assert.Null(s.CharacterInfo("e_f"));
    }

    [Fact]
    public void CharacterInfo_ReturnsACopy()
    {
        var s = Open();
        s.RememberCharacter("a_b", "콜헨", 1000);
        s.CharacterInfo("a_b")!.LastMap = "바뀜";
        s.Current.Characters["a_b"].WeightMax = 1;
        Assert.Equal("콜헨", s.CharacterInfo("a_b")!.LastMap);
        Assert.Equal(1000, s.CharacterInfo("a_b")!.WeightMax);
    }

    // ── 잠긴 파일로 시작 (위험 지점 h) ──

    private void WriteExisting()
    {
        var data = new AppSettingsData { CliPath = @"C:\game\cli.exe", AlwaysOnTop = false, CloseToTray = false };
        data.Characters["a_b"] = new CharacterAppInfo { LastMap = "콜헨", WeightMax = 1500 };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
    }

    [Fact]
    public void LockedAtStart_ShowsDefaults_ButNeverOverwritesTheFile_AndUpdateKeepsTheOldValuesOnceUnlocked()
    {
        WriteExisting();
        var original = File.ReadAllBytes(FilePath);

        AppSettingsStore s;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))   // 다른 프로그램이 쥐고 있다
        {
            s = new AppSettingsStore(_dir, null, retries: 1, retryDelay: TimeSpan.Zero);
            Assert.False(s.IsLoaded);                                   // 읽지 못함
            Assert.Null(s.Current.CliPath);                             // 화면에는 기본값
            Assert.True(s.Current.CloseToTray);

            Assert.False(s.Update(d => d.AlwaysOnTop = true));          // 잠긴 동안 저장은 보류
            Assert.False(s.RememberCharacter("c_d", "던바튼", 2000));
            Assert.False(s.IsLoaded);
        }
        Assert.Equal(original, File.ReadAllBytes(FilePath));            // 파일은 그대로(기본값으로 덮어쓰지 않았다)

        // 잠금이 풀린 뒤의 Update는 디스크의 기존 값 위에 변경을 얹는다
        Assert.True(s.Update(d => d.AlwaysOnTop = true));
        Assert.True(s.IsLoaded);
        var now = s.Current;
        Assert.Equal(@"C:\game\cli.exe", now.CliPath);                  // 기존 값 보존
        Assert.False(now.CloseToTray);                                  // 사용자가 꺼 둔 값 보존
        Assert.True(now.AlwaysOnTop);                                   // 새 변경 적용
        Assert.Equal("콜헨", now.Characters["a_b"].LastMap);             // 캐릭터별 값 보존
        Assert.Equal(1500, now.Characters["a_b"].WeightMax);

        var onDisk = JsonSerializer.Deserialize<AppSettingsData>(File.ReadAllText(FilePath))!;
        Assert.Equal(@"C:\game\cli.exe", onDisk.CliPath);
        Assert.True(onDisk.AlwaysOnTop);
        Assert.Equal(1500, onDisk.Characters["a_b"].WeightMax);
    }

    [Fact]
    public async Task LockedAtStart_RetriesAndLoadsWhenTheFileIsFreedInTime()
    {
        WriteExisting();
        var gate = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var opening = Task.Run(() => new AppSettingsStore(_dir, null, retries: 300, retryDelay: TimeSpan.FromMilliseconds(10)));
        await Task.Delay(100);                                           // 잠금을 쥔 채 재시도가 몇 번 돈다
        Assert.False(opening.IsCompleted);
        gate.Dispose();
        var s = await opening.WaitAsync(Wait.Default);

        Assert.True(s.IsLoaded);
        Assert.Equal(@"C:\game\cli.exe", s.Current.CliPath);
        Assert.Equal("콜헨", s.CharacterInfo("a_b")!.LastMap);
    }

    [Fact]
    public void UpdateWithoutChange_DoesNotTouchTheFile()
    {
        WriteExisting();
        var s = Open();
        var stamp = FileStamp.Of(FilePath);
        Assert.True(s.Update(_ => { }));
        Assert.True(s.Update(d => d.CliPath = @"C:\game\cli.exe"));      // 같은 값으로 덮어쓰기
        Assert.Equal(stamp, FileStamp.Of(FilePath));
    }

    [Fact]
    public void Update_BuildsOnWhatAnotherWriterSavedInTheMeantime()
    {
        var a = Open();
        a.Update(d => d.CliPath = @"C:\one.exe");
        var b = Open();                                                  // 같은 파일을 연 다른 객체(예: 다른 실행)
        b.RememberCharacter("a_b", "콜헨", 1000);

        a.Update(d => d.AlwaysOnTop = true);                             // a는 b의 저장을 모른 채 변경하지만 파일을 다시 읽어 얹는다
        var disk = Open().Current;
        Assert.Equal(@"C:\one.exe", disk.CliPath);
        Assert.True(disk.AlwaysOnTop);
        Assert.Equal("콜헨", disk.Characters["a_b"].LastMap);
    }

    // ── CLI 경로 정하기 ──

    [Fact]
    public void ResolveCliPath_PrefersOptions_ThenOwnSettings_ThenWebSettingsReadOnly()
    {
        var s = Open();
        var webFile = Path.Combine(_dir, AppSettingsStore.WebSettingsFileName);
        File.WriteAllText(webFile, """{ "CliPath": "C:\\web\\cli.exe" }""");

        Assert.Equal(@"C:\web\cli.exe", AppSettingsStore.ResolveCliPath(new AppOptions(), s));
        s.Update(d => d.CliPath = @"C:\own\cli.exe");
        Assert.Equal(@"C:\own\cli.exe", AppSettingsStore.ResolveCliPath(new AppOptions(), s));
        Assert.Equal(@"C:\arg\cli.exe", AppSettingsStore.ResolveCliPath(new AppOptions { CliPath = @"C:\arg\cli.exe" }, s));
    }

    [Fact]
    public void ResolveCliPath_BrokenWebSettings_AreNotQuarantinedOrChanged()
    {
        var webFile = Path.Combine(_dir, AppSettingsStore.WebSettingsFileName);
        File.WriteAllText(webFile, "{ 깨진 json");
        var before = File.ReadAllBytes(webFile);

        Assert.Null(AppSettingsStore.ResolveCliPath(new AppOptions(), Open()));
        Assert.Equal(before, File.ReadAllBytes(webFile));                // 웹앱 파일은 읽기만 한다(이름 바꾸기·덮어쓰기 없음)
        Assert.Single(Directory.GetFiles(_dir, "web_settings*"));
    }
}
