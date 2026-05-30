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

/// <summary>画面に対する 0-1 の正規化矩形。実画面サイズに乗算して実座標を求める。</summary>
public class NormalizedZone
{
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
}

/// <summary>ウィンドウレイアウトの定義済みパターン9種。Windows 11 のスナップレイアウトに準拠。</summary>
public static class LayoutPatterns
{
    /// <summary>全パターン一覧（3x3 グリッドで上から左→右の順）。</summary>
    public static readonly IReadOnlyList<LayoutPattern> All = new[]
    {
        Make("2col_50_50",        "2分割均等",
            (0,0,0.5,1), (0.5,0,0.5,1)),
        Make("2col_30_70",        "2分割（左1/4・右3/4）",
            (0,0,0.3,1), (0.3,0,0.7,1)),
        Make("2col_70_30",        "2分割（左3/4・右1/4）",
            (0,0,0.7,1), (0.7,0,0.3,1)),
        Make("3col_equal",        "3分割均等",
            (0,0,1.0/3,1), (1.0/3,0,1.0/3,1), (2.0/3,0,1.0/3,1)),
        Make("3col_center_wide",  "3分割（中央大 25/50/25）",
            (0,0,0.25,1), (0.25,0,0.5,1), (0.75,0,0.25,1)),
        Make("left_split_right",  "左を上下分割 + 右",
            (0,0,0.5,0.5), (0,0.5,0.5,0.5), (0.5,0,0.5,1)),
        Make("left_right_split",  "左 + 右を上下分割",
            (0,0,0.5,1), (0.5,0,0.5,0.5), (0.5,0.5,0.5,0.5)),
        Make("4_quadrants",       "4分割（田の字）",
            (0,0,0.5,0.5), (0.5,0,0.5,0.5), (0,0.5,0.5,0.5), (0.5,0.5,0.5,0.5)),
        Make("2row_50_50",        "上下分割",
            (0,0,1,0.5), (0,0.5,1,0.5)),
    };

    /// <summary>ID からパターンを検索する。見つからない場合は null。</summary>
    public static LayoutPattern? FindById(string id) =>
        All.FirstOrDefault(p => p.Id == id);

    /// <summary>(x, y, w, h) のタプル配列からパターンを構築する短縮ヘルパー。</summary>
    private static LayoutPattern Make(string id, string name,
        params (double X, double Y, double W, double H)[] zones)
        => new()
        {
            Id    = id,
            Name  = name,
            Zones = zones.Select(z => new NormalizedZone { X = z.X, Y = z.Y, W = z.W, H = z.H }).ToList()
        };
}
