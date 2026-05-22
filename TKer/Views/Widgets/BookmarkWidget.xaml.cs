using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TKer.Models;
using TKer.Services;

namespace TKer.Views.Widgets;

/// <summary>画面上を自由にドラッグ移動できる栞型ウィジェットランチャー。タブとスライドパネルで構成される。</summary>
public partial class BookmarkWidget : Window
{
    // ── 定数 ──────────────────────────────────────────
    private const double PANEL_WIDTH = 280.0;
    private const double TAB_WIDTH   = 52.0;
    private static readonly TimeSpan SNAP_INTERVAL = TimeSpan.FromSeconds(30);

    // ── 依存 ──────────────────────────────────────────
    private readonly WidgetServiceProvider _svc;
    private readonly TodoService           _todoSvc;

    // ── 状態 ──────────────────────────────────────────
    private bool _forceClose   = false;
    private bool _isPanelOpen  = false;
    private bool _suppressSave = false;
    private bool _isPinned     = false;
    private bool _isDragging   = false;

    // 端への自動スナップ用タイマー（30 秒間隔）
    private readonly DispatcherTimer _snapTimer;

    // ── コンストラクタ ────────────────────────────────
    /// <summary>サービスプロバイダーを受け取り、ウィジェットの位置を復元して初期化する。</summary>
    public BookmarkWidget(WidgetServiceProvider svc)
    {
        _svc     = svc;
        _todoSvc = svc.TodoService;

        InitializeComponent();

        _isPinned = _svc.AppSettingsService.BookmarkWidgetSettings.IsPinned;
        RestorePosition();

        // 30 秒ごとに最寄りの端へ自動スナップ
        _snapTimer = new DispatcherTimer { Interval = SNAP_INTERVAL };
        _snapTimer.Tick += (_, _) => SnapToNearestEdge();
        _snapTimer.Start();

        Loaded += (_, _) =>
        {
            UpdatePinVisual();
            // 初回表示時にパネルコンテンツを準備
            if (_isPanelOpen) BuildPanelContent();
        };
    }

    // ────────────────────────────────────────────────
    // ドラッグハンドル（タブ最上部 ・・・ の帯）
    // ────────────────────────────────────────────────
    /// <summary>ドラッグハンドルのマウスダウンでウィンドウをドラッグ移動し、完了後に位置を保存する。</summary>
    private void DragHandle_MouseLeftButtonDown(object s, MouseButtonEventArgs e)
    {
        e.Handled = true; // TabBorder.Tab_MouseLeftButtonDown への伝播を止める

        if (e.ButtonState != MouseButtonState.Pressed) return;

        _isDragging = true;
        try { DragMove(); } // ← ユーザーがマウスを放すまでブロック
        finally { _isDragging = false; }

        // DragMove 完了 → 新しい位置を保存
        SavePosition();
    }

    // ────────────────────────────────────────────────
    // タブ背景クリック（ボタン以外の空き領域）→ パネル開閉
    // ────────────────────────────────────────────────
    /// <summary>タブ背景のクリックでパネルを開閉する（ボタン・ドラッグハンドル領域のクリックは除外）。</summary>
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
    /// <summary>パネルの開閉状態をトグルする。</summary>
    private void TogglePanel()
    {
        if (_isPanelOpen) ClosePanel();
        else              OpenPanel();
    }

    /// <summary>パネルを左方向に展開し、タブの画面座標を維持しながらウィンドウ幅を拡張する。</summary>
    private void OpenPanel()
    {
        if (_isPanelOpen) return;

        // ── タブの画面座標を固定しながらウィンドウを左に拡張 ──
        //    現在 Width=52 → Left がタブの画面Left と同じ
        //    Width=332 にすると列0(パネル)が左に出るため Left を -280 補正
        double tabScreenLeft = Left; // 閉じているとき Left = タブ画面Left
        double newLeft = tabScreenLeft - PANEL_WIDTH;

        // 画面外に出ないようクランプ（最低でも 0）
        newLeft = Math.Max(SystemParameters.WorkArea.Left, newLeft);

        _isPanelOpen = true;
        Left  = newLeft;
        Width = PANEL_WIDTH + TAB_WIDTH;

        ExpandPanel.Visibility = Visibility.Visible;
        TxtToggle.Text = "▶"; // 現在 OPEN → クリックで閉じる
        BuildPanelContent();
    }

    /// <summary>パネルを閉じてウィンドウをタブ幅のみに縮小し、タブの画面座標を維持する。</summary>
    private void ClosePanel()
    {
        if (!_isPanelOpen) return;

        // タブの現在の画面 Left = Left(window) + PANEL_WIDTH(column0幅)
        double tabScreenLeft = Left + PANEL_WIDTH;

        _isPanelOpen = false;
        Left  = tabScreenLeft; // まず位置を確定
        Width = TAB_WIDTH;      // 次にリサイズ（左側が縮む）

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
    /// <summary>TODOボタンクリック時にパネルを開いてTODOコンテンツを表示する。</summary>
    private void BtnTodo_Click(object s, RoutedEventArgs e)
    {
        if (!_isPanelOpen) OpenPanel();
        else               BuildPanelContent();
        ShowTodoContent();
    }

    /// <summary>メモボタンクリック時にパネルを開いてクイックメモパネルを表示する。</summary>
    private void BtnNote_Click(object s, RoutedEventArgs e)
    {
        if (!_isPanelOpen) OpenPanel();
        ShowNotePanel();
    }

    private void BtnPomodoro_Click(object s, RoutedEventArgs e)
        => NavigateMainWindow("Pomodoro");

    /// <summary>タスクボタンクリック時にパネルを開いてタスクコンテンツを表示する。</summary>
    private void BtnTask_Click(object s, RoutedEventArgs e)
    {
        if (!_isPanelOpen) OpenPanel();
        else               BuildPanelContent();
        ShowTaskContent();
    }

    private void BtnCalendar_Click(object s, RoutedEventArgs e)
        => NavigateMainWindow("Calendar");

    /// <summary>ウィジェット非表示ボタンクリック時にパネルを閉じて非表示状態を保存する。</summary>
    private void BtnCloseWidget_Click(object s, RoutedEventArgs e)
    {
        ClosePanel();
        SaveHidden();
        Hide();
    }

    // ────────────────────────────────────────────────
    // パネルコンテンツ構築
    // ────────────────────────────────────────────────
    /// <summary>進行中タスクと未完了TODOの一覧、およびクイックTODO追加フォームをパネルに構築する。</summary>
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

    /// <summary>クイックメモ入力エリアとTODOとして保存するボタンをパネルに表示する。</summary>
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

    /// <summary>タスク情報を表示するチップUI要素を生成して返す。</summary>
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

    /// <summary>TODOアイテムのチェックボックス付きチップUI要素を生成して返す。</summary>
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

    /// <summary>テキストボックスと追加ボタンからなるクイックTODO入力フォームを生成して返す。</summary>
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
                tabLeft = screen.Right - TAB_WIDTH;
                tabTop  = screen.Top + screen.Height * 0.30;
            }
            else
            {
                // 保存済み位置を画面内にクランプ
                tabLeft = Math.Clamp(bs.TabLeft, screen.Left, screen.Right - TAB_WIDTH);
                tabTop  = Math.Clamp(bs.TabTop,  screen.Top,  screen.Bottom - 80);
            }

            // パネルが閉じているとき: Window.Left = タブ画面Left
            Left  = tabLeft;
            Top   = tabTop;
            Width = TAB_WIDTH; // 念のため閉じた状態に戻す
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

        // タブ画面 Left: パネル閉=Left、パネル開=Left+PANEL_WIDTH
        double tabScreenLeft = _isPanelOpen ? Left + PANEL_WIDTH : Left;

        var bs = _svc.AppSettingsService.BookmarkWidgetSettings;
        bs.TabLeft   = tabScreenLeft;
        bs.TabTop    = Top;
        bs.IsVisible = true;
        _svc.AppSettingsService.SaveBookmarkSettings(bs);
    }

    /// <summary>ウィジェットを非表示状態として設定に保存する。</summary>
    private void SaveHidden()
    {
        var bs = _svc.AppSettingsService.BookmarkWidgetSettings;
        bs.IsVisible = false;
        _svc.AppSettingsService.SaveBookmarkSettings(bs);
    }

    // ────────────────────────────────────────────────
    // 端への自動スナップ / ピン留め
    // ────────────────────────────────────────────────

    /// <summary>
    /// 最寄りの画面端（左右）へタブをスライド移動する。
    /// 固定中・ドラッグ中・パネル展開中・非表示中はスキップする。
    /// </summary>
    private void SnapToNearestEdge()
    {
        if (_isPinned || _isDragging || _isPanelOpen || !IsVisible) return;

        var screen = SystemParameters.WorkArea;
        // パネルが閉じているとき Window.Left == タブの画面 Left
        double tabScreenLeft = Left;

        double distLeft  = tabScreenLeft - screen.Left;
        double distRight = screen.Right - (tabScreenLeft + TAB_WIDTH);
        double targetLeft = distLeft <= distRight
            ? screen.Left                 // 左端へ
            : screen.Right - TAB_WIDTH;   // 右端へ

        // 既に端にある場合は何もしない
        if (Math.Abs(targetLeft - tabScreenLeft) < 0.5) return;

        AnimateLeftTo(targetLeft);
    }

    /// <summary>ウィンドウの Left をアニメーションで目標値までスライドさせ、完了後に位置を保存する。</summary>
    private void AnimateLeftTo(double targetLeft)
    {
        var anim = new DoubleAnimation
        {
            To             = targetLeft,
            Duration       = TimeSpan.FromMilliseconds(280),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior   = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, null);
            Left = targetLeft;
            SavePosition();
        };
        BeginAnimation(LeftProperty, anim);
    }

    /// <summary>ピン留めの ON/OFF を切り替え、設定に保存して見た目を更新する。</summary>
    private void BtnPin_Click(object s, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;

        var bs = _svc.AppSettingsService.BookmarkWidgetSettings;
        bs.IsPinned = _isPinned;
        _svc.AppSettingsService.SaveBookmarkSettings(bs);

        UpdatePinVisual();
    }

    /// <summary>ピン留めボタンの色とツールチップを現在の固定状態に合わせて更新する。</summary>
    private void UpdatePinVisual()
    {
        if (TxtPin == null) return;
        TxtPin.Foreground = new SolidColorBrush(_isPinned
            ? Color.FromRgb(0xFF, 0xD5, 0x4F)   // 固定中: アンバー
            : Color.FromRgb(0x6A, 0x70, 0x85)); // 解除: グレー
        if (BtnPin != null)
            BtnPin.ToolTip = _isPinned ? "位置を固定中（クリックで解除）" : "位置を固定する";
    }

    // ────────────────────────────────────────────────
    // ウィンドウイベント
    // ────────────────────────────────────────────────
    /// <summary>ウィンドウを閉じる操作をHideに差し替えて非表示状態を保存する。</summary>
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
        _snapTimer?.Stop();
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
