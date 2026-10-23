// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

public static class CoffeePayloadInspector
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetCompressedFileSize(string file, out uint high);
    public static ulong AllocatedBytes(string file)
    {
        uint low = GetCompressedFileSize(Path.GetFullPath(file), out uint high);
        if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0) throw new Win32Exception();
        return ((ulong)high << 32) | low;
    }
    public static bool HasPayload(string file)
    {
        IntPtr module = LoadLibraryEx(Path.GetFullPath(file), IntPtr.Zero, 0x60);
        if (module == IntPtr.Zero) throw new Win32Exception();
        try { return FindResource(module, (IntPtr)301, (IntPtr)10) != IntPtr.Zero; }
        finally { FreeLibrary(module); }
    }
    public static byte[] Read(string file)
    {
        IntPtr module = LoadLibraryEx(Path.GetFullPath(file), IntPtr.Zero, 0x60);
        if (module == IntPtr.Zero) throw new Win32Exception();
        try
        {
            IntPtr resource = FindResource(module, (IntPtr)301, (IntPtr)10);
            if (resource == IntPtr.Zero) throw new InvalidDataException("The portable payload resource is missing.");
            uint size = SizeofResource(module, resource);
            if (size == 0 || size > 128 * 1024 * 1024) throw new InvalidDataException("Portable resource size is invalid.");
            IntPtr loaded = LoadResource(module, resource), address = LockResource(loaded);
            if (address == IntPtr.Zero) throw new Win32Exception();
            byte[] bytes = new byte[size]; Marshal.Copy(address, bytes, 0, bytes.Length); return bytes;
        }
        finally { FreeLibrary(module); }
    }
}
