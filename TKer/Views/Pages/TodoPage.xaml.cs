using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;

namespace TKer.Views.Pages;

public partial class TodoPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;
    private readonly TodoService   _svc;

    public TodoPage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = vm.TodoService;

        // ZeroCollapsedConverter が必要なら Resources に追加
        Resources["ZeroCollapsedConverter"] = new ZeroStringToVisibilityConverter();

        InitializeComponent();

        _svc.DataChanged += OnDataChanged;
        Loaded   += (_, _) => Refresh();
        Unloaded += (_, _) => _svc.DataChanged -= OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e) => Dispatcher.Invoke(Refresh);

    // ── IRefreshable ─────────────────────────────────────
    public void Refresh()
    {
        // InitializeComponent() 途中の呼び出しをガード
        if (TodoPanel == null) return;
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Todo_Header"));

        var all = _svc.GetAll().ToList();
        UpdateBadges(all);
        BuildList(FilterItems(all));
    }

    private void UpdateBadges(List<TodoItem> all)
    {
        if (TxtTotalCount == null) return;
        TxtTotalCount.Text   = all.Count.ToString();
        TxtDoneCount.Text    = all.Count(t => t.IsCompleted).ToString();
        TxtOverdueCount.Text = all.Count(t => t.IsOverdue).ToString();
    }

    private List<TodoItem> FilterItems(List<TodoItem> all)
    {
        var kw  = TxtSearch?.Text.Trim() ?? "";
        var idx = CboFilter?.SelectedIndex ?? 0;

        return all.Where(t =>
        {
            // キーワード
            if (!string.IsNullOrEmpty(kw) &&
                !t.Title.Contains(kw, StringComparison.OrdinalIgnoreCase) &&
                !(t.Notes?.Contains(kw, StringComparison.OrdinalIgnoreCase) ?? false))
                return false;

            return idx switch
            {
                1 => !t.IsCompleted,
                2 => t.IsCompleted,
                3 => t.IsOverdue,
                4 => t.DueDate?.Date == DateTime.Today,
                5 => t.Repeat != TodoRepeat.None,
                _ => true
            };
        }).ToList();
    }

    // ── リスト描画 ────────────────────────────────────────
    private void BuildList(List<TodoItem> items)
    {
        if (TodoPanel == null) return;
        TodoPanel.Children.Clear();

        if (!items.Any())
        {
            TodoPanel.Children.Add(new TextBlock
            {
                Text              = "TODOはありません",
                Foreground        = (Brush)FindResource("TextDimBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin            = new Thickness(0, 40, 0, 0),
                FontSize          = 14
            });
            return;
        }

        // 完了・未完了で分けて表示
        var pending   = items.Where(t => !t.IsCompleted).ToList();
        var completed = items.Where(t => t.IsCompleted).ToList();

        if (pending.Any())
        {
            TodoPanel.Children.Add(MakeSectionHeader("📌 未完了"));
            foreach (var item in pending) TodoPanel.Children.Add(BuildCard(item));
        }

        if (completed.Any())
        {
            TodoPanel.Children.Add(MakeSectionHeader("✅ 完了済み"));
            foreach (var item in completed) TodoPanel.Children.Add(BuildCard(item));
        }
    }

    private static TextBlock MakeSectionHeader(string text) => new()
    {
        Text       = text,
        FontSize   = 12,
        FontWeight = FontWeights.Bold,
        Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xB5)),
        Margin     = new Thickness(0, 8, 0, 4)
    };

    private Border BuildCard(TodoItem item)
    {
        Color accent;
        try { accent = (Color)ColorConverter.ConvertFromString(item.Color); }
        catch { accent = Color.FromRgb(0x3D, 0x7E, 0xFF); }

        var card = new Border
        {
            Background       = new SolidColorBrush(Color.FromArgb(0x18, accent.R, accent.G, accent.B)),
            BorderBrush      = new SolidColorBrush(Color.FromArgb(0x60, accent.R, accent.G, accent.B)),
            BorderThickness  = new Thickness(0, 0, 0, 0),
            CornerRadius     = new CornerRadius(8),
            Margin           = new Thickness(0, 0, 0, 6),
            Padding          = new Thickness(0),
            Cursor           = Cursors.Hand
        };

        // 左アクセントバー
        var grid = new Grid();
        var accentBar = new Border
        {
            Width        = 4,
            Background   = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(8, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        var content = new Grid { Margin = new Thickness(12, 10, 12, 10) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // チェックボックス
        var chk = new CheckBox
        {
            IsChecked         = item.IsCompleted,
            VerticalAlignment = VerticalAlignment.Top,
            Margin            = new Thickness(4, 2, 10, 0)
        };
        chk.Checked   += (_, _) => { _svc.Toggle(item.Id); };
        chk.Unchecked += (_, _) => { _svc.Toggle(item.Id); };
        Grid.SetColumn(chk, 0);

        // メインコンテンツ
        var mainSp = new StackPanel();
        var titleRow = new DockPanel();
        var title = new TextBlock
        {
            Text             = item.Title,
            FontSize         = 13,
            FontWeight       = FontWeights.SemiBold,
            Foreground       = item.IsCompleted
                ? new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78))
                : (Brush)FindResource("TextPrimaryBrush"),
            TextDecorations  = item.IsCompleted ? TextDecorations.Strikethrough : null,
            TextWrapping     = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleRow.Children.Add(title);

        // バッジ
        var badgeSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(item.RepeatLabel))
            badgeSp.Children.Add(MakeBadge("🔁 " + item.RepeatLabel, Color.FromRgb(0x3D, 0x7E, 0xFF)));
        if (item.IsOverdue)
            badgeSp.Children.Add(MakeBadge("期限超過", Color.FromRgb(0xF4, 0x43, 0x36)));
        else if (item.DueDate.HasValue && item.DueDate.Value.Date == DateTime.Today)
            badgeSp.Children.Add(MakeBadge("今日期限", Color.FromRgb(0xFF, 0xC1, 0x07)));

        if (badgeSp.Children.Count > 0)
        {
            DockPanel.SetDock(badgeSp, Dock.Right);
            titleRow.Children.Insert(0, badgeSp);
        }
        mainSp.Children.Add(titleRow);

        // サブ情報
        var subSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        if (item.DueDate.HasValue)
            subSp.Children.Add(new TextBlock
            {
                Text = $"📅 {item.DueDate.Value:M月d日}",
                FontSize = 11, Foreground = item.IsOverdue
                    ? new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36))
                    : new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xB5)),
                Margin = new Thickness(0, 0, 8, 0)
            });
        if (!string.IsNullOrEmpty(item.Notes))
            subSp.Children.Add(new TextBlock
            {
                Text = item.Notes, FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xB5)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 400
            });
        if (subSp.Children.Count > 0) mainSp.Children.Add(subSp);

        Grid.SetColumn(mainSp, 1);

        // 操作ボタン (hover 表示はコスト高のため常時表示)
        var btnSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var btnEdit   = MakeIconBtn("✏", () => OpenEditDialog(item));
        var btnDelete = MakeIconBtn("🗑", () =>
        {
            if (MessageBox.Show($"「{item.Title}」を削除しますか？", "確認",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                _svc.Delete(item.Id);
        });
        btnSp.Children.Add(btnEdit);
        btnSp.Children.Add(btnDelete);
        Grid.SetColumn(btnSp, 2);

        content.Children.Add(chk);
        content.Children.Add(mainSp);
        content.Children.Add(btnSp);

        grid.Children.Add(accentBar);
        grid.Children.Add(content);
        card.Child = grid;

        card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) OpenEditDialog(item);
        };

        return card;
    }

    private static Border MakeBadge(string text, Color color) => new()
    {
        Background  = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, color.R, color.G, color.B)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(4, 1, 4, 1),
        Margin  = new Thickness(0, 0, 4, 0),
        Child   = new TextBlock { Text = text, FontSize = 10, Foreground = new SolidColorBrush(color) }
    };

    private static Button MakeIconBtn(string icon, Action onClick)
    {
        var btn = new Button
        {
            Content = icon, FontSize = 13, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Padding = new Thickness(4), Margin = new Thickness(2, 0, 0, 0)
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    // ── ダイアログ ────────────────────────────────────────
    private void BtnAdd_Click(object s, RoutedEventArgs e) => OpenEditDialog(null);

    private void OpenEditDialog(TodoItem? item)
    {
        var isNew = item == null;
        var dlg = new TodoEditDialog(item, _vm.ProjectService.CurrentProject?.Tasks)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() != true) return;

        if (isNew)
            _svc.Add(dlg.ResultTitle, dlg.ResultNotes, dlg.ResultDueDate,
                     dlg.ResultColor, dlg.ResultRepeat, dlg.ResultLinkedTaskId, dlg.ResultMapInfo);
        else
        {
            item!.Title        = dlg.ResultTitle;
            item.Notes         = dlg.ResultNotes;
            item.DueDate       = dlg.ResultDueDate;
            item.Color         = dlg.ResultColor;
            item.Repeat        = dlg.ResultRepeat;
            item.LinkedTaskId  = dlg.ResultLinkedTaskId;
            item.MapInfo       = dlg.ResultMapInfo;
            _svc.Update(item);
        }
    }

    // ── フィルターイベント ────────────────────────────────
    private void TxtSearch_Changed(object s, TextChangedEventArgs e)
    {
        if (TodoPanel == null) return;
        var all = _svc.GetAll().ToList();
        UpdateBadges(all);
        BuildList(FilterItems(all));
    }

    private void CboFilter_Changed(object s, SelectionChangedEventArgs e)
    {
        if (TodoPanel == null) return;
        var all = _svc.GetAll().ToList();
        UpdateBadges(all);
        BuildList(FilterItems(all));
    }
}

// ── TODO 編集ダイアログ ───────────────────────────────────
public class TodoEditDialog : Window
{
    // 結果プロパティ
    public string      ResultTitle        { get; private set; } = "";
    public string?     ResultNotes        { get; private set; }
    public DateTime?   ResultDueDate      { get; private set; }
    public string      ResultColor        { get; private set; } = "#3D7EFF";
    public TodoRepeat  ResultRepeat       { get; private set; } = TodoRepeat.None;
    public string?     ResultLinkedTaskId { get; private set; }
    public string?     ResultMapInfo      { get; private set; }

    private readonly TextBox  _txtTitle;
    private readonly TextBox  _txtNotes;
    private readonly DatePicker _dpDue;
    private readonly TextBox  _txtColor;
    private readonly ComboBox _cboRepeat;
    private readonly ComboBox _cboTask;
    private readonly TextBox  _txtMap;

    public TodoEditDialog(TodoItem? item, IEnumerable<TaskItem>? tasks)
    {
        Title  = item == null ? "TODO 追加" : "TODO 編集";
        Width  = 420;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var bg  = new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x2E));
        var fg  = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF));
        var dim = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xB5));
        Background = bg;

        var sp = new StackPanel { Margin = new Thickness(20) };

        sp.Children.Add(Label("タイトル *", dim));
        _txtTitle = Tb(fg); _txtTitle.Text = item?.Title ?? "";
        sp.Children.Add(_txtTitle);

        sp.Children.Add(Label("メモ", dim));
        _txtNotes = new TextBox { MinHeight = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Text = item?.Notes ?? "" };
        ApplyTbStyle(_txtNotes, fg); sp.Children.Add(_txtNotes);

        sp.Children.Add(Label("期限日", dim));
        _dpDue = new DatePicker { SelectedDate = item?.DueDate, Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(_dpDue);

        sp.Children.Add(Label("繰り返し", dim));
        _cboRepeat = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var r in Enum.GetValues<TodoRepeat>())
            _cboRepeat.Items.Add(new ComboBoxItem { Content = r switch
            {
                TodoRepeat.None    => "なし",
                TodoRepeat.Daily   => "毎日",
                TodoRepeat.Weekday => "平日",
                TodoRepeat.Weekly  => "毎週",
                TodoRepeat.Monthly => "毎月",
                TodoRepeat.Yearly  => "毎年",
                _                  => r.ToString()
            }, Tag = r });
        _cboRepeat.SelectedIndex = (int)(item?.Repeat ?? TodoRepeat.None);
        sp.Children.Add(_cboRepeat);

        sp.Children.Add(Label("タスクに紐付け（任意）", dim));
        _cboTask = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        _cboTask.Items.Add(new ComboBoxItem { Content = "（紐付けなし）", Tag = (string?)null });
        if (tasks != null)
            foreach (var t in tasks)
                _cboTask.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t.Id });
        _cboTask.SelectedIndex = 0;
        if (item?.LinkedTaskId != null)
        {
            for (int i = 1; i < _cboTask.Items.Count; i++)
                if (((ComboBoxItem)_cboTask.Items[i]).Tag as string == item.LinkedTaskId)
                { _cboTask.SelectedIndex = i; break; }
        }
        sp.Children.Add(_cboTask);

        sp.Children.Add(Label("色 (#RRGGBB)", dim));
        _txtColor = Tb(fg); _txtColor.Text = item?.Color ?? "#3D7EFF";
        sp.Children.Add(_txtColor);

        sp.Children.Add(Label("場所・地図情報（任意）", dim));
        _txtMap = Tb(fg); _txtMap.Text = item?.MapInfo ?? "";
        sp.Children.Add(_txtMap);

        var btnRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var btnOk  = new Button { Content = item == null ? "追加" : "更新", Width = 80, Height = 30, Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
        var btnCnl = new Button { Content = "キャンセル", Width = 90, Height = 30, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x31, 0x45)), Foreground = dim, BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x45, 0x60)), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
        DockPanel.SetDock(btnOk, Dock.Right);
        DockPanel.SetDock(btnCnl, Dock.Right);
        btnOk.Click  += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtTitle.Text)) { MessageBox.Show("タイトルを入力してください"); return; }
            ResultTitle        = _txtTitle.Text.Trim();
            ResultNotes        = string.IsNullOrWhiteSpace(_txtNotes.Text) ? null : _txtNotes.Text.Trim();
            ResultDueDate      = _dpDue.SelectedDate;
            ResultColor        = _txtColor.Text.Trim();
            ResultRepeat       = (TodoRepeat)((ComboBoxItem)_cboRepeat.SelectedItem).Tag;
            ResultLinkedTaskId = ((ComboBoxItem)_cboTask.SelectedItem).Tag as string;
            ResultMapInfo      = string.IsNullOrWhiteSpace(_txtMap.Text) ? null : _txtMap.Text.Trim();
            DialogResult = true;
        };
        btnCnl.Click += (_, _) => DialogResult = false;
        btnRow.Children.Add(btnOk);
        btnRow.Children.Add(btnCnl);
        sp.Children.Add(btnRow);

        Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Loaded += (_, _) => _txtTitle.Focus();
    }

    private static TextBlock Label(string t, Brush fg) =>
        new() { Text = t, Foreground = fg, FontSize = 11, Margin = new Thickness(0, 6, 0, 2) };

    private static TextBox Tb(Brush fg)
    {
        var tb = new TextBox();
        ApplyTbStyle(tb, fg);
        return tb;
    }

    private static void ApplyTbStyle(TextBox tb, Brush fg)
    {
        tb.Background   = new SolidColorBrush(Color.FromRgb(0x25, 0x2C, 0x3F));
        tb.Foreground   = fg;
        tb.BorderBrush  = new SolidColorBrush(Color.FromRgb(0x3A, 0x45, 0x60));
        tb.BorderThickness = new Thickness(1);
        tb.Padding      = new Thickness(6, 4, 6, 4);
        tb.CaretBrush   = fg;
        tb.Margin       = new Thickness(0, 0, 0, 0);
    }
}

// ── ゼロ文字列を Collapsed にするコンバーター ─────────────
public class ZeroStringToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is string s && s == "0" ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotImplementedException();
}
