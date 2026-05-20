using System;
using System.IO;
using Newtonsoft.Json;

namespace TKer.Helpers;

/// <summary>
/// %AppData%\TKer 配下への JSON 読み書きを集約するヘルパー。
/// </summary>
public static class JsonFileStore
{
    public static readonly string AppDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKer");

    /// <summary>指定ファイルを読み込む。存在しない・破損の場合は new T() を返す。</summary>
    public static T Load<T>(string fileName) where T : new()
    {
        var path = Path.Combine(AppDataDir, fileName);
        try
        {
            if (File.Exists(path))
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? new T();
        }
        catch { }
        return new T();
    }

    /// <summary>指定ファイルへ書き込む。ディレクトリが無ければ作成する。</summary>
    public static void Save<T>(string fileName, T data)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(
                Path.Combine(AppDataDir, fileName),
                JsonConvert.SerializeObject(data, Formatting.Indented));
        }
        catch { }
    }
}
