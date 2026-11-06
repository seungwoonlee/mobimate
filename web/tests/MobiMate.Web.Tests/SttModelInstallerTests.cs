using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using MobiMate.Web.Infrastructure;
using MobiMate.Web.Voice;

namespace MobiMate.Web.Tests;

/// <summary>모델 설치 (FR-VC-07): 커밋 고정 주소, 허용 호스트만, 크기·SHA-256 검증, .part 정리, 동시 설치 차단</summary>
public class SttModelInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mm-stt-inst-" + Guid.NewGuid().ToString("N"));
    private readonly TestHost _host = new();

    public void Dispose()
    {
        _host.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static readonly byte[] A = Encoding.UTF8.GetBytes(new string('a', 5000));
    private static readonly byte[] B = Encoding.UTF8.GetBytes(new string('b', 300));
    private static SttModelFile[] Manifest => new[] { new SttModelFile("one.onnx", A.Length, Sha(A)), new SttModelFile("tokens.txt", B.Length, Sha(B)) };

    /// <summary>요청 주소별로 응답을 정하는 가짜 서버. 요청 기록을 남긴다.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();
        public Func<Uri, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        public SemaphoreSlim? Gate { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests) Requests.Add(request.RequestUri!);
            if (Gate != null) await Gate.WaitAsync(cancellationToken);
            return Respond(request.RequestUri!);
        }
    }

    /// <summary>길이를 알 수 없는 스트림: StreamContent가 Content-Length를 계산하지 못해 본문으로 크기를 검증하는 경로를 탄다</summary>
    private sealed class NonSeekable(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Ok(byte[] body)
    {
        var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        return r;
    }

    private static HttpResponseMessage Redirect(string to, HttpStatusCode code = HttpStatusCode.Found)
    {
        var r = new HttpResponseMessage(code);
        r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
        return r;
    }

    private (SttModelInstaller Installer, SttModelLocator Model, FakeServer Server) Make(Func<Uri, HttpResponseMessage>? respond = null, SttModelFile[]? files = null)
    {
        Environment.SetEnvironmentVariable("MOBIMATE_STT_MODEL_DIR", null);
        var model = new SttModelLocator(_dir, files ?? Manifest);
        var server = new FakeServer
        {
            Respond = respond ?? (u => Path.GetFileName(u.AbsolutePath) switch { "one.onnx" => Ok(A), "tokens.txt" => Ok(B), _ => new HttpResponseMessage(HttpStatusCode.NotFound) }),
        };
        var installer = new SttModelInstaller(model, _host.Services.GetRequiredService<SseHub>(), NullLogger<SttModelInstaller>.Instance,
            _host.Services.GetRequiredService<IHostApplicationLifetime>(), () => server);
        return (installer, model, server);
    }

    [Fact]
    public async Task Install_DownloadsVerifiesAndMovesFilesIntoPlace()
    {
        var (installer, model, server) = Make();
        Assert.False(model.IsInstalled);

        Assert.Equal(SttInstallStart.Started, installer.Start());
        await installer.WaitAsync();

        Assert.Equal(SttInstallPhase.Done, installer.State.Phase);
        Assert.True(model.IsInstalled);
        Assert.Equal(A, await File.ReadAllBytesAsync(model.PathOf("one.onnx")));
        Assert.Empty(Directory.GetFiles(model.Dir, "*.part"));   // 임시 파일이 남지 않는다
        Assert.Equal(A.Length + B.Length, installer.State.DoneBytes);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task Install_UsesThePinnedCommit_NotAMovingBranch()
    {
        var (installer, _, server) = Make();
        installer.Start();
        await installer.WaitAsync();
        Assert.All(server.Requests, u =>
        {
            Assert.Equal("huggingface.co", u.Host);
            Assert.Equal("https", u.Scheme);
            Assert.StartsWith($"/{SttModelInstaller.Repo}/resolve/{SttModelInstaller.Revision}/", u.AbsolutePath);   // main이 아니라 커밋
        });
        Assert.Equal(40, SttModelInstaller.Revision.Length);
    }

    [Fact]
    public async Task Install_RejectsAFileWhoseHashDiffers_AndLeavesNothingBehind()
    {
        var tampered = Encoding.UTF8.GetBytes(new string('x', A.Length));   // 크기는 같고 내용만 다르다
        var (installer, model, _) = Make(u => Path.GetFileName(u.AbsolutePath) == "one.onnx" ? Ok(tampered) : Ok(B));
        installer.Start();
        await installer.WaitAsync();

        Assert.Equal(SttInstallPhase.Failed, installer.State.Phase);
        Assert.Contains("SHA-256", installer.State.Error);
        Assert.False(File.Exists(model.PathOf("one.onnx")));   // 검증을 통과하지 못한 파일은 제 이름을 얻지 못한다
        Assert.Empty(Directory.GetFiles(model.Dir, "*.part"));
        Assert.False(model.IsInstalled);
    }

    [Fact]
    public async Task Install_RejectsAWrongSize_FromTheHeaderAndFromTheBody()
    {
        var (installer, model, _) = Make(u => Ok(new byte[10]));   // 어느 파일이든 10바이트
        installer.Start();
        await installer.WaitAsync();
        Assert.Equal(SttInstallPhase.Failed, installer.State.Phase);
        Assert.Contains("크기", installer.State.Error);
        Assert.False(model.IsInstalled);
        Assert.Empty(Directory.GetFiles(model.Dir, "*.part"));
    }

    [Theory]
    [InlineData(100)]    // 예상보다 큰 본문(길이 헤더 없음)
    [InlineData(-100)]   // 예상보다 작은 본문
    public async Task Install_RejectsAChunkedBodyOfTheWrongSize(int delta)
    {
        // Content-Length가 없어도(StreamContent) 받은 양으로 검증한다
        var (installer, model, _) = Make(u => Path.GetFileName(u.AbsolutePath) == "one.onnx"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekable(new MemoryStream(new byte[A.Length + delta]))) }
            : Ok(B));
        installer.Start();
        await installer.WaitAsync();
        Assert.Equal(SttInstallPhase.Failed, installer.State.Phase);
        Assert.Contains(delta > 0 ? "큽니다" : "맞지 않습니다", installer.State.Error);
        Assert.False(File.Exists(model.PathOf("one.onnx")));
        Assert.Empty(Directory.GetFiles(model.Dir, "*.part"));
    }

    [Fact]
    public async Task Install_FollowsRedirectsToTheCdn_ButOnlyOnAllowedHosts()
    {
        var (installer, model, server) = Make(u => u.Host == "huggingface.co"
            ? Redirect("https://cas-bridge.xethub.hf.co/blob/" + Path.GetFileName(u.AbsolutePath))
            : Ok(Path.GetFileName(u.AbsolutePath) == "one.onnx" ? A : B));
        installer.Start();
        await installer.WaitAsync();
        Assert.Equal(SttInstallPhase.Done, installer.State.Phase);
        Assert.True(model.IsInstalled);
        Assert.Contains(server.Requests, u => u.Host == "cas-bridge.xethub.hf.co");

        // 같은 호스트 안의 상대 경로 리다이렉트도 따라간다
        var (installer2, _, _) = Make(u => u.AbsolutePath.StartsWith("/api/cache/") ? Ok(Path.GetFileName(u.AbsolutePath) == "one.onnx" ? A : B) : Redirect("/api/cache/" + Path.GetFileName(u.AbsolutePath), HttpStatusCode.TemporaryRedirect));
        Directory.Delete(_dir, true);
        installer2.Start();
        await installer2.WaitAsync();
        Assert.Equal(SttInstallPhase.Done, installer2.State.Phase);
    }

    [Theory]
    [InlineData("https://evil.example/one.onnx")]
    [InlineData("https://huggingface.co.evil.example/one.onnx")]   // 이름만 닮은 다른 도메인
    [InlineData("https://nothf.co/one.onnx")]
    [InlineData("http://cdn-lfs.huggingface.co/one.onnx")]         // https가 아니다
    public async Task Install_NeverContactsAHostOutsideTheAllowList(string target)
    {
        var (installer, model, server) = Make(u => u.Host == "huggingface.co" ? Redirect(target) : Ok(A));
        installer.Start();
        await installer.WaitAsync();

        Assert.Equal(SttInstallPhase.Failed, installer.State.Phase);
        Assert.False(model.IsInstalled);
        Assert.DoesNotContain(server.Requests, u => u.Host != "huggingface.co");   // 나쁜 주소로는 요청조차 보내지 않았다
    }

    [Fact]
    public async Task Install_StopsAfterTooManyRedirects()
    {
        var (installer, _, server) = Make(u => Redirect("https://huggingface.co/loop"));   // 끝없이 돌려보낸다
        installer.Start();
        await installer.WaitAsync();
        Assert.Equal(SttInstallPhase.Failed, installer.State.Phase);
        Assert.Contains("너무 많습니다", installer.State.Error);
        Assert.Equal(6, server.Requests.Count);   // 처음 요청 + 리다이렉트 5번, 그 뒤는 따라가지 않는다
    }

    [Fact]
    public async Task Install_NotFound_FailsWithTheStatus()
    {
        var (installer, _, _) = Make(u => new HttpResponseMessage(HttpStatusCode.NotFound));
        installer.Start();
        await installer.WaitAsync();
        Assert.Equal(SttInstallPhase.Failed, installer.State.Phase);
        Assert.Contains("404", installer.State.Error);
    }

    [Fact]
    public async Task Install_WhenAlreadyInstalled_DoesNothing()
    {
        var (installer, model, server) = Make();
        Directory.CreateDirectory(model.Dir);
        await File.WriteAllBytesAsync(model.PathOf("one.onnx"), A);
        await File.WriteAllBytesAsync(model.PathOf("tokens.txt"), B);

        Assert.Equal(SttInstallStart.AlreadyInstalled, installer.Start());
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task Install_SkipsFilesThatAreAlreadyThere_AndFetchesOnlyTheMissingOne()
    {
        var (installer, model, server) = Make();
        Directory.CreateDirectory(model.Dir);
        await File.WriteAllBytesAsync(model.PathOf("one.onnx"), A);   // 하나는 이미 있다

        installer.Start();
        await installer.WaitAsync();
        Assert.True(model.IsInstalled);
        Assert.Single(server.Requests);
        Assert.EndsWith("tokens.txt", server.Requests[0].AbsolutePath);
    }

    [Fact]
    public async Task Install_RemovesStalePartFilesFromAnInterruptedRun()
    {
        var (installer, model, _) = Make();
        Directory.CreateDirectory(model.Dir);
        await File.WriteAllBytesAsync(model.PathOf("one.onnx.part"), new byte[123]);   // 지난번 중단된 잔재
        installer.Start();
        await installer.WaitAsync();
        Assert.Empty(Directory.GetFiles(model.Dir, "*.part"));
        Assert.True(model.IsInstalled);
    }

    [Fact]
    public async Task Install_OnlyOneRunsAtATime()
    {
        var gate = new SemaphoreSlim(0);
        var (installer, _, server) = Make();
        server.Gate = gate;

        Assert.Equal(SttInstallStart.Started, installer.Start());
        Assert.Equal(SttInstallStart.AlreadyRunning, installer.Start());   // 두 번째는 새로 시작하지 않는다
        Assert.Equal(SttInstallPhase.Downloading, installer.State.Phase);

        gate.Release(10);
        await installer.WaitAsync();
        Assert.Equal(SttInstallPhase.Done, installer.State.Phase);
        Assert.Equal(2, server.Requests.Count);   // 파일마다 한 번씩만
    }

    [Fact]
    public async Task Install_CanBeRetriedAfterAFailure()
    {
        var ok = false;
        var (installer, model, _) = Make(u => ok ? Ok(Path.GetFileName(u.AbsolutePath) == "one.onnx" ? A : B) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        installer.Start();
        await installer.WaitAsync();
        Assert.Equal(SttInstallPhase.Failed, installer.State.Phase);

        ok = true;
        Assert.Equal(SttInstallStart.Started, installer.Start());
        await installer.WaitAsync();
        Assert.Equal(SttInstallPhase.Done, installer.State.Phase);
        Assert.True(model.IsInstalled);
    }

    [Theory]
    [InlineData("https://huggingface.co/a", true)]
    [InlineData("https://cdn-lfs.huggingface.co/a", true)]
    [InlineData("https://cas-bridge.xethub.hf.co/a", true)]
    [InlineData("https://us.aws.cdn.hf.co/xet-bridge-us/abc?X-Xet-Cas-Uid=public", true)]   // 2026-11-06 실제 큰 파일 리다이렉트 대상
    [InlineData("https://hf.co/a", true)]
    [InlineData("http://huggingface.co/a", false)]
    [InlineData("https://huggingface.co.evil.com/a", false)]
    [InlineData("https://evilhuggingface.co/a", false)]
    [InlineData("https://github.com/a", false)]
    [InlineData("https://huggingface.co@evil.com/a", false)]   // userinfo로 위장: 실제 호스트는 evil.com
    [InlineData("https://evil.com@huggingface.co/a", true)]    // 실제 호스트가 huggingface.co (자격 증명은 요청에 쓰이지 않는다)
    [InlineData("https://huggingface.co:443/a", true)]
    [InlineData("https://HuggingFace.CO/a", true)]             // 대소문자 무관
    [InlineData("https://huggingface.co./a", false)]           // 끝점이 붙으면 거절 (안전한 쪽으로 실패)
    public void AllowedUri_OnlyHttpsHuggingFace(string url, bool expected) => Assert.Equal(expected, SttModelInstaller.AllowedUri(new Uri(url)));

    [Fact]
    public void RealManifest_HasFourFiles_WithHashesAndSizes()
    {
        Assert.Equal(4, SttModelLocator.DefaultFiles.Count);
        Assert.All(SttModelLocator.DefaultFiles, f =>
        {
            Assert.Equal(64, f.Sha256.Length);
            Assert.True(f.Size > 0);
        });
        Assert.Contains(SttModelLocator.DefaultFiles, f => f.Name == "tokens.txt");
        Assert.All(SttModelLocator.DefaultFiles, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256));   // 16진수만 (오타 방지)
        Assert.Equal(SttModelLocator.DefaultFiles.Count, SttModelLocator.DefaultFiles.Select(f => f.Sha256).Distinct().Count());
        Assert.InRange(SttModelLocator.DefaultFiles.Sum(f => f.Size), 155_000_000L, 165_000_000L);   // 약 160MB
    }
}
