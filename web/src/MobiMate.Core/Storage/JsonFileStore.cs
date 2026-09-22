using System.Collections.Concurrent;
using System.Text.Json;

namespace MobiMate;

/// <summary>
/// JSON 파일 입출력 (NFR-13·NFR-14 프로세스 내 범위).
/// 읽기: 0바이트·손상 파일은 "*.corrupted.yyyyMMdd_HHmmss.bak"으로 격리하고 기본값을 돌려준다 (WPF판과 같은 이름 규칙).
/// 쓰기: 같은 폴더 임시 파일 → 대상이 있으면 File.Replace(백업 *.bak 1개 유지), 없으면 File.Move.
/// 같은 파일에 대한 읽기·쓰기는 프로세스 안에서 파일 경로별 잠금으로 직렬화한다. 프로세스 간 병합은 S8에서 다룬다.
/// </summary>
public sealed class JsonFileStore
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private static object LockFor(string path) => Locks.GetOrAdd(Path.GetFullPath(path), _ => new object());

    /// <returns>파일이 없거나, 격리했거나, 읽을 수 없으면 null</returns>
    public T? Load<T>(string path) where T : class
    {
        lock (LockFor(path))
        {
            try
            {
                if (!File.Exists(path)) return null;
                if (new FileInfo(path).Length == 0)
                {
                    Quarantine(path);
                    return null;
                }
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                Quarantine(path);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
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
