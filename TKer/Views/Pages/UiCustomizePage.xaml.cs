using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>UIテーマ・ホームレイアウト・メニュー順序をカスタマイズするページ。</summary>
public partial class UiCustomizePage : Page, IRefreshable
{
    private readonly MainViewModel      _vm;
    private readonly AppSettingsService _svc;
    private List<string> _menuOrder    = new();
    private ThemeColors  _originalTheme = new();
    private Dictionary<string, HomeLayoutSlot> _homeSlots = new();
    private List<HomeLayoutSlot> _gridSlots = new();
    private List<double> _columnWidths = new();
    private List<double> _rowHeights   = new();
    private string? _selectedHomeId = null;

    // ── 正規メニュー項目 ──────────────────────────────────────────────
    private static readonly string[] CANONICAL_MENU_ITEMS =
        { "ホーム", "ライブラリ", "タスク管理", "ツール", "カスタマイズ", "ヘルプ" };

    // ── ホームコンポーネント ID → 表示名 ─────────────────────────────
    private static readonly Dictionary<string, string> HOME_COMPONENT_LABELS = new()
    {
        ["RecentTask"] = "直近タスク",
        ["Project"]    = "プロジェクト一覧",
        ["Shortcut"]   = "ショートカット",
        ["Calendar"]   = "ミニカレンダー",
        ["QuickNav"]   = "クイックナビ",
        ["Version"]    = "バージョン情報",
        ["Header"]     = "ヘッダーバー",
        ["Alert"]      = "アラートセクション",
        // カードテンプレートの部品
        ["Card_TodoTasks"]  = "ToDo / タスク",
        ["Card_Schedule"]   = "予定",
        ["Card_Collection"] = "コレクション",
        ["Card_Projects"]   = "プロジェクト",
        ["Card_Notify"]     = "システム通知",
        ["Card_Media"]      = "メディア",
        ["Card_Tools"]      = "ショートカット",
    };

    private static readonly string[] FIXED_HOME_COMPONENTS = { };

    // ── 一括適用対象キー（ホーム + 各画面） ─────────────────────────
    private static readonly string[] ALL_BULK_TARGET_KEYS =
    {
        // ホーム画面コンポーネント
        "Header", "Alert", "RecentTask", "Project", "Shortcut", "Calendar", "QuickNav", "Version",
        // タスク管理
        "Task_Header", "Task_Toolbar", "Task_Row",
        // カテゴリー管理
        "Cat_Header", "Cat_Toolbar", "Cat_Card",
        // ダッシュボード
        "Dash_Header", "Dash_Card",
        // カレンダー
        "Cal_Header", "Cal_Grid",
        // 成果物管理
        "Del_Header", "Del_Card",
        // プロジェクト管理
        "PL_Header", "PL_Card",
        // プロジェクト詳細
        "Proj_Header", "Proj_Tab", "Proj_Content",
        // テーブル一覧
        "Table_Header", "Table_Row",
        // ショートカット
        "SC_Header", "SC_Card",
        // ポモドーロ
        "Pomo_Header", "Pomo_Card",
        // 記事
        "Article_Header", "Article_Card",
        // TODO
        "Todo_Header", "Todo_Row",
        // 書籍リスト
        "Book_Header", "Book_Card",
        // イラスト一覧
        "Illust_Header", "Illust_Card",
        // ログビューアー
        "Log_Header", "Log_Row",
    };

    // ── その他画面のコンポーネント定義 ───────────────────────────────
    private static readonly (string Screen, (string Key, string Label)[] Components)[] SCREENS =
    [
        ("タスク管理", [
            ("Task_Header",  "ヘッダー"),
            ("Task_Toolbar", "縦ツールバー"),
            ("Task_Row",     "タスク行"),
        ]),
        ("カテゴリー管理", [
            ("Cat_Header",  "ヘッダー"),
            ("Cat_Toolbar", "縦ツールバー"),
            ("Cat_Card",    "カテゴリーカード"),
        ]),
        ("ダッシュボード", [
            ("Dash_Header", "ヘッダー"),
            ("Dash_Card",   "ダッシュボードカード"),
        ]),
        ("カレンダー", [
            ("Cal_Header", "ヘッダー"),
            ("Cal_Grid",   "カレンダーグリッド"),
        ]),
        ("成果物管理", [
            ("Del_Header", "ヘッダー"),
            ("Del_Card",   "成果物カード"),
        ]),
        ("プロジェクト管理", [
            ("PL_Header", "ヘッダー"),
            ("PL_Card",   "プロジェクトカード"),
        ]),
        ("プロジェクト詳細", [
            ("Proj_Header",  "ヘッダー"),
            ("Proj_Tab",     "タブバー"),
            ("Proj_Content", "コンテンツエリア"),
        ]),
        ("テーブル一覧", [
            ("Table_Header", "ヘッダー"),
            ("Table_Row",    "テーブル行"),
        ]),
        ("ショートカット", [
            ("SC_Header", "ヘッダー"),
            ("SC_Card",   "ショートカットカード"),
        ]),
        ("ポモドーロ", [
            ("Pomo_Header", "ヘッダー"),
            ("Pomo_Card",   "タイマーカード"),
        ]),
        ("記事", [
            ("Article_Header", "ヘッダー"),
            ("Article_Card",   "記事カード"),
        ]),
        ("TODO", [
            ("Todo_Header", "ヘッダー"),
            ("Todo_Row",    "TODO行"),
        ]),
        ("書籍リスト", [
            ("Book_Header", "ヘッダー"),
            ("Book_Card",   "書籍カード"),
        ]),
        ("イラスト一覧", [
            ("Illust_Header", "ヘッダー"),
            ("Illust_Card",   "イラストカード"),
        ]),
        ("ログビューアー", [
            ("Log_Header", "ヘッダー"),
            ("Log_Row",    "ログ行"),
        ]),
    ];

    /// <summary>UIカスタマイズページを初期化する。</summary>
    public UiCustomizePage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = vm.AppSettingsService;
        InitializeComponent();
    }

    // ══════════════════════════════════════════════
    //  Refresh
    // ══════════════════════════════════════════════

    /// <summary>テーマ・レイアウト・メニュー順序を再読み込みして画面を更新する。</summary>
    public void Refresh()
    {
        _gridSlots = _svc.GetEffectiveHomeLayout()
            .Select(s => new HomeLayoutSlot
            {
                ComponentId = s.ComponentId,
                Visible     = s.Visible,
                Row         = s.Row,
                Column      = s.Column,
                RowSpan     = s.RowSpan,
                ColumnSpan  = s.ColumnSpan,
            })
            .ToList();
        _homeSlots = _gridSlots.ToDictionary(s => s.ComponentId, s => s);
        _columnWidths = _svc.GetEffectiveColumnWidths();
        _rowHeights   = _svc.GetEffectiveRowHeights();

        _selectedHomeId = null;
        ApplyTemplateRadios();
        BuildHomePreview();
        UpdateHomePreviewMode();
        ShowHomeStylePlaceholder();
        BuildContent();

        var t = _svc.Theme;
        TxtColorTextPrimary.Text  = t.TextPrimary  ?? "";
        TxtColorTextSecond.Text   = t.TextSecond   ?? "";
        TxtColorAccent.Text       = t.AccentCyan   ?? "";
        TxtColorBgSecond.Text     = t.BgSecondary  ?? "";
        TxtColorBgCard.Text       = t.BgCard       ?? "";
        TxtColorBorder.Text       = t.Border       ?? "";
        TxtColorButtonBg.Text     = t.ButtonBg     ?? "";
        TxtColorButtonBorder.Text = t.ButtonBorder ?? "";
        TxtColorDropdownBg.Text   = t.DropdownBg   ?? "";

        var savedFont = t.FontFamily ?? "";
        CbFontFamily.SelectedIndex = 0;
        foreach (ComboBoxItem item in CbFontFamily.Items)
        {
            if ((item.Tag as string ?? "") == savedFont)
            { CbFontFamily.SelectedItem = item; break; }
        }

        _originalTheme = new ThemeColors
        {
            TextPrimary  = t.TextPrimary,
            TextSecond   = t.TextSecond,
            AccentCyan   = t.AccentCyan,
            BgSecondary  = t.BgSecondary,
            BgCard       = t.BgCard,
            Border       = t.Border,
            FontFamily   = t.FontFamily,
            ButtonBg     = t.ButtonBg,
            ButtonBorder = t.ButtonBorder,
            DropdownBg   = t.DropdownBg,
        };

        var savedOrder = _svc.MenuOrder.ToList();
        _menuOrder = CANONICAL_MENU_ITEMS
            .OrderBy(c =>
            {
                int i = savedOrder.IndexOf(c);
                return i < 0 ? CANONICAL_MENU_ITEMS.Length : i;
            })
            .ToList();
        RefreshMenuOrderList();

        BuildPresetPanel();
    }

    // ══════════════════════════════════════════════
    //  Tab 1 — ホーム画面ビジュアルプレビュー
    // ══════════════════════════════════════════════

    /// <summary>テンプレート ID とラジオの対応（表示順）。</summary>
    private (string Key, RadioButton? Rb)[] TemplateRadios() => new (string, RadioButton?)[]
    {
        ("Grid",         RbTplGrid),
        ("Planet",       RbTplPlanet),
        ("Card",         RbTplCard),
        ("Magazine",     RbTplMagazine),
        ("Dock",         RbTplDock),
        ("Tri",          RbTplTri),
        ("Timeline",     RbTplTimeline),
        ("CalendarFull", RbTplCalendar),
        ("Journal",      RbTplJournal),
        ("Glass",        RbTplGlass),
    };

    /// <summary>保存済みテンプレート設定に合わせてラジオボタンの選択状態を反映する。</summary>
    private void ApplyTemplateRadios()
    {
        if (RbTplGrid == null) return;
        var current = _svc.HomeTemplate;
        var radios  = TemplateRadios();
        foreach (var (_, rb) in radios)
            if (rb != null) rb.Checked -= HomeTemplate_Changed;
        bool matched = false;
        foreach (var (key, rb) in radios)
            if (rb != null) { rb.IsChecked = key == current; if (rb.IsChecked == true) matched = true; }
        if (!matched) RbTplGrid.IsChecked = true;
        foreach (var (_, rb) in radios)
            if (rb != null) rb.Checked += HomeTemplate_Changed;
        UpdateTemplateHint(current);
    }

    /// <summary>テンプレートラジオボタンの選択変更時に設定を保存する。</summary>
    private void HomeTemplate_Changed(object sender, RoutedEventArgs e)
    {
        var radios = TemplateRadios();
        string newTpl = radios.FirstOrDefault(r => r.Rb?.IsChecked == true).Key ?? "Grid";
        if (newTpl == _svc.HomeTemplate) return;
        _svc.SaveHomeTemplate(newTpl);
        UpdateTemplateHint(newTpl);
        UpdateHomePreviewMode();
    }

    /// <summary>仮想ウィンドウ内のホームプレビューを保存中のプレビュー HomePage インスタンス。</summary>
    private HomePage? _previewHome;

    /// <summary>
    /// 選択テンプレートに応じてプレビュー表示を切り替える。
    /// グリッド: 部品配置エディタ / 惑星・カード: 実際の HomePage を縮小ライブ表示。
    /// </summary>
    private void UpdateHomePreviewMode()
    {
        if (HomeEditorScroll == null || HomeLivePreviewBox == null) return;
        var tpl = _svc.HomeTemplate;
        bool isGrid = tpl != "Planet" && tpl != "Card";

        HomeEditorScroll.Visibility  = isGrid ? Visibility.Visible : Visibility.Collapsed;
        HomeLivePreviewBox.Visibility = isGrid ? Visibility.Collapsed : Visibility.Visible;

        if (isGrid) return;

        // 実際の適用イメージ：HomePage を生成（再利用）して最新状態を描画
        if (_previewHome == null)
        {
            _previewHome = new HomePage(_vm) { IsEditPreview = true };
            HomePreviewFrame.Navigate(_previewHome);
            // レイアウト確定後に描画（Frame の Navigate は非同期反映のため）
            Dispatcher.BeginInvoke(new Action(() => _previewHome?.Refresh()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            _previewHome.Refresh();
        }
    }

    /// <summary>ライブプレビュー上のクリックで、Tag を持つ部品を選択してスタイル編集パネルを開く。</summary>
    private void LivePreview_Click(object sender, MouseButtonEventArgs e)
    {
        var node = e.OriginalSource as DependencyObject;
        while (node != null)
        {
            if (node is FrameworkElement fe && fe.Tag is string tag && tag.StartsWith("Card_"))
            {
                _selectedHomeId = tag;
                UpdateHomeStylePanel();
                e.Handled = true;   // カードのドラッグ等を抑止
                return;
            }
            node = VisualTreeHelper.GetParent(node);
        }
    }

    /// <summary>テンプレートに応じた補助テキストを表示する。</summary>
    private void UpdateTemplateHint(string template)
    {
        if (TplHint == null) return;
        TplHint.Text = template switch
        {
            "Planet"       => "惑星スタイル: プレビューの部品をクリックでスタイル編集できます",
            "Card"         => "カードスタイル: プレビューの部品をクリックでスタイル編集できます",
            "Magazine"     => "（未実装）マガジン: 中央カード＋年月の透かし＋下部ウィジェット帯",
            "Dock"         => "（未実装）ドック: フル幅カード＋右の縦アイコンドック",
            "Tri"          => "（未実装）3分割: ヘッダー / カード中央 / 下ショートカット",
            "Timeline"     => "（未実装）タイムライン: 縦時間軸＋イベント帯",
            "CalendarFull" => "（未実装）カレンダー全面: 月カレンダーが背景・選択日が拡大",
            "Journal"      => "（未実装）手帳: 見開き2ページで予定と ToDo",
            "Glass"        => "（未実装）グラス: ぼかし背景＋浮遊するガラスパネル",
            _              => "グリッドスタイル: 下記のレーン編集で自由にレイアウトできます",
        };
    }

    /// <summary>ホーム画面レイアウトのビジュアルプレビューを再構築する。</summary>
    private void BuildHomePreview()
    {
        if (HomePreviewGrid == null) return;
        HomePreviewGrid.Children.Clear();
        HomePreviewGrid.ColumnDefinitions.Clear();
        HomePreviewGrid.RowDefinitions.Clear();

        if (_columnWidths.Count == 0) _columnWidths.Add(-1);
        if (_rowHeights.Count   == 0) _rowHeights.Add(-1);

        // 範囲外のスロットを末尾にクランプ
        foreach (var slot in _gridSlots)
        {
            slot.Row    = Math.Clamp(slot.Row,    0, _rowHeights.Count   - 1);
            slot.Column = Math.Clamp(slot.Column, 0, _columnWidths.Count - 1);
            if (slot.RowSpan    < 1) slot.RowSpan    = 1;
            if (slot.ColumnSpan < 1) slot.ColumnSpan = 1;
        }

        // 列・行定義
        for (int c = 0; c < _columnWidths.Count; c++)
            HomePreviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = ToGridLength(_columnWidths[c]), MinWidth = 60 });
        for (int r = 0; r < _rowHeights.Count; r++)
            HomePreviewGrid.RowDefinitions.Add(new RowDefinition { Height = ToGridLength(_rowHeights[r]), MinHeight = 40 });

        // 各セルへドロップターゲットを敷く（背景の見えない受け皿）
        var dropZones = new FrameworkElement[_rowHeights.Count, _columnWidths.Count];
        for (int r = 0; r < _rowHeights.Count; r++)
        {
            for (int c = 0; c < _columnWidths.Count; c++)
            {
                int capR = r, capC = c;
                var zone = new Border
                {
                    Background  = Brushes.Transparent,
                    AllowDrop   = true,
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(2),
                    CornerRadius = new CornerRadius(4),
                };
                zone.DragOver += (_, e) =>
                {
                    e.Effects = e.Data.GetDataPresent("HomeCardId") ? DragDropEffects.Move : DragDropEffects.None;
                    e.Handled = true;
                };
                zone.Drop += (_, e) =>
                {
                    if (!e.Data.GetDataPresent("HomeCardId")) return;
                    var id = e.Data.GetData("HomeCardId") as string;
                    if (string.IsNullOrEmpty(id)) return;
                    var slot = _gridSlots.FirstOrDefault(x => x.ComponentId == id);
                    if (slot == null) return;
                    slot.Row    = capR;
                    slot.Column = capC;
                    BuildHomePreview();
                    e.Handled = true;
                };
                Grid.SetRow(zone, r);
                Grid.SetColumn(zone, c);
                HomePreviewGrid.Children.Add(zone);
                dropZones[r, c] = zone;
            }
        }

        // 部品配置
        foreach (var slot in _gridSlots)
        {
            var card = BuildPreviewCard(slot.ComponentId, slot);
            card.Margin = new Thickness(2);
            Grid.SetRow(card, slot.Row);
            Grid.SetColumn(card, slot.Column);
            Grid.SetRowSpan(card, Math.Max(1, slot.RowSpan));
            Grid.SetColumnSpan(card, Math.Max(1, slot.ColumnSpan));
            HomePreviewGrid.Children.Add(card);
        }

        // 列境界の GridSplitter
        for (int c = 0; c < _columnWidths.Count - 1; c++)
        {
            int capCol = c;
            var sp = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment   = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Color.FromArgb(80, 100, 150, 200)),
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Columns,
                ShowsPreview = false,
            };
            Grid.SetColumn(sp, c);
            Grid.SetRowSpan(sp, Math.Max(1, _rowHeights.Count));
            sp.DragCompleted += (_, _) =>
            {
                CaptureColumnWidths();
            };
            HomePreviewGrid.Children.Add(sp);
        }

        // 行境界の GridSplitter
        for (int r = 0; r < _rowHeights.Count - 1; r++)
        {
            int capRow = r;
            var sp = new GridSplitter
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment   = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(80, 100, 150, 200)),
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Rows,
                ShowsPreview = false,
            };
            Grid.SetRow(sp, r);
            Grid.SetColumnSpan(sp, Math.Max(1, _columnWidths.Count));
            sp.DragCompleted += (_, _) =>
            {
                CaptureRowHeights();
            };
            HomePreviewGrid.Children.Add(sp);
        }
    }

    /// <summary>GridSplitter操作後の列幅をキャプチャして内部リストに保存する。</summary>
    private void CaptureColumnWidths()
    {
        if (HomePreviewGrid == null) return;
        for (int i = 0; i < HomePreviewGrid.ColumnDefinitions.Count && i < _columnWidths.Count; i++)
        {
            // ドラッグ後は ActualWidth を px として保存
            _columnWidths[i] = Math.Max(60, HomePreviewGrid.ColumnDefinitions[i].ActualWidth);
        }
        BuildHomePreview();
    }

    /// <summary>GridSplitter操作後の行高さをキャプチャして内部リストに保存する。</summary>
    private void CaptureRowHeights()
    {
        if (HomePreviewGrid == null) return;
        for (int i = 0; i < HomePreviewGrid.RowDefinitions.Count && i < _rowHeights.Count; i++)
        {
            _rowHeights[i] = Math.Max(40, HomePreviewGrid.RowDefinitions[i].ActualHeight);
        }
        BuildHomePreview();
    }

    /// <summary>数値をGridLengthに変換する（0=Auto、負=Star、正=Pixel）。</summary>
    private static GridLength ToGridLength(double v)
    {
        if (v == 0) return GridLength.Auto;
        if (v < 0)  return new GridLength(-v, GridUnitType.Star);
        return new GridLength(v, GridUnitType.Pixel);
    }

    // ── レーン操作 ─────────────────────────────────
    /// <summary>ホームプレビューグリッドに列を追加する。</summary>
    private void LaneAddCol_Click(object sender, RoutedEventArgs e)
    {
        _columnWidths.Add(-1);
        BuildHomePreview();
    }

    /// <summary>ホームプレビューグリッドの末尾列を削除する。</summary>
    private void LaneRemoveCol_Click(object sender, RoutedEventArgs e)
    {
        if (_columnWidths.Count <= 1) return;
        int last = _columnWidths.Count - 1;
        _columnWidths.RemoveAt(last);
        // 末尾列に含まれる部品を新しい末尾列へクランプ
        foreach (var s in _gridSlots)
        {
            if (s.Column >= _columnWidths.Count) s.Column = _columnWidths.Count - 1;
        }
        BuildHomePreview();
    }

    /// <summary>ホームプレビューグリッドに行を追加する。</summary>
    private void LaneAddRow_Click(object sender, RoutedEventArgs e)
    {
        _rowHeights.Add(-1);
        BuildHomePreview();
    }

    /// <summary>ホームプレビューグリッドの末尾行を削除する。</summary>
    private void LaneRemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if (_rowHeights.Count <= 1) return;
        int last = _rowHeights.Count - 1;
        _rowHeights.RemoveAt(last);
        foreach (var s in _gridSlots)
        {
            if (s.Row >= _rowHeights.Count) s.Row = _rowHeights.Count - 1;
        }
        BuildHomePreview();
    }

    /// <summary>ホームレイアウトをデフォルトにリセットする。</summary>
    private void LaneReset_Click(object sender, RoutedEventArgs e)
    {
        _svc.SaveHomeLanes(new List<double>(), new List<double>());
        _svc.SaveHomeLayout(new Dictionary<string, HomeLayoutSlot>());
        _gridSlots = _svc.GetEffectiveHomeLayout()
            .Select(s => new HomeLayoutSlot
            {
                ComponentId = s.ComponentId, Visible = s.Visible,
                Row = s.Row, Column = s.Column,
                RowSpan = s.RowSpan, ColumnSpan = s.ColumnSpan,
            })
            .ToList();
        _homeSlots = _gridSlots.ToDictionary(s => s.ComponentId, s => s);
        _columnWidths = _svc.GetEffectiveColumnWidths();
        _rowHeights   = _svc.GetEffectiveRowHeights();
        BuildHomePreview();
    }

    // ── プレビューカード生成 ─────────────────────────────────────────

    /// <summary>ホームコンポーネントのプレビューカードを生成して返す。</summary>
    private Border BuildPreviewCard(string id, HomeLayoutSlot? slot)
    {
        var theme       = _svc.GetSectionTheme(id);
        var isSelected  = id == _selectedHomeId;
        var isFixed     = false;
        var isVisible   = slot?.Visible ?? true;
        var label       = HOME_COMPONENT_LABELS.TryGetValue(id, out var l) ? l : id;

        var fg         = TryBrush("TextPrimaryBrush") ?? Brushes.White;
        var fgDim      = TryBrush("TextDimBrush")     ?? Brushes.Gray;
        var defaultBg  = TryBrush("BgCardBrush")      ?? Brushes.DimGray;
        var defBorder  = TryBrush("BorderBrush")      ?? Brushes.DarkGray;
        var accentCyan = TryBrush("AccentCyanBrush")  ?? Brushes.Cyan;

        // テーマ適用
        Brush cardBg     = defaultBg;
        Brush cardBorder = isSelected ? accentCyan : defBorder;

        if (!string.IsNullOrEmpty(theme.BgColor))
        {
            var c = TryParseColor(theme.BgColor);
            if (c.HasValue) cardBg = new SolidColorBrush(c.Value);
        }
        if (!string.IsNullOrEmpty(theme.BorderColor) && !isSelected)
        {
            var c = TryParseColor(theme.BorderColor);
            if (c.HasValue) cardBorder = new SolidColorBrush(c.Value);
        }

        var card = new Border
        {
            Background      = cardBg,
            BorderBrush     = cardBorder,
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            CornerRadius    = new CornerRadius(8),
            Margin          = new Thickness(0),
            Opacity         = theme.Opacity * (isVisible ? 1.0 : 0.35),
            Tag             = id,
            AllowDrop       = false,
            IsHitTestVisible = true,
        };

        if (isSelected)
        {
            card.Effect = new DropShadowEffect
            {
                Color = Colors.Cyan, Opacity = 0.5, BlurRadius = 10, ShadowDepth = 0
            };
        }

        var mainStack = new StackPanel();

        // ── ドラッグハンドルバー ──
        var handle = new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
            Padding         = new Thickness(10, 6, 10, 6),
            CornerRadius    = new CornerRadius(7, 7, 0, 0),
            Cursor          = isFixed ? Cursors.Arrow : Cursors.SizeAll,
            Tag             = id,
        };

        var hGrid = new Grid();
        hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        hGrid.Children.Add(new TextBlock
        {
            Text = "⠿",
            FontSize = 14,
            Foreground = fgDim,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });

        var nameTb = new TextBlock
        {
            Text       = label,
            FontSize   = 12,
            FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = isSelected ? accentCyan : fg,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameTb, 1);
        hGrid.Children.Add(nameTb);

        // 右コントロール群
        var ctrlPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(ctrlPanel, 2);

        var capturedId = id;

        // 表示/非表示トグル（可動カードのみ）
        if (slot != null)
        {
            var capturedSlot = slot;
            var visElem = new TextBlock
            {
                Text = capturedSlot.Visible ? "👁" : "🚫",
                FontSize = 12,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = capturedSlot.Visible ? "クリックで非表示にする" : "クリックで表示する",
            };
            visElem.MouseLeftButtonDown += (_, e) =>
            {
                capturedSlot.Visible = !capturedSlot.Visible;
                BuildHomePreview();
                e.Handled = true;
            };
            ctrlPanel.Children.Add(visElem);
        }

        // スタイル編集ボタン
        var styleElem = new TextBlock
        {
            Text = "🎨", FontSize = 12,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "スタイルを編集",
        };
        styleElem.MouseLeftButtonDown += (_, e) =>
        {
            _selectedHomeId = capturedId;
            BuildHomePreview();
            UpdateHomeStylePanel();
            e.Handled = true;
        };
        ctrlPanel.Children.Add(styleElem);

        hGrid.Children.Add(ctrlPanel);
        handle.Child = hGrid;

        // ハンドルクリック → 選択 / ドラッグ開始（DnD で並び替え）
        handle.MouseLeftButtonDown += (_, e) =>
        {
            _selectedHomeId = capturedId;
            BuildHomePreview();
            UpdateHomeStylePanel();
            try
            {
                DragDrop.DoDragDrop(handle, new DataObject("HomeCardId", capturedId), DragDropEffects.Move);
            }
            catch { }
            e.Handled = true;
        };

        mainStack.Children.Add(handle);

        // ── スケルトンコンテンツ ──
        var content = new StackPanel { Margin = new Thickness(10, 8, 10, 10), Tag = id };
        content.MouseLeftButtonDown += (_, _) =>
        {
            _selectedHomeId = capturedId;
            BuildHomePreview();
            UpdateHomeStylePanel();
        };
        AddSkeletonContent(content, id);
        mainStack.Children.Add(content);

        card.Child = mainStack;
        return card;
    }

    // ── スケルトンコンテンツ ────────────────────────────────────────

    /// <summary>コンポーネントIDに応じたスケルトンUI要素をパネルに追加する。</summary>
    private void AddSkeletonContent(StackPanel p, string id)
    {
        var fg         = TryBrush("TextPrimaryBrush")  ?? Brushes.White;
        var fgDim      = TryBrush("TextDimBrush")      ?? Brushes.Gray;
        var cardBg     = TryBrush("BgCardBrush")       ?? Brushes.DimGray;
        var borderBrush = TryBrush("BorderBrush")      ?? Brushes.DarkGray;
        var accentCyan = TryBrush("AccentCyanBrush")   ?? Brushes.Cyan;
        var accentGreen = TryBrush("AccentGreenBrush") ?? Brushes.LimeGreen;
        var accentRed  = TryBrush("AccentRedBrush")    ?? Brushes.Red;

        Border Row(string text, Brush? bg = null, Brush? border = null, double mb = 4) =>
            new Border
            {
                Background = bg ?? cardBg, BorderBrush = border ?? borderBrush,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, mb),
                Child = new TextBlock { Text = text, FontSize = 11, Foreground = fgDim }
            };

        TextBlock SectionTitle(string t) =>
            new TextBlock
            {
                Text = t, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = fg, Margin = new Thickness(0, 0, 0, 6)
            };

        switch (id)
        {
            // ────── ヘッダー ──────
            case "Header":
            {
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new TextBlock
                {
                    Text = "プロジェクト名", FontSize = 20, FontWeight = FontWeights.Black,
                    Foreground = fg, VerticalAlignment = VerticalAlignment.Center
                });
                var dateStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
                dateStack.Children.Add(new TextBlock
                {
                    Text = DateTime.Now.ToString("yyyy/MM/dd   HH:mm"),
                    FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = fgDim
                });
                Grid.SetColumn(dateStack, 1);
                g.Children.Add(dateStack);
                p.Children.Add(g);
                break;
            }

            // ────── アラート ──────
            case "Alert":
            {
                p.Children.Add(SectionTitle("⚠  アラート"));
                p.Children.Add(Row("タスク A ・ カテゴリ1 ・ 期限: 昨日 ・ 2日超過",
                    border: new SolidColorBrush(Color.FromRgb(0xCC, 0x44, 0x44))));
                p.Children.Add(Row("タスク B ・ カテゴリ2 ・ 期限: 今日",
                    border: new SolidColorBrush(Color.FromRgb(0xCC, 0xAA, 0x00))));
                break;
            }

            // ────── 直近タスク ──────
            case "RecentTask":
            {
                p.Children.Add(SectionTitle("📋  直近のタスク"));
                p.Children.Add(Row($"タスク 1 ・ カテゴリA ・ 期限: {DateTime.Now.AddDays(1):MM/dd}"));
                p.Children.Add(Row($"タスク 2 ・ カテゴリB ・ 期限: {DateTime.Now.AddDays(3):MM/dd}"));
                p.Children.Add(Row($"タスク 3 ・ カテゴリA ・ 期限: {DateTime.Now.AddDays(5):MM/dd}"));
                break;
            }

            // ────── プロジェクト一覧 ──────
            case "Project":
            {
                p.Children.Add(SectionTitle("📁  最近開いたプロジェクト"));
                foreach (var (name, pct) in new[] { ("サンプルプロジェクト 1", 0.62), ("サンプルプロジェクト 2", 0.30) })
                {
                    var inner = new StackPanel();
                    inner.Children.Add(new TextBlock
                    {
                        Text = name, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = fg
                    });
                    // Progress bar
                    var pgGrid = new Grid { Height = 5, Margin = new Thickness(0, 4, 0, 0) };
                    pgGrid.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = borderBrush });
                    pgGrid.Children.Add(new Border
                    {
                        CornerRadius = new CornerRadius(2), Background = accentGreen,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        MinWidth = 4
                    });
                    // Width binding is not available here; use fixed skeleton widths
                    (pgGrid.Children[1] as Border)!.Width = pct * 120;
                    inner.Children.Add(pgGrid);
                    p.Children.Add(new Border
                    {
                        Background = cardBg, BorderBrush = borderBrush, BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 0, 0, 6), Child = inner
                    });
                }
                break;
            }

            // ────── ショートカット ──────
            case "Shortcut":
            {
                p.Children.Add(SectionTitle("⚡  ショートカット"));
                var wrap = new WrapPanel();
                foreach (var n in new[] { "🔗 ファイル 1", "🔗 ファイル 2", "🔗 アプリ 1" })
                    wrap.Children.Add(new Border
                    {
                        Background = cardBg, BorderBrush = borderBrush, BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 5, 8, 5),
                        Margin = new Thickness(0, 0, 4, 4),
                        Child = new TextBlock { Text = n, FontSize = 11, Foreground = fg }
                    });
                p.Children.Add(wrap);
                break;
            }

            // ────── ミニカレンダー ──────
            case "Calendar":
            {
                p.Children.Add(new TextBlock
                {
                    Text = $"◀  {DateTime.Now:yyyy年M月}  ▶",
                    FontSize = 13, FontWeight = FontWeights.Bold,
                    Foreground = fg, Margin = new Thickness(0, 0, 0, 6),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                var daysRow = new UniformGrid { Columns = 7, Rows = 1 };
                foreach (var d in new[] { "日", "月", "火", "水", "木", "金", "土" })
                    daysRow.Children.Add(new TextBlock
                    {
                        Text = d, FontSize = 10, Foreground = fgDim, TextAlignment = TextAlignment.Center
                    });
                p.Children.Add(daysRow);
                var cellGrid = new UniformGrid { Columns = 7, Rows = 3 };
                int today = DateTime.Now.Day;
                for (int i = 1; i <= 21; i++)
                    cellGrid.Children.Add(new TextBlock
                    {
                        Text = i.ToString(),
                        FontSize = 10, TextAlignment = TextAlignment.Center,
                        Foreground  = i == today ? accentCyan : fg,
                        FontWeight  = i == today ? FontWeights.Bold : FontWeights.Normal
                    });
                p.Children.Add(cellGrid);
                break;
            }

            // ────── クイックナビ ──────
            case "QuickNav":
            {
                p.Children.Add(SectionTitle("🧭  クイックナビ"));
                foreach (var (icon, name) in new[] {
                    ("📋", "プロジェクト一覧"), ("📊", "ダッシュボード"),
                    ("✅", "タスク管理"),       ("📅", "カレンダー") })
                    p.Children.Add(Row($"{icon}  {name}"));
                break;
            }

            // ────── バージョン情報 ──────
            case "Version":
            {
                p.Children.Add(SectionTitle("ℹ  アプリ情報"));
                p.Children.Add(new TextBlock { Text = $"バージョン    {TKer.Models.AppVersion.DISPLAY_NAME}", FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 3) });
                p.Children.Add(new TextBlock { Text = $"ビルド日      {TKer.Models.AppVersion.BUILD_DATE}",  FontSize = 11, Foreground = fgDim });
                break;
            }
        }
    }

    // ── 右パネル: スタイル編集 ──────────────────────────────────────

    /// <summary>選択コンポーネントのスタイル編集パネルを更新する。</summary>
    private void UpdateHomeStylePanel()
    {
        HomeStylePanel.Children.Clear();
        if (_selectedHomeId == null) { ShowHomeStylePlaceholder(); return; }

        var label   = HOME_COMPONENT_LABELS.TryGetValue(_selectedHomeId, out var l) ? l : _selectedHomeId;
        var fg      = TryBrush("TextPrimaryBrush") ?? Brushes.White;
        var fgDim   = TryBrush("TextDimBrush")     ?? Brushes.Gray;
        var border  = TryBrush("BorderBrush")       ?? Brushes.DarkGray;
        var current = _svc.GetSectionTheme(_selectedHomeId);

        HomeStylePanel.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Yu Gothic UI"), FontWeight = FontWeights.Bold,
            FontSize = 14, Foreground = fg, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        // 背景色
        HomeStylePanel.Children.Add(new TextBlock
        {
            Text = "背景色（HEX 例: #1A2B3C　空欄=デフォルト）",
            FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4)
        });
        var bgPreview = ColorPreviewDot(border);
        var bgBox = new TextBox { Text = current.BgColor ?? "", Style = (Style)Application.Current.Resources["DarkTextBox"] };
        var bgDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(bgPreview, Dock.Right);
        bgDock.Children.Add(bgPreview); bgDock.Children.Add(bgBox);
        void RefreshBgPreview() { var c = TryParseColor(bgBox.Text); bgPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent; }
        bgBox.TextChanged += (_, _) => RefreshBgPreview();
        RefreshBgPreview();
        HomeStylePanel.Children.Add(bgDock);

        // 文字色
        HomeStylePanel.Children.Add(new TextBlock
        {
            Text = "文字色（HEX　空欄=デフォルト）",
            FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4)
        });
        var textPreview = ColorPreviewDot(border);
        var textBox = new TextBox { Text = current.TextColor ?? "", Style = (Style)Application.Current.Resources["DarkTextBox"] };
        var textDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(textPreview, Dock.Right);
        textDock.Children.Add(textPreview); textDock.Children.Add(textBox);
        void RefreshTextPreview() { var c = TryParseColor(textBox.Text); textPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent; }
        textBox.TextChanged += (_, _) => RefreshTextPreview();
        RefreshTextPreview();
        HomeStylePanel.Children.Add(textDock);

        // 枠線色
        HomeStylePanel.Children.Add(new TextBlock
        {
            Text = "枠線色（HEX　空欄=デフォルト）",
            FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4)
        });
        var borderPreview = ColorPreviewDot(border);
        var borderBox = new TextBox { Text = current.BorderColor ?? "", Style = (Style)Application.Current.Resources["DarkTextBox"] };
        var borderDock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(borderPreview, Dock.Right);
        borderDock.Children.Add(borderPreview); borderDock.Children.Add(borderBox);
        void RefreshBorderPreview() { var c = TryParseColor(borderBox.Text); borderPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent; }
        borderBox.TextChanged += (_, _) => RefreshBorderPreview();
        RefreshBorderPreview();
        HomeStylePanel.Children.Add(borderDock);

        // 透明度
        HomeStylePanel.Children.Add(new TextBlock
        {
            Text = "透明度", FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 6)
        });
        var opGrid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        opGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        opGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        var opSlider = new Slider
        {
            Minimum = 0, Maximum = 100,
            Value   = Math.Clamp(current.Opacity * 100, 0, 100),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TryBrush("AccentCyanBrush") ?? Brushes.Cyan
        };
        var opLabel = new TextBlock
        {
            Text = $"{(int)opSlider.Value}%",
            FontSize = 12, FontFamily = new FontFamily("Consolas"),
            Foreground = fg, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(opLabel, 1);
        opSlider.ValueChanged += (_, e) => opLabel.Text = $"{(int)e.NewValue}%";
        opGrid.Children.Add(opSlider); opGrid.Children.Add(opLabel);
        HomeStylePanel.Children.Add(opGrid);

        // 適用ボタン
        var capturedId = _selectedHomeId;
        var applyBtn = new Button
        {
            Content = "このコンポーネントに適用",
            Style = (Style)Application.Current.Resources["PrimaryButton"],
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        applyBtn.Click += (_, _) =>
        {
            _svc.UpdateSectionTheme(capturedId, new SectionTheme
            {
                BgColor     = NullIfEmpty(bgBox.Text),
                TextColor   = NullIfEmpty(textBox.Text),
                BorderColor = NullIfEmpty(borderBox.Text),
                Opacity     = opSlider.Value / 100.0
            });
            BuildHomePreview();
            _previewHome?.Refresh();   // ライブプレビューにも即反映
        };
        HomeStylePanel.Children.Add(applyBtn);

        var resetBtn = new Button
        {
            Content = "リセット",
            Style = (Style)Application.Current.Resources["SecondaryButton"],
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 8, 0, 0)
        };
        resetBtn.Click += (_, _) =>
        {
            _svc.UpdateSectionTheme(capturedId, new SectionTheme());
            UpdateHomeStylePanel();
            BuildHomePreview();
            _previewHome?.Refresh();   // ライブプレビューにも即反映
        };
        HomeStylePanel.Children.Add(resetBtn);
    }

    /// <summary>スタイル編集パネルに選択待ちのプレースホルダーを表示する。</summary>
    private void ShowHomeStylePlaceholder()
    {
        HomeStylePanel.Children.Clear();
        HomeStylePanel.Children.Add(new TextBlock
        {
            Text = "コンポーネントをクリックするとスタイルを編集できます。",
            FontSize = 12, Foreground = TryBrush("TextDimBrush") ?? Brushes.Gray,
            TextWrapping = TextWrapping.Wrap
        });
    }

    /// <summary>カラー入力欄に添えるプレビュードットを生成して返す。</summary>
    private static Border ColorPreviewDot(Brush borderBrush) => new Border
    {
        Width = 24, Height = 24, CornerRadius = new CornerRadius(4),
        BorderBrush = borderBrush, BorderThickness = new Thickness(1),
        Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>HEX文字列をColorに変換し失敗時はnullを返す。</summary>
    private static Color? TryParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return (Color)ColorConverter.ConvertFromString(s); }
        catch { return null; }
    }

    // ══════════════════════════════════════════════
    //  Tab 2 — 各画面の個別設定（インラインエディター）
    // ══════════════════════════════════════════════

    /// <summary>各画面コンポーネントのスタイル設定パネルを構築する。</summary>
    private void BuildContent()
    {
        ContentPanel.Children.Clear();

        var fg        = TryBrush("TextPrimaryBrush") ?? Brushes.White;
        var fgDim     = TryBrush("TextDimBrush")     ?? Brushes.Gray;
        var bgSec     = TryBrush("BgSecondaryBrush") ?? Brushes.DimGray;
        var borderBr  = TryBrush("BorderBrush")       ?? Brushes.DarkGray;
        var accentCyan = TryBrush("AccentCyanBrush")  ?? Brushes.Cyan;

        ContentPanel.Children.Add(new TextBlock
        {
            Text = "各画面のコンポーネントの外観を個別に設定します。色は HEX コードで入力（例: #1A2B3C）。空欄はデフォルト色。",
            FontSize = 12, Foreground = fgDim, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 24)
        });

        foreach (var (screen, components) in SCREENS)
        {
            ContentPanel.Children.Add(new TextBlock
            {
                Text = screen,
                FontFamily = new FontFamily("Yu Gothic UI"), FontWeight = FontWeights.Bold,
                FontSize = 14, Foreground = fg, Margin = new Thickness(0, 0, 0, 8)
            });

            var screenCard = new Border
            {
                Background = bgSec, BorderBrush = borderBr, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 20)
            };
            var screenStack = new StackPanel();
            screenCard.Child = screenStack;

            bool firstComp = true;
            foreach (var (key, lbl) in components)
            {
                var theme = _svc.GetSectionTheme(key);

                // セパレーター（2番目以降）
                if (!firstComp)
                    screenStack.Children.Add(new Border
                    {
                        BorderBrush = borderBr, BorderThickness = new Thickness(0, 1, 0, 0)
                    });
                firstComp = false;

                var compPanel = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

                // コンポーネント名
                compPanel.Children.Add(new TextBlock
                {
                    Text = lbl, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = fg, Margin = new Thickness(0, 0, 0, 10)
                });

                // 色入力: 3カラム（背景色 / 文字色 / 枠線色）
                var colorGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var (bgPanel,  bgBox)     = MakeInlineColorInput("背景色",  theme.BgColor     ?? "", borderBr);
                var (txtPanel, txtBox)    = MakeInlineColorInput("文字色",  theme.TextColor   ?? "", borderBr);
                var (bdrPanel, borderBox) = MakeInlineColorInput("枠線色",  theme.BorderColor ?? "", borderBr);
                Grid.SetColumn(bgPanel,  0);
                Grid.SetColumn(txtPanel, 2);
                Grid.SetColumn(bdrPanel, 4);
                colorGrid.Children.Add(bgPanel);
                colorGrid.Children.Add(txtPanel);
                colorGrid.Children.Add(bdrPanel);
                compPanel.Children.Add(colorGrid);

                // 透明度スライダー
                var opRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                opRow.Children.Add(new TextBlock
                {
                    Text = "透明度", FontSize = 11, Foreground = fgDim,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
                });
                var opSlider = new Slider
                {
                    Minimum = 0, Maximum = 100, Value = Math.Clamp(theme.Opacity * 100, 0, 100),
                    VerticalAlignment = VerticalAlignment.Center, Foreground = accentCyan
                };
                var opLabel = new TextBlock
                {
                    Text = $"{(int)opSlider.Value}%", FontSize = 11,
                    FontFamily = new FontFamily("Consolas"), Foreground = fgDim,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right
                };
                opSlider.ValueChanged += (_, ev) => opLabel.Text = $"{(int)ev.NewValue}%";
                Grid.SetColumn(opSlider, 1);
                Grid.SetColumn(opLabel,  2);
                opRow.Children.Add(opSlider);
                opRow.Children.Add(opLabel);
                compPanel.Children.Add(opRow);

                // ボタン行
                var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
                var capturedKey = key;

                var applyBtn = new Button
                {
                    Content = "適用", Padding = new Thickness(16, 5, 16, 5), FontSize = 11,
                    Style = (Style)Application.Current.Resources["PrimaryButton"],
                    Margin = new Thickness(0, 0, 8, 0)
                };
                applyBtn.Click += (_, _) =>
                {
                    _svc.UpdateSectionTheme(capturedKey, new SectionTheme
                    {
                        BgColor     = NullIfEmpty(bgBox.Text),
                        TextColor   = NullIfEmpty(txtBox.Text),
                        BorderColor = NullIfEmpty(borderBox.Text),
                        Opacity     = opSlider.Value / 100.0
                    });
                    BuildContent();
                };

                var resetBtn = new Button
                {
                    Content = "リセット", Padding = new Thickness(12, 5, 12, 5), FontSize = 11,
                    Style = (Style)Application.Current.Resources["SecondaryButton"]
                };
                resetBtn.Click += (_, _) =>
                {
                    _svc.UpdateSectionTheme(capturedKey, new SectionTheme());
                    BuildContent();
                };

                btnRow.Children.Add(applyBtn);
                btnRow.Children.Add(resetBtn);
                compPanel.Children.Add(btnRow);

                screenStack.Children.Add(compPanel);
            }

            // ── 画面固有の追加設定 ──
            switch (screen)
            {
                case "タスク管理":
                    screenStack.Children.Add(new Border
                        { BorderBrush = borderBr, BorderThickness = new Thickness(0, 1, 0, 0) });
                    screenStack.Children.Add(BuildTaskGanttExtraPanel(borderBr, fg, fgDim, accentCyan));
                    break;

                case "カテゴリー管理":
                    screenStack.Children.Add(new Border
                        { BorderBrush = borderBr, BorderThickness = new Thickness(0, 1, 0, 0) });
                    screenStack.Children.Add(BuildBgExtraPanel(
                        "カテゴリー一覧の背景", borderBr, fg, fgDim, accentCyan,
                        () => _svc.CategoryListBgColor,
                        () => _svc.CategoryListBgOpacity,
                        (c, o) => _svc.UpdateCategoryListBackground(c, o)));
                    break;

                case "プロジェクト管理":
                    screenStack.Children.Add(new Border
                        { BorderBrush = borderBr, BorderThickness = new Thickness(0, 1, 0, 0) });
                    screenStack.Children.Add(BuildBgExtraPanel(
                        "プロジェクト一覧の背景", borderBr, fg, fgDim, accentCyan,
                        () => _svc.ProjectListBgColor,
                        () => _svc.ProjectListBgOpacity,
                        (c, o) => _svc.UpdateProjectListBackground(c, o)));
                    break;
            }

            ContentPanel.Children.Add(screenCard);
        }

        // ── アプリ設定（詳細設定画面の背景） ──
        ContentPanel.Children.Add(new TextBlock
        {
            Text = "アプリ設定",
            FontFamily = new FontFamily("Yu Gothic UI"), FontWeight = FontWeights.Bold,
            FontSize = 14, Foreground = fg, Margin = new Thickness(0, 0, 0, 8)
        });
        var appCard = new Border
        {
            Background = bgSec, BorderBrush = borderBr, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 20)
        };
        var appStack = new StackPanel();
        appCard.Child = appStack;
        appStack.Children.Add(BuildBgExtraPanel(
            "詳細設定画面の背景", borderBr, fg, fgDim, accentCyan,
            () => _svc.AppSettingsBgColor,
            () => _svc.AppSettingsBgOpacity,
            (c, o) => _svc.UpdateAppSettingsBackground(c, o)));
        ContentPanel.Children.Add(appCard);
    }

    // ── タスク管理画面固有設定（タスク行 + ガント行） ────────────────

    /// <summary>タスク行・ガント行の透明度・色設定パネルを構築して返す。</summary>
    private StackPanel BuildTaskGanttExtraPanel(Brush borderBr, Brush fg, Brush fgDim, Brush accentCyan)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

        panel.Children.Add(new TextBlock
        {
            Text = "タスク行・ガント行設定", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = fg, Margin = new Thickness(0, 0, 0, 10)
        });

        // タスク行の透過度
        panel.Children.Add(new TextBlock
            { Text = "タスク行の透過度", FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4) });
        var taskOpRow = MakeSliderRow(Math.Clamp(_svc.TaskRowOpacity * 100, 10, 100), 10, 100, accentCyan, fgDim);
        panel.Children.Add(taskOpRow.grid);
        panel.Children.Add(new TextBlock
            { Text = "※ タスク行全体の不透明度（10%〜100%）", FontSize = 10, Foreground = fgDim, Margin = new Thickness(0, 2, 0, 10) });

        // ガント行の透過度
        panel.Children.Add(new TextBlock
            { Text = "ガント行の透過度", FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4) });
        var ganttOpRow = MakeSliderRow(Math.Clamp(_svc.GanttRowOpacity * 100, 0, 100), 0, 100, accentCyan, fgDim);
        panel.Children.Add(ganttOpRow.grid);
        panel.Children.Add(new TextBlock
            { Text = "※ ガント行の不透明度（0%=完全透明）", FontSize = 10, Foreground = fgDim, Margin = new Thickness(0, 2, 0, 10) });

        // ガント行の色
        panel.Children.Add(new TextBlock
            { Text = "ガント行の色", FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4) });
        var ganttColorBox = new TextBox
        {
            Text = _svc.GanttRowColor ?? "#EBEBEB",
            Style = (Style)Application.Current.Resources["DarkTextBox"],
            FontFamily = new FontFamily("Consolas"), FontSize = 11, Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4)
        };
        panel.Children.Add(ganttColorBox);

        // 行の枠線色
        panel.Children.Add(new TextBlock
            { Text = "行の枠線色", FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 6, 0, 4) });
        var borderColorBox = new TextBox
        {
            Text = _svc.RowBorderColor ?? "#606060",
            Style = (Style)Application.Current.Resources["DarkTextBox"],
            FontFamily = new FontFamily("Consolas"), FontSize = 11, Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10)
        };
        panel.Children.Add(borderColorBox);
        panel.Children.Add(new TextBlock
            { Text = "※ カテゴリー行・タスク行の枠線色", FontSize = 10, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 10) });

        // ボタン
        var applyBtn = new Button
        {
            Content = "適用", Padding = new Thickness(16, 5, 16, 5), FontSize = 11,
            Style = (Style)Application.Current.Resources["PrimaryButton"],
            Margin = new Thickness(0, 0, 8, 0)
        };
        applyBtn.Click += (_, _) =>
        {
            _svc.UpdateAppearanceSettings(
                _svc.BackgroundImagePath, _svc.AppMode,
                taskOpRow.slider.Value  / 100.0,
                NullIfEmpty(ganttColorBox.Text) ?? "#EBEBEB",
                ganttOpRow.slider.Value / 100.0,
                NullIfEmpty(borderColorBox.Text) ?? "#606060");
            BuildContent();
        };
        var resetBtn = new Button
        {
            Content = "リセット", Padding = new Thickness(12, 5, 12, 5), FontSize = 11,
            Style = (Style)Application.Current.Resources["SecondaryButton"]
        };
        resetBtn.Click += (_, _) =>
        {
            _svc.UpdateAppearanceSettings(_svc.BackgroundImagePath, _svc.AppMode,
                1.0, "#EBEBEB", 1.0, "#606060");
            BuildContent();
        };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        btnRow.Children.Add(applyBtn);
        btnRow.Children.Add(resetBtn);
        panel.Children.Add(btnRow);

        return panel;
    }

    // ── 背景色＋透過度の汎用パネル ───────────────────────────────────

    /// <summary>背景色と透過度を設定する汎用パネルを構築して返す。</summary>
    private StackPanel BuildBgExtraPanel(
        string title, Brush borderBr, Brush fg, Brush fgDim, Brush accentCyan,
        Func<string> getColor, Func<double> getOpacity, Action<string, double> save)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };

        panel.Children.Add(new TextBlock
        {
            Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = fg, Margin = new Thickness(0, 0, 0, 10)
        });

        // 背景色入力
        panel.Children.Add(new TextBlock
            { Text = "背景色", FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4) });
        var colorDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var colorPreview = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(3),
            BorderBrush = borderBr, BorderThickness = new Thickness(1),
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(colorPreview, Dock.Right);
        var colorBox = new TextBox
        {
            Text = getColor(),
            Style = (Style)Application.Current.Resources["DarkTextBox"],
            FontFamily = new FontFamily("Consolas"), FontSize = 11
        };
        void UpdateColorPreview()
        {
            var c = TryParseColor(colorBox.Text);
            colorPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
        }
        colorBox.TextChanged += (_, _) => UpdateColorPreview();
        UpdateColorPreview();
        colorDock.Children.Add(colorPreview);
        colorDock.Children.Add(colorBox);
        panel.Children.Add(colorDock);

        // 透過度スライダー
        panel.Children.Add(new TextBlock
            { Text = "透過度", FontSize = 11, Foreground = fgDim, Margin = new Thickness(0, 0, 0, 4) });
        var opRow = MakeSliderRow(Math.Clamp(getOpacity() * 100, 0, 100), 0, 100, accentCyan, fgDim);
        opRow.grid.Margin = new Thickness(0, 0, 0, 10);
        panel.Children.Add(opRow.grid);

        // ボタン
        var applyBtn = new Button
        {
            Content = "適用", Padding = new Thickness(16, 5, 16, 5), FontSize = 11,
            Style = (Style)Application.Current.Resources["PrimaryButton"],
            Margin = new Thickness(0, 0, 8, 0)
        };
        applyBtn.Click += (_, _) =>
        {
            save(NullIfEmpty(colorBox.Text) ?? "#2F2F2F", opRow.slider.Value / 100.0);
            BuildContent();
        };
        var resetBtn = new Button
        {
            Content = "リセット", Padding = new Thickness(12, 5, 12, 5), FontSize = 11,
            Style = (Style)Application.Current.Resources["SecondaryButton"]
        };
        resetBtn.Click += (_, _) => { save("#2F2F2F", 1.0); BuildContent(); };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
        btnRow.Children.Add(applyBtn);
        btnRow.Children.Add(resetBtn);
        panel.Children.Add(btnRow);

        return panel;
    }

    // ── スライダー行ヘルパー ─────────────────────────────────────────

    /// <summary>値・範囲指定のスライダー行UIを生成して返す。</summary>
    private static (Grid grid, Slider slider) MakeSliderRow(
        double value, double min, double max, Brush accent, Brush fgDim)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value,
            VerticalAlignment = VerticalAlignment.Center, Foreground = accent
        };
        var label = new TextBlock
        {
            Text = $"{(int)value}%", FontSize = 11,
            FontFamily = new FontFamily("Consolas"), Foreground = fgDim,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right
        };
        slider.ValueChanged += (_, ev) => label.Text = $"{(int)ev.NewValue}%";
        Grid.SetColumn(label, 1);
        grid.Children.Add(slider);
        grid.Children.Add(label);

        return (grid, slider);
    }

    // ラベル付きカラー入力（テキストボックス + プレビュードット）
    /// <summary>ラベル付きカラー入力欄とプレビュードットを生成して返す。</summary>
    private (StackPanel panel, TextBox textBox) MakeInlineColorInput(string label, string initialValue, Brush borderBrush)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = label, FontSize = 10,
            Foreground = TryBrush("TextDimBrush") ?? Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 3)
        });

        var inputRow = new DockPanel();
        var preview = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(3),
            BorderBrush = borderBrush, BorderThickness = new Thickness(1),
            Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(preview, Dock.Right);

        var textBox = new TextBox
        {
            Text = initialValue,
            Style = (Style)Application.Current.Resources["DarkTextBox"],
            FontSize = 11
        };

        void UpdatePreview()
        {
            var c = TryParseColor(textBox.Text);
            preview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
        }
        textBox.TextChanged += (_, _) => UpdatePreview();
        UpdatePreview();

        inputRow.Children.Add(preview);
        inputRow.Children.Add(textBox);
        panel.Children.Add(inputRow);

        return (panel, textBox);
    }

    /// <summary>すべてのUIカスタマイズをデフォルトにリセットする。</summary>
    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (!AppDialog.Confirm("すべてのUIカスタマイズをリセットしますか？\nこの操作は元に戻せません。",
            "リセット確認", Window.GetWindow(this))) return;

        _svc.ResetAllSectionThemes();
        _svc.SaveHomeLayout(new Dictionary<string, HomeLayoutSlot>());
        _svc.SaveHomeLanes(new List<double>(), new List<double>());

        _gridSlots = _svc.GetEffectiveHomeLayout()
            .Select(s => new HomeLayoutSlot
            {
                ComponentId = s.ComponentId, Visible = s.Visible,
                Row = s.Row, Column = s.Column,
                RowSpan = s.RowSpan, ColumnSpan = s.ColumnSpan,
            })
            .ToList();
        _homeSlots = _gridSlots.ToDictionary(s => s.ComponentId, s => s);
        _columnWidths = _svc.GetEffectiveColumnWidths();
        _rowHeights   = _svc.GetEffectiveRowHeights();

        _selectedHomeId = null;
        BuildHomePreview();
        ShowHomeStylePlaceholder();
        BuildContent();
    }

    // ══════════════════════════════════════════════
    //  Tab 4 — プリセット
    // ══════════════════════════════════════════════

    /// <summary>プリセット一覧パネルを再構築する。</summary>
    private void BuildPresetPanel()
    {
        if (PresetPanel == null) return;
        PresetPanel.Children.Clear();

        var presets    = _svc.GetAllPresets();
        var wrapPanel  = new WrapPanel { Orientation = Orientation.Horizontal };

        foreach (var preset in presets)
            wrapPanel.Children.Add(BuildPresetCard(preset));

        PresetPanel.Children.Add(wrapPanel);
    }

    /// <summary>テーマプリセット1件分のカードUIを生成して返す。</summary>
    private Border BuildPresetCard(ThemePreset preset)
    {
        var fg       = TryBrush("TextPrimaryBrush") ?? Brushes.White;
        var fgDim    = TryBrush("TextDimBrush")     ?? Brushes.Gray;
        var borderBr = TryBrush("BorderBrush")      ?? Brushes.DarkGray;

        // アクセント色・背景色をプリセットから取得（null → デフォルト色）
        var accentColor = TryParseColor(preset.Theme.AccentCyan ?? "#00BFD8")
                          ?? Color.FromRgb(0x00, 0xBF, 0xD8);
        var bgCardColor = TryParseColor(preset.Theme.BgCard ?? "#252535")
                          ?? Color.FromRgb(0x25, 0x25, 0x35);

        var card = new Border
        {
            Width           = 152,
            Margin          = new Thickness(0, 0, 8, 8),
            BorderBrush     = borderBr,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            ClipToBounds    = true,
        };

        var mainStack = new StackPanel();

        // ── カラーストリップ（グラデーション）──
        var strip = new Border
        {
            Height     = 36,
            Background = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0.5),
                EndPoint   = new System.Windows.Point(1, 0.5),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(accentColor, 0.0),
                    new GradientStop(bgCardColor,  0.6),
                    new GradientStop(bgCardColor,  1.0),
                }
            }
        };

        // プリセット名（ストリップ内）
        var nameGrid = new Grid();
        nameGrid.Children.Add(new TextBlock
        {
            Text               = preset.Name,
            FontSize           = 11,
            FontWeight         = FontWeights.SemiBold,
            Foreground         = Brushes.White,
            VerticalAlignment  = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming        = TextTrimming.CharacterEllipsis,
            Margin              = new Thickness(6, 0, 6, 0),
            Effect              = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, Opacity = 0.8, BlurRadius = 4, ShadowDepth = 0
            }
        });
        strip.Child = nameGrid;
        mainStack.Children.Add(strip);

        // ── ボタンエリア ──
        var btnStack = new StackPanel { Margin = new Thickness(8, 8, 8, 8) };

        // 適用ボタン
        var capturedPreset = preset;
        var applyBtn = new Button
        {
            Content             = "適用",
            FontSize            = 11,
            Padding             = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (FindResource("PrimaryButton") is Style primStyle)
            applyBtn.Style = primStyle;
        applyBtn.Click += (_, _) => ApplyPreset(capturedPreset);
        btnStack.Children.Add(applyBtn);

        // 削除ボタン（ユーザープリセットのみ）
        if (!preset.IsBuiltIn)
        {
            var delBtn = new Button
            {
                Content             = "削除",
                FontSize            = 11,
                Padding             = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin              = new Thickness(0, 4, 0, 0),
            };
            if (FindResource("DangerButton") is Style dangerStyle)
                delBtn.Style = dangerStyle;
            delBtn.Click += (_, _) =>
            {
                if (!AppDialog.Confirm($"「{capturedPreset.Name}」を削除しますか？",
                        "削除確認", Window.GetWindow(this))) return;
                _svc.DeleteUserPreset(capturedPreset.Id);
                BuildPresetPanel();
            };
            btnStack.Children.Add(delBtn);
        }

        mainStack.Children.Add(btnStack);
        card.Child = mainStack;
        return card;
    }

    /// <summary>指定プリセットを適用してテーマを更新する。</summary>
    private void ApplyPreset(ThemePreset preset)
    {
        _svc.ApplyPreset(preset);
        App.ApplyTheme(_svc.Theme);
        if (Window.GetWindow(this) is Views.MainWindow mw)
            mw.ApplyBackground();
        Refresh();
    }

    /// <summary>現在のテーマを名前付きプリセットとして保存する。</summary>
    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var bg  = TryBrush("BgCardBrush")      ?? Brushes.Black;
        var fg  = TryBrush("TextPrimaryBrush") ?? Brushes.White;
        var dim = TryBrush("TextDimBrush")     ?? Brushes.Gray;

        var win = new Window
        {
            Title = "プリセットを保存", Width = 320, Height = 148,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = (Brush)bg
        };
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(new TextBlock
        {
            Text = "プリセット名", FontSize = 12, Foreground = (Brush)dim,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var txtName = new TextBox
        {
            Style = (Style)FindResource("DarkTextBox"),
            Margin = new Thickness(0, 0, 0, 16)
        };
        sp.Children.Add(txtName);
        var btnOk = new Button
        {
            Content = "保存",
            Style   = (Style)FindResource("PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(24, 6, 24, 6)
        };
        btnOk.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(txtName.Text)) win.DialogResult = true; };
        txtName.KeyDown += (_, ev) => { if (ev.Key == System.Windows.Input.Key.Enter && !string.IsNullOrWhiteSpace(txtName.Text)) win.DialogResult = true; };
        sp.Children.Add(btnOk);
        win.Content = sp;
        win.Loaded += (_, _) => txtName.Focus();

        if (win.ShowDialog() != true) return;
        var name = txtName.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var preset = _svc.CreatePresetFromCurrent(name);
        _svc.SaveUserPreset(preset);
        BuildPresetPanel();
    }

    // ── 一括適用 ────────────────────────────────────────────────────

    private void SliderBulkOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LblBulkOpacity != null) LblBulkOpacity.Text = $"{(int)e.NewValue}%";
    }

    private void BulkBg_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (BulkBgPreview == null) return;
        var c = TryParseColor(TxtBulkBgColor.Text);
        BulkBgPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
    }

    private void BulkText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (BulkTextPreview == null) return;
        var c = TryParseColor(TxtBulkTextColor.Text);
        BulkTextPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
    }

    private void BulkBorder_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (BulkBorderPreview == null) return;
        var c = TryParseColor(TxtBulkBorderColor.Text);
        BulkBorderPreview.Background = c.HasValue ? new SolidColorBrush(c.Value) : Brushes.Transparent;
    }

    /// <summary>一括適用ボタンで全コンポーネントに指定スタイルを反映する。</summary>
    private void BulkApply_Click(object sender, RoutedEventArgs e)
    {
        var bulkBg      = NullIfEmpty(TxtBulkBgColor.Text);
        var bulkText    = NullIfEmpty(TxtBulkTextColor.Text);
        var bulkBorder  = NullIfEmpty(TxtBulkBorderColor.Text);
        var bulkOpacity = SliderBulkOpacity.Value / 100.0;

        foreach (var key in ALL_BULK_TARGET_KEYS)
        {
            var existing = _svc.GetSectionTheme(key);
            _svc.UpdateSectionTheme(key, new SectionTheme
            {
                BgColor     = bulkBg     ?? existing.BgColor,
                TextColor   = bulkText   ?? existing.TextColor,
                BorderColor = bulkBorder ?? existing.BorderColor,
                Opacity     = bulkOpacity
            });
        }

        // プレビューを再構築
        BuildHomePreview();
        BuildContent();
    }

    // ══════════════════════════════════════════════
    //  Tab 3 — ツールバー順序
    // ══════════════════════════════════════════════

    /// <summary>メニュー順序リストボックスを最新の並び順で更新する。</summary>
    private void RefreshMenuOrderList()
    {
        TbListBox.Items.Clear();
        foreach (var name in _menuOrder)
            TbListBox.Items.Add(name);
        if (TbListBox.Items.Count > 0)
            TbListBox.SelectedIndex = 0;
    }

    private void TbMoveUp_Click(object sender, RoutedEventArgs e)   => TbMove(-1);
    private void TbMoveDown_Click(object sender, RoutedEventArgs e) => TbMove(+1);

    /// <summary>選択中のメニュー項目を指定方向に移動する。</summary>
    private void TbMove(int delta)
    {
        int idx = TbListBox.SelectedIndex;
        if (idx < 0) return;
        int newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= _menuOrder.Count) return;
        (_menuOrder[idx], _menuOrder[newIdx]) = (_menuOrder[newIdx], _menuOrder[idx]);
        RefreshMenuOrderList();
        TbListBox.SelectedIndex = newIdx;
    }

    // ══════════════════════════════════════════════
    //  保存
    // ══════════════════════════════════════════════

    /// <summary>テーマ・レイアウト・メニュー順序を保存してアプリに適用する。</summary>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var font = (CbFontFamily.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        var newTheme = new ThemeColors
        {
            TextPrimary  = NullIfEmpty(TxtColorTextPrimary.Text),
            TextSecond   = NullIfEmpty(TxtColorTextSecond.Text),
            AccentCyan   = NullIfEmpty(TxtColorAccent.Text),
            BgSecondary  = NullIfEmpty(TxtColorBgSecond.Text),
            BgCard       = NullIfEmpty(TxtColorBgCard.Text),
            Border       = NullIfEmpty(TxtColorBorder.Text),
            FontFamily   = NullIfEmpty(font),
            ButtonBg     = NullIfEmpty(TxtColorButtonBg.Text),
            ButtonBorder = NullIfEmpty(TxtColorButtonBorder.Text),
            DropdownBg   = NullIfEmpty(TxtColorDropdownBg.Text),
        };

        _svc.SaveHomeLayout(_homeSlots);
        _svc.SaveHomeLanes(_columnWidths, _rowHeights);
        _svc.SaveTheme(newTheme);
        _svc.SaveMenuOrder(_menuOrder);

        if (Window.GetWindow(this) is Views.MainWindow mw)
        {
            mw.ApplyBackground();
            mw.ApplyMenuOrder();
        }
        App.ApplyTheme(_svc.Theme);
        App.ApplyFont(_svc.Theme.FontFamily, Window.GetWindow(this)!);
        _vm.RefreshAlerts();

        if (!_svc.SkipRestartConfirm && IsThemeChanged(newTheme))
            ShowRestartConfirmDialog();

        _originalTheme = newTheme;
        BuildContent();
    }

    /// <summary>新旧テーマを比較して変更があればtrueを返す。</summary>
    private bool IsThemeChanged(ThemeColors n) =>
        n.TextPrimary  != _originalTheme.TextPrimary  ||
        n.TextSecond   != _originalTheme.TextSecond   ||
        n.AccentCyan   != _originalTheme.AccentCyan   ||
        n.BgSecondary  != _originalTheme.BgSecondary  ||
        n.BgCard       != _originalTheme.BgCard       ||
        n.Border       != _originalTheme.Border       ||
        n.FontFamily   != _originalTheme.FontFamily   ||
        n.ButtonBg     != _originalTheme.ButtonBg     ||
        n.ButtonBorder != _originalTheme.ButtonBorder ||
        n.DropdownBg   != _originalTheme.DropdownBg;

    /// <summary>テーマ変更後に再起動を促す確認ダイアログを表示する。</summary>
    private void ShowRestartConfirmDialog()
    {
        var bg  = TryBrush("BgCardBrush")      ?? Brushes.Black;
        var fg  = TryBrush("TextPrimaryBrush") ?? Brushes.White;
        var dim = TryBrush("TextDimBrush")     ?? Brushes.Gray;

        var win = new Window
        {
            Title = "テーマ設定変更", Width = 380, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = (Brush)bg
        };
        var sp = new StackPanel { Margin = new Thickness(24) };
        sp.Children.Add(new TextBlock
        {
            Text = "テーマ設定を変更しました。\n一部の変更はアプリを再起動後に完全適用されます。",
            Foreground = (Brush)fg, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        var chk = new CheckBox
        {
            Content = "今後このメッセージを表示しない",
            Foreground = (Brush)dim, FontSize = 12, Margin = new Thickness(0, 0, 0, 16)
        };
        sp.Children.Add(chk);
        var okBtn = new Button
        {
            Content = "OK", Style = (Style)FindResource("PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(20, 6, 20, 6)
        };
        okBtn.Click += (_, _) => win.Close();
        sp.Children.Add(okBtn);
        win.Content = sp;
        win.ShowDialog();

        if (chk.IsChecked == true)
            _svc.SetSkipRestartConfirm(true);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("AppSettings");

    // ══════════════════════════════════════════════
    //  ユーティリティ
    // ══════════════════════════════════════════════

    /// <summary>アプリリソースからブラシを取得し失敗時はnullを返す。</summary>
    private static Brush? TryBrush(string key)
    {
        try { return Application.Current.Resources[key] as Brush; }
        catch { return null; }
    }

    /// <summary>空白のみの文字列をnullに変換し、それ以外はトリムして返す。</summary>
    private static string? NullIfEmpty(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
