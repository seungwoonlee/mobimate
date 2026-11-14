namespace MobiMateHomeWork.Images;

/// <summary>공식 일러스트 하나: 이미지 id, 한글 이름, 고정 원격 주소.</summary>
public sealed record BackgroundImage(string Id, string Title, string Url)
{
    public RemoteImage Remote => new(Id, Url);
}

/// <summary>
/// 앱 배경으로 고를 수 있는 마비노기 모바일 공식 홈페이지(미디어 → 이미지)의 공식 일러스트 6개(모두 1280×720 PNG).
/// 공식 홈페이지 공개 이미지를 <b>이 PC에만</b> 캐시하는 것이고 배포 파일(exe·zip)에는 넣지 않는다(저작권) — 웹앱 직업 이미지와 같은 방식이다.
/// 첫 실행 때 공유 저장 폴더의 <c>backgrounds\id.png</c>에 없는 것만 백그라운드로 받는다. 주소는 이 고정 목록만 쓴다(요청 값으로 주소를 만들지 않는다).
/// </summary>
public static class BackgroundCatalog
{
    public const string FolderName = "backgrounds";
    public const string DefaultId = "night_wish";

    private const string Cdn = "https://dszw1qtcnsa5e.cloudfront.net/community/";

    public static readonly IReadOnlyList<BackgroundImage> Known = new[]
    {
        new BackgroundImage("night_wish", "밤하늘에 빛나는 소망", Cdn + "20251229/073a7dd4-d42f-4cb8-987d-54920420a361/11%EA%B3%B5%EC%8B%9D%ED%99%88%ED%8E%98%EC%9D%B4%EC%A7%80%EB%AF%B8%EB%94%94%EC%96%B4%EA%B2%8C%EC%8B%9C%EB%AC%BC15.png"),
        new BackgroundImage("full_moon", "보름달 아래 나누는 마음", Cdn + "20260916/c36aba2b-78b5-4c1d-9131-a83addf71037/1%EA%B3%B5%EC%8B%9D%ED%99%88%ED%8E%98%EC%9D%B4%EC%A7%80%EB%AF%B8%EB%94%94%EC%96%B41280x720.png"),
        new BackgroundImage("sunny_outing", "햇살 가득 나들이", Cdn + "20260429/2ea8ad76-edbb-4c8a-bc13-0d36ba9f61a2/1%EA%B3%B5%EC%8B%9D%ED%99%88%ED%8E%98%EC%9D%B4%EC%A7%80%EB%AF%B8%EB%94%94%EC%96%B41280x720.png"),
        new BackgroundImage("white_snow", "흰 눈의 선물", Cdn + "20251217/7c168773-f5b1-4215-ae20-87c706467739/9%EA%B3%B5%EC%8B%9D%ED%99%88%ED%8E%98%EC%9D%B4%EC%A7%80%EB%AF%B8%EB%94%94%EC%96%B4%EC%9D%B4%EB%AF%B8%EC%A7%80%ED%9D%B0%EB%88%88%EC%9D%98%EC%84%A0%EB%AC%BC.png"),
        new BackgroundImage("summer_sea", "여름 바다 산책", Cdn + "20260715/ae08c8b3-cf94-405c-b85b-ccec1bd9efd7/1%EA%B3%B5%EC%8B%9D%ED%99%88%ED%8E%98%EC%9D%B4%EC%A7%80%EB%AF%B8%EB%94%94%EC%96%B41280x720.png"),
        new BackgroundImage("new_morning", "함께 맞는 새 아침", Cdn + "20260210/dd781a87-e775-4b0f-9b84-2641e29e8da7/9%EA%B3%B5%EC%8B%9D%ED%99%88%ED%8E%98%EC%9D%B4%EC%A7%80%EB%AF%B8%EB%94%94%EC%96%B4%EC%9D%B4%EB%AF%B8%EC%A7%80%ED%95%A8%EA%BB%98%EB%A7%9E%EB%8A%94%EC%83%88%EC%95%84%EC%B9%A8.png"),
    };

    public static IReadOnlyList<RemoteImage> Remotes { get; } = Known.Select(k => k.Remote).ToList();

    public static BackgroundImage? Find(string? id) => Known.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.Ordinal));
}
