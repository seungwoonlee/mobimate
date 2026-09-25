using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace MobiMate.Web.Hosting;

/// <summary>
/// exe 안에 넣은 앱 셸 파일을 읽는 파일 제공자 (NFR-02 단일 exe).
/// 리소스 이름은 "wwwroot/&lt;상대 경로&gt;"(빌드의 IncludeClientAssets 타깃)이다. 파일은 바뀌지 않으므로 변경 감시는 없다.
/// 디렉터리 내용은 기본 문서 확인(UseDefaultFiles)에 필요한 만큼만 돌려준다.
/// </summary>
public sealed class EmbeddedWebRoot : IFileProvider
{
    private const string Prefix = "wwwroot/";
    private readonly Assembly _assembly;
    private readonly Dictionary<string, string> _files;   // 상대 경로(대소문자 구분) → 리소스 이름
    private readonly DateTimeOffset _modified;

    public EmbeddedWebRoot(Assembly assembly)
    {
        _assembly = assembly;
        _files = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(n => n[Prefix.Length..], n => n, StringComparer.Ordinal);   // 이름이 겹칠 일이 없도록 대소문자를 구분한다
        var location = assembly.Location;
        // 단일 exe에서는 Location이 비어 있다. 빌드 시각을 알 수 없으면 기동 시각을 쓴다(ETag·Last-Modified용)
        _modified = !string.IsNullOrEmpty(location) && File.Exists(location) ? File.GetLastWriteTimeUtc(location) : DateTimeOffset.UtcNow;
    }

    public int Count => _files.Count;

    public IFileInfo GetFileInfo(string subpath)
    {
        var key = Normalize(subpath);
        return _files.TryGetValue(key, out var name)
            ? new ResourceFile(_assembly, name, Path.GetFileName(key), _modified)
            : new NotFoundFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var dir = Normalize(subpath);
        if (dir.Length > 0) dir += "/";
        var entries = _files.Keys
            .Where(k => k.StartsWith(dir, StringComparison.Ordinal) && k.IndexOf('/', dir.Length) < 0)
            .Select(k => (IFileInfo)new ResourceFile(_assembly, _files[k], k[dir.Length..], _modified))
            .ToList();
        var exists = dir.Length == 0 || _files.Keys.Any(k => k.StartsWith(dir, StringComparison.Ordinal));
        return exists ? new Contents(entries) : NotFoundDirectoryContents.Singleton;
    }

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    private static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

    private sealed class ResourceFile(Assembly assembly, string resource, string name, DateTimeOffset modified) : IFileInfo
    {
        private long? _length;
        public bool Exists => true;
        public long Length => _length ??= ReadLength();
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => modified;
        public bool IsDirectory => false;
        private long ReadLength()
        {
            using var s = assembly.GetManifestResourceStream(resource);
            return s?.Length ?? 0;
        }

        public Stream CreateReadStream() => assembly.GetManifestResourceStream(resource) ?? throw new FileNotFoundException(resource);
    }

    private sealed class Contents(List<IFileInfo> entries) : IDirectoryContents
    {
        public bool Exists => true;
        public IEnumerator<IFileInfo> GetEnumerator() => entries.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
