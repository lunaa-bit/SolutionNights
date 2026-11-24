using System.IO.Compression;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SaucerNights.Preview;

/// <summary>Rasterises ImGui draw data on the CPU (textured, coloured, alpha-blended triangles) and saves PNGs.</summary>
internal sealed unsafe class SoftwareRenderer
{
    private sealed record Texture(int Width, int Height, uint[] Pixels);

    private readonly Dictionary<ulong, Texture> _textures = new();

    public void AddTexture(ulong id, int width, int height, uint[] rgba) => _textures[id] = new Texture(width, height, rgba);

    public uint[] Render(ImDrawDataPtr data, int width, int height, uint background)
    {
        var fb = new uint[width * height];
        Array.Fill(fb, background);
        var lists = data.CmdListsCount;
        for (var n = 0; n < lists; n++)
        {
            var list = new ImDrawListPtr(data.CmdLists[n]);
            var vtx = (ImDrawVert*)list.VtxBuffer.Data;
            var idx = (ushort*)list.IdxBuffer.Data;
            var cmds = (ImDrawCmd*)list.CmdBuffer.Data;
            for (var c = 0; c < list.CmdBuffer.Size; c++)
            {
                var cmd = cmds[c];
                if (cmd.UserCallback != null) continue;
                _textures.TryGetValue(cmd.TextureId.Handle, out var tex);
                var clip = cmd.ClipRect;
                var cx0 = Math.Max(0, (int)clip.X);
                var cy0 = Math.Max(0, (int)clip.Y);
                var cx1 = Math.Min(width, (int)Math.Ceiling(clip.Z));
                var cy1 = Math.Min(height, (int)Math.Ceiling(clip.W));
                if (cx0 >= cx1 || cy0 >= cy1) continue;
                for (var e = 0; e < cmd.ElemCount; e += 3)
                {
                    var a = vtx[cmd.VtxOffset + idx[cmd.IdxOffset + e]];
                    var b = vtx[cmd.VtxOffset + idx[cmd.IdxOffset + e + 1]];
                    var d = vtx[cmd.VtxOffset + idx[cmd.IdxOffset + e + 2]];
                    Triangle(fb, width, a, b, d, tex, cx0, cy0, cx1, cy1);
                }
            }
        }
        return fb;
    }

    private static void Triangle(uint[] fb, int width, ImDrawVert a, ImDrawVert b, ImDrawVert c, Texture? tex, int cx0, int cy0, int cx1, int cy1)
    {
        var area = Edge(a.Pos, b.Pos, c.Pos);
        if (Math.Abs(area) < 1e-6f) return;
        var minX = Math.Max(cx0, (int)Math.Floor(Math.Min(a.Pos.X, Math.Min(b.Pos.X, c.Pos.X))));
        var maxX = Math.Min(cx1 - 1, (int)Math.Ceiling(Math.Max(a.Pos.X, Math.Max(b.Pos.X, c.Pos.X))));
        var minY = Math.Max(cy0, (int)Math.Floor(Math.Min(a.Pos.Y, Math.Min(b.Pos.Y, c.Pos.Y))));
        var maxY = Math.Min(cy1 - 1, (int)Math.Ceiling(Math.Max(a.Pos.Y, Math.Max(b.Pos.Y, c.Pos.Y))));
        if (minX > maxX || minY > maxY) return;

        Vector4 ca = Unpack(a.Col), cb = Unpack(b.Col), cc = Unpack(c.Col);
        var sameColor = a.Col == b.Col && b.Col == c.Col;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = Edge(b.Pos, c.Pos, p) / area;
                var w1 = Edge(c.Pos, a.Pos, p) / area;
                var w2 = 1 - w0 - w1;
                if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                var col = sameColor ? ca : ca * w0 + cb * w1 + cc * w2;
                if (tex != null)
                {
                    var uv = a.Uv * w0 + b.Uv * w1 + c.Uv * w2;
                    col *= Sample(tex, uv);
                }
                if (col.W <= 0.001f) continue;
                var i = y * width + x;
                var dst = Unpack(fb[i]);
                var outC = new Vector4(
                    col.X * col.W + dst.X * (1 - col.W),
                    col.Y * col.W + dst.Y * (1 - col.W),
                    col.Z * col.W + dst.Z * (1 - col.W),
                    1);
                fb[i] = Pack(outC);
            }
        }
    }

    private static Vector4 Sample(Texture t, Vector2 uv)
    {
        // Bilinear, so scaled text and smooth shapes look as they do in game.
        var fx = uv.X * t.Width - 0.5f;
        var fy = uv.Y * t.Height - 0.5f;
        var x0 = (int)Math.Floor(fx);
        var y0 = (int)Math.Floor(fy);
        var tx = fx - x0;
        var ty = fy - y0;
        Vector4 Px(int x, int y)
        {
            x = Math.Clamp(x, 0, t.Width - 1);
            y = Math.Clamp(y, 0, t.Height - 1);
            return Unpack(t.Pixels[y * t.Width + x]);
        }
        var top = Vector4.Lerp(Px(x0, y0), Px(x0 + 1, y0), tx);
        var bottom = Vector4.Lerp(Px(x0, y0 + 1), Px(x0 + 1, y0 + 1), tx);
        return Vector4.Lerp(top, bottom, ty);
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static Vector4 Unpack(uint c) => new((c & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, ((c >> 16) & 0xFF) / 255f, ((c >> 24) & 0xFF) / 255f);

    private static uint Pack(Vector4 v) =>
        (uint)Math.Clamp((int)(v.X * 255 + 0.5f), 0, 255)
        | (uint)Math.Clamp((int)(v.Y * 255 + 0.5f), 0, 255) << 8
        | (uint)Math.Clamp((int)(v.Z * 255 + 0.5f), 0, 255) << 16
        | (uint)Math.Clamp((int)(v.W * 255 + 0.5f), 0, 255) << 24;

    public static void SavePng(string path, uint[] pixels, int width, int height)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (var x = 0; x < width; x++)
            {
                var c = pixels[y * width + x];
                raw.WriteByte((byte)c);
                raw.WriteByte((byte)(c >> 8));
                raw.WriteByte((byte)(c >> 16));
            }
        }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) raw.WriteTo(zs);

        using var f = File.Create(path);
        f.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, (uint)width);
        WriteBe(ihdr, 4, (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 2; // RGB
        Chunk(f, "IHDR", ihdr);
        Chunk(f, "IDAT", z.ToArray());
        Chunk(f, "IEND", []);
    }

    private static void WriteBe(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = new byte[4];
        WriteBe(crc, 0, Crc(t, data));
        s.Write(crc);
    }

    private static uint Crc(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a.Concat(b))
        {
            c ^= x;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFFu;
    }
}
