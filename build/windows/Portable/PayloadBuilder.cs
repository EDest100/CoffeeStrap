// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

public static class CoffeePayloadBuilder
{
    [DllImport("cabinet.dll", SetLastError = true)] private static extern bool CreateCompressor(uint algorithm, IntPtr allocation, out IntPtr handle);
    [DllImport("cabinet.dll", SetLastError = true)] private static extern bool Compress(IntPtr handle, byte[] input, UIntPtr size, byte[] output, UIntPtr capacity, out UIntPtr required);
    [DllImport("cabinet.dll")] private static extern bool CloseCompressor(IntPtr handle);
    [DllImport("cabinet.dll", SetLastError = true)] private static extern bool SetCompressorInformation(IntPtr handle, uint informationClass, ref uint value, UIntPtr size);

    public static void Build(string directory, string entryAssembly, string output, uint algorithm, uint blockSize = 0)
    {
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => new[] { ".dll", ".json" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !Path.GetRelativePath(root, file).Replace('\\', '/').StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase) || Path.GetRelativePath(root, file).Replace('\\', '/').StartsWith("runtimes/win-x64/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => Path.GetRelativePath(root, file), StringComparer.Ordinal).ToArray();
        if (!files.Any(file => Path.GetRelativePath(root, file) == entryAssembly) || files.Length > 1024) throw new InvalidDataException("Invalid entry assembly or file count.");
        using var manifest = new MemoryStream(); using var data = new MemoryStream();
        using (var writer = new BinaryWriter(manifest, Encoding.UTF8, true))
        {
            foreach (string file in files)
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                byte[] name = Encoding.ASCII.GetBytes(relative), bytes = File.ReadAllBytes(file);
                if (name.Length > 512 || bytes.Length > 64 * 1024 * 1024 || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid payload file.");
                writer.Write((ushort)name.Length); writer.Write((ulong)bytes.Length); writer.Write(SHA256.HashData(bytes)); writer.Write(name);
                data.Write(bytes);
            }
        }
        byte[] plain = data.ToArray();
        if (plain.Length > 128 * 1024 * 1024) throw new InvalidDataException("The payload exceeds its size limit.");
        if (!CreateCompressor(algorithm, IntPtr.Zero, out IntPtr compressor)) throw new System.ComponentModel.Win32Exception();
        byte[] packed;
        try
        {
            if (blockSize != 0 && !SetCompressorInformation(compressor, 1, ref blockSize, (UIntPtr)sizeof(uint))) throw new System.ComponentModel.Win32Exception();
            Compress(compressor, plain, (UIntPtr)plain.Length, null, UIntPtr.Zero, out UIntPtr length);
            packed = new byte[checked((int)length)];
            if (!Compress(compressor, plain, (UIntPtr)plain.Length, packed, (UIntPtr)packed.Length, out length)) throw new System.ComponentModel.Win32Exception();
            Array.Resize(ref packed, checked((int)length));
        }
        finally { CloseCompressor(compressor); }
        using var outputFile = new BinaryWriter(File.Create(output));
        byte[] metadata = manifest.ToArray(), entryName = Encoding.ASCII.GetBytes(entryAssembly);
        outputFile.Write("CSCOFF01"u8); outputFile.Write(1u); outputFile.Write(algorithm); outputFile.Write((uint)files.Length); outputFile.Write((uint)metadata.Length);
        outputFile.Write((ulong)plain.Length); outputFile.Write((ulong)packed.Length); outputFile.Write(SHA256.HashData(metadata)); outputFile.Write(SHA256.HashData(plain));
        outputFile.Write((ushort)entryName.Length); outputFile.Write(entryName); outputFile.Write(metadata); outputFile.Write(packed);
    }
}
