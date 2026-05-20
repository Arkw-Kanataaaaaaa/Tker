using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;
using TKer.Models;

namespace TKer.Views.Dialogs;

/// <summary>
/// タスクの進捗達成条件を設定・確認するダイアログ（コードオンリー）。
/// 他ダイアログと同一の標準構造：
///   Row0 = タイトルバー(36px)
///   Row1 = コンテンツ(ScrollViewer)
///   Row2 = フッター(OK/キャンセル)
/// </summary>
public class ProgressConditionDialog : Window
{
    private readonly TaskItem               _task;
    private readonly List<ProgressCondition> _conditions;

    // ── UI 参照（フィールド化してメソッド間で共有） ────────
    private readonly StackPanel  _listPanel;
    private readonly ProgressBar _progressBar;
    private readonly TextBlock   _progressLabel;

    /// <summary>タスクを受け取り進捗条件ダイアログのレイアウトと初期データを構築する。</summary>
    public ProgressConditionDialog(TaskItem task)
    {
        _task       = task;
        _conditions = task.ProgressConditions.Select(CloneCondition).ToList();

        // ── ウィンドウ設定（他ダイアログと統一）─────────────
        Title  = $"進捗達成条件 — {task.Name}";
        Width  = 520;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle  = WindowStyle.None;
        ResizeMode   = ResizeMode.CanResize;
        Background   = Res("BgCardBrush");

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight         = 36,
            ResizeBorderThickness = new Thickness(4),
            GlassFrameThickness   = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => { DialogResult = false; }), Key.Escape, ModifierKeys.None));

        // ── レイアウト ─────────────────────────────────────
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row0: タイトルバー
        var titleBar = BuildTitleBar();
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        // Row1: コンテンツ
        var contentSp = new StackPanel { Margin = new Thickness(24, 20, 24, 8) };

        // 追加ボタン
        var addRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        addRow.Children.Add(MakeAddBtn("＋ 手動確認",    () => AddCondition(ProgressConditionType.Manual)));
        addRow.Children.Add(MakeAddBtn("＋ ファイル存在", () => AddCondition(ProgressConditionType.FileExists)));
        addRow.Children.Add(MakeAddBtn("＋ アプリ起動",  () => AddCondition(ProgressConditionType.AppLaunched)));
        contentSp.Children.Add(addRow);

        // 達成率バー
        var progressRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        _progressBar = new ProgressBar
        {
            Height          = 8, Width = 200,
            Background      = Res("BgSecondaryBrush"),
            Foreground      = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _progressLabel = new TextBlock
        {
            Foreground        = Res("TextPrimaryBrush"),
            FontSize          = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(10, 0, 0, 0)
        };
        progressRow.Children.Add(_progressBar);
        progressRow.Children.Add(_progressLabel);
        contentSp.Children.Add(progressRow);

        // 条件リスト
        _listPanel = new StackPanel();
        contentSp.Children.Add(_listPanel);

        var sv = new ScrollViewer
        {
            Content = contentSp,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(sv, 1);
        root.Children.Add(sv);

        // Row2: フッター
        var footer = new Border
        {
            Background      = Res("BgSecondaryBrush"),
            BorderBrush     = Res("BorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding         = new Thickness(24, 12, 24, 12)
        };
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancel = MakeSecondaryBtn("キャンセル", () => { DialogResult = false; }, new Thickness(0, 0, 8, 0));
        var btnOk     = MakePrimaryBtn("保存",       SaveAndClose);
        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnOk);
        footer.Child = btnRow;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;

        // 初期描画
        RebuildList();
    }

    // ── タイトルバー（標準パターン） ──────────────────────
    /// <summary>標準パターンのタイトルバーUIを構築して返す。</summary>
    private Border BuildTitleBar()
    {
        var bar = new Border
        {
            Background      = Res("BgSecondaryBrush"),
            BorderBrush     = Res("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var dp = new DockPanel();

        var closeBtn = new Button
        {
            Width = 44, Height = 36,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Arrow, Command = SystemCommands.CloseWindowCommand
        };
        closeBtn.SetValue(WindowChrome.IsHitTestVisibleInChromeProperty, true);
        closeBtn.MouseEnter += (_, _) => closeBtn.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0x00, 0x00));
        closeBtn.MouseLeave += (_, _) => closeBtn.Background = Brushes.Transparent;
        closeBtn.Content = new TextBlock
        {
            Text = "✕", FontSize = 11, Foreground = Res("TextSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center
        };
        DockPanel.SetDock(closeBtn, Dock.Right);

        var titleTb = new TextBlock
        {
            Text = Title, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };

        dp.Children.Add(closeBtn);
        dp.Children.Add(titleTb);
        bar.Child = dp;
        return bar;
    }

    // ── 条件追加 ─────────────────────────────────────────
    /// <summary>指定された種別の進捗条件を追加する。ファイル/アプリ種別はダイアログでパスを選択する。</summary>
    private void AddCondition(ProgressConditionType type)
    {
        var condition = new ProgressCondition
        {
            Label = type switch
            {
                ProgressConditionType.FileExists  => "ファイルの存在確認",
                ProgressConditionType.AppLaunched => "アプリ起動確認",
                _                                 => "手動確認"
            },
            Type = type
        };

        if (type == ProgressConditionType.FileExists)
        {
            var ofd = new OpenFileDialog { Title = "確認するファイルを選択" };
            if (ofd.ShowDialog() != true) return;
            condition.Path  = ofd.FileName;
            condition.Label = Path.GetFileName(ofd.FileName) + " の存在";
        }
        else if (type == ProgressConditionType.AppLaunched)
        {
            var ofd = new OpenFileDialog
            {
                Title  = "起動するアプリを選択",
                Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*"
            };
            if (ofd.ShowDialog() != true) return;
            condition.Path  = ofd.FileName;
            condition.Label = Path.GetFileNameWithoutExtension(ofd.FileName) + " の起動";
        }

        _conditions.Add(condition);
        RebuildList();
    }

    // ── 条件リストの再描画 ────────────────────────────────
    /// <summary>条件リストパネルを現在の条件一覧で再構築し達成率を更新する。</summary>
    private void RebuildList()
    {
        _listPanel.Children.Clear();

        if (!_conditions.Any())
        {
            _listPanel.Children.Add(new TextBlock
            {
                Text         = "条件がまだ設定されていません。上のボタンから追加してください。",
                Foreground   = Res("TextDimBrush"),
                FontSize     = 12, TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 8, 0, 0)
            });
        }

        int achieved = 0;
        foreach (var cond in _conditions)
        {
            if (cond.IsAchieved) achieved++;
            _listPanel.Children.Add(BuildConditionRow(cond));
        }

        int total = _conditions.Count;
        _progressBar.Maximum = Math.Max(1, total);
        _progressBar.Value   = achieved;
        _progressLabel.Text  = total > 0
            ? $"{achieved}/{total} 達成 ({achieved * 100 / total}%)"
            : "条件なし";
    }

    /// <summary>1つの進捗条件を表す行UIを構築して返す。</summary>
    private Border BuildConditionRow(ProgressCondition cond)
    {
        var accentColor = cond.Type switch
        {
            ProgressConditionType.FileExists  => Color.FromRgb(0x4C, 0xAF, 0x50),
            ProgressConditionType.AppLaunched => Color.FromRgb(0x3D, 0x7E, 0xFF),
            _                                 => Color.FromRgb(0x9A, 0xA0, 0xB5)
        };

        var row = new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(0x18, accentColor.R, accentColor.G, accentColor.B)),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(0x60, accentColor.R, accentColor.G, accentColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Margin          = new Thickness(0, 0, 0, 6),
            Padding         = new Thickness(10, 8, 10, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // チェックボックス
        var chk = new CheckBox { IsChecked = cond.IsAchieved, VerticalAlignment = VerticalAlignment.Center };
        chk.Checked   += (_, _) => { cond.IsAchieved = true;  cond.AchievedAt = DateTime.Now; RebuildList(); };
        chk.Unchecked += (_, _) => { cond.IsAchieved = false; cond.AchievedAt = null;         RebuildList(); };
        Grid.SetColumn(chk, 0);

        // ラベル
        var labelSp = new StackPanel { Margin = new Thickness(10, 0, 8, 0) };
        labelSp.Children.Add(new TextBlock
        {
            Text = cond.Type switch
            {
                ProgressConditionType.FileExists  => "📄 ファイル存在",
                ProgressConditionType.AppLaunched => "🚀 アプリ起動",
                _                                 => "✋ 手動確認"
            },
            FontSize   = 10,
            Foreground = new SolidColorBrush(accentColor),
            Margin     = new Thickness(0, 0, 0, 2)
        });
        labelSp.Children.Add(new TextBlock
        {
            Text            = cond.Label,
            FontSize        = 12,
            Foreground      = Res("TextPrimaryBrush"),
            FontWeight      = cond.IsAchieved ? FontWeights.Normal : FontWeights.SemiBold,
            TextDecorations = cond.IsAchieved ? TextDecorations.Strikethrough : null,
            TextWrapping    = TextWrapping.Wrap
        });
        if (!string.IsNullOrEmpty(cond.Path))
            labelSp.Children.Add(new TextBlock
            {
                Text = cond.Path, FontSize = 10, Foreground = Res("TextDimBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0)
            });
        if (cond.IsAchieved && cond.AchievedAt.HasValue)
            labelSp.Children.Add(new TextBlock
            {
                Text = $"✅ {cond.AchievedAt.Value:MM/dd HH:mm} に達成",
                FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                Margin = new Thickness(0, 2, 0, 0)
            });
        Grid.SetColumn(labelSp, 1);

        // 操作ボタン
        var btnSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(cond.Path) && cond.Type != ProgressConditionType.Manual)
        {
            var btnRun = MakeSecondaryBtn(
                cond.Type == ProgressConditionType.AppLaunched ? "▶ 起動" : "📂 開く",
                () =>
                {
                    try { Process.Start(new ProcessStartInfo(cond.Path!) { UseShellExecute = true }); }
                    catch (Exception ex) { MessageBox.Show($"起動失敗: {ex.Message}"); }
                },
                new Thickness(0, 0, 4, 0));
            btnRun.FontSize = 10;
            btnRun.Padding  = new Thickness(6, 2, 6, 2);
            btnSp.Children.Add(btnRun);
        }
        var btnDel = new Button
        {
            Content = "🗑", FontSize = 12, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Padding = new Thickness(4)
        };
        btnDel.Click += (_, _) => { _conditions.Remove(cond); RebuildList(); };
        btnSp.Children.Add(btnDel);
        Grid.SetColumn(btnSp, 2);

        grid.Children.Add(chk);
        grid.Children.Add(labelSp);
        grid.Children.Add(btnSp);
        row.Child = grid;
        return row;
    }

    // ── 保存 ─────────────────────────────────────────────
    /// <summary>編集した条件リストをタスクに反映してダイアログを閉じる。</summary>
    private void SaveAndClose()
    {
        _task.ProgressConditions = _conditions;
        DialogResult = true;
    }

    // ── ボタンヘルパー ────────────────────────────────────
    /// <summary>追加操作用のセカンダリスタイルボタンを生成する。</summary>
    private Button MakeAddBtn(string text, Action onClick)
    {
        var btn = new Button
        {
            Content = text, FontSize = 11, Padding = new Thickness(8, 4, 8, 4),
            Margin  = new Thickness(0, 0, 6, 6),
            Background      = Res("BgSecondaryBrush"),
            Foreground      = Res("TextSecondaryBrush"),
            BorderBrush     = Res("BorderBrush"),
            BorderThickness = new Thickness(1), Cursor = Cursors.Hand
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    /// <summary>セカンダリスタイルのボタンを指定マージン付きで生成する。</summary>
    private Button MakeSecondaryBtn(string label, Action action, Thickness margin)
    {
        var btn = new Button
        {
            Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = margin,
            FontSize = 12, Background = Res("BgSecondaryBrush"), Foreground = Res("TextPrimaryBrush"),
            BorderBrush = Res("BorderBrush"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    /// <summary>プライマリスタイルのボタンを生成する。</summary>
    private static Button MakePrimaryBtn(string label, Action action)
    {
        var btn = new Button
        {
            Content = label, Padding = new Thickness(20, 6, 20, 6), FontSize = 12,
            Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)),
            Foreground = Brushes.White, BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    // ── リソース取得 ──────────────────────────────────────
    private static Brush Res(string key) =>
        Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;

    // ── その他ヘルパー ────────────────────────────────────
    /// <summary>ProgressConditionを複製して返す。</summary>
    private static ProgressCondition CloneCondition(ProgressCondition src) => new()
    {
        Id = src.Id, Label = src.Label, Type = src.Type, Path = src.Path,
        IsAchieved = src.IsAchieved, AchievedAt = src.AchievedAt
    };

    // ── RelayCommand（InputBinding 用） ───────────────────
    /// <summary>InputBinding用の汎用リレーコマンド。</summary>
    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => execute(p);
    }
}
