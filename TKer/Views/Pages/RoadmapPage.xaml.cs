using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>
/// ロードマップ項目をバージョン/期間ごとにグループ化して表示する試用ページ。
/// 各項目はカード形式で表示され、クリックで選択、F2 で編集、Ctrl+- で削除。
/// </summary>
public partial class RoadmapPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;

    private RoadmapItem? _selectedItem;
    private Border?      _selectedCard;

    /// <summary>ViewModel を受け取り初期化する。</summary>
    public RoadmapPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>テーマを適用してロードマップ項目を再構築する。</summary>
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("RM_Header"));
        BuildList();
    }

    /// <summary>項目をバージョン別にグループ化して描画する。</summary>
    private void BuildList()
    {
        GroupsPanel.Children.Clear();
        ClearSelection();

        var all = _vm.RoadmapService.All;
        NoItemBanner.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Version でグループ化（空文字は "(未分類)" に寄せる）
        var groups = all
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Version) ? "(未分類)" : i.Version)
            .OrderBy(g => g.Key);

        foreach (var g in groups)
            GroupsPanel.Children.Add(BuildGroupSection(g.Key, g.ToList()));
    }

    /// <summary>1グループ分の見出し+カード群セクションを返す。</summary>
    private Border BuildGroupSection(string version, System.Collections.Generic.List<RoadmapItem> items)
    {
        var sectionContainer = new Border { Margin = new Thickness(0, 0, 0, 20) };
        var sectionStack = new StackPanel();

        // バージョン見出し
        var headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };
        headerRow.Children.Add(new TextBlock
        {
            Text       = version,
            FontFamily = new FontFamily("Yu Gothic UI"),
            FontSize   = 16, FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        headerRow.Children.Add(new TextBlock
        {
            Text       = $"  ({items.Count}件)",
            FontSize   = 11,
            Foreground = (Brush)FindResource("TextDimBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        sectionStack.Children.Add(headerRow);

        // カード一覧（折り返し横並び）
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var item in items)
            wrap.Children.Add(BuildCard(item));
        sectionStack.Children.Add(wrap);

        sectionContainer.Child = sectionStack;
        return sectionContainer;
    }

    /// <summary>項目1件分のカードを返す（クリックで選択、ダブルクリックで編集）。</summary>
    private Border BuildCard(RoadmapItem item)
    {
        var card = new Border
        {
            Width           = 260,
            Margin          = new Thickness(0, 0, 12, 12),
            Padding         = new Thickness(14),
            Background      = (Brush)FindResource("BgCardBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(8),
            Cursor          = Cursors.Hand,
            Tag             = item,
            ToolTip         = "クリックで選択 ／ ダブルクリックで編集"
        };

        var sp = new StackPanel();

        // ステータスバッジ
        var (pillBg, pillText) = StatusColors(item.Status);
        var statusPill = new Border
        {
            Background      = new SolidColorBrush(pillBg),
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text       = item.Status,
                FontSize   = 10, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(pillText)
            }
        };
        sp.Children.Add(statusPill);

        // タイトル
        sp.Children.Add(new TextBlock
        {
            Text         = item.Title,
            FontSize     = 14, FontWeight = FontWeights.Bold,
            Foreground   = (Brush)FindResource("TextPrimaryBrush"),
            TextWrapping = TextWrapping.Wrap, MaxHeight = 42,
            Margin       = new Thickness(0, 8, 0, 0)
        });

        // 説明
        if (!string.IsNullOrEmpty(item.Description))
        {
            sp.Children.Add(new TextBlock
            {
                Text         = item.Description,
                FontSize     = 11,
                Foreground   = (Brush)FindResource("TextSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap, MaxHeight = 50,
                Margin       = new Thickness(0, 6, 0, 0)
            });
        }

        // 目標日
        if (item.TargetDate.HasValue)
        {
            sp.Children.Add(new TextBlock
            {
                Text       = $"目標: {item.TargetDate.Value:yyyy/MM/dd}",
                FontSize   = 10,
                Foreground = (Brush)FindResource("TextDimBrush"),
                Margin     = new Thickness(0, 10, 0, 0)
            });
        }

        card.Child = sp;
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ClickCount >= 2) OpenEditDialog(item);
            else                   SelectCard(card, item);
        };

        return card;
    }

    /// <summary>ステータス値に対応する (背景色, 文字色) を返す。</summary>
    private static (Color Bg, Color Fg) StatusColors(string status) => status switch
    {
        "進行中" => (Color.FromRgb(0x23, 0x83, 0xE2), Colors.White),
        "完了"   => (Color.FromRgb(0x52, 0x9E, 0x72), Colors.White),
        "保留"   => (Color.FromRgb(0xE5, 0x8A, 0x1F), Colors.White),
        _        => (Color.FromRgb(0x70, 0x70, 0x70), Colors.White),
    };

    // ── 選択管理 ─────────────────────────────────
    private void SelectCard(Border card, RoadmapItem item)
    {
        if (_selectedCard != null)
        {
            _selectedCard.BorderBrush     = (Brush)FindResource("BorderBrush");
            _selectedCard.BorderThickness = new Thickness(1);
        }
        _selectedCard = card;
        _selectedCard.BorderBrush     = (Brush)FindResource("AccentCyanBrush");
        _selectedCard.BorderThickness = new Thickness(2);
        _selectedItem = item;
        BtnEdit.IsEnabled   = true;
        BtnDelete.IsEnabled = true;
    }

    private void ClearSelection()
    {
        if (_selectedCard != null)
        {
            _selectedCard.BorderBrush     = (Brush)FindResource("BorderBrush");
            _selectedCard.BorderThickness = new Thickness(1);
        }
        _selectedCard = null;
        _selectedItem = null;
        BtnEdit.IsEnabled   = false;
        BtnDelete.IsEnabled = false;
    }

    private void OuterArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe is ScrollViewer)
            ClearSelection();
    }

    // ── ツールバー ────────────────────────────────
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new RoadmapItemDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _vm.RoadmapService.Add(dlg.Item.Title, dlg.Item.Description, dlg.Item.Version,
                               dlg.Item.Status, dlg.Item.TargetDate);
        BuildList();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;
        OpenEditDialog(_selectedItem);
    }

    private void OpenEditDialog(RoadmapItem item)
    {
        var dlg = new RoadmapItemDialog(item) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _vm.RoadmapService.Update(dlg.Item);
        BuildList();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem == null) return;
        var result = MessageBox.Show(Window.GetWindow(this),
            $"「{_selectedItem.Title}」を削除しますか?",
            "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;
        _vm.RoadmapService.Delete(_selectedItem.Id);
        BuildList();
    }

    // ── ショートカット ─────────────────────────────
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBoxBase) return;

        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   == ModifierKeys.Shift;

        if (e.Key == Key.F2 && _selectedItem != null)
        {
            Edit_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.OemSemicolon)
        {
            Add_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.OemMinus && _selectedItem != null)
        {
            Delete_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
