using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

/// <summary>保存済みウィンドウレイアウトをグリッド表示し、選択時にウィンドウを自動配置するページ。</summary>
public partial class WindowLayoutPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;

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
        var all = _vm.WindowLayoutService.All;
        NoItemBanner.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var layout in all)
            LayoutGrid.Items.Add(BuildCard(layout));
    }

    /// <summary>レイアウト1件分のカードUI（クリックで適用・編集・削除ボタン付き）を構築する。</summary>
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
            ToolTip         = "クリックでこの配置を復元"
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
            Margin     = new Thickness(0, 8, 0, 0)
        });

        var ops = new StackPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin              = new Thickness(0, 10, 0, 0)
        };
        ops.Children.Add(MakeMiniButton("編集", () => OnEdit(layout)));
        ops.Children.Add(MakeMiniButton("削除", () => OnDelete(layout)));
        sp.Children.Add(ops);

        card.Child = sp;
        // カード本体クリックで適用（子のボタンクリックは Button が e.Handled するので干渉しない）
        card.MouseLeftButtonUp += (_, _) => ApplyLayout(layout);
        return card;
    }

    private static Button MakeMiniButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text, FontSize = 10,
            Padding = new Thickness(8, 2, 8, 2),
            Margin  = new Thickness(4, 0, 0, 0),
            Cursor  = Cursors.Hand
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private void SaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new WindowLayoutSaveDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        _vm.WindowLayoutService.CaptureCurrent(dlg.LayoutName, dlg.LayoutDescription);
        BuildList();
    }

    private void OnEdit(WindowLayout layout)
    {
        var dlg = new WindowLayoutSaveDialog(layout.Name, layout.Description)
        { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        layout.Name        = dlg.LayoutName;
        layout.Description = dlg.LayoutDescription;
        _vm.WindowLayoutService.Update(layout);
        BuildList();
    }

    private void OnDelete(WindowLayout layout)
    {
        var result = MessageBox.Show(Window.GetWindow(this),
            $"「{layout.Name}」を削除しますか?",
            "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;
        _vm.WindowLayoutService.Delete(layout.Id);
        BuildList();
    }

    private void ApplyLayout(WindowLayout layout)
    {
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var failed = _vm.WindowLayoutService.Apply(layout);
            Mouse.OverrideCursor = null;
            if (failed.Count > 0)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "以下のウィンドウは配置できませんでした:\n\n" + string.Join("\n", failed),
                    "適用結果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }
}
