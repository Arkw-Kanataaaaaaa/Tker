using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>
/// パターン選択方式のウィンドウレイアウト編集ページ。
/// 左サイドバーから 9 種類のパターンを選び、仮想デスクトップに固定ゾーンが配置される。
/// 各ゾーンの中央ボタンでアプリを割り当てる。
/// </summary>
public partial class WindowLayoutEditPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;

    private string? _editingId;
    private string? _patternId;
    private LayoutPattern? _pattern;
    private readonly List<SnapRect> _snaps = new();

    // パターン名 → サイドバーの該当カード（選択ハイライト用）
    private readonly Dictionary<string, Border> _patternCards = new();

    // 「全ウィンドウ最小化後、レイアウト適用」トグル状態
    private bool _minimizeOthers;

    // 起動中アプリ一覧
    private Point _appDragStartPoint;
    private DispatcherTimer? _appsRefreshTimer;

    /// <summary>ViewModel を受け取り初期化する。</summary>
    public WindowLayoutEditPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>ナビゲーション直後の再表示用フック。</summary>
    public void Refresh() { }

    /// <summary>
    /// キャンバスサイズを実画面解像度に合わせ、サイドバーのパターン一覧を構築し、
    /// 編集モードまたは選択済みパターンがあれば対応するパターンを表示する。
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // DPI 補正: SystemParameters は DIP のためキャンバスを物理ピクセルに揃える
        var dpi  = VisualTreeHelper.GetDpi(this);
        double dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        EditorCanvas.Width  = SystemParameters.PrimaryScreenWidth  * dpiX;
        EditorCanvas.Height = SystemParameters.PrimaryScreenHeight * dpiY;

        ApplyDesktopWallpaper();
        BuildPatternList();

        // 編集モード: 既存レイアウト読み込み
        Dictionary<int, WindowEntry>? preserved = null;
        var editId = _vm.EditingWindowLayoutId;
        if (!string.IsNullOrEmpty(editId))
        {
            var existing = _vm.WindowLayoutService.All.FirstOrDefault(l => l.Id == editId);
            if (existing != null)
            {
                _editingId          = existing.Id;
                HeaderTitle.Text    = "ウィンドウレイアウト編集";
                BtnSave.Content     = "保存";
                TxtName.Text        = existing.Name;
                TxtDescription.Text = existing.Description;
                _minimizeOthers     = existing.MinimizeOthers;
                _patternId          = string.IsNullOrEmpty(existing.PatternId) ? null : existing.PatternId;
                // ゾーン番号 → 既存エントリ のマッピングを作って復元時にアプリ割当を引き継ぐ
                preserved = existing.Windows
                    .Where(w => w.ZoneIndex >= 0)
                    .GroupBy(w => w.ZoneIndex)
                    .ToDictionary(g => g.Key, g => g.First());
            }
        }
        else
        {
            HeaderTitle.Text = "ウィンドウレイアウト追加";
            BtnSave.Content  = "作成";
            // 新規時は VM からパターンが渡されていれば採用
            _patternId = _vm.EditingWindowLayoutPatternId;
        }

        // パターンが決まっていればサイドバーで選択状態にしてゾーンを描画
        if (!string.IsNullOrEmpty(_patternId))
            SelectPattern(_patternId!, preserved);

        UpdateMinimizeToggleVisual();
        UpdateTestApplyEnabled();
        RefreshRunningAppsList();
        StartAppsRefreshTimer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _appsRefreshTimer?.Stop();

    /// <summary>仮想デスクトップ Border の背景にデスクトップ壁紙を適用する（失敗時は素のダーク背景）。</summary>
    private void ApplyDesktopWallpaper()
    {
        var path = Win32Window.GetDesktopWallpaperPath();
        if (path == null) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            DesktopBg.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill };
        }
        catch { }
    }

    // ── パターン一覧サイドバー ───────────────────────
    /// <summary>サイドバーに 9 種類のパターンカードを縦に並べる。</summary>
    private void BuildPatternList()
    {
        PatternList.Children.Clear();
        _patternCards.Clear();
        foreach (var pattern in LayoutPatterns.All)
        {
            var card = BuildPatternCard(pattern);
            _patternCards[pattern.Id] = card;
            PatternList.Children.Add(card);
        }
    }

    /// <summary>パターン1件分の縦並びサムネイル付きカードを構築する。</summary>
    private Border BuildPatternCard(LayoutPattern pattern)
    {
        var border = new Border
        {
            Margin          = new Thickness(0, 0, 0, 8),
            Padding         = new Thickness(8),
            Background      = (Brush)FindResource("BgSecondaryBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Cursor          = Cursors.Hand
        };

        var stack = new StackPanel();
        stack.Children.Add(BuildPreviewCanvas(pattern, 180, 100));
        stack.Children.Add(new TextBlock
        {
            Text         = pattern.Name,
            FontSize     = 11, FontWeight = FontWeights.SemiBold,
            Foreground   = (Brush)FindResource("TextPrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin       = new Thickness(0, 6, 0, 0)
        });
        border.Child = stack;

        border.MouseLeftButtonUp += (_, _) => SelectPattern(pattern.Id, preserved: null);
        return border;
    }

    /// <summary>パターンのゾーンを正規化座標で塗り分けたミニプレビュー Canvas を返す。</summary>
    private Canvas BuildPreviewCanvas(LayoutPattern pattern, double w, double h)
    {
        var canvas = new Canvas
        {
            Width = w, Height = h,
            Background = (Brush)FindResource("BgCardBrush")
        };
        var fill   = new SolidColorBrush(Color.FromArgb(0x60, 0x3D, 0x7E, 0xFF));
        var stroke = new SolidColorBrush(Color.FromArgb(0xCC, 0x3D, 0x7E, 0xFF));
        foreach (var z in pattern.Zones)
        {
            var rect = new Rectangle
            {
                Width  = z.W * w - 2,
                Height = z.H * h - 2,
                Fill   = fill,
                Stroke = stroke,
                StrokeThickness = 1
            };
            Canvas.SetLeft(rect, z.X * w + 1);
            Canvas.SetTop(rect,  z.Y * h + 1);
            canvas.Children.Add(rect);
        }
        return canvas;
    }

    /// <summary>
    /// 指定パターンを選択する。サイドバーのハイライトを更新し、
    /// 仮想デスクトップに固定ゾーンスナップを再構築する。
    /// preserved が与えられた場合、ゾーン番号一致のエントリのアプリ割当を引き継ぐ。
    /// </summary>
    private void SelectPattern(string patternId, Dictionary<int, WindowEntry>? preserved)
    {
        var pattern = LayoutPatterns.FindById(patternId);
        if (pattern == null) return;

        // パターン切り替え時はカレントゾーンのアプリ割当を保持して引き継ぐ
        preserved ??= _snaps
            .Where(s => s.ZoneIndex >= 0 && !string.IsNullOrEmpty(s.ExePath))
            .ToDictionary(s => s.ZoneIndex, s => new WindowEntry
            {
                Title   = s.DisplayTitle,
                ExePath = s.ExePath!,
                ZoneIndex = s.ZoneIndex
            });

        // 既存スナップを撤去
        foreach (var snap in _snaps) EditorCanvas.Children.Remove(snap.Container);
        _snaps.Clear();

        // パターン情報を保存し、サイドバーのハイライトを切り替え
        _patternId = patternId;
        _pattern   = pattern;
        UpdatePatternCardHighlights();

        // ゾーンを物理ピクセルに変換して固定スナップを配置
        for (int i = 0; i < pattern.Zones.Count; i++)
        {
            var z = pattern.Zones[i];
            double x = z.X * EditorCanvas.Width;
            double y = z.Y * EditorCanvas.Height;
            double w = z.W * EditorCanvas.Width;
            double h = z.H * EditorCanvas.Height;
            var snap = CreateZoneSnap(x, y, w, h, i);
            if (preserved != null && preserved.TryGetValue(i, out var entry) && !string.IsNullOrEmpty(entry.ExePath))
                snap.SetExePath(entry.ExePath, entry.Title);
        }
        UpdateTestApplyEnabled();
    }

    /// <summary>選択中パターンのカードを縁色でハイライトする。</summary>
    private void UpdatePatternCardHighlights()
    {
        var normalBrush = (Brush)FindResource("BorderBrush");
        var accentBrush = (Brush)FindResource("AccentCyanBrush");
        foreach (var (id, card) in _patternCards)
        {
            bool selected = id == _patternId;
            card.BorderBrush     = selected ? accentBrush : normalBrush;
            card.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    // ── ゾーンスナップ生成・アプリ割当 ───────────────────
    /// <summary>パターン由来の固定ゾーン（移動・リサイズ不可）スナップを生成する。</summary>
    private SnapRect CreateZoneSnap(double x, double y, double w, double h, int zoneIndex)
    {
        var snap = new SnapRect(EditorCanvas, x, y, w, h, isLocked: true);
        snap.ZoneIndex = zoneIndex;
        snap.PickAppRequested += (_, _) => OnPickApp(snap);
        snap.AppAssigned      += (_, _) => UpdateTestApplyEnabled();
        EditorCanvas.Children.Add(snap.Container);
        _snaps.Add(snap);
        return snap;
    }

    /// <summary>ファイル選択ダイアログを直接開いて、結果をスナップに反映する。</summary>
    private void OnPickApp(SnapRect snap)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "実行ファイル (*.exe)|*.exe|すべて (*.*)|*.*",
            Title  = "アプリの実行ファイルを選択"
        };
        if (ofd.ShowDialog(Window.GetWindow(this)) == true)
        {
            var name = Path.GetFileNameWithoutExtension(ofd.FileName);
            snap.SetExePath(ofd.FileName, name);
        }
    }

    /// <summary>アプリ設定済みのスナップが1つでもあればテスト適用ボタンを有効化する。</summary>
    private void UpdateTestApplyEnabled()
    {
        BtnTestApply.IsEnabled = _snaps.Any(s => !string.IsNullOrEmpty(s.ExePath));
    }

    // ── テスト適用 ───────────────────────────────
    /// <summary>現在のスナップ構成を保存せずに即座に適用してテストする。</summary>
    private void TestApply_Click(object sender, RoutedEventArgs e)
    {
        var entries = BuildEntries();
        if (entries.Count == 0) return;
        var tempLayout = new WindowLayout
        {
            Name           = "(テスト)",
            PatternId      = _patternId ?? "",
            Windows        = entries,
            MinimizeOthers = _minimizeOthers
        };
        ApplyResult result;
        Mouse.OverrideCursor = Cursors.Wait;
        try     { result = _vm.WindowLayoutService.Apply(tempLayout); }
        finally { Mouse.OverrideCursor = null; }

        var dlg = new WindowLayoutApplyResultDialog("テスト適用結果", result) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    // ── 空領域クリックは無効化（特に何もしない） ──────
    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }
    private void OuterArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { }

    // ── ナビゲーション・保存 ───────────────────────
    /// <summary>パンくず「ウィンドウレイアウト」クリックで一覧画面に戻る（編集破棄）。</summary>
    private void LayoutCrumb_Click(object sender, MouseButtonEventArgs e) => NavigateBackToList();

    /// <summary>キャンセルボタンで一覧画面に戻る（編集破棄）。</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e) => NavigateBackToList();

    private void NavigateBackToList()
    {
        _vm.EditingWindowLayoutId        = null;
        _vm.EditingWindowLayoutPatternId = null;
        _vm.NavigateToCommand.Execute("WindowLayout");
    }

    /// <summary>現在のゾーンスナップから WindowEntry リスト（アプリ未設定は除外）を構築する。</summary>
    private List<WindowEntry> BuildEntries() => _snaps
        .Where(s => !string.IsNullOrEmpty(s.ExePath))
        .Select(s => new WindowEntry
        {
            Title     = s.DisplayTitle,
            ExePath   = s.ExePath!,
            ClassName = "",
            X         = (int)Math.Round(Canvas.GetLeft(s.Container)),
            Y         = (int)Math.Round(Canvas.GetTop(s.Container)),
            Width     = (int)Math.Round(s.Container.Width),
            Height    = (int)Math.Round(s.Container.Height),
            ShowState = Win32Window.SW_SHOWNORMAL,
            ZoneIndex = s.ZoneIndex
        }).ToList();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            AppDialog.ShowWarning("レイアウト名を入力してください", "入力エラー", Window.GetWindow(this));
            TxtName.Focus();
            return;
        }
        if (string.IsNullOrEmpty(_patternId))
        {
            AppDialog.ShowWarning("レイアウトパターンを選択してください", "入力エラー", Window.GetWindow(this));
            return;
        }

        var entries = BuildEntries();

        if (_editingId != null)
        {
            var existing = _vm.WindowLayoutService.All.FirstOrDefault(l => l.Id == _editingId);
            if (existing != null)
            {
                existing.Name           = TxtName.Text.Trim();
                existing.Description    = TxtDescription.Text.Trim();
                existing.PatternId      = _patternId!;
                existing.Windows        = entries;
                existing.MinimizeOthers = _minimizeOthers;
                _vm.WindowLayoutService.Update(existing);
            }
        }
        else
        {
            var created = _vm.WindowLayoutService.Create(
                TxtName.Text.Trim(), TxtDescription.Text.Trim(), entries);
            created.PatternId      = _patternId!;
            created.MinimizeOthers = _minimizeOthers;
            _vm.WindowLayoutService.Update(created);
        }

        NavigateBackToList();
    }

    // ── 最小化トグル ───────────────────────
    private void ToggleMinimize_Click(object sender, MouseButtonEventArgs e)
    {
        _minimizeOthers = !_minimizeOthers;
        UpdateMinimizeToggleVisual();
    }

    private void UpdateMinimizeToggleVisual()
    {
        MinimizeToggleSwitch.Background = new SolidColorBrush(_minimizeOthers
            ? Color.FromRgb(0x23, 0x83, 0xE2)
            : Color.FromRgb(0x50, 0x50, 0x50));
        MinimizeToggleThumb.Margin = new Thickness(_minimizeOthers ? 22 : 2, 0, 0, 0);
    }

    // ── 起動中アプリ一覧（リアルタイム差分更新） ────────
    private void StartAppsRefreshTimer()
    {
        _appsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _appsRefreshTimer.Tick += (_, _) => RefreshRunningAppsList();
        _appsRefreshTimer.Start();
    }

    private void RefreshRunningAppsList()
    {
        var current = Win32Window.EnumerateVisibleWindows()
            .Where(w => !string.IsNullOrEmpty(w.ExePath))
            .GroupBy(w => w.ExePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var keepExisting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = RunningAppsList.Items.Count - 1; i >= 0; i--)
        {
            if (RunningAppsList.Items[i] is ListBoxItem item && item.Tag is string path)
            {
                if (!current.ContainsKey(path)) RunningAppsList.Items.RemoveAt(i);
                else keepExisting.Add(path);
            }
        }
        foreach (var kvp in current)
        {
            if (keepExisting.Contains(kvp.Key)) continue;
            RunningAppsList.Items.Add(BuildAppRow(kvp.Value.ExePath, kvp.Value.Title));
        }
    }

    private ListBoxItem BuildAppRow(string exePath, string title)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = AppIconHelper.TryGetExeIcon(exePath);
        if (icon != null)
            sp.Children.Add(new Image
            {
                Width = 22, Height = 22, Source = icon,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textPanel.Children.Add(new TextBlock
        {
            Text = Path.GetFileName(exePath), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(title))
            textPanel.Children.Add(new TextBlock
            {
                Text = title, FontSize = 10,
                Foreground = (Brush)FindResource("TextDimBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240
            });
        sp.Children.Add(textPanel);
        return new ListBoxItem
        {
            Tag = exePath, Content = sp,
            Padding = new Thickness(6, 5, 6, 5),
            Cursor = Cursors.Hand,
            ToolTip = $"{exePath}\n(ドラッグしてゾーンにドロップ)"
        };
    }

    private void RunningAppsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _appDragStartPoint = e.GetPosition(null);
    }

    private void RunningAppsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(null) - _appDragStartPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var src = e.OriginalSource as DependencyObject;
        ListBoxItem? item = null;
        while (src != null && item == null)
        {
            if (src is ListBoxItem li) item = li;
            else src = VisualTreeHelper.GetParent(src);
        }
        if (item?.Tag is not string exePath) return;

        DragDrop.DoDragDrop(item, new DataObject("ExePath", exePath), DragDropEffects.Copy);
    }
}

/// <summary>スナップの4辺を識別する列挙体（隣接境界の同期リサイズ用）。</summary>
internal enum EdgeKind { Left, Right, Top, Bottom }

// ════════════════════════════════════════════════════
//  仮想デスクトップ上の1つのスナップ矩形
// ════════════════════════════════════════════════════
/// <summary>仮想デスクトップ上の1スナップ矩形。ドラッグ移動・四隅リサイズ・アプリ選択ボタンを内包する。</summary>
internal class SnapRect
{
    /// <summary>スナップの最小ピクセルサイズ。</summary>
    public const double MIN_SIZE = 120;

    /// <summary>Canvas に追加する見た目のコンテナ。</summary>
    public Border Container { get; }
    /// <summary>選択された実行ファイルのフルパス。未選択時は null。</summary>
    public string? ExePath { get; private set; }
    /// <summary>表示用タイトル（exe ファイル名から自動設定）。</summary>
    public string DisplayTitle { get; private set; } = "";

    /// <summary>スナップ本体がクリックされたとき発火する選択通知イベント。</summary>
    public event EventHandler? Selected;
    /// <summary>中央のアプリボタンがクリックされたとき発火するイベント。</summary>
    public event EventHandler? PickAppRequested;
    /// <summary>アプリが設定された（変更された）とき発火するイベント。</summary>
    public event EventHandler? AppAssigned;
    /// <summary>辺（中央のエッジハンドル）がドラッグされたとき発火するイベント。</summary>
    public event Action<SnapRect, EdgeKind, double>? EdgeDrag;

    private readonly Canvas    _parent;
    private readonly Grid      _content;
    private readonly Button    _appButton;
    private readonly TextBlock _placeholderText;
    private readonly Image     _appIcon;
    private readonly List<Thumb> _handles = new();

    // ドラッグ移動状態
    private bool   _isDragging;
    private Point  _dragStart;
    private double _origX, _origY;

    // Aero Snap 風のゴーストプレビュー
    private Border? _ghost;
    private (double X, double Y, double W, double H)? _ghostTarget;

    private bool _isSelected;
    /// <summary>選択中の見た目（縁色強調・リサイズハンドル可視）に切り替える。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            Container.BorderBrush     = value
                ? new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            Container.BorderThickness = new Thickness(value ? 2.5 : 1);
            foreach (var h in _handles)
                h.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>パターン内のゾーン番号（0始まり、パターン由来でない場合は -1）。</summary>
    public int ZoneIndex { get; set; } = -1;

    private readonly bool _isLocked;

    /// <summary>
    /// 指定位置・サイズでスナップを構築する。
    /// isLocked=true ならドラッグ移動・リサイズハンドル・ゴーストは無効化され、
    /// アプリ選択ボタンとドロップ受付のみが有効になる（パターン由来の固定ゾーン用）。
    /// </summary>
    public SnapRect(Canvas parent, double x, double y, double w, double h, bool isLocked = false)
    {
        _parent   = parent;
        _isLocked = isLocked;
        Container = new Border
        {
            Width       = Math.Max(MIN_SIZE, w),
            Height      = Math.Max(MIN_SIZE, h),
            Background  = new SolidColorBrush(Color.FromArgb(0x66, 0xC8, 0xC8, 0xC8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            BorderThickness = new Thickness(1),
            Cursor      = isLocked ? Cursors.Arrow : Cursors.SizeAll,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(Container, x);
        Canvas.SetTop(Container, y);

        _content = new Grid();
        Container.Child = _content;

        // 中央アプリ選択ボタン
        _placeholderText = new TextBlock
        {
            Text       = "＋",
            FontSize   = 32,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        _appIcon = new Image
        {
            Width = 48, Height = 48, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        _appButton = new Button
        {
            Width  = 72, Height = 72,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x18, 0x1F, 0x2E)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            ToolTip = "アプリを選択",
            Content = _placeholderText
        };
        _appButton.Click += (_, _) => PickAppRequested?.Invoke(this, EventArgs.Empty);
        _content.Children.Add(_appButton);

        if (!_isLocked)
        {
            // 四隅のリサイズハンドル
            AddHandle(HorizontalAlignment.Left,  VerticalAlignment.Top,    Cursors.SizeNWSE, -1, -1);
            AddHandle(HorizontalAlignment.Right, VerticalAlignment.Top,    Cursors.SizeNESW,  1, -1);
            AddHandle(HorizontalAlignment.Left,  VerticalAlignment.Bottom, Cursors.SizeNESW, -1,  1);
            AddHandle(HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE,  1,  1);

            // 4辺中央のエッジハンドル（隣接境界の同期リサイズ用）
            AddEdgeHandle(EdgeKind.Top,    HorizontalAlignment.Center, VerticalAlignment.Top,    Cursors.SizeNS);
            AddEdgeHandle(EdgeKind.Bottom, HorizontalAlignment.Center, VerticalAlignment.Bottom, Cursors.SizeNS);
            AddEdgeHandle(EdgeKind.Left,   HorizontalAlignment.Left,   VerticalAlignment.Center, Cursors.SizeWE);
            AddEdgeHandle(EdgeKind.Right,  HorizontalAlignment.Right,  VerticalAlignment.Center, Cursors.SizeWE);

            // 移動ドラッグ（バブリング: ボタン・ハンドルが消費した後の空き領域でのみ発火）
            Container.MouseLeftButtonDown += OnDragStart;
            Container.MouseMove           += OnDragging;
            Container.MouseLeftButtonUp   += OnDragEnd;
        }

        // 選択（トンネリング: 子要素のクリックでも発火する）
        Container.PreviewMouseLeftButtonDown += (_, _) => Selected?.Invoke(this, EventArgs.Empty);

        // 起動中アプリ一覧からのドロップ受付
        Container.AllowDrop = true;
        Container.Drop += (_, e) =>
        {
            if (!e.Data.GetDataPresent("ExePath")) return;
            var path = e.Data.GetData("ExePath") as string;
            if (string.IsNullOrEmpty(path)) return;
            SetExePath(path, Path.GetFileNameWithoutExtension(path));
            e.Handled = true;
        };

        IsSelected = false;
    }

    /// <summary>指定実行ファイルをこのスナップに割り当て、中央ボタンにアイコンを表示する。</summary>
    public void SetExePath(string exePath, string displayName)
    {
        ExePath      = exePath;
        DisplayTitle = displayName;

        var icon = AppIconHelper.TryGetExeIcon(exePath);
        if (icon != null)
        {
            _appIcon.Source  = icon;
            _appButton.Content = _appIcon;
        }
        else
        {
            _placeholderText.Text     = Path.GetFileNameWithoutExtension(exePath);
            _placeholderText.FontSize = 12;
            _appButton.Content        = _placeholderText;
        }
        _appButton.ToolTip = $"{Path.GetFileName(exePath)}（クリックで変更）";
        AppAssigned?.Invoke(this, EventArgs.Empty);
    }

    // ── 移動ドラッグ ───────────────────────────────
    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        _isDragging = true;
        _dragStart  = e.GetPosition(_parent);
        _origX      = Canvas.GetLeft(Container);
        _origY      = Canvas.GetTop(Container);
        Container.CaptureMouse();
        e.Handled = true;
    }

    private void OnDragging(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var p = e.GetPosition(_parent);
        double nx = _origX + (p.X - _dragStart.X);
        double ny = _origY + (p.Y - _dragStart.Y);
        nx = Math.Clamp(nx, 0, Math.Max(0, _parent.Width  - Container.Width));
        ny = Math.Clamp(ny, 0, Math.Max(0, _parent.Height - Container.Height));
        Canvas.SetLeft(Container, nx);
        Canvas.SetTop(Container,  ny);
        UpdateGhost();
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        Container.ReleaseMouseCapture();
        ApplyGhostIfAny();
    }

    // ── Aero Snap 風ゴースト ────────────────────────
    /// <summary>
    /// スナップ位置と画面端の接触状況から自動想定枠の配置を決定し、該当する場合はゴーストを表示。
    /// 接触組合せ別の想定枠:
    ///   上+左 → 左上1/4   上+右 → 右上1/4
    ///   下+左 → 左下1/4   下+右 → 右下1/4
    ///   上のみ → 上半分   下のみ → 下半分
    ///   左のみ → 左半分   右のみ → 右半分
    /// </summary>
    private void UpdateGhost()
    {
        const double TOL = 0.5;

        double x  = Canvas.GetLeft(Container);
        double y  = Canvas.GetTop(Container);
        double w  = Container.Width;
        double h  = Container.Height;
        double cw = _parent.Width;
        double ch = _parent.Height;

        bool atLeft   = x <= TOL;
        bool atRight  = (x + w) >= (cw - TOL);
        bool atTop    = y <= TOL;
        bool atBottom = (y + h) >= (ch - TOL);

        double tx, ty, tw, th;
        if      (atTop    && atLeft)  { tx = 0;      ty = 0;      tw = cw / 2; th = ch / 2; }
        else if (atTop    && atRight) { tx = cw / 2; ty = 0;      tw = cw / 2; th = ch / 2; }
        else if (atBottom && atLeft)  { tx = 0;      ty = ch / 2; tw = cw / 2; th = ch / 2; }
        else if (atBottom && atRight) { tx = cw / 2; ty = ch / 2; tw = cw / 2; th = ch / 2; }
        else if (atTop)               { tx = 0;      ty = 0;      tw = cw;     th = ch / 2; }
        else if (atBottom)            { tx = 0;      ty = ch / 2; tw = cw;     th = ch / 2; }
        else if (atLeft)              { tx = 0;      ty = 0;      tw = cw / 2; th = ch;     }
        else if (atRight)             { tx = cw / 2; ty = 0;      tw = cw / 2; th = ch;     }
        else { HideGhost(); return; }

        EnsureGhost();
        Canvas.SetLeft(_ghost!, tx);
        Canvas.SetTop(_ghost!,  ty);
        _ghost!.Width  = tw;
        _ghost!.Height = th;
        _ghostTarget = (tx, ty, tw, th);
    }

    /// <summary>Win11 スナップレイアウト風のゴースト Border をキャンバス最背面に作成する。</summary>
    private void EnsureGhost()
    {
        if (_ghost != null) return;

        _ghost = new Border
        {
            Background = new LinearGradientBrush(
                Color.FromArgb(0x96, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x6E, 0xD8, 0xD8, 0xD8),
                angle: 90),
            BorderThickness = new Thickness(0),
            CornerRadius    = new CornerRadius(24),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius  = 60,
                ShadowDepth = 0,
                Color       = Color.FromRgb(0xE0, 0xE0, 0xE0),
                Opacity     = 0.55
            },
            IsHitTestVisible    = false,
            SnapsToDevicePixels = true
        };
        _parent.Children.Insert(0, _ghost);
    }

    /// <summary>表示中のゴーストを削除して状態をクリアする。</summary>
    private void HideGhost()
    {
        if (_ghost != null)
        {
            _parent.Children.Remove(_ghost);
            _ghost = null;
        }
        _ghostTarget = null;
    }

    /// <summary>ゴーストが表示されていれば、スナップ位置・サイズをゴースト枠に揃えてから消す。</summary>
    private void ApplyGhostIfAny()
    {
        if (_ghostTarget is { } t)
        {
            Canvas.SetLeft(Container, t.X);
            Canvas.SetTop(Container,  t.Y);
            Container.Width  = t.W;
            Container.Height = t.H;
        }
        HideGhost();
    }

    /// <summary>4辺中央のエッジハンドル（細い Thumb）を追加して EdgeDrag イベントを発火させる。</summary>
    private void AddEdgeHandle(EdgeKind kind, HorizontalAlignment ha, VerticalAlignment va, Cursor cursor)
    {
        bool horizontal = kind == EdgeKind.Top || kind == EdgeKind.Bottom;
        var thumb = new Thumb
        {
            Width  = horizontal ? 56 : 8,
            Height = horizontal ? 8  : 56,
            HorizontalAlignment = ha, VerticalAlignment = va,
            Margin = horizontal ? new Thickness(0, -4, 0, -4) : new Thickness(-4, 0, -4, 0),
            Cursor = cursor,
            Visibility = Visibility.Collapsed,
            Template = BuildHandleTemplate()
        };
        thumb.DragDelta += (_, e) =>
        {
            double delta = horizontal ? e.VerticalChange : e.HorizontalChange;
            EdgeDrag?.Invoke(this, kind, delta);
        };
        _content.Children.Add(thumb);
        _handles.Add(thumb);
    }

    // ── リサイズハンドル ─────────────────────────────
    /// <summary>四隅のいずれかにリサイズ用ハンドル（Thumb）を追加する。</summary>
    private void AddHandle(HorizontalAlignment ha, VerticalAlignment va, Cursor cursor, int signX, int signY)
    {
        var thumb = new Thumb
        {
            Width  = 16, Height = 16,
            HorizontalAlignment = ha, VerticalAlignment = va,
            Margin = new Thickness(-8),
            Cursor = cursor,
            Visibility = Visibility.Collapsed,
            Template = BuildHandleTemplate()
        };
        thumb.DragDelta += (_, e) => OnResize(e.HorizontalChange, e.VerticalChange, signX, signY);
        _content.Children.Add(thumb);
        _handles.Add(thumb);
    }

    private void OnResize(double dx, double dy, int signX, int signY)
    {
        double curX = Canvas.GetLeft(Container);
        double curY = Canvas.GetTop(Container);
        double curW = Container.Width;
        double curH = Container.Height;

        if (signX < 0)
        {
            double newW = Math.Max(MIN_SIZE, curW - dx);
            double newX = curX + (curW - newW);
            newX = Math.Max(0, newX);
            Canvas.SetLeft(Container, newX);
            Container.Width = Math.Min(newW, _parent.Width - newX);
        }
        else
        {
            double newW = Math.Max(MIN_SIZE, curW + dx);
            Container.Width = Math.Min(newW, Math.Max(MIN_SIZE, _parent.Width - curX));
        }

        if (signY < 0)
        {
            double newH = Math.Max(MIN_SIZE, curH - dy);
            double newY = curY + (curH - newH);
            newY = Math.Max(0, newY);
            Canvas.SetTop(Container, newY);
            Container.Height = Math.Min(newH, _parent.Height - newY);
        }
        else
        {
            double newH = Math.Max(MIN_SIZE, curH + dy);
            Container.Height = Math.Min(newH, Math.Max(MIN_SIZE, _parent.Height - curY));
        }
    }

    /// <summary>リサイズハンドルの見た目（青い四角）を返す。</summary>
    private static ControlTemplate BuildHandleTemplate()
    {
        var t = new ControlTemplate(typeof(Thumb));
        var f = new FrameworkElementFactory(typeof(Border));
        f.SetValue(Border.BackgroundProperty, (Brush)new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)));
        f.SetValue(Border.BorderBrushProperty, Brushes.White);
        f.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
        t.VisualTree = f;
        return t;
    }
}

/// <summary>ビジュアルツリー祖先判定のためのヘルパー拡張。</summary>
internal static class FrameworkElementExtensions
{
    /// <summary>this が指定要素の子孫（または本人）かどうかを返す。</summary>
    public static bool IsDescendantOf(this DependencyObject? self, DependencyObject ancestor)
    {
        var d = self;
        while (d != null)
        {
            if (ReferenceEquals(d, ancestor)) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }
}
