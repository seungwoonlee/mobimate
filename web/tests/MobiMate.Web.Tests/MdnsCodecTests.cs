using System.Net;
using MobiMate.Web.Lan;

namespace MobiMate.Web.Tests;

/// <summary>mDNS 코덱 (FR-MB-13, 상세설계 §3.6): 질의 판정, 응답 형식, 이상한 패킷 방어.</summary>
public class MdnsCodecTests
{
    private static readonly IPAddress Ip = IPAddress.Parse("192.168.0.23");

    [Fact]
    public void Query_RoundTrip_AndCaseInsensitiveMatch()
    {
        var q = MdnsCodec.BuildQuery("mobimate.local");
        Assert.True(MdnsCodec.TryParse(q, out var m));
        Assert.False(m!.IsResponse);
        Assert.NotNull(MdnsCodec.AsksFor(m, "MobiMate.Local."));
        Assert.Null(MdnsCodec.AsksFor(m, "mobimate-2.local"));
    }

    [Fact]
    public void MulticastResponse_HasCacheFlush_Id0_NoQuestion()
    {
        var r = MdnsCodec.BuildResponse("mobimate.local", Ip, 120);
        Assert.True(MdnsCodec.TryParse(r, out var m));
        Assert.True(m!.IsResponse);
        Assert.Equal(0, m.Id);
        Assert.Empty(m.Questions);
        Assert.Equal(Ip, Assert.Single(MdnsCodec.AddressesFor(m, "mobimate.local")));
        Assert.Equal(120u, m.Answers[0].Ttl);
        Assert.Equal(0x80, r[^(4 + 2 + 4 + 2)] & 0x80);   // class 상위 비트 = 캐시 플러시
    }

    [Fact]
    public void LegacyUnicastResponse_EchoesIdAndQuestion_TtlAtMost10()
    {
        Assert.True(MdnsCodec.TryParse(MdnsCodec.BuildQuery("mobimate.local"), out var q));
        var r = MdnsCodec.BuildResponse("mobimate.local", Ip, 120, id: 0x1234, echo: q!.Questions[0]);
        Assert.True(MdnsCodec.TryParse(r, out var m));
        Assert.Equal(0x1234, m!.Id);
        Assert.Single(m.Questions);
        Assert.Equal(10u, m.Answers[0].Ttl);
    }

    [Fact]
    public void QuestionWithUnicastBit_IsDetected()
    {
        var q = MdnsCodec.BuildQuery("mobimate.local");
        q[^2] |= 0x80;   // QU
        Assert.True(MdnsCodec.TryParse(q, out var m));
        Assert.True(MdnsCodec.AsksFor(m!, "mobimate.local")!.UnicastResponse);
    }

    [Fact]
    public void CompressedName_IsFollowed()
    {
        // 질문 이름을 가리키는 압축 포인터(0xC00C)를 쓴 응답
        var bytes = new List<byte> { 0, 0, 0x84, 0, 0, 1, 0, 1, 0, 0, 0, 0 };
        bytes.AddRange(new byte[] { 8, (byte)'m', (byte)'o', (byte)'b', (byte)'i', (byte)'m', (byte)'a', (byte)'t', (byte)'e', 5, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0, 0, 1, 0, 1 });
        bytes.AddRange(new byte[] { 0xC0, 0x0C, 0, 1, 0x80, 1, 0, 0, 0, 120, 0, 4, 10, 0, 0, 9 });
        Assert.True(MdnsCodec.TryParse(bytes.ToArray(), out var m));
        Assert.Equal(IPAddress.Parse("10.0.0.9"), Assert.Single(MdnsCodec.AddressesFor(m!, "mobimate.local")));
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 1 })]                                                   // 헤더보다 짧음
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 5, (byte)'a' })]                   // 레이블이 잘림
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 0x0C, 0, 1, 0, 1 })]         // 자기 자신을 가리키는 포인터 루프
    [InlineData(new byte[] { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 1, 0, 200 })] // rdlength가 패킷보다 김
    public void MalformedPackets_AreRejectedWithoutThrowing(byte[] data)
    {
        Assert.False(MdnsCodec.TryParse(data, out _));
    }

    [Fact]
    public void RandomBytes_NeverThrow()
    {
        var rnd = new Random(1234);
        for (var i = 0; i < 5000; i++)
        {
            var buf = new byte[rnd.Next(0, 200)];
            rnd.NextBytes(buf);
            if (buf.Length > 12) buf[4] = 0;   // 질문 수를 가끔 현실적으로
            MdnsCodec.TryParse(buf, out _);
        }
    }
}
