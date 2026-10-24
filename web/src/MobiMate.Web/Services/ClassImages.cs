namespace MobiMate.Web.Services;

/// <summary>한 직업의 이미지 정보: 이미지 id와 원격 주소</summary>
public sealed record ClassImage(string Id, string Url)
{
    /// <summary>옛 전신 그림(`id.png`)과 섞이지 않게 새 이름으로 저장한다 — 옛 파일은 쓰이지 않고 그대로 남는다.</summary>
    public string FileName => $"{Id}.main.png";
}

/// <summary>
/// 직업별 전신 일러스트 (v1.5 요청 4). 넥슨 마비노기 모바일 메인 페이지 "클래스 선택" 카드의 직업별 상반신 그림(견습 클래스는 숙제가 의미 없어 그림이 없다, 21개)을
/// **첫 실행 때 이 PC에만 내려받아** 저장 폴더(class-images)에 둔다. 배포 파일(exe·zip)에는 넣지 않는다(저작권).
/// 내려받지 못하면 SVG 아이콘으로 대신한다.
/// 직업 이름 → 이미지 id 연결은 화면(lib/job.ts)에 있다. 주소는 정해진 넥슨 이미지 서버(lwi.nexon.com)만 쓰고,
/// 요청으로 받은 값으로 주소를 만들지 않는다.
/// </summary>
public sealed class ClassImages
{
    private const string Main = "https://lwi.nexon.com/m_mabinogim/brand/main/";
    private const long MaxBytes = 4 * 1024 * 1024;

    /// <summary>계열 카드 이미지(메인 페이지의 클래스 선택 카드, 상반신)</summary>
    private static ClassImage M(string id, string file) => new(id, Main + file + ".png");

    /// <summary>이미지 id → 주소 (2026-10-24: 메인 페이지의 클래스 카드 그림으로 교체). 파일 이름의 해시가 바뀌면(넥슨이 이미지를 교체하면) 내려받기가 실패하고 SVG 아이콘으로 대신한다.</summary>
    public static readonly IReadOnlyList<ClassImage> Known = new[]
    {
        M("warrior_2", "warrior_line_5_90B3235A24048548"),   // 기사
        M("warrior_3", "warrior_line_2_28C37233E92909BE"),   // 전사
        M("warrior_4", "warrior_line_3_B378B9AA701FB26A"),   // 대검전사
        M("warrior_5", "warrior_line_4_9434BAA792C97CF7"),   // 검술사
        M("archer_2", "archer_line_2_D6E45A19E1687DFD"),     // 궁수
        M("archer_3", "archer_line_3_F973703EF8785147"),     // 석궁사수
        M("archer_4", "archer_line_4_CEB489C064CB2A2C"),     // 장궁병
        M("thief_2", "thief_line_2_8F6A4BFBAA15E580"),       // 도적
        M("thief_3", "thief_line_3_8F6A4BFBAA15E580"),       // 격투가
        M("thief_4", "thief_line_4_8F6A4BFBAA15E580"),       // 듀얼블레이드
        M("mage_2", "mage_line_5_5F3544A216895074"),         // 전격술사
        M("mage_3", "mage_line_2_E7B399F3D4DDBD91"),         // 마법사
        M("mage_4", "mage_line_3_CDAD51308B7CFACB"),         // 화염술사
        M("mage_5", "mage_line_4_4F9B334E42140CEF"),         // 빙결술사
        M("bard_2", "bard_line_2_D8FC30473250D673"),         // 음유시인
        M("bard_3", "bard_line_3_E9065DF6A2199236"),         // 댄서
        M("bard_4", "bard_line_4_7E8E049E8DD9B5A8"),         // 악사
        M("healer_2", "healer_line_5_E9B9FAAA0F0D6D5F"),     // 암흑술사
        M("healer_3", "healer_line_2_6E1C3698B3515641"),     // 힐러
        M("healer_4", "healer_line_3_708525A531164BA0"),     // 사제
        M("healer_5", "healer_line_4_7AB184D51EAB25D1"),     // 수도사
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
