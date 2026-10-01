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

    /// <summary>트레이 아이콘 (테스트에서 끈다).</summary>
    public bool Tray { get; set; } = true;

    /// <summary>LAN 모드에서 mDNS 이름 광고 (FR-MB-13).</summary>
    public bool Mdns { get; set; } = true;

    /// <summary>LAN을 올리기 전 개인 네트워크 판정을 두 번 하는 간격, 네트워크 변경 이벤트 디바운스 (NFR-03).</summary>
    public TimeSpan LanConfirmDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 단일 인스턴스 검사. 호스트를 만들기 전에 판단하므로 환경변수 MobiMate__SingleInstance로만 끈다(테스트).
    /// </summary>
    public bool SingleInstance { get; set; } = true;

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
    /// <summary>빌드 버전 원문 "0.9.MMdd.HHmm" (NFR-16). 네 자리를 모두 쓰고 앞자리 0을 살린다.</summary>
    public string Version { get; } =
        (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(ServerIdentity).Assembly)
            ?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>실제로 듣고 있는 루프백 포트 (빈 포트 자동 선택 결과).</summary>
    public int Port { get; set; }

    /// <summary>설정 포트. Port와 다르면 로컬 전용 대체 기동 중이라 LAN을 열지 않는다 (NFR-04).</summary>
    public int PreferredPort { get; set; }
}
