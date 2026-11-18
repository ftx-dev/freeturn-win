using System;
using System.Runtime.InteropServices;

namespace FreeTurnX.Services;

public static class PackageChannel
{
    // Query identity, not installation path: unpackaged EXE must keep its updater.
    public static bool IsPackaged { get; } = Detect();
    static bool Detect()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) == 122;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int GetCurrentPackageFullName(ref uint length, IntPtr name);
    public static void OpenUpdates() => AppModel.OpenUrl("ms-windows-store://downloadsandupdates");
}
