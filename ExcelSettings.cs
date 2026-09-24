using System;
using System.IO;
using System.Text.Json;

namespace MobiMate;

public class ExcelSettings
{
    public string FilePath { get; set; } = "";
    public bool AutoSyncOnCharChange { get; set; } = true;
    public DateTime? LastSyncTime { get; set; }
    public string? LastSyncCharacter { get; set; }
    public string? LastSyncStatus { get; set; }
}

public class ExcelSettingsManager
{
    private readonly string _storageDir;
    private readonly string _settingsFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _lock = new();

    public ExcelSettings CurrentSettings { get; private set; } = new();

    public ExcelSettingsManager(string? customStorageDir = null)
    {
        if (!string.IsNullOrWhiteSpace(customStorageDir))
        {
            _storageDir = customStorageDir;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _storageDir = Path.Combine(appData, "MobiMate");
        }

        Directory.CreateDirectory(_storageDir);
        _settingsFilePath = Path.Combine(_storageDir, "excel_settings.json");

        LoadSettings();
    }

    public void LoadSettings()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<ExcelSettings>(json);
                    if (loaded != null)
                    {
                        CurrentSettings = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                App.LogTrace($"ExcelSettingsManager.LoadSettings error: {ex.Message}");
                CurrentSettings = new ExcelSettings();
            }

            if (string.IsNullOrWhiteSpace(CurrentSettings.FilePath))
            {
                CurrentSettings.FilePath = ExcelSheetSyncService.ResolveDefaultSavePath();
            }
        }
    }

    public void SaveSettings()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(CurrentSettings, JsonOptions);
                var tempPath = _settingsFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _settingsFilePath, true);
            }
            catch (Exception ex)
            {
                App.LogTrace($"ExcelSettingsManager.SaveSettings error: {ex.Message}");
            }
        }
    }
}
