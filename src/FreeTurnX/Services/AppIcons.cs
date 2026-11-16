using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FreeTurnX.Services;

/// <summary>Три варианта иконки (Настройки → Интерфейс). Значок .exe в Проводнике остаётся стандартным:
/// он вшит в файл, а окно, панель задач и трей берут выбранный.</summary>
public static class AppIcons
{
    public static readonly string[] Variants = ["standard", "black", "x"];

    public static Uri IcoUri(string variant) => new($"pack://application:,,,/Resources/icon-{Pick(variant)}.ico");
    public static Uri LogoUri(string variant) => new($"pack://application:,,,/Resources/logo-{Pick(variant)}.png");

    public static ImageSource Window(string variant) => BitmapFrame.Create(IcoUri(variant));
    public static ImageSource Logo(string variant) => new BitmapImage(LogoUri(variant));

    public static System.Drawing.Icon Tray(string variant)
    {
        using var stream = Application.GetResourceStream(IcoUri(variant))!.Stream;
        return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    static string Pick(string variant) => Array.IndexOf(Variants, variant) >= 0 ? variant : "standard";
}
