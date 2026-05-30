using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TKer.Helpers;

namespace TKer.Views.Dialogs;

/// <summary>
/// 9 種類のレイアウトパターンをサムネイル付きで一覧表示し、ユーザーに1つ選ばせるダイアログ。
/// 選択結果は SelectedPatternId に格納される。
/// </summary>
public partial class WindowLayoutPatternPickerDialog : Window
{
    /// <summary>選択されたパターン ID。キャンセル時は null。</summary>
    public string? SelectedPatternId { get; private set; }

    /// <summary>パターン一覧を構築してダイアログを初期化する。</summary>
    public WindowLayoutPatternPickerDialog()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => Close()));
        BuildPatternThumbnails();
    }

    /// <summary>各パターンを 3x3 グリッドにサムネイル化して並べる。</summary>
    private void BuildPatternThumbnails()
    {
        foreach (var pattern in LayoutPatterns.All)
            PatternGrid.Children.Add(BuildPatternCard(pattern));
    }

    /// <summary>パターン1件のクリック可能なサムネイルカードを構築する。</summary>
    private Border BuildPatternCard(LayoutPattern pattern)
    {
        var border = new Border
        {
            Margin          = new Thickness(8),
            Padding         = new Thickness(10),
            Background      = (Brush)FindResource("BgSecondaryBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Cursor          = Cursors.Hand
        };

        var stack = new StackPanel();

        // パターンプレビュー（160 x 100 のミニ画面に各ゾーンを描画）
        var preview = BuildPreviewCanvas(pattern, 180, 110);
        stack.Children.Add(preview);

        stack.Children.Add(new TextBlock
        {
            Text       = pattern.Name,
            FontSize   = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin     = new Thickness(0, 8, 0, 0)
        });

        border.Child = stack;

        // ホバーで縁を強調
        border.MouseEnter += (_, _) =>
        {
            border.BorderBrush     = (Brush)FindResource("AccentCyanBrush");
            border.BorderThickness = new Thickness(2);
        };
        border.MouseLeave += (_, _) =>
        {
            border.BorderBrush     = (Brush)FindResource("BorderBrush");
            border.BorderThickness = new Thickness(1);
        };

        // クリックで選択して閉じる
        border.MouseLeftButtonUp += (_, _) =>
        {
            SelectedPatternId = pattern.Id;
            DialogResult      = true;
        };

        return border;
    }

    /// <summary>パターンのゾーンを正規化座標で塗り分けた小さなプレビュー Canvas を返す。</summary>
    private Canvas BuildPreviewCanvas(LayoutPattern pattern, double w, double h)
    {
        var canvas = new Canvas
        {
            Width  = w,
            Height = h,
            Background = (Brush)FindResource("BgCardBrush")
        };
        var zoneBrush  = new SolidColorBrush(Color.FromArgb(0x60, 0x3D, 0x7E, 0xFF));
        var zoneEdge   = new SolidColorBrush(Color.FromArgb(0xCC, 0x3D, 0x7E, 0xFF));

        foreach (var z in pattern.Zones)
        {
            var rect = new Rectangle
            {
                Width  = z.W * w - 2,
                Height = z.H * h - 2,
                Fill   = zoneBrush,
                Stroke = zoneEdge,
                StrokeThickness = 1
            };
            Canvas.SetLeft(rect, z.X * w + 1);
            Canvas.SetTop(rect,  z.Y * h + 1);
            canvas.Children.Add(rect);
        }
        return canvas;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
