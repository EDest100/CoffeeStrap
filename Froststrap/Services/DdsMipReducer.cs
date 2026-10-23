// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Froststrap.Services;

internal static class DdsMipReducer
{
    internal static string Prepare(string source, int droppedLevels)
    {
        if (droppedLevels is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(droppedLevels));
        using var input = File.OpenRead(source);
        if (input.Length is < 128 or > 67108864) throw new InvalidDataException("DDS file size is unsupported.");
        byte[] header = new byte[128]; input.ReadExactly(header);
        uint Read(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(offset, 4));
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset, 4), value);
        if (!header.AsSpan(0, 4).SequenceEqual("DDS "u8) || Read(4) != 124 || Read(76) != 32)
            throw new InvalidDataException("A standard DDS header is required.");
        uint pixelFlags = Read(80), fourCC = Read(84);
        bool extended = (pixelFlags & 4) != 0 && fourCC == 0x30315844;
        if (extended)
        {
            if (input.Length < 148) throw new InvalidDataException("The DDS DX10 header is truncated.");
            Array.Resize(ref header, 148); input.ReadExactly(header.AsSpan(128));
            if (Read(128) is not (61 or 80) || Read(132) is not (3 or 4) || Read(136) != 0 || Read(140) > 1 || Read(144) != 0)
                throw new InvalidDataException("Only single R8/BC4 UNORM DX10 textures without additional metadata are supported.");
        }
        bool volume = extended && Read(132) == 4;
        if (extended && Read(140) == 0 && !(volume && Read(128) == 80))
            throw new InvalidDataException("The DDS array size is invalid.");
        uint rawWidth = Read(16), rawHeight = Read(12), rawDepth = volume ? Read(24) : 1;
        if (rawWidth is < 1 or > 8192 || rawHeight is < 1 or > 8192 || rawDepth is < 1 or > 2048 || volume && (rawWidth > 2048 || rawHeight > 2048) || (long)rawWidth * rawHeight * rawDepth > 16777216 ||
            (volume ? Read(112) != 0x200000 || (Read(8) & 0x800000) == 0 : Read(24) > 1 || Read(112) != 0 || (Read(8) & 0x800000) != 0))
            throw new InvalidDataException("Only bounded single 2D/3D DDS textures are supported.");
        int width = (int)rawWidth, height = (int)rawHeight, depth = (int)rawDepth;
        bool compressed = extended ? Read(128) == 80 : (pixelFlags & 4) != 0;
        int blockBytes = extended && compressed ? 8 : fourCC switch { 0x31545844 => 8, 0x33545844 or 0x35545844 => 16, _ => 0 };
        int pixelBytes = extended ? 1 : 4;
        if (!extended && (compressed ? blockBytes == 0 : (pixelFlags & 0x40) == 0 || Read(88) != 32 || fourCC != 0))
            throw new InvalidDataException("Only DXT1/3/5, tightly packed RGB32 and R8/BC4 DX10 DDS textures are supported.");
        if (!compressed && (Read(8) & 8) != 0 && Read(20) != width * pixelBytes)
            throw new InvalidDataException("Padded DDS scan lines are unsupported.");
        int possibleLevels = 1;
        for (int size = Math.Max(Math.Max(width, height), depth); size > 1; size /= 2) possibleLevels++;
        uint declaredLevels = Read(28);
        if (declaredLevels > possibleLevels) throw new InvalidDataException("The DDS mip count exceeds its dimensions.");
        int levels = Math.Max(1, (int)declaredLevels), drop = droppedLevels;
        if (levels <= drop) throw new InvalidDataException("This DDS does not contain the requested smaller mip level.");
        long expected = header.Length, skipped = 0;
        int nextWidth = width, nextHeight = height, nextDepth = depth;
        long LevelBytes(int w, int h, int d) => (compressed ? (long)Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * blockBytes : (long)w * h * pixelBytes) * d;
        for (int i = 0, w = width, h = height, d = depth; i < levels; i++, w = Math.Max(1, w / 2), h = Math.Max(1, h / 2), d = Math.Max(1, d / 2))
        {
            long bytes = LevelBytes(w, h, d); expected += bytes;
            if (i < drop) { skipped += bytes; nextWidth = Math.Max(1, w / 2); nextHeight = Math.Max(1, h / 2); nextDepth = Math.Max(1, d / 2); }
        }
        if (input.Length != expected) throw new InvalidDataException("The DDS mip payload is truncated or has an unsupported layout.");
        if (compressed && (nextWidth % 4 != 0 || nextHeight % 4 != 0))
            throw new InvalidDataException("The requested mip is too small or unaligned for a block-compressed texture. Original resolution retained.");
        input.Position = 0;
        string hash = Convert.ToHexString(SHA256.HashData(input));
        string target = Path.Combine(Paths.Cache, "PreparedTextures", $"mip-v1-{drop}-{hash}.dds");
        if (File.Exists(target) && new FileInfo(target).Length == expected - skipped) return target;
        Write(8, (Read(8) & ~(8u | 0x80000u | 0x20000u)) | 0x1007u | (compressed ? 0x80000u : 8u) | (levels - drop > 1 ? 0x20000u : 0));
        Write(12, (uint)nextHeight); Write(16, (uint)nextWidth);
        Write(20, checked((uint)(compressed ? LevelBytes(nextWidth, nextHeight, nextDepth) : nextWidth * pixelBytes)));
        if (volume) Write(24, (uint)nextDepth);
        if (extended) Write(140, 1);
        Write(28, (uint)(levels - drop)); Write(108, 0x1000u | (levels - drop > 1 ? 0x400008u : volume ? 8u : 0));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temporary))
            {
                output.Write(header); input.Position = header.Length + skipped; input.CopyTo(output);
            }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return target;
    }
}
