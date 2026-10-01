namespace MobiMate.Web.Services;

/// <summary>한 직업의 이미지 정보: 이미지 id와 원격 주소</summary>
public sealed record ClassImage(string Id, string Url)
{
    public string FileName => $"{Id}.png";
}

/// <summary>
/// 직업별 전신 일러스트 (v1.5 요청 4). 넥슨 마비노기 모바일 "클래스 소개 → 자세히 보기" 페이지의 직업별 이미지(27개)를
/// **첫 실행 때 이 PC에만 내려받아** 저장 폴더(class-images)에 둔다. 배포 파일(exe·zip)에는 넣지 않는다(저작권).
/// 직업은 얼굴이 아니라 전체 실루엣으로 구분하므로 전신을 그대로 보여 준다. 내려받지 못하면 SVG 아이콘으로 대신한다.
/// 직업 이름 → 이미지 id 연결은 화면(lib/job.ts)에 있다. 주소는 정해진 넥슨 이미지 서버(lwi.nexon.com)만 쓰고,
/// 요청으로 받은 값으로 주소를 만들지 않는다.
/// </summary>
public sealed class ClassImages
{
    private const string Base = "https://lwi.nexon.com/m_mabinogim/brand/info/class/";
    private const long MaxBytes = 4 * 1024 * 1024;

    private static ClassImage I(string id, string file) => new(id, Base + file + ".png");

    /// <summary>이미지 id → 주소. 파일 이름의 해시가 바뀌면(넥슨이 이미지를 교체하면) 내려받기가 실패하고 SVG 아이콘으로 대신한다.</summary>
    public static readonly IReadOnlyList<ClassImage> Known = new[]
    {
        I("warrior_1", "warrior_line_1_06066EE99E761EFA"),   // 견습 전사
        I("warrior_2", "warrior_line_5_8A452CCB080CB6B1"),   // 기사
        I("warrior_3", "warrior_line_2_37B2D3A04C09CC2F"),   // 전사
        I("warrior_4", "warrior_line_3_68958D98B3DDC1BF"),   // 대검전사
        I("warrior_5", "warrior_line_4_5F017124236C03D6"),   // 검술사
        I("archer_1", "archer_line_1_91D9F178871DF33E"),     // 견습 궁수
        I("archer_2", "archer_line_2_946AEAF0A67A9596"),     // 궁수
        I("archer_3", "archer_line_3_1C943631C2AC78C3"),     // 석궁사수
        I("archer_4", "archer_line_4_87DDBBCFAFAB46A2"),     // 장궁병
        I("thief_1", "thief_line_1_329BACBB215B9685"),       // 견습 도적
        I("thief_2", "thief_line_2_329BACBB215B9685"),       // 도적
        I("thief_3", "thief_line_3_329BACBB215B9685"),       // 격투가
        I("thief_4", "thief_line_4_329BACBB215B9685"),       // 듀얼블레이드
        I("mage_1", "mage_line_1_ABCF78594DE826C6"),         // 견습 마법사
        I("mage_2", "mage_line_5_B081998986AB2402"),         // 전격술사
        I("mage_3", "mage_line_2_3B83737BEFFE2F1E"),         // 마법사
        I("mage_4", "mage_line_3_8B52C51AC86C7FF4"),         // 화염술사
        I("mage_5", "mage_line_4_1407F0E540914B42"),         // 빙결술사
        I("bard_1", "bard_line_1_665D3DD481B4F2B1"),         // 견습 음유시인
        I("bard_2", "bard_line_2_9BC13B02244F2CCE"),         // 음유시인
        I("bard_3", "bard_line_3_292F7849A4CD3D6D"),         // 댄서
        I("bard_4", "bard_line_4_EE862F9C6C2F489E"),         // 악사
        I("healer_1", "healer_line_1_CAA08532FD6A5564"),     // 견습 힐러
        I("healer_2", "healer_line_5_A81ECBD118FDBD03"),     // 암흑술사
        I("healer_3", "healer_line_2_F36BCFDF34ADEF01"),     // 힐러
        I("healer_4", "healer_line_3_2D469FC76BCE4EA2"),     // 사제
        I("healer_5", "healer_line_4_937ABDCBE7D1B193"),     // 수도사
    };

    private readonly string _dir;
    private readonly Func<HttpClient> _http;
    private readonly ILogger<ClassImages> _log;

    public ClassImages(string storageDir, ILogger<ClassImages> log, Func<HttpClient>? http = null)
    {
        _dir = Path.Combine(storageDir, "class-images");
        _log = log;
        _http = http ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
    }

    /// <summary>알려진 이미지 id이면 이미지를, 아니면 null (경로 조작을 막기 위해 목록에 있는 id만 받는다)</summary>
    public static ClassImage? Find(string? id) => Known.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));

    public string PathOf(ClassImage c) => Path.Combine(_dir, c.FileName);

    /// <summary>저장된 파일이 있으면 그 경로, 없으면 null</summary>
    public string? Existing(string? id) => Find(id) is { } c && File.Exists(PathOf(c)) ? PathOf(c) : null;

    public IReadOnlyList<(string Id, bool Available)> Status() => Known.Select(c => (c.Id, File.Exists(PathOf(c)))).ToList();

    /// <summary>없는 이미지만 내려받는다. 하나가 실패해도 나머지는 계속한다. 내려받은 개수를 돌려준다.</summary>
    public async Task<int> EnsureDownloadedAsync(CancellationToken ct = default)
    {
        var done = 0;
        using var http = _http();
        foreach (var c in Known)
        {
            if (File.Exists(PathOf(c))) continue;
            try
            {
                Directory.CreateDirectory(_dir);
                using var res = await http.GetAsync(c.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!res.IsSuccessStatusCode) { _log.LogInformation("클래스 이미지 {Id}: 내려받지 못했습니다 ({Status})", c.Id, (int)res.StatusCode); continue; }
                if (res.Content.Headers.ContentLength is > MaxBytes) { _log.LogInformation("클래스 이미지 {Id}: 너무 큽니다", c.Id); continue; }
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                if (bytes.Length is 0 or > (int)MaxBytes || !IsPng(bytes)) { _log.LogInformation("클래스 이미지 {Id}: PNG가 아닙니다", c.Id); continue; }
                var tmp = PathOf(c) + ".part";
                await File.WriteAllBytesAsync(tmp, bytes, ct);
                File.Move(tmp, PathOf(c), overwrite: true);
                done++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
            {
                _log.LogInformation("클래스 이미지 {Id}: 내려받지 못했습니다 ({Message}). SVG 아이콘으로 보여 줍니다.", c.Id, ex.Message);
            }
        }
        return done;
    }

    private static bool IsPng(byte[] b) => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
}
