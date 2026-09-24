using System;
using System.IO;
using Xunit;

namespace MobiMate.Tests;

public class ExcelSettingsTests : IDisposable
{
    private readonly string _tempTestDir;

    public ExcelSettingsTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "MobiMateTests_ExcelSettings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempTestDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempTestDir))
            {
                Directory.Delete(_tempTestDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void DefaultSettings_InitializesWithResolvedPath()
    {
        var manager = new ExcelSettingsManager(_tempTestDir);

        Assert.NotNull(manager.CurrentSettings);
        Assert.False(string.IsNullOrWhiteSpace(manager.CurrentSettings.FilePath));
        Assert.EndsWith(".xlsx", manager.CurrentSettings.FilePath);
        Assert.True(manager.CurrentSettings.AutoSyncOnCharChange);
    }

    [Fact]
    public void SaveAndLoad_PreservesCustomPathAndSyncStatus()
    {
        var manager1 = new ExcelSettingsManager(_tempTestDir);
        var customPath = Path.Combine(_tempTestDir, "CustomMabiBook.xlsx");

        manager1.CurrentSettings.FilePath = customPath;
        manager1.CurrentSettings.AutoSyncOnCharChange = false;
        manager1.CurrentSettings.LastSyncCharacter = "빅클라우드";
        manager1.CurrentSettings.LastSyncStatus = "동기화 완료";
        manager1.CurrentSettings.LastSyncTime = new DateTime(2026, 9, 24, 18, 30, 0);
        manager1.SaveSettings();

        // 새 인스턴스로 로드
        var manager2 = new ExcelSettingsManager(_tempTestDir);

        Assert.Equal(customPath, manager2.CurrentSettings.FilePath);
        Assert.False(manager2.CurrentSettings.AutoSyncOnCharChange);
        Assert.Equal("빅클라우드", manager2.CurrentSettings.LastSyncCharacter);
        Assert.Equal("동기화 완료", manager2.CurrentSettings.LastSyncStatus);
        Assert.Equal(new DateTime(2026, 9, 24, 18, 30, 0), manager2.CurrentSettings.LastSyncTime);
    }

    [Fact]
    public void CorruptedFile_RecoversGracefully()
    {
        var settingsFile = Path.Combine(_tempTestDir, "excel_settings.json");
        File.WriteAllText(settingsFile, "{ corrupted json content !!! }}}");

        var manager = new ExcelSettingsManager(_tempTestDir);

        Assert.NotNull(manager.CurrentSettings);
        Assert.False(string.IsNullOrWhiteSpace(manager.CurrentSettings.FilePath));
        Assert.EndsWith(".xlsx", manager.CurrentSettings.FilePath);
    }

    [Fact]
    public void SetTargetFilePath_DynamicallyChangesPath()
    {
        var initialPath = Path.Combine(_tempTestDir, "Initial.xlsx");
        var service = new ExcelSheetSyncService(initialPath);

        Assert.Equal(initialPath, service.TargetFilePath);

        var newPath = Path.Combine(_tempTestDir, "SubDir", "NewTarget.xlsx");
        service.SetTargetFilePath(newPath);

        Assert.Equal(newPath, service.TargetFilePath);
        Assert.True(Directory.Exists(Path.GetDirectoryName(newPath)));
    }
}
