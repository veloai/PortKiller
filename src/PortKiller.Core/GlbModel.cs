using System.Text.Json;

namespace PortKiller.Core;

/// <summary>
/// 읽어 들인 3D 모델 한 덩어리. 화면 기술(WPF 등)을 모르는 순수한 숫자 배열이다.
/// 좌표는 glTF 그대로(오른손 좌표, Y 위, +Z 가 화면 쪽)라서 WPF 3D 와 그대로 맞는다.
/// </summary>
public sealed class GlbModel
{
    public required float[] Positions { get; init; }   // xyz xyz ...
    public required float[] Normals { get; init; }     // xyz xyz ... (없으면 빈 배열)
    public required float[] Uvs { get; init; }         // uv uv ...   (없으면 빈 배열)
    public required int[] Indices { get; init; }       // 삼각형 목록
    public byte[]? TextureBytes { get; init; }         // PNG/JPEG 원본 바이트

    public (float X, float Y, float Z) Min { get; init; }
    public (float X, float Y, float Z) Max { get; init; }

    /// <summary>
    /// 의도적으로 좁게 만든 읽기 도구: 정적 메시 1개(첫 primitive)와 기본색 텍스처 1장만 읽는다.
    /// 뼈대·애니메이션·여러 재질은 읽지 않는다 — 지금 모델이 그렇고, 필요해지면 그때 넓힌다.
    /// 실패는 <see cref="InvalidDataException"/> 하나로 모은다.
    /// </summary>
    public static GlbModel Load(byte[] glb)
    {
        try
        {
            return Parse(glb);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException
                                      or ArgumentException or InvalidCastException or InvalidOperationException)
        {
            throw new InvalidDataException("GLB 를 읽지 못했다: " + ex.Message, ex);
        }
    }

    private static GlbModel Parse(byte[] glb)
    {
        if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67) // "glTF"
            throw new InvalidDataException("GLB 머리말이 아니다.");

        var jsonLen = (int)BitConverter.ToUInt32(glb, 12);
        if (BitConverter.ToUInt32(glb, 16) != 0x4E4F534A) // "JSON"
            throw new InvalidDataException("첫 덩어리가 JSON 이 아니다.");

        using var doc = JsonDocument.Parse(new ReadOnlyMemory<byte>(glb, 20, jsonLen));
        var root = doc.RootElement;

        // BIN 덩어리는 JSON 다음에 온다.
        var binHeader = 20 + jsonLen;
        if (binHeader + 8 > glb.Length) throw new InvalidDataException("BIN 덩어리가 없다.");
        var binStart = binHeader + 8;

        var prim = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var attrs = prim.GetProperty("attributes");

        var pos = ReadFloats(root, glb, binStart, attrs.GetProperty("POSITION").GetInt32(), 3, out var acc);
        var normals = attrs.TryGetProperty("NORMAL", out var n)
            ? ReadFloats(root, glb, binStart, n.GetInt32(), 3, out _) : Array.Empty<float>();
        var uvs = attrs.TryGetProperty("TEXCOORD_0", out var t)
            ? ReadFloats(root, glb, binStart, t.GetInt32(), 2, out _) : Array.Empty<float>();
        var indices = ReadIndices(root, glb, binStart, prim.GetProperty("indices").GetInt32());

        var min = acc.GetProperty("min");
        var max = acc.GetProperty("max");

        return new GlbModel
        {
            Positions = pos,
            Normals = normals,
            Uvs = uvs,
            Indices = indices,
            TextureBytes = ReadTexture(root, glb, binStart),
            Min = (min[0].GetSingle(), min[1].GetSingle(), min[2].GetSingle()),
            Max = (max[0].GetSingle(), max[1].GetSingle(), max[2].GetSingle()),
        };
    }

    private static (int Offset, int Length) View(JsonElement root, int bufferView, int binStart)
    {
        var v = root.GetProperty("bufferViews")[bufferView];
        var off = v.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
        return (binStart + off, v.GetProperty("byteLength").GetInt32());
    }

    private static float[] ReadFloats(JsonElement root, byte[] glb, int binStart, int accessor, int width, out JsonElement acc)
    {
        acc = root.GetProperty("accessors")[accessor];
        if (acc.GetProperty("componentType").GetInt32() != 5126) throw new InvalidDataException("float 이 아니다.");

        var count = acc.GetProperty("count").GetInt32();
        var (viewOffset, _) = View(root, acc.GetProperty("bufferView").GetInt32(), binStart);
        var start = viewOffset + (acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);

        // 사이를 건너뛰는 배치(byteStride)는 쓰지 않는 모델만 받는다.
        var view = root.GetProperty("bufferViews")[acc.GetProperty("bufferView").GetInt32()];
        if (view.TryGetProperty("byteStride", out var stride) && stride.GetInt32() != width * 4)
            throw new InvalidDataException("byteStride 배치는 지원하지 않는다.");

        var result = new float[count * width];
        Buffer.BlockCopy(glb, start, result, 0, result.Length * 4);
        return result;
    }

    private static int[] ReadIndices(JsonElement root, byte[] glb, int binStart, int accessor)
    {
        var acc = root.GetProperty("accessors")[accessor];
        var count = acc.GetProperty("count").GetInt32();
        var (viewOffset, _) = View(root, acc.GetProperty("bufferView").GetInt32(), binStart);
        var start = viewOffset + (acc.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);

        var result = new int[count];
        switch (acc.GetProperty("componentType").GetInt32())
        {
            case 5125: // uint32
                for (var i = 0; i < count; i++) result[i] = (int)BitConverter.ToUInt32(glb, start + i * 4);
                break;
            case 5123: // uint16
                for (var i = 0; i < count; i++) result[i] = BitConverter.ToUInt16(glb, start + i * 2);
                break;
            case 5121: // uint8
                for (var i = 0; i < count; i++) result[i] = glb[start + i];
                break;
            default:
                throw new InvalidDataException("지원하지 않는 인덱스 형식이다.");
        }

        return result;
    }

    private static byte[]? ReadTexture(JsonElement root, byte[] glb, int binStart)
    {
        if (!root.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array
            || images.GetArrayLength() == 0) return null;
        if (!images[0].TryGetProperty("bufferView", out var bv)) return null;

        var (offset, length) = View(root, bv.GetInt32(), binStart);
        var bytes = new byte[length];
        Buffer.BlockCopy(glb, offset, bytes, 0, length);
        return bytes;
    }
}
