using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MobiMate;
using Xunit;

namespace MobiMate.Tests;

public class GoogleSheetSyncTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseContent;

        public string? LastRequestBody { get; private set; }

        public MockHttpMessageHandler(HttpStatusCode statusCode, string responseContent)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseContent, Encoding.UTF8, "application/json")
            };
            return response;
        }
    }

    [Fact]
    public void BuildPayload_ExtractsAccurateStatsAndHomework()
    {
        // Arrange: 빅플라우드 실측 스탯
        var charInfo = new CharacterInfo(
            Title: "칭호",
            RealmName: "아이라",
            Level: 100,
            JobName: "전사",
            CombatScore: new ScoreVal("전투력", 102756),
            LivingScore: new ScoreVal("생활력", 18526),
            AttractivenessScore: new ScoreVal("매력", 26999),
            DecorScore: null,
            HealthMax: null,
            AttackPower: null,
            DefencePower: null,
            ArcaneResistance: new ScoreVal("마도 저항", 4736),
            STR: null,
            DEX: null,
            INT: null,
            LUCK: null,
            WILL: null,
            PaladinStats: null,
            Vitals: null
        );

        var tempDir = Path.Combine(Path.GetTempPath(), "MobiMate_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new HomeworkRepository(tempDir);
            var record = repo.GetOrCreateRecord("아이라_전사", DateTime.Now);
            record.Items["raid_cavrak"].IsCompleted = true;
            record.Items["raid_airel"].IsCompleted = false;
            record.Items["raid_white_succubus"].IsCompleted = true;
            record.Items["fieldboss_peri"].IsCompleted = true; // 필드보스 택1
            record.Items["weekly_vanguard_breach"].IsCompleted = true;
            repo.SaveRecord(record);

            // Act
            var payload = GoogleSheetSyncService.BuildPayload("빅플라우드", charInfo, repo, requestBackup: true);

            // Assert
            Assert.True(payload.Backup);
            Assert.Equal("빅플라우드", payload.Name);
            Assert.Equal("전사", payload.Job);
            Assert.Equal(102756, payload.CombatScore);
            Assert.Equal(18526, payload.LivingScore);
            Assert.Equal(26999, payload.Attractiveness);
            Assert.Equal(4736, payload.ArcaneResist);
            Assert.True(payload.RaidCavrak);
            Assert.False(payload.RaidEirel);
            Assert.True(payload.RaidWhiteSuccubus);
            Assert.True(payload.FieldBoss);
            Assert.True(payload.Vanguard);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SyncAsync_SuccessResponse_ParsesCorrectly()
    {
        // Arrange
        var mockResponse = "{\"status\":\"success\",\"message\":\"[빅플라우드] 동기화 완료 (13행)\",\"backupCreated\":true,\"updatedRow\":13}";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, mockResponse);
        using var client = new HttpClient(handler);
        var service = new GoogleSheetSyncService(client);

        var payload = new GoogleSheetPayload
        {
            Name = "빅플라우드",
            CombatScore = 102756
        };

        // Act
        var result = await service.SyncAsync("https://script.google.com/macros/s/test/exec", payload);

        // Assert
        Assert.True(result.success);
        Assert.Contains("동기화 완료", result.message);
        Assert.NotNull(result.responseData);
        Assert.Equal("success", result.responseData.Status);
        Assert.Equal(13, result.responseData.UpdatedRow);
        Assert.True(result.responseData.BackupCreated);
        Assert.NotNull(handler.LastRequestBody);
        Assert.Contains("\"name\":\"빅플라우드\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task SyncAsync_ErrorResponse_ReturnsFalse()
    {
        // Arrange
        var mockResponse = "{\"status\":\"error\",\"message\":\"시트에서 캐릭터를 찾을 수 없습니다: 알수없는캐릭\"}";
        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, mockResponse);
        using var client = new HttpClient(handler);
        var service = new GoogleSheetSyncService(client);

        var payload = new GoogleSheetPayload
        {
            Name = "알수없는캐릭"
        };

        // Act
        var result = await service.SyncAsync("https://script.google.com/macros/s/test/exec", payload);

        // Assert
        Assert.False(result.success);
        Assert.Contains("찾을 수 없습니다", result.message);
    }

    [Fact]
    public async Task SyncAsync_HttpFailure_ReturnsFalse()
    {
        // Arrange
        var mockResponse = "Internal Server Error";
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, mockResponse);
        using var client = new HttpClient(handler);
        var service = new GoogleSheetSyncService(client);

        var payload = new GoogleSheetPayload
        {
            Name = "빅플라우드"
        };

        // Act
        var result = await service.SyncAsync("https://script.google.com/macros/s/test/exec", payload);

        // Assert
        Assert.False(result.success);
        Assert.Contains("HTTP 오류", result.message);
    }

    [Fact]
    public void SettingsManager_SavesAndLoads_WithIsolation()
    {
        // Arrange: W-09 준수 임시 디렉토리
        var tempDir = Path.Combine(Path.GetTempPath(), "MobiMate_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new GoogleSheetSettingsManager(tempDir);
            manager.CurrentSettings.WebhookUrl = "https://script.google.com/macros/s/my-test-url/exec";
            manager.CurrentSettings.AutoSyncOnCharChange = true;
            manager.CurrentSettings.AutoBackupDaily = true;
            manager.CurrentSettings.LastSyncCharacter = "빅플라우드";
            manager.SaveSettings();

            // Act: 새로 로드
            var reloaded = new GoogleSheetSettingsManager(tempDir);

            // Assert
            Assert.Equal("https://script.google.com/macros/s/my-test-url/exec", reloaded.CurrentSettings.WebhookUrl);
            Assert.True(reloaded.CurrentSettings.AutoSyncOnCharChange);
            Assert.True(reloaded.CurrentSettings.AutoBackupDaily);
            Assert.Equal("빅플라우드", reloaded.CurrentSettings.LastSyncCharacter);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
