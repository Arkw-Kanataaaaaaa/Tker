using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>保存済みウィンドウレイアウトをグリッド表示し、ダブルクリックで自動配置するページ。</summary>
public partial class WindowLayoutPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;

    // 現在選択中のレイアウト（編集・削除ツールバーボタンの対象）
    private WindowLayout? _selectedLayout;
    private Border?       _selectedCard;

    /// <summary>ViewModel を受け取り初期化する。</summary>
    public WindowLayoutPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
    }

    /// <summary>テーマを適用してレイアウト一覧を再構築する。</summary>
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("WL_Header"));
        BuildList();
    }

    /// <summary>保存済みレイアウトを読み込んでカードグリッドを生成する。</summary>
    private void BuildList()
    {
        LayoutGrid.Items.Clear();
        ClearSelection();

        var all = _vm.WindowLayoutService.All;
        NoItemBanner.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var layout in all)
            LayoutGrid.Items.Add(BuildCard(layout));
    }

    /// <summary>
    /// レイアウト1件分のカードUIを構築する。
    /// 単クリックで選択、ダブルクリックで適用する。
    /// </summary>
    private Border BuildCard(WindowLayout layout)
    {
        var card = new Border
        {
            Width           = 240,
            Margin          = new Thickness(0, 0, 12, 12),
            Padding         = new Thickness(14),
            Background      = (Brush)FindResource("BgCardBrush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(10),
            Cursor          = Cursors.Hand,
            ToolTip         = "クリックで選択 ／ ダブルクリックで適用",
            Tag             = layout
        };

        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text         = layout.Name,
            FontSize     = 14, FontWeight = FontWeights.Bold,
            Foreground   = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        sp.Children.Add(new TextBlock
        {
            Text         = layout.Description,
            FontSize     = 11,
            Foreground   = (Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap, MaxHeight = 54,
            Margin       = new Thickness(0, 4, 0, 0)
        });
        sp.Children.Add(new TextBlock
        {
            Text       = $"{layout.Windows.Count} ウィンドウ ・ {layout.CreatedAt:yyyy/MM/dd}",
            FontSize   = 10,
            Foreground = (Brush)FindResource("TextDimBrush"),
            Margin     = new Thickness(0, 10, 0, 0)
        });
        card.Child = sp;

        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ClickCount >= 2) ApplyLayout(layout);
            else                   SelectCard(card, layout);
        };
        return card;
    }

    /// <summary>指定カードを選択状態にして縁色を変更し、ツールバーボタンを有効化する。</summary>
    private void SelectCard(Border card, WindowLayout layout)
    {
        if (_selectedCard != null)
        {
            _selectedCard.BorderBrush     = (Brush)FindResource("BorderBrush");
            _selectedCard.BorderThickness = new Thickness(1);
        }
        _selectedCard = card;
        _selectedCard.BorderBrush     = (Brush)FindResource("AccentCyanBrush");
        _selectedCard.BorderThickness = new Thickness(2);
        _selectedLayout = layout;
        BtnToolbarEdit.IsEnabled   = true;
        BtnToolbarDelete.IsEnabled = true;
    }

    /// <summary>カード選択状態を解除してツールバーボタンを無効化する。</summary>
    private void ClearSelection()
    {
        if (_selectedCard != null)
        {
            _selectedCard.BorderBrush     = (Brush)FindResource("BorderBrush");
            _selectedCard.BorderThickness = new Thickness(1);
        }
        _selectedCard   = null;
        _selectedLayout = null;
        BtnToolbarEdit.IsEnabled   = false;
        BtnToolbarDelete.IsEnabled = false;
    }

    // ── ツールバー ────────────────────────────────────
    /// <summary>追加ボタン: 編集画面へ遷移する（パターン選択は編集画面のサイドバーで行う）。</summary>
    private void ToolbarAdd_Click(object sender, RoutedEventArgs e)
    {
        _vm.EditingWindowLayoutId        = null;
        _vm.EditingWindowLayoutPatternId = null;
        _vm.NavigateToCommand.Execute("WindowLayoutEdit");
    }

    private void ToolbarEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLayout == null) return;
        _vm.EditingWindowLayoutId = _selectedLayout.Id;
        _vm.NavigateToCommand.Execute("WindowLayoutEdit");
    }

    private void ToolbarDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedLayout == null) return;
        var result = MessageBox.Show(Window.GetWindow(this),
            $"「{_selectedLayout.Name}」を削除しますか?",
            "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;
        _vm.WindowLayoutService.Delete(_selectedLayout.Id);
        BuildList();
    }

    // ── 適用 ─────────────────────────────────────────
    /// <summary>レイアウトを現在のデスクトップに適用し、成功・失敗を TKer 標準ダイアログで通知する。</summary>
    private void ApplyLayout(WindowLayout layout)
    {
        ApplyResult result;
        Mouse.OverrideCursor = Cursors.Wait;
        try     { result = _vm.WindowLayoutService.Apply(layout); }
        finally { Mouse.OverrideCursor = null; }

        ShowApplyResultDialog(result, "適用結果");
    }

    /// <summary>適用結果（アイコン+アプリ名+〇/×の表）を TKer 標準ダイアログで表示する。</summary>
    private void ShowApplyResultDialog(ApplyResult result, string title)
    {
        var dlg = new WindowLayoutApplyResultDialog(title, result) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }
}
