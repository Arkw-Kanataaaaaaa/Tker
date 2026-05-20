using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TKer.Models;
using TKer.Services;

namespace TKer.Views.Widgets;

/// <summary>
/// 画面上を自由に移動できる「栞」型ウィジェットランチャー。
///
/// ─ 移動方法 ─────────────────────────────────────────
///   タブ最上部のドラッグハンドル（・・・）を左クリックしたままドラッグ
///
/// ─ パネル開閉 ──────────────────────────────────────
///   ◀ ボタン、または各ツールアイコンをクリック
///   タブ背景（ボタン以外の空き領域）をクリックしても開閉できる
///
/// ─ 設計メモ（Left 調整）────────────────────────────
///   パネルは常にタブの LEFT に展開する。
///   パネルを開くとき  → Left -= PanelWidth  (タブの画面座標を維持)
///   パネルを閉じるとき → Left += PanelWidth  (タブの画面座標を維持)
///   保存する値は「タブの Left 画面座標」= パネル閉時の Window.Left
/// </summary>
public partial class BookmarkWidget : Window
{
    // ── 定数 ──────────────────────────────────────────
    private const double PanelWidth = 280.0;
    private const double TabWidth   = 52.0;

    // ── 依存 ──────────────────────────────────────────
    private readonly WidgetServiceProvider _svc;
    private readonly TodoService           _todoSvc;

    // ── 状態 ──────────────────────────────────────────
    private bool _forceClose   = false;
    private bool _isPanelOpen  = false;
    private bool _suppressSave = false;

    // ── コンストラクタ ────────────────────────────────
    public BookmarkWidget(WidgetServiceProvider svc)
    {
        _svc     = svc;
        _todoSvc = svc.TodoService;

        InitializeComponent();
        RestorePosition();

        Loaded += (_, _) =>
        {
            // 初回表示時にパネルコンテンツを準備
            if (_isPanelOpen) BuildPanelContent();
        };
    }

    // ────────────────────────────────────────────────
    // ドラッグハンドル（タブ最上部 ・・・ の帯）
    // ────────────────────────────────────────────────
    private void DragHandle_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        e.Handled = true; // TabBorder.Tab_MouseLeftButtonDown への伝播を止める

        DragMove(); // ← ユーザーがマウスを放すまでブロック

        // DragMove 完了 → 新しい位置を保存
        SavePosition();
    }

    // ────────────────────────────────────────────────
    // タブ背景クリック（ボタン以外の空き領域）→ パネル開閉
    // ────────────────────────────────────────────────
    private void Tab_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // ドラッグハンドル・Button 子要素からのバブルアップは無視する
        // （DragHandle は e.Handled = true で止めているのでここには来ない）
        var src = e.OriginalSource as DependencyObject;
        while (src != null && !ReferenceEquals(src, TabBorder))
        {
            if (src is Button) return; // ボタンクリックはそれぞれのハンドラに任せる
            src = VisualTreeHelper.GetParent(src);
        }

        TogglePanel();
    }

    // ────────────────────────────────────────────────
    // パネル開閉
    // ────────────────────────────────────────────────
    private void TogglePanel()
    {
        if (_isPanelOpen) ClosePanel();
        else              OpenPanel();
    }

    private void OpenPanel()
    {
        if (_isPanelOpen) return;

        // ── タブの画面座標を固定しながらウィンドウを左に拡張 ──
        //    現在 Width=52 → Left がタブの画面Left と同じ
        //    Width=332 にすると列0(パネル)が左に出るため Left を -280 補正
        double tabScreenLeft = Left; // 閉じているとき Left = タブ画面Left
        double newLeft = tabScreenLeft - PanelWidth;

        // 画面外に出ないようクランプ（最低でも 0）
        newLeft = Math.Max(SystemParameters.WorkArea.Left, newLeft);

        _isPanelOpen = true;
        Left  = newLeft;
        Width = PanelWidth + TabWidth;

        ExpandPanel.Visibility = Visibility.Visible;
        TxtToggle.Text = "▶"; // 現在 OPEN → クリックで閉じる
        BuildPanelContent();
    }

    private void ClosePanel()
    {
        if (!_isPanelOpen) return;

        // タブの現在の画面 Left = Left(window) + PanelWidth(column0幅)
        double tabScreenLeft = Left + PanelWidth;

        _isPanelOpen = false;
        Left  = tabScreenLeft; // まず位置を確定
        Width = TabWidth;      // 次にリサイズ（左側が縮む）

        ExpandPanel.Visibility = Visibility.Collapsed;
        TxtToggle.Text = "◀"; // 現在 CLOSED → クリックで開く
    }

    private void BtnClosePanel_Click(object s, RoutedEventArgs e) => ClosePanel();

    // ────────────────────────────────────────────────
    // 明示的なトグルボタン（◀ / ▶）
    // ────────────────────────────────────────────────
    private void BtnToggle_Click(object s, RoutedEventArgs e) => TogglePanel();

    // ────────────────────────────────────────────────
    // ツールアイコンボタン
    // ────────────────────────────────────────────────
    private void BtnTodo_Click(object s, RoutedEventArgs e)
    {
        if (!_isPanelOpen) OpenPanel();
        else               BuildPanelContent();
        ShowTodoContent();
    }

    private void BtnNote_Click(object s, RoutedEventArgs e)
    {
        if (!_isPanelOpen) OpenPanel();
        ShowNotePanel();
    }

    private void BtnPomodoro_Click(object s, RoutedEventArgs e)
        => NavigateMainWindow("Pomodoro");

    private void BtnTask_Click(object s, RoutedEventArgs e)
    {
        if (!_isPanelOpen) OpenPanel();
        else               BuildPanelContent();
        ShowTaskContent();
    }

    private void BtnCalendar_Click(object s, RoutedEventArgs e)
        => NavigateMainWindow("Calendar");

    private void BtnCloseWidget_Click(object s, RoutedEventArgs e)
    {
        ClosePanel();
        SaveHidden();
        Hide();
    }

    // ────────────────────────────────────────────────
    // パネルコンテンツ構築
    // ────────────────────────────────────────────────
    private void BuildPanelContent()
    {
        if (PanelContent == null) return;
        PanelContent.Children.Clear();

        // ── 進行中タスク ──
        PanelContent.Children.Add(MakeSectionHeader("📌 進行中タスク"));

        var tasks = _svc.ProjectService.CurrentProject?.Tasks
            .Where(t => t.Status == "対応中" || t.Status == "レビュー中")
            .OrderByDescending(t => t.PlannedEndDate ?? DateTime.MaxValue)
            .Take(5)
            .ToList();

        if (tasks?.Count > 0)
            foreach (var t in tasks)
                PanelContent.Children.Add(BuildTaskChip(t));
        else
            PanelContent.Children.Add(MakeDimText("現在進行中のタスクはありません"));

        // ── 未完了 TODO ──
        PanelContent.Children.Add(MakeSectionHeader("📋 TODO（未完了）"));
        var todos = _todoSvc.GetAll().Where(t => !t.IsCompleted).Take(5).ToList();
        if (todos.Count > 0)
            foreach (var td in todos)
                PanelContent.Children.Add(BuildTodoChip(td));
        else
            PanelContent.Children.Add(MakeDimText("TODOはありません"));

        // ── クイックTODO追加 ──
        PanelContent.Children.Add(BuildQuickTodoForm());
    }

    private void ShowTodoContent()
    {
        // BuildPanelContent と同じコンテンツ（TODO追加フォームまで表示）
        BuildPanelContent();
    }

    private void ShowTaskContent()
    {
        BuildPanelContent();
    }

    private void ShowNotePanel()
    {
        if (PanelContent == null) return;
        PanelContent.Children.Clear();
        PanelContent.Children.Add(MakeSectionHeader("📝 クイックメモ"));

        var tb = new TextBox
        {
            MinHeight   = 120, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            Background  = new SolidColorBrush(Color.FromRgb(0x25, 0x2C, 0x3F)),
            Foreground  = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x45, 0x60)),
            BorderThickness = new Thickness(1), Padding = new Thickness(8),
            FontSize    = 12, CaretBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
            Margin      = new Thickness(0, 0, 0, 8)
        };
        PanelContent.Children.Add(tb);

        var btnSave = new Button
        {
            Content = "TODO として保存", FontSize = 11, Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right
        };
        btnSave.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(tb.Text))
            {
                _todoSvc.Add(tb.Text.Trim());
                tb.Clear();
                BuildPanelContent();
            }
        };
        PanelContent.Children.Add(btnSave);
        tb.Focus();
    }

    // ────────────────────────────────────────────────
    // UI 部品ビルダー
    // ────────────────────────────────────────────────
    private static TextBlock MakeSectionHeader(string text) => new()
    {
        Text = text, FontSize = 11, FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xB5)),
        Margin = new Thickness(0, 8, 0, 4)
    };

    private static TextBlock MakeDimText(string text) => new()
    {
        Text = text, FontSize = 11,
        Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x70, 0x85)),
        Margin = new Thickness(0, 0, 0, 4)
    };

    private Border BuildTaskChip(TaskItem task)
    {
        var isOverdue = task.IsOverdue;
        var color     = isOverdue
            ? Color.FromRgb(0xF4, 0x43, 0x36)
            : Color.FromRgb(0x3D, 0x7E, 0xFF);

        var border = new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(0x60, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(5),
            Padding         = new Thickness(8, 5, 8, 5),
            Margin          = new Thickness(0, 0, 0, 4),
            Cursor          = Cursors.Hand
        };

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text         = task.Name, FontSize = 11,
            Foreground   = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
            TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = FontWeights.SemiBold
        });
        if (task.PlannedEndDate.HasValue)
            sp.Children.Add(new TextBlock
            {
                Text = $"期限: {task.PlannedEndDate.Value:M/d}",
                FontSize = 10, Foreground = new SolidColorBrush(isOverdue
                    ? Color.FromRgb(0xF4, 0x43, 0x36) : Color.FromRgb(0x9A, 0xA0, 0xB5))
            });
        border.Child = sp;
        border.MouseLeftButtonDown += (_, _) => NavigateMainWindow("TaskList");
        return border;
    }

    private Border BuildTodoChip(TodoItem todo)
    {
        Color accent;
        try { accent = (Color)ColorConverter.ConvertFromString(todo.Color); }
        catch { accent = Color.FromRgb(0x3D, 0x7E, 0xFF); }

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

        var chk = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        chk.Checked   += (_, _) => { _todoSvc.Toggle(todo.Id); BuildPanelContent(); };
        chk.Unchecked += (_, _) => { _todoSvc.Toggle(todo.Id); BuildPanelContent(); };
        DockPanel.SetDock(chk, Dock.Left);

        var tb = new TextBlock
        {
            Text         = todo.Title, FontSize = 11,
            Foreground   = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        row.Children.Add(chk);
        row.Children.Add(tb);

        return new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(0x15, accent.R, accent.G, accent.B)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(0x50, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(6, 4, 6, 4),
            Child           = row
        };
    }

    private Border BuildQuickTodoForm()
    {
        var container = new Border
        {
            Background   = new SolidColorBrush(Color.FromRgb(0x14, 0x19, 0x26)),
            CornerRadius = new CornerRadius(6),
            Margin       = new Thickness(0, 8, 0, 0),
            Padding      = new Thickness(8)
        };

        var sp = new StackPanel();
        sp.Children.Add(MakeSectionHeader("＋ クイックTODO追加"));

        var row   = new DockPanel();
        var tb    = new TextBox
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x25, 0x2C, 0x3F)),
            Foreground      = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x3A, 0x45, 0x60)),
            BorderThickness = new Thickness(1), Padding = new Thickness(6, 4, 6, 4),
            FontSize        = 11, CaretBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF))
        };
        var btnAdd = new Button
        {
            Content         = "追加", FontSize = 10, Width = 44, Margin = new Thickness(4, 0, 0, 0),
            Background      = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)),
            Foreground      = Brushes.White, BorderThickness = new Thickness(0),
            Cursor          = Cursors.Hand
        };
        Action addAction = () =>
        {
            if (!string.IsNullOrWhiteSpace(tb.Text))
            {
                _todoSvc.Add(tb.Text.Trim());
                tb.Clear();
                BuildPanelContent();
            }
        };
        btnAdd.Click += (_, _) => addAction();
        tb.KeyDown   += (_, e) => { if (e.Key == Key.Return) addAction(); };
        DockPanel.SetDock(btnAdd, Dock.Right);
        row.Children.Add(btnAdd);
        row.Children.Add(tb);
        sp.Children.Add(row);

        container.Child = sp;
        return container;
    }

    // ────────────────────────────────────────────────
    // ナビゲーション
    // ────────────────────────────────────────────────
    private void NavigateMainWindow(string view) => _svc.NavigateTo(view);

    // ────────────────────────────────────────────────
    // 位置管理
    // ────────────────────────────────────────────────

    /// <summary>
    /// 設定から位置を復元する。
    /// 未設定（初回起動）の場合は右端・縦 1/3 の位置をデフォルトにする。
    /// 必ずパネルが閉じた状態（Width=52）で呼ぶこと。
    /// </summary>
    private void RestorePosition()
    {
        _suppressSave = true;
        try
        {
            var screen = SystemParameters.WorkArea;
            var bs     = _svc.AppSettingsService.BookmarkWidgetSettings;

            double tabLeft, tabTop;

            if (double.IsNaN(bs.TabLeft) || double.IsNaN(bs.TabTop))
            {
                // 初回起動: 右端・縦 30% の位置
                tabLeft = screen.Right - TabWidth;
                tabTop  = screen.Top + screen.Height * 0.30;
            }
            else
            {
                // 保存済み位置を画面内にクランプ
                tabLeft = Math.Clamp(bs.TabLeft, screen.Left, screen.Right - TabWidth);
                tabTop  = Math.Clamp(bs.TabTop,  screen.Top,  screen.Bottom - 80);
            }

            // パネルが閉じているとき: Window.Left = タブ画面Left
            Left  = tabLeft;
            Top   = tabTop;
            Width = TabWidth; // 念のため閉じた状態に戻す
        }
        finally
        {
            _suppressSave = false;
        }
    }

    /// <summary>
    /// 現在のタブ画面座標（= 開閉状態に依存しない「タブの Left」）を設定に保存する。
    /// ドラッグ終了時に呼ばれる。
    /// </summary>
    private void SavePosition()
    {
        if (_suppressSave || !IsLoaded) return;

        // タブ画面 Left: パネル閉=Left、パネル開=Left+PanelWidth
        double tabScreenLeft = _isPanelOpen ? Left + PanelWidth : Left;

        var bs = _svc.AppSettingsService.BookmarkWidgetSettings;
        bs.TabLeft   = tabScreenLeft;
        bs.TabTop    = Top;
        bs.IsVisible = true;
        _svc.AppSettingsService.SaveBookmarkSettings(bs);
    }

    private void SaveHidden()
    {
        var bs = _svc.AppSettingsService.BookmarkWidgetSettings;
        bs.IsVisible = false;
        _svc.AppSettingsService.SaveBookmarkSettings(bs);
    }

    // ────────────────────────────────────────────────
    // ウィンドウイベント
    // ────────────────────────────────────────────────
    private void Widget_Closing(object s, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose) return;
        e.Cancel = true;
        ClosePanel();
        SaveHidden();
        Hide();
    }

    /// <summary>アプリ終了時の強制クローズ</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    /// <summary>ウィジェットを表示する（位置を復元してから Show）</summary>
    public void ShowWidget()
    {
        // 閉じた状態に戻してから位置を復元
        if (_isPanelOpen) ClosePanel();
        RestorePosition();

        var bs = _svc.AppSettingsService.BookmarkWidgetSettings;
        if (!bs.IsVisible)
        {
            bs.IsVisible = true;
            _svc.AppSettingsService.SaveBookmarkSettings(bs);
        }

        Show();
        Activate();
    }
}
