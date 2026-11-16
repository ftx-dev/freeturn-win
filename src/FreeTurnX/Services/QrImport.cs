using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using FreeTurn.Core;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace FreeTurnX.Services;

/// <summary>QR-код со ссылкой freeturn:// с картинки: из файла, из буфера, со снимка экрана.</summary>
public static class QrImport
{
    public static string? Decode(Bitmap bitmap)
    {
        var reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] },
        };
        return reader.DecodeMultiple(bitmap)?.Select(r => r.Text).FirstOrDefault(FreeturnLink.LooksLikeLink);
    }

    public static string? FromFile(string path)
    {
        using var bitmap = new Bitmap(path);
        return Decode(bitmap);
    }

    public static string? FromClipboardImage()
    {
        if (!System.Windows.Forms.Clipboard.ContainsImage()) return null;
        using var image = System.Windows.Forms.Clipboard.GetImage();
        return image is null ? null : Decode(new Bitmap(image));
    }

    /// <summary>Системная «Ножницы» (Win+Shift+S): снимок области приходит в буфер обмена - ждём его до минуты.</summary>
    public static async Task<string?> FromScreenAsync()
    {
        System.Windows.Forms.Clipboard.Clear();
        Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        for (var i = 0; i < 120; i++)
        {
            await Task.Delay(500);
            if (System.Windows.Forms.Clipboard.ContainsImage()) return FromClipboardImage();
        }
        return null;
    }
}
