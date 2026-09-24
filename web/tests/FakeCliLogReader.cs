namespace MobiMate.Tests;

/// <summary>
/// 가짜 CLI 호출 로그(calls.log)를 읽는다. Core·Web 테스트 프로젝트가 링크로 함께 쓴다.
/// 가짜 CLI가 덧붙이는 중에도 읽을 수 있게 쓰기·삭제 공유로 열고(File.ReadAllLines는 FileShare.Read라
/// 쓰기 핸들과 충돌해 IOException이 난다), 개행으로 끝나지 않은 마지막 줄(쓰는 중)은 버린다.
/// </summary>
internal static class FakeCliLogReader
{
    public static IReadOnlyList<string> ReadLines(string path)
    {
        if (!File.Exists(path)) return Array.Empty<string>();
        string text;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(fs);
            text = r.ReadToEnd();
        }
        catch (FileNotFoundException) { return Array.Empty<string>(); }
        catch (DirectoryNotFoundException) { return Array.Empty<string>(); }

        var end = text.LastIndexOf('\n');
        if (end < 0) return Array.Empty<string>();
        return text[..end].Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
    }
}
