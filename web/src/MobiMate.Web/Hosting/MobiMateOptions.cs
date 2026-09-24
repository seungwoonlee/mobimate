using System.Security.Cryptography;

namespace MobiMate.Web.Hosting;

/// <summary>서버 설정 (구성 섹션 "MobiMate"). 테스트는 이 값을 바꿔 임시 폴더·가짜 CLI를 쓴다.</summary>
public sealed class MobiMateOptions
{
    public const string Section = "MobiMate";

    /// <summary>웹앱 저장 폴더. 기본 %APPDATA%\MobiMateWeb (NFR-13).</summary>
    public string StorageDir { get; set; } = SnapshotManager.DefaultStorageDirectory;

    /// <summary>WPF판 저장 폴더(가져오기 원본, 읽기 전용). 비우면 가져오지 않는다.</summary>
    public string? WpfStorageDir { get; set; } = SnapshotManager.WpfStorageDirectory;

    /// <summary>게임 CLI 경로. 비우면 설정 파일 → 환경변수 → 기본 경로 순으로 정한다.</summary>
    public string? CliPath { get; set; }

    public int Port { get; set; } = 17800;
    public bool OpenBrowser { get; set; } = true;

    /// <summary>게임 상태(status) 확인 주기 (FR-CN-01).</summary>
    public TimeSpan StatusInterval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan QueryCacheTtl { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan SsePingInterval { get; set; } = TimeSpan.FromSeconds(20);
}

/// <summary>서버 인스턴스 식별자. 기동할 때마다 새로 만든다 (/api/ping, SSE hello).</summary>
public sealed class ServerIdentity
{
    public string ServerId { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public string Version { get; } = typeof(ServerIdentity).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>실제로 듣고 있는 루프백 포트 (빈 포트 자동 선택 결과).</summary>
    public int Port { get; set; }
}
