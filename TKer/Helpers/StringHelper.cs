using System.IO;
using System.Linq;

namespace TKer.Helpers;

/// <summary>
/// 文字列操作のユーティリティ。
/// </summary>
public static class StringHelper
{
    /// <summary>ファイル名として使えない文字を _ に置換し maxLength で切り詰める。</summary>
    public static string SanitizeFileName(string name, int maxLength = 30)
    {
        var invalid    = Path.GetInvalidFileNameChars();
        var sanitized  = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Length > maxLength ? sanitized[..maxLength] : sanitized;
    }

    /// <summary>指定文字数を超えた場合に切り詰める。</summary>
    public static string Truncate(string name, int maxLength)
        => name.Length <= maxLength ? name : name[..maxLength];
}
