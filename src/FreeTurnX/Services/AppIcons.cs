using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeTurnX.Services;

/// <summary>Три варианта иконки (Настройки → Интерфейс). Значок .exe в Проводнике остаётся стандартным:
/// он вшит в файл, а окно, панель задач и трей берут выбранный.</summary>
public static class AppIcons
{
    static string? shortcutVariant;
    public static readonly string[] Variants = ["standard", "black", "x"];

    public static Uri IcoUri(string variant) => new($"pack://application:,,,/Resources/icon-{Pick(variant)}.ico");
    public static Uri LogoUri(string variant) => new($"pack://application:,,,/Resources/logo-{Pick(variant)}.png");

    static readonly System.Collections.Generic.Dictionary<string, ImageSource> windowIcons = [];
    public static ImageSource Window(string variant)
    {
        var key = Pick(variant);
        if (!windowIcons.TryGetValue(key, out var image))
        {
            var frame = BitmapFrame.Create(IcoUri(key));
            frame.Freeze();
            windowIcons[key] = image = frame;
        }
        return image;
    }
    public static ImageSource Logo(string variant) => new BitmapImage(LogoUri(variant));

    public static System.Drawing.Icon Tray(string variant)
    {
        using var stream = Application.GetResourceStream(IcoUri(variant))!.Stream;
        using var source = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)source.Clone();
    }

    public static void UpdateStartMenu(string variant)
    {
        if (PackageChannel.IsPackaged) return; // Package manifest owns Start menu registration.
        if (shortcutVariant == variant) return;
        // Development/test copies must never redirect the installed shortcut.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FREETURN_DATA_DIR")) ||
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "uninstall.exe"))) return;
        object? shell = null;
        object? shortcut = null;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FreeTurn", "icons");
            Directory.CreateDirectory(folder);
            var iconPath = Path.Combine(folder, $"icon-{Pick(variant)}.ico");
            using (var source = Application.GetResourceStream(IcoUri(variant))!.Stream)
            using (var output = File.Create(iconPath)) source.CopyTo(output);
            var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FreeTurnX.lnk");
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(link);
            dynamic item = shortcut;
            item.TargetPath = Environment.ProcessPath!;
            item.WorkingDirectory = AppContext.BaseDirectory;
            item.IconLocation = iconPath + ",0";
            item.Save();
            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            shortcutVariant = variant;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException)
        {
            System.Diagnostics.Debug.WriteLine($"Start menu icon: {e.Message}");
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    static string Pick(string variant) => Array.IndexOf(Variants, variant) >= 0 ? variant : "standard";
}
