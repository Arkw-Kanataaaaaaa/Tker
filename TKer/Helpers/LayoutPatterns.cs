using System.Collections.Generic;
using System.Linq;

namespace TKer.Helpers;

/// <summary>レイアウトパターン1件分の定義（正規化座標0-1のゾーン一覧）。</summary>
public class LayoutPattern
{
    /// <summary>パターンの一意識別子（保存される）。</summary>
    public string Id { get; init; } = "";
    /// <summary>UI に表示するパターン名。</summary>
    public string Name { get; init; } = "";
    /// <summary>パターンを構成するゾーン一覧（インデックスがそのまま ZoneIndex になる）。</summary>
    public List<NormalizedZone> Zones { get; init; } = new();
}

/// <summary>画面に対する 0-1 の正規化矩形。プレビュー描画と Win+矢印スナップ種別を持つ。</summary>
public class NormalizedZone
{
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    /// <summary>
    /// このゾーンを発火させる Win+矢印スナップ種別。
    /// "Maximize" / "LeftHalf" / "RightHalf" / "TopLeft" / "TopRight" / "BottomLeft" / "BottomRight"
    /// </summary>
    public string Snap { get; init; } = "";
}

/// <summary>
/// ウィンドウレイアウトの定義済みパターン。
/// Win+矢印で確実に再現できる Windows 標準スナップに限定（2分割均等・4分割・最大化）。
/// </summary>
public static class LayoutPatterns
{
    /// <summary>全パターン一覧（サイドバーに縦並びで表示される順）。</summary>
    public static readonly IReadOnlyList<LayoutPattern> All = new[]
    {
        new LayoutPattern
        {
            Id    = "2col_50_50",
            Name  = "2分割均等",
            Zones = new List<NormalizedZone>
            {
                new() { X = 0,   Y = 0, W = 0.5, H = 1, Snap = "LeftHalf"  },
                new() { X = 0.5, Y = 0, W = 0.5, H = 1, Snap = "RightHalf" },
            }
        },
        new LayoutPattern
        {
            Id    = "4_quadrants",
            Name  = "4分割（田の字）",
            Zones = new List<NormalizedZone>
            {
                new() { X = 0,   Y = 0,   W = 0.5, H = 0.5, Snap = "TopLeft"     },
                new() { X = 0.5, Y = 0,   W = 0.5, H = 0.5, Snap = "TopRight"    },
                new() { X = 0,   Y = 0.5, W = 0.5, H = 0.5, Snap = "BottomLeft"  },
                new() { X = 0.5, Y = 0.5, W = 0.5, H = 0.5, Snap = "BottomRight" },
            }
        },
        new LayoutPattern
        {
            Id    = "maximize",
            Name  = "最大化",
            Zones = new List<NormalizedZone>
            {
                new() { X = 0, Y = 0, W = 1, H = 1, Snap = "Maximize" },
            }
        },
    };

    /// <summary>ID からパターンを検索する。見つからない場合は null。</summary>
    public static LayoutPattern? FindById(string id) =>
        All.FirstOrDefault(p => p.Id == id);
}
