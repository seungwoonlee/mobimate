using System.Windows.Media.Imaging;

namespace MobiMateHomeWork.Images;

/// <summary>
/// 공유 저장 폴더 안의 이미지 파일(class-images, backgrounds) 위치와 디코드 캐시.
/// 같은 (파일, 크기)는 한 번만 디코드하고(OnLoad·Freeze라 파일을 잠그지 않는다) 성공한 것만 캐시한다 — 없던 파일이 내려받아지면
/// <see cref="Refresh"/>로 알려 화면이 다시 읽게 한다. 스레드 안전.
/// </summary>
public static class ImageFiles
{
    private static readonly object Lock = new();
    private static readonly Dictionary<(string Path, int H, int W), BitmapSource> Cache = [];
    private static string? _root;

    /// <summary>이미지가 새로 생겼거나 설정이 바뀌어 화면이 다시 읽어야 할 때. 어느 스레드에서든 올 수 있다.</summary>
    public static event Action? Changed;

    public static string? Root { get { lock (Lock) return _root; } }

    public static void Initialize(string storageDir)
    {
        lock (Lock) { _root = storageDir; Cache.Clear(); }
    }

    /// <summary>직업 이미지 id의 파일 경로(알려진 id만). 초기화 전이거나 모르는 id면 빈 문자열.</summary>
    public static string ClassImagePath(string? id)
    {
        var root = Root;
        return root == null || ClassImageCatalog.Known.FirstOrDefault(c => c.Id == id) is not { } c ? "" : Path.Combine(root, ClassImageCatalog.FolderName, c.FileName);
    }

    /// <summary>공식 배경 일러스트 id의 파일 경로(알려진 id만). 초기화 전이거나 모르는 id면 빈 문자열.</summary>
    public static string BackgroundPath(string? id)
    {
        var root = Root;
        return root == null || BackgroundCatalog.Find(id) is not { } b ? "" : Path.Combine(root, BackgroundCatalog.FolderName, b.Id + ".png");
    }

    public static void Refresh() => Changed?.Invoke();

    /// <summary>파일을 디코드해 얼린 비트맵으로 돌려준다(없거나 못 읽으면 null). 0이면 그 방향은 원본 크기.</summary>
    public static BitmapSource? Load(string? path, int decodePixelHeight = 0, int decodePixelWidth = 0)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var key = (path, decodePixelHeight, decodePixelWidth);
        lock (Lock) if (Cache.TryGetValue(key, out var hit)) return hit;
        try
        {
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                if (decodePixelHeight > 0) bmp.DecodePixelHeight = decodePixelHeight;
                else if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth;
                bmp.StreamSource = fs;
                bmp.EndInit();
            }
            bmp.Freeze();
            lock (Lock) Cache[key] = bmp;
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>캐시된 디코드 결과를 버린다(파일이 바뀌었을 때).</summary>
    public static void Forget()
    {
        lock (Lock) Cache.Clear();
    }
}
