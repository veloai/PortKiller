using System.Text;
using System.Text.Json;
using PortKiller.Core;
using Xunit;

namespace PortKiller.Core.Tests;

public class GlbModelTests
{
    /// <summary>삼각형 하나짜리 GLB 를 손으로 만든다. 실제 파일 없이도 읽기 규칙을 검사하려는 것이다.</summary>
    private static byte[] TinyGlb(bool withTexture)
    {
        var bin = new List<byte>();
        foreach (var f in new float[] { 0, 0, 0, 1, 0, 0, 0, 2, 0 }) bin.AddRange(BitConverter.GetBytes(f)); // 36 bytes
        foreach (var f in new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 }) bin.AddRange(BitConverter.GetBytes(f)); // normals 36
        foreach (var f in new float[] { 0, 0, 1, 0, 0, 1 }) bin.AddRange(BitConverter.GetBytes(f));          // uv 24
        foreach (var i in new ushort[] { 0, 1, 2, 0 }) bin.AddRange(BitConverter.GetBytes(i));               // idx 8 (4 = 패딩 포함)
        var tex = new byte[] { 1, 2, 3, 4 };
        if (withTexture) bin.AddRange(tex);

        var views = new List<object>
        {
            new { buffer = 0, byteOffset = 0, byteLength = 36 },
            new { buffer = 0, byteOffset = 36, byteLength = 36 },
            new { buffer = 0, byteOffset = 72, byteLength = 24 },
            new { buffer = 0, byteOffset = 96, byteLength = 6 },
        };
        if (withTexture) views.Add(new { buffer = 0, byteOffset = 104, byteLength = 4 });

        var json = JsonSerializer.Serialize(new
        {
            asset = new { version = "2.0" },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1, TEXCOORD_0 = 2 }, indices = 3 } } } },
            accessors = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3", min = new[] { 0f, 0f, 0f }, max = new[] { 1f, 2f, 0f } },
                new { bufferView = 1, componentType = 5126, count = 3, type = "VEC3" },
                new { bufferView = 2, componentType = 5126, count = 3, type = "VEC2" },
                new { bufferView = 3, componentType = 5123, count = 3, type = "SCALAR" },
            },
            bufferViews = views,
            images = withTexture ? new object[] { new { bufferView = 4, mimeType = "image/png" } } : null,
        });

        var jsonBytes = Pad(Encoding.UTF8.GetBytes(json), 0x20);
        var binBytes = Pad(bin.ToArray(), 0);
        var total = 12 + 8 + jsonBytes.Length + 8 + binBytes.Length;

        var glb = new List<byte>();
        glb.AddRange(BitConverter.GetBytes(0x46546C67u));
        glb.AddRange(BitConverter.GetBytes(2u));
        glb.AddRange(BitConverter.GetBytes((uint)total));
        glb.AddRange(BitConverter.GetBytes((uint)jsonBytes.Length));
        glb.AddRange(BitConverter.GetBytes(0x4E4F534Au));
        glb.AddRange(jsonBytes);
        glb.AddRange(BitConverter.GetBytes((uint)binBytes.Length));
        glb.AddRange(BitConverter.GetBytes(0x004E4942u));
        glb.AddRange(binBytes);
        return glb.ToArray();
    }

    private static byte[] Pad(byte[] b, byte fill)
    {
        var padded = new byte[(b.Length + 3) / 4 * 4];
        Array.Fill(padded, fill);
        Buffer.BlockCopy(b, 0, padded, 0, b.Length);
        return padded;
    }

    [Fact]
    public void Reads_positions_indices_and_bounds()
    {
        var m = GlbModel.Load(TinyGlb(withTexture: false));

        Assert.Equal(9, m.Positions.Length);
        Assert.Equal(new[] { 0, 1, 2 }, m.Indices);
        Assert.Equal(6, m.Uvs.Length);
        Assert.Equal((1f, 2f, 0f), m.Max);
        Assert.Null(m.TextureBytes);
    }

    [Fact]
    public void Reads_embedded_texture_bytes()
    {
        var m = GlbModel.Load(TinyGlb(withTexture: true));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, m.TextureBytes);
    }

    [Fact]
    public void Garbage_input_becomes_InvalidDataException_not_a_crash()
    {
        Assert.Throws<InvalidDataException>(() => GlbModel.Load(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }));
    }
}
