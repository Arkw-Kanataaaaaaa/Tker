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
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>仮想デスクトップ上にスナップを配置してウィンドウレイアウトを新規作成・編集するページ。</summary>
public partial class WindowLayoutEditPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;

    private string?        _editingId;
    private readonly List<SnapRect> _snaps = new();
    private SnapRect?      _selected;

    /// <summary>ViewModel を受け取り初期化する。</summary>
    public WindowLayoutEditPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>ナビゲーション直後の再表示用フック（編集対象は OnLoaded で読み込む）。</summary>
    public void Refresh() { }

    /// <summary>キャンバスサイズを実画面の解像度に合わせ、編集モードならスナップを復元する。</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EditorCanvas.Width  = SystemParameters.PrimaryScreenWidth;
        EditorCanvas.Height = SystemParameters.PrimaryScreenHeight;

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
                foreach (var entry in existing.Windows)
                {
                    var snap = CreateSnap(entry.X, entry.Y, entry.Width, entry.Height);
                    if (!string.IsNullOrEmpty(entry.ExePath))
                        snap.SetExePath(entry.ExePath, entry.Title);
                }
                SetSelected(null);
            }
        }
        else
        {
            HeaderTitle.Text = "ウィンドウレイアウト新規作成";
        }
    }

    // ── スナップ生成・選択 ─────────────────────────────
    /// <summary>仮想デスクトップ上に新しいスナップを生成して登録する。</summary>
    private SnapRect CreateSnap(double x, double y, double w, double h)
    {
        var snap = new SnapRect(EditorCanvas, x, y, w, h);
        snap.Selected         += (_, _) => SetSelected(snap);
        snap.PickAppRequested += (_, _) => OnPickApp(snap);
        EditorCanvas.Children.Add(snap.Container);
        _snaps.Add(snap);
        return snap;
    }

    /// <summary>選択中スナップを切り替え、リサイズハンドルとツールバーボタンの状態を更新する。</summary>
    private void SetSelected(SnapRect? snap)
    {
        if (_selected != null) _selected.IsSelected = false;
        _selected = snap;
        if (_selected != null) _selected.IsSelected = true;
        BtnDeleteSnap.IsEnabled = _selected != null;
    }

    /// <summary>アプリ選択ダイアログを開いて、結果をスナップに反映する。</summary>
    private void OnPickApp(SnapRect snap)
    {
        var dlg = new AppPickerDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedExePath))
        {
            var displayName = Path.GetFileNameWithoutExtension(dlg.SelectedExePath);
            snap.SetExePath(dlg.SelectedExePath, displayName);
        }
    }

    // ── ツールバー ────────────────────────────────────
    private void AddSnap_Click(object sender, RoutedEventArgs e)
    {
        double w = Math.Max(SnapRect.MIN_SIZE, EditorCanvas.Width  / 3);
        double h = Math.Max(SnapRect.MIN_SIZE, EditorCanvas.Height / 3);
        var snap = CreateSnap(0, 0, w, h);
        SetSelected(snap);
    }

    private void DeleteSnap_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        EditorCanvas.Children.Remove(_selected.Container);
        _snaps.Remove(_selected);
        SetSelected(null);
    }

    // ── 空領域クリックで選択解除 ────────────────────────
    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, EditorCanvas))
            SetSelected(null);
    }

    private void OuterArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 仮想デスクトップ外（背景）クリックでも選択解除
        if (!(e.OriginalSource is FrameworkElement fe && fe.IsDescendantOf(EditorCanvas)))
            SetSelected(null);
    }

    // ── 保存・キャンセル ─────────────────────────────
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _vm.EditingWindowLayoutId = null;
        _vm.NavigateToCommand.Execute("WindowLayout");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            MessageBox.Show(Window.GetWindow(this), "名前を入力してください。",
                "入力チェック", MessageBoxButton.OK, MessageBoxImage.Information);
            TxtName.Focus();
            return;
        }

        // アプリが未設定のスナップはスキップ
        var entries = _snaps
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

        if (_editingId != null)
        {
            var existing = _vm.WindowLayoutService.All.FirstOrDefault(l => l.Id == _editingId);
            if (existing != null)
            {
                existing.Name        = TxtName.Text.Trim();
                existing.Description = TxtDescription.Text.Trim();
                existing.Windows     = entries;
                _vm.WindowLayoutService.Update(existing);
            }
        }
        else
        {
            _vm.WindowLayoutService.Create(TxtName.Text.Trim(), TxtDescription.Text.Trim(), entries);
        }

        _vm.EditingWindowLayoutId = null;
        _vm.NavigateToCommand.Execute("WindowLayout");
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

        IsSelected = false; // ハンドルを非表示初期化
    }

    /// <summary>指定実行ファイルをこのスナップに割り当て、中央ボタンにアイコンを表示する。</summary>
    public void SetExePath(string exePath, string displayName)
    {
        ExePath      = exePath;
        DisplayTitle = displayName;

        var icon = AppPickerDialog.TryGetExeIcon(exePath);
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
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        Container.ReleaseMouseCapture();
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
