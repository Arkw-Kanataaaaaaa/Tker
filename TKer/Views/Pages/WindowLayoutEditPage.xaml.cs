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
using System.Windows.Threading;
using System.Text;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>仮想デスクトップ上にスナップを配置してウィンドウレイアウトを新規作成・編集するページ。</summary>
public partial class WindowLayoutEditPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;

    private string?        _editingId;
    private readonly List<SnapRect> _snaps        = new();
    private readonly List<SnapRect> _selectedList = new();

    // 「全ウィンドウ最小化後、レイアウト適用」トグルの状態
    private bool _minimizeOthers;

    // 起動中アプリ一覧のドラッグ開始判定・リアルタイム更新
    private Point _appDragStartPoint;
    private DispatcherTimer? _appsRefreshTimer;

    /// <summary>ViewModel を受け取り初期化する。</summary>
    public WindowLayoutEditPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>ナビゲーション直後の再表示用フック（編集対象は OnLoaded で読み込む）。</summary>
    public void Refresh() { }

    /// <summary>キャンバスサイズを実画面の解像度に合わせ、編集モードならスナップを復元する。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // DPI 補正
        var dpi  = VisualTreeHelper.GetDpi(this);
        double dpiX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double dpiY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;
        EditorCanvas.Width  = SystemParameters.PrimaryScreenWidth  * dpiX;
        EditorCanvas.Height = SystemParameters.PrimaryScreenHeight * dpiY;

        ApplyDesktopWallpaper();

        var editId = _vm.EditingWindowLayoutId;
        if (!string.IsNullOrEmpty(editId))
        {
            var existing = _vm.WindowLayoutService.All.FirstOrDefault(l => l.Id == editId);
            if (existing != null)
            {
                _editingId          = existing.Id;
                HeaderTitle.Text    = "ウィンドウレイアウト編集";
                TxtName.Text        = existing.Name;
                TxtDescription.Text = existing.Description;
                _minimizeOthers     = existing.MinimizeOthers;
                foreach (var entry in existing.Windows)
                {
                    var snap = CreateSnap(entry.X, entry.Y, entry.Width, entry.Height);
                    if (!string.IsNullOrEmpty(entry.ExePath))
                        snap.SetExePath(entry.ExePath, entry.Title);
                }
                ClearSelection();
            }
        }
        else
        {
            HeaderTitle.Text = "ウィンドウレイアウト追加";
        }

        UpdateMinimizeToggleVisual();
        UpdateTestApplyEnabled();
        RefreshRunningAppsList();
        StartAppsRefreshTimer();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _appsRefreshTimer?.Stop();

    /// <summary>仮想デスクトップ Border の背景に現在のデスクトップ壁紙を適用する（取得失敗時は素のダーク背景）。</summary>
    private void ApplyDesktopWallpaper()
    {
        var path = Win32Window.GetDesktopWallpaperPath();
        if (path == null) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource     = new Uri(path);
            bmp.CacheOption   = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            DesktopBg.Background = new ImageBrush { ImageSource = bmp, Stretch = Stretch.UniformToFill };
        }
        catch { /* 失敗時はデフォルト背景のまま */ }
    }

    // ── スナップ生成・選択 ─────────────────────────────
    /// <summary>仮想デスクトップ上に新しいスナップを生成して登録する。</summary>
    private SnapRect CreateSnap(double x, double y, double w, double h)
    {
        var snap = new SnapRect(EditorCanvas, x, y, w, h);
        snap.Selected         += (_, _) => OnSnapSelected(snap);
        snap.PickAppRequested += (_, _) => OnPickApp(snap);
        snap.AppAssigned      += (_, _) => UpdateTestApplyEnabled();
        EditorCanvas.Children.Add(snap.Container);
        _snaps.Add(snap);
        return snap;
    }

    /// <summary>アプリ設定済みのスナップが1つでもあればテスト適用ボタンを有効化する。</summary>
    private void UpdateTestApplyEnabled()
    {
        BtnTestApply.IsEnabled = _snaps.Any(s => !string.IsNullOrEmpty(s.ExePath));
    }

    /// <summary>
    /// スナップ選択イベントを処理する。Ctrl 押下中はトグル選択、押下なしは単一選択。
    /// 選択状態に応じて削除ツールボタンを有効化する。
    /// </summary>
    private void OnSnapSelected(SnapRect snap)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (ctrl)
        {
            if (_selectedList.Contains(snap))
            {
                snap.IsSelected = false;
                _selectedList.Remove(snap);
            }
            else
            {
                snap.IsSelected = true;
                _selectedList.Add(snap);
            }
        }
        else
        {
            foreach (var s in _selectedList) s.IsSelected = false;
            _selectedList.Clear();
            snap.IsSelected = true;
            _selectedList.Add(snap);
        }
        BtnDeleteSnap.IsEnabled = _selectedList.Count > 0;
    }

    /// <summary>全選択を解除して削除ツールボタンを無効化する。</summary>
    private void ClearSelection()
    {
        foreach (var s in _selectedList) s.IsSelected = false;
        _selectedList.Clear();
        BtnDeleteSnap.IsEnabled = false;
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

    // ── ツールバー ────────────────────────────────────
    private void AddSnap_Click(object sender, RoutedEventArgs e)
    {
        double w = Math.Max(SnapRect.MIN_SIZE, EditorCanvas.Width  / 3);
        double h = Math.Max(SnapRect.MIN_SIZE, EditorCanvas.Height / 3);
        var snap = CreateSnap(0, 0, w, h);
        // 単一選択
        foreach (var s in _selectedList) s.IsSelected = false;
        _selectedList.Clear();
        snap.IsSelected = true;
        _selectedList.Add(snap);
        BtnDeleteSnap.IsEnabled = true;
    }

    /// <summary>選択中のスナップをすべて削除する。</summary>
    private void DeleteSnap_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedList.Count == 0) return;
        foreach (var snap in _selectedList.ToList())
        {
            EditorCanvas.Children.Remove(snap.Container);
            _snaps.Remove(snap);
        }
        _selectedList.Clear();
        BtnDeleteSnap.IsEnabled = false;
        UpdateTestApplyEnabled();
    }

    /// <summary>配置されたスナップをすべて削除する（確認なし）。</summary>
    private void ResetSnaps_Click(object sender, RoutedEventArgs e)
    {
        foreach (var snap in _snaps.ToList())
            EditorCanvas.Children.Remove(snap.Container);
        _snaps.Clear();
        _selectedList.Clear();
        BtnDeleteSnap.IsEnabled = false;
        UpdateTestApplyEnabled();
    }

    /// <summary>現在のスナップ構成を保存せずに即座に適用してテストする。</summary>
    private void TestApply_Click(object sender, RoutedEventArgs e)
    {
        var entries = BuildEntries();
        if (entries.Count == 0) return; // ボタンが活性のときは entries が空でないことが保証される
        var tempLayout = new WindowLayout
        {
            Name           = "(テスト)",
            Windows        = entries,
            MinimizeOthers = _minimizeOthers
        };
        ApplyResult result;
        Mouse.OverrideCursor = Cursors.Wait;
        try     { result = _vm.WindowLayoutService.Apply(tempLayout); }
        finally { Mouse.OverrideCursor = null; }

        ShowApplyResultDialog(result, "テスト適用結果");
    }

    /// <summary>適用結果（成功・失敗一覧）を TKer 標準ダイアログで表示する。</summary>
    private void ShowApplyResultDialog(ApplyResult result, string title)
    {
        var sb = new StringBuilder();
        if (result.Succeeded.Count > 0)
        {
            sb.AppendLine($"✓ 成功 ({result.Succeeded.Count}件)");
            foreach (var name in result.Succeeded) sb.AppendLine($"   ・{name}");
        }
        if (result.Failed.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"✗ 失敗 ({result.Failed.Count}件)");
            foreach (var name in result.Failed) sb.AppendLine($"   ・{name}");
        }

        var owner = Window.GetWindow(this);
        if (result.Failed.Count == 0)
            AppDialog.ShowInfo(sb.ToString().TrimEnd(), title, owner);
        else
            AppDialog.ShowWarning(sb.ToString().TrimEnd(), title, owner);
    }

    // ── 空領域クリックで選択解除 ────────────────────────
    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, EditorCanvas)) ClearSelection();
    }

    private void OuterArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!(e.OriginalSource is FrameworkElement fe && fe.IsDescendantOf(EditorCanvas)))
            ClearSelection();
    }

    // ── ナビゲーション・保存 ───────────────────────
    /// <summary>パンくずリストの「ウィンドウレイアウト」クリックで一覧画面に戻る（編集破棄）。</summary>
    private void LayoutCrumb_Click(object sender, MouseButtonEventArgs e) => NavigateBackToList();

    /// <summary>キャンセルボタンで一覧画面に戻る（編集破棄）。</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e) => NavigateBackToList();

    private void NavigateBackToList()
    {
        _vm.EditingWindowLayoutId = null;
        _vm.NavigateToCommand.Execute("WindowLayout");
    }

    /// <summary>現在のスナップ群から WindowEntry リスト（アプリ未設定は除外）を構築する。</summary>
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
            ShowState = Win32Window.SW_SHOWNORMAL
        }).ToList();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show(Window.GetWindow(this), "名前を入力してください。",
                "入力チェック", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtName.Focus();
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
                existing.Windows        = entries;
                existing.MinimizeOthers = _minimizeOthers;
                _vm.WindowLayoutService.Update(existing);
            }
        }
        else
        {
            var created = _vm.WindowLayoutService.Create(
                TxtName.Text.Trim(), TxtDescription.Text.Trim(), entries);
            created.MinimizeOthers = _minimizeOthers;
            _vm.WindowLayoutService.Update(created);
        }

        NavigateBackToList();
    }

    // ── 設定トグル ───────────────────────
    private void ToggleMinimize_Click(object sender, MouseButtonEventArgs e)
    {
        _minimizeOthers = !_minimizeOthers;
        UpdateMinimizeToggleVisual();
    }

    /// <summary>トグルスイッチの色とつまみ位置を現在の状態に合わせて更新する。</summary>
    private void UpdateMinimizeToggleVisual()
    {
        MinimizeToggleSwitch.Background = new SolidColorBrush(_minimizeOthers
            ? Color.FromRgb(0x23, 0x83, 0xE2)
            : Color.FromRgb(0x50, 0x50, 0x50));
        MinimizeToggleThumb.Margin = new Thickness(_minimizeOthers ? 22 : 2, 0, 0, 0);
    }

    // ── ショートカット ─────────────────────────────
    /// <summary>Ctrl+Shift+; で追加、Ctrl+- で削除、Ctrl+R でリセットを実行する。</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // TextBox 入力中はショートカット無効
        if (Keyboard.FocusedElement is TextBoxBase) return;

        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   == ModifierKeys.Shift;

        if (ctrl && shift && e.Key == Key.OemSemicolon)
        {
            AddSnap_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.OemMinus)
        {
            DeleteSnap_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.R)
        {
            ResetSnaps_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    // ── 起動中アプリ一覧（リアルタイム差分更新） ────────────────────
    /// <summary>2秒周期で RunningAppsList を再取得・差分反映するタイマーを開始する。</summary>
    private void StartAppsRefreshTimer()
    {
        _appsRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _appsRefreshTimer.Tick += (_, _) => RefreshRunningAppsList();
        _appsRefreshTimer.Start();
    }

    /// <summary>
    /// 現在の可視ウィンドウから取得した exe 一覧と ListBox の項目を差分比較し、
    /// 消えたものを削除・新規のものを追加する（順序・選択・スクロール位置を保持）。
    /// </summary>
    private void RefreshRunningAppsList()
    {
        var current = Win32Window.EnumerateVisibleWindows()
            .Where(w => !string.IsNullOrEmpty(w.ExePath))
            .GroupBy(w => w.ExePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // 消えたものを削除
        var keepExisting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = RunningAppsList.Items.Count - 1; i >= 0; i--)
        {
            if (RunningAppsList.Items[i] is ListBoxItem item && item.Tag is string path)
            {
                if (!current.ContainsKey(path))
                    RunningAppsList.Items.RemoveAt(i);
                else
                    keepExisting.Add(path);
            }
        }

        // 新規追加
        foreach (var kvp in current)
        {
            if (keepExisting.Contains(kvp.Key)) continue;
            RunningAppsList.Items.Add(BuildAppRow(kvp.Value.ExePath, kvp.Value.Title));
        }
    }

    /// <summary>アプリ1件分のリスト行（アイコン+名前+タイトル）を構築して返す。</summary>
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
            Tag     = exePath,
            Content = sp,
            Padding = new Thickness(6, 5, 6, 5),
            Cursor  = Cursors.Hand,
            ToolTip = $"{exePath}\n(ドラッグしてスナップにドロップ)"
        };
    }

    /// <summary>ListBox 上でマウスダウン時の位置を保存（後の距離判定でドラッグ開始判定に使う）。</summary>
    private void RunningAppsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _appDragStartPoint = e.GetPosition(null);
    }

    /// <summary>ドラッグ閾値を超えたら DragDrop を開始してドラッグソースとなる。</summary>
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

    /// <summary>指定位置・サイズでスナップを構築する。</summary>
    public SnapRect(Canvas parent, double x, double y, double w, double h)
    {
        _parent = parent;
        Container = new Border
        {
            Width       = Math.Max(MIN_SIZE, w),
            Height      = Math.Max(MIN_SIZE, h),
            Background  = new SolidColorBrush(Color.FromArgb(0x66, 0xC8, 0xC8, 0xC8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            BorderThickness = new Thickness(1),
            Cursor      = Cursors.SizeAll,
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

        // 四隅のリサイズハンドル
        AddHandle(HorizontalAlignment.Left,  VerticalAlignment.Top,    Cursors.SizeNWSE, -1, -1);
        AddHandle(HorizontalAlignment.Right, VerticalAlignment.Top,    Cursors.SizeNESW,  1, -1);
        AddHandle(HorizontalAlignment.Left,  VerticalAlignment.Bottom, Cursors.SizeNESW, -1,  1);
        AddHandle(HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE,  1,  1);

        // 選択（トンネリング: 子要素のクリックでも発火する）
        Container.PreviewMouseLeftButtonDown += (_, _) => Selected?.Invoke(this, EventArgs.Empty);

        // 移動ドラッグ（バブリング: ボタン・ハンドルが消費した後の空き領域でのみ発火）
        Container.MouseLeftButtonDown += OnDragStart;
        Container.MouseMove           += OnDragging;
        Container.MouseLeftButtonUp   += OnDragEnd;

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
        var accent = Color.FromRgb(0x9A, 0x9A, 0x9A);

        _ghost = new Border
        {
            Background = new LinearGradientBrush(
                Color.FromArgb(0x96, 0xFF, 0xFF, 0xFF),
                Color.FromArgb(0x6E, 0xD8, 0xD8, 0xD8),
                angle: 90),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(0xCC, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(4),
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
