using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using TKer.Services;

namespace TKer.Views.Dialogs;

/// <summary>ツールバーメニュー項目の順序を変更するダイアログ（コードオンリー）</summary>
public class ToolbarCustomizeDialog : Window
{
    private readonly AppSettingsService _svc;
    private readonly Menu               _menu;
    private readonly List<string>       _order;
    private readonly ListBox            _listBox;

    public ToolbarCustomizeDialog(AppSettingsService svc, Menu menu)
    {
        _svc   = svc;
        _menu  = menu;
        _order = svc.MenuOrder.ToList();

        // ── ウィンドウ設定（他ダイアログと統一）─────────────
        Title  = "ツールバーカスタマイズ";
        Width  = 380;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle  = WindowStyle.None;
        ResizeMode   = ResizeMode.NoResize;
        Background   = Res("BgCardBrush");

        // WindowChrome（ドラッグ移動を有効化）
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight       = 36,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        // Escape / CloseWindow コマンド
        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => { DialogResult = false; }), Key.Escape, ModifierKeys.None));

        // ── レイアウト ─────────────────────────────────────
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ── タイトルバー ───────────────────────────────────
        var titleBar = BuildTitleBar();
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        // ── コンテンツ ─────────────────────────────────────
        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 0) };

        content.Children.Add(new TextBlock
        {
            Text       = "メニュー項目の表示順",
            FontSize   = 15, FontWeight = FontWeights.Bold,
            Foreground = Res("TextPrimaryBrush"),
            Margin     = new Thickness(0, 0, 0, 6)
        });
        content.Children.Add(new TextBlock
        {
            Text       = "項目を選択して ▲ ▼ ボタンで順序を変更します",
            FontSize   = 11, Foreground = Res("TextDimBrush"),
            Margin     = new Thickness(0, 0, 0, 14)
        });

        _listBox = new ListBox
        {
            Height          = 180,
            Background      = Res("BgSecondaryBrush"),
            Foreground      = Res("TextPrimaryBrush"),
            BorderBrush     = Res("BorderBrush"),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(0, 0, 0, 10)
        };
        RefreshList();
        content.Children.Add(_listBox);

        // ▲ ▼ ボタン
        var movePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 0) };
        movePanel.Children.Add(MakeSecondaryBtn("▲ 上へ",  () => MoveItem(-1)));
        movePanel.Children.Add(MakeSecondaryBtn("▼ 下へ",  () => MoveItem(+1)));
        content.Children.Add(movePanel);

        var sv = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(sv, 1);
        root.Children.Add(sv);

        // ── フッター（他ダイアログと統一）────────────────────
        var footer = new Border
        {
            Background      = Res("BgSecondaryBrush"),
            BorderBrush     = Res("BorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding         = new Thickness(24, 12, 24, 12)
        };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(MakeSecondaryBtn("キャンセル", () => { DialogResult = false; }, margin: new Thickness(0, 0, 8, 0)));
        btnRow.Children.Add(MakePrimaryBtn("保存", SaveAndClose));
        footer.Child = btnRow;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }

    // ── タイトルバー（標準パターン） ──────────────────────
    private Border BuildTitleBar()
    {
        var bar = new Border
        {
            Background      = Res("BgSecondaryBrush"),
            BorderBrush     = Res("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var dp = new DockPanel();

        // 閉じるボタン
        var closeBtn = new Button
        {
            Width           = 44,
            Height          = 36,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor          = Cursors.Arrow,
            Command         = SystemCommands.CloseWindowCommand
        };
        closeBtn.SetValue(WindowChrome.IsHitTestVisibleInChromeProperty, true);
        // ホバー効果
        closeBtn.MouseEnter += (_, _) => closeBtn.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x00, 0x00));
        closeBtn.MouseLeave += (_, _) => closeBtn.Background = Brushes.Transparent;
        closeBtn.Content = new TextBlock
        {
            Text              = "✕",
            FontSize          = 11,
            Foreground        = Res("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        DockPanel.SetDock(closeBtn, Dock.Right);

        // タイトルテキスト
        var titleTb = new TextBlock
        {
            Text              = Title,
            FontSize          = 13,
            FontWeight        = FontWeights.SemiBold,
            Foreground        = Res("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(16, 0, 0, 0)
        };

        dp.Children.Add(closeBtn);
        dp.Children.Add(titleTb);
        bar.Child = dp;
        return bar;
    }

    private void RefreshList()
    {
        _listBox.Items.Clear();
        foreach (var name in _order)
        {
            _listBox.Items.Add(new ListBoxItem
            {
                Content         = name,
                Foreground      = Res("TextPrimaryBrush"),
                Padding         = new Thickness(10, 6, 10, 6),
                FontSize        = 13
            });
        }
        if (_listBox.Items.Count > 0)
            _listBox.SelectedIndex = 0;
    }

    private void MoveItem(int delta)
    {
        int idx = _listBox.SelectedIndex;
        if (idx < 0) return;
        int newIdx = idx + delta;
        if (newIdx < 0 || newIdx >= _order.Count) return;

        (_order[idx], _order[newIdx]) = (_order[newIdx], _order[idx]);
        RefreshList();
        _listBox.SelectedIndex = newIdx;
    }

    private void SaveAndClose()
    {
        _svc.SaveMenuOrder(_order);
        DialogResult = true;
    }

    // ── ボタンヘルパー ────────────────────────────────────
    private Button MakeSecondaryBtn(string label, System.Action action, Thickness? margin = null)
    {
        var btn = new Button
        {
            Content         = label,
            Padding         = new Thickness(12, 6, 12, 6),
            Margin          = margin ?? new Thickness(0, 0, 8, 0),
            FontSize        = 12,
            Background      = Res("BgSecondaryBrush"),
            Foreground      = Res("TextPrimaryBrush"),
            BorderBrush     = Res("BorderBrush"),
            BorderThickness = new Thickness(1),
            Cursor          = Cursors.Hand
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    private static Button MakePrimaryBtn(string label, System.Action action)
    {
        var btn = new Button
        {
            Content         = label,
            Padding         = new Thickness(20, 6, 20, 6),
            FontSize        = 12,
            Background      = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)),
            Foreground      = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight      = FontWeights.SemiBold,
            Cursor          = Cursors.Hand
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    // ── リソース取得ヘルパー ──────────────────────────────
    private static Brush Res(string key) =>
        Application.Current.TryFindResource(key) as Brush
        ?? Brushes.Transparent;

    // ── RelayCommand（InputBinding 用） ───────────────────
    private sealed class RelayCommand(System.Action<object?> execute) : ICommand
    {
        public event System.EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => execute(p);
    }
}
