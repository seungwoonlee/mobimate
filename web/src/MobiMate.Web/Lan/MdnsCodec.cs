using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MobiMate.Web.Lan;

/// <summary>
/// mDNS(RFC 6762) 최소 패킷 코덱. 우리 이름의 A 레코드 질의·응답만 다룬다 (상세설계 §3.6).
/// 소켓과 분리한 순수 함수라 단위 테스트한다. 형식이 이상한 패킷은 예외 없이 false로 버린다.
/// </summary>
public static class MdnsCodec
{
    public const int Port = 5353;
    public static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");
    public const ushort TypeA = 1;
    public const ushort TypeAny = 255;
    private const ushort ClassIn = 1;
    private const ushort CacheFlush = 0x8000;
    private const ushort UnicastBit = 0x8000;
    private const int MaxPointerJumps = 16;

    public sealed record Question(string Name, ushort Type, bool UnicastResponse);
    public sealed record Record(string Name, ushort Type, uint Ttl, byte[] Data);
    public sealed record Message(ushort Id, bool IsResponse, IReadOnlyList<Question> Questions, IReadOnlyList<Record> Answers);

    public static bool TryParse(ReadOnlySpan<byte> data, out Message? message)
    {
        message = null;
        try
        {
            if (data.Length < 12) return false;
            var id = BinaryPrimitives.ReadUInt16BigEndian(data);
            var flags = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
            int qd = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
            int an = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
            int ns = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
            int ar = BinaryPrimitives.ReadUInt16BigEndian(data[10..]);
            if (qd > 64 || an + ns + ar > 256) return false;

            var offset = 12;
            var questions = new List<Question>(qd);
            for (var i = 0; i < qd; i++)
            {
                var name = ReadName(data, ref offset);
                if (name == null || offset + 4 > data.Length) return false;
                var type = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
                var cls = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
                offset += 4;
                questions.Add(new Question(name, type, (cls & UnicastBit) != 0));
            }

            var answers = new List<Record>();
            for (var i = 0; i < an + ns + ar; i++)
            {
                var name = ReadName(data, ref offset);
                if (name == null || offset + 10 > data.Length) return false;
                var type = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
                var ttl = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
                int len = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 8)..]);
                offset += 10;
                if (offset + len > data.Length) return false;
                if (i < an) answers.Add(new Record(name, type, ttl, data.Slice(offset, len).ToArray()));
                offset += len;
            }
            message = new Message(id, (flags & 0x8000) != 0, questions, answers);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    /// <summary>우리 이름의 A 레코드를 묻는 질문인가 (A 또는 ANY, 대소문자 무시).</summary>
    public static Question? AsksFor(Message m, string name) =>
        m.IsResponse ? null : m.Questions.FirstOrDefault(q => (q.Type == TypeA || q.Type == TypeAny) && SameName(q.Name, name));

    /// <summary>응답 안에 이 이름의 A 레코드가 있으면 그 주소들 (이름 충돌 검사용).</summary>
    public static IReadOnlyList<IPAddress> AddressesFor(Message m, string name) =>
        !m.IsResponse ? Array.Empty<IPAddress>()
        : m.Answers.Where(a => a.Type == TypeA && a.Data.Length == 4 && SameName(a.Name, name)).Select(a => new IPAddress(a.Data)).ToList();

    public static bool SameName(string a, string b) => string.Equals(a.TrimEnd('.'), b.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A 레코드 응답. 멀티캐스트 응답은 id 0·질문 없음·캐시 플러시. 레거시 유니캐스트 질의(출발 포트 ≠ 5353)에는
    /// 같은 id와 질문을 되돌려 주고 TTL을 10초 이하로 한다 (RFC 6762 §6.7).
    /// </summary>
    public static byte[] BuildResponse(string name, IPAddress address, uint ttl, ushort id = 0, Question? echo = null)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("IPv4만 알린다.", nameof(address));
        var legacy = echo != null;
        using var ms = new MemoryStream();
        WriteU16(ms, id);
        WriteU16(ms, 0x8400);            // QR + AA
        WriteU16(ms, (ushort)(legacy ? 1 : 0));
        WriteU16(ms, 1);
        WriteU16(ms, 0);
        WriteU16(ms, 0);
        if (legacy)
        {
            WriteName(ms, echo!.Name);
            WriteU16(ms, echo.Type);
            WriteU16(ms, ClassIn);
        }
        WriteName(ms, name);
        WriteU16(ms, TypeA);
        WriteU16(ms, (ushort)(legacy ? ClassIn : ClassIn | CacheFlush));
        WriteU32(ms, legacy ? Math.Min(ttl, 10u) : ttl);
        WriteU16(ms, 4);
        ms.Write(address.GetAddressBytes());
        return ms.ToArray();
    }

    /// <summary>이름 충돌 검사용 질의 (A, 멀티캐스트 응답 요청).</summary>
    public static byte[] BuildQuery(string name)
    {
        using var ms = new MemoryStream();
        WriteU16(ms, 0);
        WriteU16(ms, 0);
        WriteU16(ms, 1);
        WriteU16(ms, 0);
        WriteU16(ms, 0);
        WriteU16(ms, 0);
        WriteName(ms, name);
        WriteU16(ms, TypeA);
        WriteU16(ms, ClassIn);
        return ms.ToArray();
    }

    private static string? ReadName(ReadOnlySpan<byte> data, ref int offset)
    {
        var sb = new StringBuilder();
        var pos = offset;
        var jumped = false;
        var jumps = 0;
        while (true)
        {
            if (pos >= data.Length) return null;
            var len = data[pos];
            if (len == 0)
            {
                pos++;
                break;
            }
            if ((len & 0xC0) == 0xC0)
            {
                if (pos + 1 >= data.Length || ++jumps > MaxPointerJumps) return null;
                var target = ((len & 0x3F) << 8) | data[pos + 1];
                if (!jumped) offset = pos + 2;
                jumped = true;
                pos = target;
                continue;
            }
            if ((len & 0xC0) != 0 || pos + 1 + len > data.Length) return null;
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.UTF8.GetString(data.Slice(pos + 1, len)));
            if (sb.Length > 255) return null;
            pos += 1 + len;
        }
        if (!jumped) offset = pos;
        return sb.ToString();
    }

    private static void WriteName(Stream s, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new ArgumentException($"잘못된 이름: {name}", nameof(name));
            s.WriteByte((byte)bytes.Length);
            s.Write(bytes);
        }
        s.WriteByte(0);
    }

    private static void WriteU16(Stream s, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        s.Write(b);
    }

    private static void WriteU32(Stream s, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        s.Write(b);
    }
}
