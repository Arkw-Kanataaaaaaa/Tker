using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TKer.Helpers;

/// <summary>実行ファイルからアイコンを抽出する静的ヘルパー。</summary>
public static class AppIconHelper
{
    /// <summary>指定実行ファイルからアイコンを抽出して WPF 用 BitmapSource に変換する。失敗時は null。</summary>
    public static BitmapSource? TryGetExeIcon(string exePath)
    {
        try
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
    }
}
