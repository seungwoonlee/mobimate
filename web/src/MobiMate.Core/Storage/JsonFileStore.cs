using System.Collections.Concurrent;
using System.Text.Json;

namespace MobiMate;

/// <summary>
/// JSON 파일 입출력 (NFR-13·NFR-14 프로세스 내 범위).
/// 읽기: 0바이트·손상 파일은 "*.corrupted.yyyyMMdd_HHmmss.bak"으로 격리하고 기본값을 돌려준다 (WPF판과 같은 이름 규칙).
/// 쓰기: 같은 폴더 임시 파일 → 대상이 있으면 File.Replace(백업 *.bak 1개 유지), 없으면 File.Move.
/// 같은 파일에 대한 읽기·쓰기는 프로세스 안에서 파일 경로별 잠금으로 직렬화한다.
/// 웹앱은 전용 폴더(%APPDATA%\\MobiMateWeb)를 쓰므로 WPF판과 같은 파일을 동시에 쓰지 않는다.
/// </summary>
public enum LoadStatus { Missing, Loaded, Quarantined, Failed }

public sealed class JsonFileStore
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static object LockFor(string path) => Locks.GetOrAdd(Path.GetFullPath(path), _ => new object());

    /// <returns>파일이 없거나, 격리했거나, 읽을 수 없으면 null</returns>
    public T? Load<T>(string path) where T : class => TryLoad<T>(path, out var v) == LoadStatus.Loaded ? v : null;

    /// <summary>
    /// 읽기 결과를 구분해서 돌려준다. <see cref="LoadStatus.Failed"/>(잠김·권한 등 일시적 실패)이면 호출자는
    /// 빈 값으로 덮어쓰지 말고 이번 작업을 건너뛰어야 한다.
    /// </summary>
    public LoadStatus TryLoad<T>(string path, out T? value) where T : class
    {
        value = null;
        lock (LockFor(path))
        {
            try
            {
                if (!File.Exists(path)) return LoadStatus.Missing;
                if (new FileInfo(path).Length == 0)
                {
                    Quarantine(path);
                    return LoadStatus.Quarantined;
                }
                value = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
                return value == null ? LoadStatus.Quarantined : LoadStatus.Loaded;
            }
            catch (JsonException)
            {
                Quarantine(path);
                return LoadStatus.Quarantined;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return LoadStatus.Failed;
            }
        }
    }

    public bool Save<T>(string path, T value)
    {
        lock (LockFor(path))
        {
            var temp = $"{path}.tmp.{Guid.NewGuid():N}";
            try
            {
                var dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(temp, JsonSerializer.Serialize(value, WriteOptions));
                if (File.Exists(path))
                {
                    File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temp, path);
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
            }
        }
    }

    public static string? Quarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bak = $"{path}.corrupted.{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            File.Move(path, bak, overwrite: true);
            return bak;
        }
        catch
        {
            return null;
        }
    }
}
