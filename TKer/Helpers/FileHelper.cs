using System.IO;

namespace TKer.Helpers;

public static class FileHelper
{
    /// <summary>ファイルサイズをエクスプローラー風に整形する。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)               return $"{bytes} バイト";
        if (bytes < 1024 * 1024)        return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    /// <summary>拡張子からファイル種類の日本語表記を返す。</summary>
    public static string GetFileType(string path)
    {
        var ext = Path.GetExtension(path).ToUpperInvariant();
        return ext switch
        {
            ".TXT"            => "テキスト ドキュメント",
            ".PDF"            => "PDF ドキュメント",
            ".XLSX"           => "Microsoft Excel ワークシート",
            ".XLS"            => "Microsoft Excel 97-2003 ワークシート",
            ".DOCX"           => "Microsoft Word ドキュメント",
            ".DOC"            => "Microsoft Word 97-2003 ドキュメント",
            ".PPTX"           => "Microsoft PowerPoint プレゼンテーション",
            ".PPT"            => "Microsoft PowerPoint 97-2003 プレゼンテーション",
            ".PNG"            => "PNG イメージ",
            ".JPG" or ".JPEG" => "JPEG イメージ",
            ".GIF"            => "GIF イメージ",
            ".BMP"            => "ビットマップ イメージ",
            ".ZIP"            => "圧縮 (zip 形式) フォルダー",
            ".RAR"            => "RAR アーカイブ",
            ".7Z"             => "7-Zip アーカイブ",
            ".CS"             => "Visual C# ソース ファイル",
            ".PY"             => "Python スクリプト",
            ".JS"             => "JavaScript ファイル",
            ".TS"             => "TypeScript ファイル",
            ".JSON"           => "JSON ファイル",
            ".XML"            => "XML ドキュメント",
            ".CSV"            => "CSV ファイル",
            ".MP4"            => "MP4 ビデオ ファイル",
            ".MP3"            => "MP3 オーディオ ファイル",
            ".HTML" or ".HTM" => "HTML ドキュメント",
            ".MD"             => "Markdown ファイル",
            ""                => "ファイル",
            _                 => $"{ext.TrimStart('.')} ファイル"
        };
    }

    /// <summary>拡張子からファイルアイコン文字（絵文字）を返す。</summary>
    public static string GetFileIcon(string path)
    {
        return Path.GetExtension(path).ToLower() switch
        {
            ".txt" or ".md"              => "📄",
            ".pdf"                       => "📕",
            ".xlsx" or ".xls" or ".csv"  => "📊",
            ".docx" or ".doc"            => "📝",
            ".pptx" or ".ppt"            => "📑",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "🖼️",
            ".zip" or ".7z" or ".rar"    => "📦",
            ".exe" or ".bat"             => "⚙️",
            _                            => "📄"
        };
    }
}
