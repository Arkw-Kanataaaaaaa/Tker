using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClosedXML.Excel;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

public class TaskRow
{
    public string    Id               { get; set; } = "";
    public string    Name             { get; set; } = "";
    public string    CategoryName     { get; set; } = "";
    public string    Assignee         { get; set; } = "";
    public string    Priority         { get; set; } = "";
    public string    Status           { get; set; } = "";
    public string    Tags             { get; set; } = "";
    public DateTime? PlannedStartDate { get; set; }
    public DateTime? PlannedEndDate   { get; set; }
    public DateTime? ActualStartDate  { get; set; }
    public DateTime? ActualEndDate    { get; set; }
    public int?      DelayDays        { get; set; }
    public bool      DelayApproved    { get; set; }
    public TaskItem  Source           { get; set; } = null!;
}

public partial class TaskListPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;
    private DateTime _viewStart;
    private int      _viewDays      = 60;
    private bool     _isGanttMode   = true;
    private bool     _isAnimating   = false;
    private TaskItem? _selectedTask;
    private Border?   _selectedTaskBorder;

    private string? _selectedCategoryId;   // null = すべて

    private readonly SolidColorBrush _toggleBg = new(Color.FromRgb(11, 110, 153));

    private SolidColorBrush? _selectedTaskNormalBg;
    private SolidColorBrush? _selectedTaskSelBg;
    private SolidColorBrush? _selectedTaskSelBorder;

    // 予定設定モード
    private bool      _isScheduleMode         = false;
    private int       _scheduleClickCount      = 0;
    private DateTime? _scheduleFirstClickDate  = null;
    private bool      _suppressDateChanged     = false;

    private const double RowH    = 44;
    private const double ColW    = 28;
    private const double PlanBarY = 8;
    private const double ActBarY  = 26;
    private const double BarH    = 12;

    private static readonly SolidColorBrush TextPrimBrush  = new(Color.FromRgb(207, 207, 207));
    private static readonly SolidColorBrush TextSecBrush   = new(Color.FromRgb(120, 119, 116));
    private static readonly SolidColorBrush TextDimBrush_  = new(Color.FromRgb(72,  72,  72));
    private static readonly SolidColorBrush BgHoverBrush_  = new(Color.FromRgb(55,  55,  55));
    private static readonly SolidColorBrush GridBrush_     = new(Color.FromArgb(80, 55, 55, 55));
    // 列区切り線：行背景より少し明るい半透明白
    private static readonly SolidColorBrush ColSepBrush_   = new(Color.FromArgb(45, 255, 255, 255));
    private static readonly SolidColorBrush PlanBrush_     = new(Color.FromArgb(210, 35, 131, 226));
    private static readonly SolidColorBrush ActBrush_      = new(Color.FromArgb(210, 82, 158, 114));
    private static readonly SolidColorBrush TodayBrush_    = new(Color.FromArgb(220, 11, 110, 153));
    private static readonly SolidColorBrush WeekendBrush_  = new(Color.FromArgb(18, 255, 255, 255));
    private static readonly SolidColorBrush HolidayBrush_  = new(Color.FromArgb(18, 224, 62, 62));
    private static readonly SolidColorBrush AccentBlueBrush_ = new(Color.FromRgb(35, 131, 226));

    private enum RowKind { Category, Task, AddTask }
    private record RowDef(RowKind Kind, Category? Cat, TaskItem? Task);
    private readonly List<RowDef> _rows = new();
    private readonly Dictionary<string, bool>    _expanded       = new();
    private readonly Dictionary<string, Color>   _catColors      = new();
    // 設定から取得した行枠線ブラシ（BuildUI()で更新）
    private SolidColorBrush _rowBorderBrush = new(Color.FromRgb(96, 96, 96));

    private readonly Dictionary<string, Border>   _taskContainers  = new();
    private readonly Dictionary<string, Border>   _ganttContainers = new();
    private readonly Dictionary<string, TextBlock> _categoryArrows = new();
    private readonly List<(Border Border, TaskItem Task)> _dragTargets = new();
    private double _ganttTotalW = 1200;

    // ── Drag state ──
    private TaskItem? _dragCandidate;
    private Border?   _dragCandidateBorder;
    private Point     _dragStartPos;
    private DispatcherTimer? _dragArmTimer;
    private bool      _isDragging;
    private Border?   _dragGhost;
    private Rectangle? _dropIndicator;
    private TaskItem? _dropTargetTask;
    private bool      _dropBefore;

    public TaskListPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        _viewStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        // Toggle switch background
        ViewToggleSwitch.Background = _toggleBg;

        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } win)
            {
                win.KeyDown -= Window_KeyDown;
                win.KeyDown += Window_KeyDown;
                _keyDownWindow = win;
            }
        };
        Unloaded += (_, _) =>
        {
            if (_keyDownWindow is { } win)
                win.KeyDown -= Window_KeyDown;
            _keyDownWindow = null;
        };

        PreviewMouseMove        += Page_PreviewMouseMove;
        PreviewMouseLeftButtonUp += Page_PreviewMouseLeftButtonUp;
    }

    private Window? _keyDownWindow;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible) return;

        bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt   = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (ctrl && !shift && !alt)
        {
            switch (e.Key)
            {
                case Key.F:
                    if (SearchSection.Visibility != Visibility.Visible)
                        ToggleSearch_Click(this, new RoutedEventArgs());
                    else
                    { SearchBox.Focus(); SearchBox.SelectAll(); }
                    e.Handled = true; break;
                case Key.S:
                    _vm.SaveCommand.Execute(null);
                    e.Handled = true; break;
                case Key.OemMinus:
                case Key.Subtract:
                    if (_selectedTask != null)
                    {
                        DeleteTask(_selectedTask);
                        ClearSelection();
                    }
                    e.Handled = true; break;
                case Key.P:
                    ExportCsv_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.K:
                    if (_selectedTask != null) ShowComments(_selectedTask);
                    e.Handled = true; break;
                case Key.H:
                    if (_selectedTask != null) ActivateScheduleMode(_selectedTask);
                    e.Handled = true; break;
            }
        }
        else if (ctrl && shift && !alt)
        {
            switch (e.Key)
            {
                case Key.OemSemicolon:
                    AddTask_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.P:
                    ExportWbs_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.F2 && _selectedTask != null)
            {
                OpenEditDialog(_selectedTask);
                e.Handled = true;
            }
            else if (e.Key == Key.F3 && _selectedCategoryId != null)
            {
                EditCategory_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }

    private bool IsPersonalMode => _vm.AppSettingsService.AppMode == "Personal";

    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Task_Header"));

        CbCategoryFilter.Items.Clear();
        CbCategoryFilter.Items.Add(new ComboBoxItem { Content = "すべてのカテゴリー", Tag = "" });
        if (_vm.ProjectService.CurrentProject != null)
            foreach (var c in _vm.ProjectService.CurrentProject.Categories)
                CbCategoryFilter.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Id });
        CbCategoryFilter.SelectedIndex = 0;

        // コンボボックス幅を最大項目文字列に合わせる
        Dispatcher.InvokeAsync(() =>
        {
            CbCategoryFilter.Width = GetMaxComboBoxWidth(CbCategoryFilter);
            CbStatusFilter.Width   = GetMaxComboBoxWidth(CbStatusFilter);
            CbPriorityFilter.Width = GetMaxComboBoxWidth(CbPriorityFilter);
        }, System.Windows.Threading.DispatcherPriority.Loaded);

        BuildUI();
    }

    // ══════════════════════════════════════════════════════
    // UI Construction
    // ══════════════════════════════════════════════════════

    private void BuildUI()
    {
        if (TaskTree == null || GanttStack == null) return;
        _rowBorderBrush = new SolidColorBrush(ParseColor(_vm.AppSettingsService.RowBorderColor));
        _rows.Clear();
        _catColors.Clear();
        _taskContainers.Clear();
        _ganttContainers.Clear();
        _categoryArrows.Clear();
        _dragTargets.Clear();
        TaskTree.Children.Clear();
        GanttStack.Children.Clear();

        var project = _vm.ProjectService.CurrentProject;
        _ganttTotalW = ComputeTotalW();
        if (project == null) { DrawDateHeader(_ganttTotalW); return; }

        var search         = SearchBox?.Text.ToLower() ?? "";
        var filterCatId    = (CbCategoryFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var filterStatus   = GetFilterStatus();
        var filterPriority = GetFilterPriority();

        foreach (var cat in project.Categories)
        {
            if (!string.IsNullOrEmpty(filterCatId) && cat.Id != filterCatId) continue;

            var catColor = ParseColor(cat.Color);
            _catColors[cat.Id] = catColor;
            bool expanded = _expanded.GetValueOrDefault(cat.Id, true);

            // ── タスクリスト（フィルタ済） ──
            var tasksQ = project.Tasks
                .Where(t => t.CategoryId == cat.Id)
                .Where(t => string.IsNullOrEmpty(filterStatus) || t.Status == filterStatus)
                .Where(t => string.IsNullOrEmpty(filterPriority) || t.Priority == filterPriority)
                .Where(t => string.IsNullOrEmpty(search)
                    || t.Name.ToLower().Contains(search)
                    || t.Assignee.ToLower().Contains(search)
                    || t.Id.ToLower().Contains(search)
                    || t.Tags.ToLower().Contains(search))
                .ToList();

            // ── _rows（ガント高さ計算用） ──
            _rows.Add(new RowDef(RowKind.Category, cat, null));
            if (expanded)
            {
                foreach (var t in tasksQ) _rows.Add(new RowDef(RowKind.Task, cat, t));
                _rows.Add(new RowDef(RowKind.AddTask, cat, null));
            }

            // ── 左パネル（TaskTree） ──
            var leftCatRow = BuildCategoryRow(cat, catColor, expanded, out var catBg, out var catHover);
            TaskTree.Children.Add(leftCatRow);

            var taskContainer = new Border { ClipToBounds = true };
            var taskSp = new StackPanel();
            foreach (var task in tasksQ) taskSp.Children.Add(BuildTaskRow(task, cat.Color));
            taskSp.Children.Add(BuildAddTaskRow(cat));
            taskContainer.Child = taskSp;
            if (!expanded) taskContainer.Height = 0;
            _taskContainers[cat.Id] = taskContainer;
            TaskTree.Children.Add(taskContainer);

            // ── 右パネル（GanttStack） ──
            var rightCatRow = BuildGanttCatRow(catBg);
            GanttStack.Children.Add(rightCatRow);

            // 左右パネル共通ホバー
            leftCatRow.MouseEnter  += (_, _) => { leftCatRow.Background = catHover; rightCatRow.Background = catHover; };
            leftCatRow.MouseLeave  += (_, _) => { leftCatRow.Background = catBg;    rightCatRow.Background = catBg; };
            rightCatRow.MouseEnter += (_, _) => { leftCatRow.Background = catHover; rightCatRow.Background = catHover; };
            rightCatRow.MouseLeave += (_, _) => { leftCatRow.Background = catBg;    rightCatRow.Background = catBg; };

            var ganttContainer = new Border { ClipToBounds = true };
            var ganttSp = new StackPanel();
            foreach (var task in tasksQ)
                ganttSp.Children.Add(_isGanttMode
                    ? BuildGanttTaskRow(task, _ganttTotalW)
                    : BuildDetailTaskRow(task, project, _ganttTotalW));
            ganttSp.Children.Add(BuildGanttAddRow(_ganttTotalW));
            ganttContainer.Child = ganttSp;
            if (!expanded) ganttContainer.Height = 0;
            _ganttContainers[cat.Id] = ganttContainer;
            GanttStack.Children.Add(ganttContainer);
        }

        DrawDateHeader(_ganttTotalW);
    }

    private double ComputeTotalW()
    {
        double avail = BarScroll.ActualWidth > 0 ? BarScroll.ActualWidth : 0;
        if (_isGanttMode)
        {
            if (avail > 0)
            {
                int minDays = (int)Math.Ceiling(avail / ColW) + 1;
                if (minDays > _viewDays) _viewDays = minDays;
            }
            return ColW * _viewDays;
        }
        return Math.Max(DetailCanvasMinW, avail);
    }

    private string GetFilterStatus()
    {
        if (CbStatusFilter?.SelectedItem is ComboBoxItem item)
        {
            var v = item.Content as string ?? "";
            return v is "すべてのステータス" or "" ? "" : v;
        }
        return "";
    }

    private string GetFilterPriority()
    {
        if (CbPriorityFilter?.SelectedItem is ComboBoxItem item)
        {
            var v = item.Content as string ?? "";
            return v is "すべての優先度" or "" ? "" : v;
        }
        return "";
    }

    private Border BuildCategoryRow(Category cat, Color catColor, bool expanded,
        out SolidColorBrush bgBrush, out SolidColorBrush hoverBrush)
    {
        byte r = (byte)Math.Min(catColor.R + 22, 255);
        byte g = (byte)Math.Min(catColor.G + 22, 255);
        byte b = (byte)Math.Min(catColor.B + 22, 255);
        bgBrush    = new SolidColorBrush(Color.FromArgb(255, catColor.R, catColor.G, catColor.B));
        hoverBrush = new SolidColorBrush(Color.FromArgb(255, r, g, b));

        var panel = new DockPanel { Height = RowH };

        var arrow = new TextBlock
        {
            Text = expanded ? "▼" : "▶", Width = 28,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10, Foreground = TextSecBrush,
            TextAlignment = TextAlignment.Center
        };
        _categoryArrows[cat.Id] = arrow;
        DockPanel.SetDock(arrow, Dock.Left);
        panel.Children.Add(arrow);

        var colorBar = new Border
        {
            Width = 4, Background = new SolidColorBrush(catColor),
            Margin = new Thickness(0, 6, 8, 6), CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        DockPanel.SetDock(colorBar, Dock.Left);
        panel.Children.Add(colorBar);

        var name = new TextBlock
        {
            Text = cat.Name, FontWeight = FontWeights.Bold, FontSize = 14,
            Foreground = TextPrimBrush, VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(name);

        var border = new Border
        {
            Child = panel, Height = RowH, Background = bgBrush,
            BorderBrush = _rowBorderBrush, BorderThickness = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand, Padding = new Thickness(4, 0, 0, 0)
        };
        border.MouseLeftButtonUp += (_, _) => ToggleCategoryAnimated(cat);
        return border;
    }

    private void ToggleCategoryAnimated(Category cat)
    {
        if (_isAnimating) return;
        bool willExpand = !_expanded.GetValueOrDefault(cat.Id, true);
        _expanded[cat.Id] = willExpand;

        // BuildUI() は呼ばず、既存コンテナをそのままアニメーション
        if (!_taskContainers.TryGetValue(cat.Id, out var container)) return;
        if (container.Child is not FrameworkElement content) return;

        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double fullH = content.DesiredSize.Height;
        if (fullH <= 0)
        {
            container.Height = willExpand ? double.NaN : 0;
            if (_ganttContainers.TryGetValue(cat.Id, out var gc))
                gc.Height = willExpand ? double.NaN : 0;
            return;
        }

        _isAnimating = true;

        if (_categoryArrows.TryGetValue(cat.Id, out var arrow))
            arrow.Text = willExpand ? "▼" : "▶";

        var ease = new CubicEase { EasingMode = willExpand ? EasingMode.EaseOut : EasingMode.EaseIn };
        int ms   = willExpand ? 240 : 200;

        // ── 完了カウンタ（左右2本同期） ──
        int done = 0;
        void OnComplete() { if (++done < 2) return; _isAnimating = false; }

        // ── 左パネル（タスクツリー） ──
        container.Height = willExpand ? 0 : fullH;
        var leftAnim = new DoubleAnimation(
            willExpand ? 0 : fullH, willExpand ? fullH : 0,
            TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        leftAnim.Completed += (_, _) =>
        {
            container.BeginAnimation(HeightProperty, null);
            container.Height = willExpand ? double.NaN : 0;
            OnComplete();
        };
        container.BeginAnimation(HeightProperty, leftAnim);

        // ── 右パネル（GanttStack の同一カテゴリコンテナ） ──
        if (_ganttContainers.TryGetValue(cat.Id, out var ganttContainer))
        {
            ganttContainer.Height = willExpand ? 0 : fullH;
            var rightAnim = new DoubleAnimation(
                willExpand ? 0 : fullH, willExpand ? fullH : 0,
                TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            rightAnim.Completed += (_, _) =>
            {
                ganttContainer.BeginAnimation(HeightProperty, null);
                ganttContainer.Height = willExpand ? double.NaN : 0;
                OnComplete();
            };
            ganttContainer.BeginAnimation(HeightProperty, rightAnim);
        }
        else { OnComplete(); }
    }

    private Border BuildTaskRow(TaskItem task, string catColorStr)
    {
        var catColor     = ParseColor(catColorStr);
        bool isCompleted = task.Status == "完了";
        return BuildTaskRowNormal(task, catColor, isCompleted);
    }

    private Border BuildTaskRowNormal(TaskItem task, Color catColor, bool isCompleted)
    {
        var grid = new Grid { Height = RowH };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        // Accent bar
        var accentBar = new Border
        {
            Width = 3, Margin = new Thickness(14, 7, 0, 7),
            Background = new SolidColorBrush(catColor),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(2)
        };
        Grid.SetColumn(accentBar, 0);
        grid.Children.Add(accentBar);

        // Task name
        var nameTb = new TextBlock
        {
            Text = task.Name, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0), FontSize = 14,
            Foreground = isCompleted ? TextDimBrush_ : TextPrimBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (isCompleted) nameTb.TextDecorations = TextDecorations.Strikethrough;
        Grid.SetColumn(nameTb, 1);
        grid.Children.Add(nameTb);

        // Assignee
        var assigneeTb = new TextBlock
        {
            Text = task.Assignee, VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13, Foreground = TextSecBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(assigneeTb, 2);
        grid.Children.Add(assigneeTb);

        // Priority badge
        var priorityBadge = MakePriorityBadge(task.Priority);
        Grid.SetColumn(priorityBadge, 3);
        grid.Children.Add(priorityBadge);

        // Status badge
        var statusBadge = MakeStatusBadge(task.Status);
        Grid.SetColumn(statusBadge, 4);
        grid.Children.Add(statusBadge);

        // 列区切り線（担当者・優先度・ステータスの左端）
        for (int col = 2; col <= 4; col++)
        {
            var sep = new Rectangle
            {
                Width = 1, Fill = ColSepBrush_,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false
            };
            Grid.SetColumn(sep, col);
            grid.Children.Add(sep);
        }

        return WrapTaskRow(grid, task);
    }

    private Border WrapTaskRow(Grid grid, TaskItem task)
    {
        double rowOpacity = _vm.AppSettingsService.TaskRowOpacity;
        // 行本体に不透明な背景を持たせ、透過率スライダが視覚的に反映されるようにする
        byte alpha = (byte)Math.Clamp((int)Math.Round(rowOpacity * 255), 0, 255);
        byte selAlpha = Math.Max(alpha, (byte)180);
        var bgBrush    = new SolidColorBrush(Color.FromArgb(alpha,    47, 47, 47));
        var selBg      = new SolidColorBrush(Color.FromArgb(selAlpha, 28, 28, 28));
        var selBorder  = new SolidColorBrush(Color.FromRgb(80, 80, 80));
        var border = new Border
        {
            Child = grid, Height = RowH,
            Background = bgBrush,
            BorderBrush = _rowBorderBrush, BorderThickness = new Thickness(0, 0, 1, 1)
        };

        // Re-apply selection state if this task is selected
        if (_selectedTask?.Id == task.Id)
        {
            border.Background = selBg;
            border.BorderBrush = selBorder;
            border.BorderThickness = new Thickness(2, 0, 1, 1);
            _selectedTaskBorder    = border;
            _selectedTaskNormalBg  = bgBrush;
            _selectedTaskSelBg     = selBg;
            _selectedTaskSelBorder = selBorder;
        }

        border.MouseEnter += (_, _) =>
        {
            if (_selectedTask?.Id != task.Id)
                border.Background = BgHoverBrush_;
        };
        border.MouseLeave += (_, _) =>
        {
            if (_selectedTask?.Id != task.Id)
                border.Background = bgBrush;
        };
        border.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Button) return;
            ArmDrag(task, border, e.GetPosition(this));
        };
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (_isDragging) return;          // ドラッグ完了時はクリック扱いしない
            if (e.OriginalSource is Button) return;
            SelectTaskRow(task, border, selBg, selBorder, bgBrush);
            if (!_isGanttMode) ShowDetailPanel(task);
        };

        border.ContextMenu = MakeTaskContextMenu(task);
        _dragTargets.Add((border, task));
        return border;
    }

    private static Border MakePriorityBadge(string priority)
    {
        var (pbg, pfg) = priority switch
        {
            "高" => (Color.FromArgb(50, 224, 62, 62),   Color.FromRgb(224, 62, 62)),
            "低" => (Color.FromArgb(50, 11, 110, 153),  Color.FromRgb(11, 110, 153)),
            _    => (Color.FromArgb(50, 217, 115, 13),  Color.FromRgb(217, 115, 13))
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(pbg), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5, 2, 5, 2),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = priority, FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(pfg)
        };
        return badge;
    }

    private static Border MakeStatusBadge(string status)
    {
        var (sbg, sfg) = status switch
        {
            "完了"     => (Color.FromArgb(50, 82, 158, 114),  Color.FromRgb(82, 158, 114)),
            "対応中"   => (Color.FromArgb(50, 35, 131, 226),  Color.FromRgb(35, 131, 226)),
            "レビュー中"=> (Color.FromArgb(50, 223, 171, 1),  Color.FromRgb(223, 171, 1)),
            _          => (Color.FromArgb(50, 120, 119, 116), Color.FromRgb(120, 119, 116))
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(sbg), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5, 2, 5, 2),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
        };
        badge.Child = new TextBlock
        {
            Text = status, FontSize = 10, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(sfg)
        };
        return badge;
    }

    private Border BuildAddTaskRow(Category cat)
    {
        var tb = new TextBlock
        {
            Text = "＋  タスクを追加", FontSize = 13, Foreground = TextDimBrush_,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(52, 0, 0, 0)
        };
        var addRowBg = new SolidColorBrush(Color.FromArgb(18, 100, 100, 100));
        var border = new Border
        {
            Child = tb, Height = RowH, Cursor = Cursors.Hand,
            Background = addRowBg,
            BorderBrush = _rowBorderBrush, BorderThickness = new Thickness(0, 0, 1, 1)
        };
        border.MouseEnter += (_, _) => { border.Background = BgHoverBrush_; tb.Foreground = AccentBlueBrush_; };
        border.MouseLeave += (_, _) => { border.Background = addRowBg;         tb.Foreground = TextDimBrush_; };
        border.MouseLeftButtonUp += (_, _) => AddTaskInCategory(cat);
        return border;
    }

    private void SelectTaskRow(TaskItem task, Border border,
        SolidColorBrush selBg, SolidColorBrush selBorderColor, SolidColorBrush normalBg)
    {
        if (_selectedTaskBorder != null)
        {
            _selectedTaskBorder.Background        = _selectedTaskNormalBg ?? new SolidColorBrush(Color.FromArgb(100, 47, 47, 47));
            _selectedTaskBorder.BorderBrush       = _rowBorderBrush;
            _selectedTaskBorder.BorderThickness   = new Thickness(0, 0, 1, 1);
        }
        _selectedTask          = task;
        _selectedTaskBorder    = border;
        _selectedTaskNormalBg  = normalBg;
        _selectedTaskSelBg     = selBg;
        _selectedTaskSelBorder = selBorderColor;
        border.Background      = selBg;
        border.BorderBrush     = selBorderColor;
        border.BorderThickness = new Thickness(2, 0, 1, 1);
        BtnEditTask.IsEnabled    = true;
        BtnDeleteTask.IsEnabled  = true;
        BtnCommentTask.IsEnabled = true;
        UpdateScheduleInputFields(task);
    }

    private void ClearSelection()
    {
        if (_selectedTaskBorder != null)
        {
            _selectedTaskBorder.Background      = _selectedTaskNormalBg ?? new SolidColorBrush(Color.FromArgb(100, 47, 47, 47));
            _selectedTaskBorder.BorderBrush     = _rowBorderBrush;
            _selectedTaskBorder.BorderThickness = new Thickness(0, 0, 1, 1);
        }
        _selectedTask       = null;
        _selectedTaskBorder = null;
        BtnEditTask.IsEnabled    = false;
        BtnDeleteTask.IsEnabled  = false;
        BtnCommentTask.IsEnabled = false;
    }

    // ══════════════════════════════════════════════════════
    // Detail Side Panel
    // ══════════════════════════════════════════════════════

    private void ShowDetailPanel(TaskItem task)
    {
        DetailPanelTitle.Text = task.Name;
        FillDetailPanel(task);

        var currentW = DetailSidePanel.Width;
        if (currentW >= 279) return; // already open

        var anim = new DoubleAnimation(currentW, 280, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        DetailSidePanel.BeginAnimation(WidthProperty, anim);
    }

    private void CloseDetailPanel_Click(object sender, RoutedEventArgs e)
    {
        var anim = new DoubleAnimation(DetailSidePanel.Width, 0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        DetailSidePanel.BeginAnimation(WidthProperty, anim);
    }

    private void FillDetailPanel(TaskItem task)
    {
        TaskDetailPanel.Children.Clear();

        // ── タスク名（省略形） ──
        if (!string.IsNullOrEmpty(task.NameShort))
        {
            TaskDetailPanel.Children.Add(new TextBlock
            {
                Text = "省略形（タスク名）", FontSize = 11, Foreground = TextSecBrush,
                Margin = new Thickness(0, 0, 0, 4)
            });
            TaskDetailPanel.Children.Add(new Border
            {
                Background   = new SolidColorBrush(Color.FromRgb(37, 37, 37)),
                CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 8, 10, 8),
                Margin       = new Thickness(0, 0, 0, 14),
                Child        = new TextBlock
                {
                    Text = task.NameShort, FontSize = 13, Foreground = TextPrimBrush,
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }

        // ── フォルダ ──
        FillTaskFolderSection(task);
    }

    private void FillTaskFolderSection(TaskItem task)
    {
        var headerSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        headerSp.Children.Add(new TextBlock
        {
            Text = "📁 タスクフォルダ", FontFamily = new FontFamily("Yu Gothic UI"),
            FontWeight = FontWeights.Bold, FontSize = 13,
            Foreground = TextPrimBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,12,0)
        });

        if (Directory.Exists(task.FolderPath))
        {
            // ── ボタン群 ──────────────────────────────────
            void MiniBtn(string text, Action onClick)
            {
                var b = new Button
                {
                    Content = text, FontSize = 11, Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 0, 4, 0),
                    Style = (Style)Application.Current.Resources["SecondaryButton"]
                };
                b.Click += (_, _) => onClick();
                headerSp.Children.Add(b);
            }

            MiniBtn("📂 開く", () => ShellHelper.OpenInExplorer(task.FolderPath));
            MiniBtn("📄 新規", () => TaskFolder_NewFile(task));
            MiniBtn("📁 フォルダ", () => TaskFolder_NewFolder(task));
            TaskDetailPanel.Children.Add(headerSp);

            // ── ファイル一覧 ────────────────────────────
            var entries = Directory.GetFiles(task.FolderPath)
                .Concat(Directory.GetDirectories(task.FolderPath))
                .OrderBy(e => !Directory.Exists(e))
                .ThenBy(e => System.IO.Path.GetFileName(e), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (entries.Length == 0)
            {
                TaskDetailPanel.Children.Add(new TextBlock
                {
                    Text = "ファイルがありません", FontSize = 11,
                    Foreground = TextDimBrush_, Margin = new Thickness(0, 4, 0, 0)
                });
            }
            else
            {
                foreach (var entry in entries)
                {
                    bool isDir    = Directory.Exists(entry);
                    var  icon     = isDir ? "📁" : GetFileIcon(entry);
                    var  entryPath = entry;
                    bool isTagged  = !isDir && task.ProgressTagFiles.Any(f =>
                        string.Equals(f, entryPath, StringComparison.OrdinalIgnoreCase));

                    var row = new Border
                    {
                        BorderBrush = GridBrush_, BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(0, 5, 0, 5), Cursor = Cursors.Hand
                    };

                    var dockPnl = new DockPanel { LastChildFill = true };

                    // 削除ボタン（右端・hover時表示）
                    var delBtn = new Button
                    {
                        Content = "✕", FontSize = 10, Width = 22, Height = 22,
                        Padding = new Thickness(0),
                        Visibility = Visibility.Collapsed,
                        Style = (Style)Application.Current.Resources["SecondaryButton"],
                        Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                        ToolTip = "削除"
                    };
                    delBtn.Click += (_, e2) =>
                    {
                        e2.Handled = true;
                        TaskFolder_Delete(task, entryPath, isDir);
                    };
                    DockPanel.SetDock(delBtn, Dock.Right);
                    dockPnl.Children.Add(delBtn);

                    // 名前変更ボタン
                    var renBtn = new Button
                    {
                        Content = "✏", FontSize = 10, Width = 22, Height = 22,
                        Padding = new Thickness(0), Margin = new Thickness(0, 0, 2, 0),
                        Visibility = Visibility.Collapsed,
                        Style = (Style)Application.Current.Resources["SecondaryButton"],
                        ToolTip = "名前変更"
                    };
                    renBtn.Click += (_, e2) =>
                    {
                        e2.Handled = true;
                        TaskFolder_Rename(task, entryPath, isDir);
                    };
                    DockPanel.SetDock(renBtn, Dock.Right);
                    dockPnl.Children.Add(renBtn);

                    // 進捗タグボタン（ファイルのみ）
                    if (!isDir)
                    {
                        var tagBtn = new Button
                        {
                            Content    = isTagged ? "📌" : "📌",
                            FontSize   = 11, Width = 22, Height = 22,
                            Padding    = new Thickness(0), Margin = new Thickness(0, 0, 2, 0),
                            Visibility = Visibility.Collapsed,
                            ToolTip    = isTagged ? "進捗タグを解除" : "進捗タグを付ける",
                            Foreground = isTagged
                                ? new SolidColorBrush(Color.FromRgb(0, 191, 216))
                                : TextSecBrush,
                            Style = (Style)Application.Current.Resources["SecondaryButton"]
                        };
                        tagBtn.Click += (_, e2) =>
                        {
                            e2.Handled = true;
                            TaskFolder_ToggleTag(task, entryPath);
                        };
                        DockPanel.SetDock(tagBtn, Dock.Right);
                        dockPnl.Children.Add(tagBtn);

                        row.MouseEnter += (_, _) => tagBtn.Visibility = Visibility.Visible;
                        row.MouseLeave += (_, _) => tagBtn.Visibility = Visibility.Collapsed;
                    }

                    var nameSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    if (isTagged)
                    {
                        nameSp.Children.Add(new TextBlock
                        {
                            Text = "📌", FontSize = 10, Margin = new Thickness(0, 0, 3, 0),
                            VerticalAlignment = VerticalAlignment.Center, ToolTip = "進捗反映タグ付き"
                        });
                    }
                    nameSp.Children.Add(new TextBlock { Text = icon, Margin = new Thickness(0, 0, 6, 0), FontSize = 13 });
                    nameSp.Children.Add(new TextBlock
                    {
                        Text = System.IO.Path.GetFileName(entry), FontSize = 12,
                        Foreground = isTagged
                            ? new SolidColorBrush(Color.FromRgb(0, 191, 216))
                            : TextPrimBrush,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    dockPnl.Children.Add(nameSp);

                    row.Child = dockPnl;
                    row.MouseEnter += (_, _) =>
                    {
                        row.Background  = BgHoverBrush_;
                        renBtn.Visibility = Visibility.Visible;
                        delBtn.Visibility = Visibility.Visible;
                    };
                    row.MouseLeave += (_, _) =>
                    {
                        row.Background  = Brushes.Transparent;
                        renBtn.Visibility = Visibility.Collapsed;
                        delBtn.Visibility = Visibility.Collapsed;
                    };
                    row.MouseLeftButtonUp += (_, _) =>
                    {
                        if (isDir) ShellHelper.OpenInExplorer(entryPath);
                        else Process.Start(new ProcessStartInfo(entryPath) { UseShellExecute = true });
                    };
                    TaskDetailPanel.Children.Add(row);
                }
            }
        }
        else
        {
            TaskDetailPanel.Children.Add(headerSp);
            TaskDetailPanel.Children.Add(new TextBlock
            {
                Text = "フォルダが存在しません", FontSize = 11,
                Foreground = TextDimBrush_, Margin = new Thickness(0, 4, 0, 0)
            });
        }
    }

    // ── タスクフォルダ ファイル操作 ────────────────────────
    private void TaskFolder_NewFile(TaskItem task)
    {
        var name = PromptSimple("新規ファイル名（拡張子を含む）", "メモ.txt");
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = System.IO.Path.Combine(task.FolderPath, name);
        try
        {
            File.WriteAllText(path, "");
            FillTaskFolderSection_Reload(task);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show($"作成失敗: {ex.Message}"); }
    }

    private void TaskFolder_NewFolder(TaskItem task)
    {
        var name = PromptSimple("新規フォルダ名", "新しいフォルダ");
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = System.IO.Path.Combine(task.FolderPath, name);
        try { Directory.CreateDirectory(path); FillTaskFolderSection_Reload(task); }
        catch (Exception ex) { MessageBox.Show($"作成失敗: {ex.Message}"); }
    }

    private void TaskFolder_Rename(TaskItem task, string entryPath, bool isDir)
    {
        var oldName = System.IO.Path.GetFileName(entryPath);
        var name = PromptSimple("新しい名前", oldName);
        if (string.IsNullOrWhiteSpace(name) || name == oldName) return;
        var newPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(entryPath)!, name);
        try
        {
            if (isDir) Directory.Move(entryPath, newPath);
            else       File.Move(entryPath, newPath);
            FillTaskFolderSection_Reload(task);
        }
        catch (Exception ex) { MessageBox.Show($"名前変更失敗: {ex.Message}"); }
    }

    private void TaskFolder_Delete(TaskItem task, string entryPath, bool isDir)
    {
        var typeName = isDir ? "フォルダ" : "ファイル";
        if (MessageBox.Show($"{typeName}「{System.IO.Path.GetFileName(entryPath)}」を削除しますか？",
            "削除確認", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            if (isDir) Directory.Delete(entryPath, recursive: true);
            else       File.Delete(entryPath);
            FillTaskFolderSection_Reload(task);
        }
        catch (Exception ex) { MessageBox.Show($"削除失敗: {ex.Message}"); }
    }

    private void TaskFolder_ToggleTag(TaskItem task, string filePath)
    {
        var existing = task.ProgressTagFiles
            .FirstOrDefault(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            task.ProgressTagFiles.Remove(existing);
        else
            task.ProgressTagFiles.Add(filePath);

        _vm.ProjectService.MarkDirtyAndSave();
        FillTaskFolderSection_Reload(task);
    }

    private void FillTaskFolderSection_Reload(TaskItem task)
    {
        // 詳細パネルを再描画
        FillDetailPanel(task);
    }

    private string? PromptSimple(string label, string defaultValue)
    {
        var win = new Window
        {
            Width = 360, Height = 150, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            Title = label,
            Background = new SolidColorBrush(Color.FromRgb(32, 32, 32))
        };
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = TextSecBrush, Margin = new Thickness(0,0,0,8) });
        var tb = new TextBox
        {
            Text = defaultValue, Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(47, 47, 47)),
            Foreground = TextPrimBrush, BorderBrush = GridBrush_,
            Margin = new Thickness(0, 0, 0, 12)
        };
        sp.Children.Add(tb);
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 72, Height = 28, Margin = new Thickness(0,0,8,0),
            Background = AccentBlueBrush_, Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        var cancel = new Button { Content = "キャンセル", Width = 80, Height = 28 };
        ok.Click     += (_, _) => win.DialogResult = true;
        cancel.Click += (_, _) => win.DialogResult = false;
        btnRow.Children.Add(ok); btnRow.Children.Add(cancel);
        sp.Children.Add(btnRow);
        win.Content = sp;
        win.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return win.ShowDialog() == true ? tb.Text.Trim() : null;
    }

    // ══════════════════════════════════════════════════════
    // 右パネル行ビルダー
    // ══════════════════════════════════════════════════════

    // 詳細モード列定義: (開始X, 列幅, ラベル)
    private static readonly (double X, double W, string Label)[] DetailColumns =
    {
        (   0, 110, "カテゴリー"),
        ( 115, 100, "中分類"),
        ( 220,  80, "環境"),
        ( 305, 175, "予定期間"),
        ( 485, 175, "作業期間"),
        ( 665,  80, "実績総工数"),
        ( 750, 110, "タグ"),
        ( 865, 215, "説明"),
        (1085, 200, "備考"),
    };
    private const double DetailCanvasMinW = 1290.0;

    // ヘッダー（DateHeaderCanvas）のみを描画
    private void DrawDateHeader(double totalW)
    {
        DateHeaderCanvas.Width = totalW;
        DateHeaderCanvas.Children.Clear();

        if (_isGanttMode)
        {
            MonthLabel.Text = _viewStart.ToString("yyyy年 M月");
            string? lastMonth = null;
            for (int i = 0; i < _viewDays; i++)
            {
                var d = _viewStart.AddDays(i);
                double x = i * ColW;
                bool isWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                bool isToday   = d.Date == DateTime.Today;
                bool isHoliday = JapaneseHolidays.IsHoliday(d);

                if (d.ToString("M月") != lastMonth)
                {
                    lastMonth = d.ToString("M月");
                    var ml = new TextBlock { Text = d.ToString("M月"), FontSize = 9, FontFamily = new FontFamily("Consolas"), Foreground = TextSecBrush };
                    Canvas.SetLeft(ml, x + 2); Canvas.SetTop(ml, 2);
                    DateHeaderCanvas.Children.Add(ml);
                }
                var dayTb = new TextBlock
                {
                    Text = d.Day.ToString(), Width = ColW, TextAlignment = TextAlignment.Center,
                    FontSize = 9, FontFamily = new FontFamily("Consolas"),
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = isToday ? TodayBrush_ :
                                 isHoliday ? new SolidColorBrush(Color.FromRgb(224, 62, 62)) :
                                 isWeekend ? new SolidColorBrush(Color.FromRgb(80, 120, 180)) : TextDimBrush_
                };
                Canvas.SetLeft(dayTb, x); Canvas.SetTop(dayTb, 20);
                DateHeaderCanvas.Children.Add(dayTb);
            }
        }
        else
        {
            for (int ci = 0; ci < DetailColumns.Length; ci++)
            {
                var (cx, cw, label) = DetailColumns[ci];
                double actualW = (ci == DetailColumns.Length - 1) ? totalW - cx : cw;
                if (cx > 0)
                    DateHeaderCanvas.Children.Add(new Line { X1 = cx, Y1 = 0, X2 = cx, Y2 = 36, Stroke = GridBrush_, StrokeThickness = 1 });
                var hdr = new TextBlock
                {
                    Text = label, FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = TextSecBrush,
                    Width = Math.Max(actualW - 8, 0), TextTrimming = TextTrimming.CharacterEllipsis
                };
                Canvas.SetLeft(hdr, cx + 4); Canvas.SetTop(hdr, 10);
                DateHeaderCanvas.Children.Add(hdr);
            }
        }
    }

    // カテゴリ行（右パネル）：透過なし・グリッド線なし
    private Border BuildGanttCatRow(SolidColorBrush catBg)
    {
        return new Border
        {
            Height = RowH, Background = catBg,
            BorderBrush = _rowBorderBrush, BorderThickness = new Thickness(0, 1, 0, 1)
        };
    }

    // タスク行（ガントモード）：設定から色・透過率を適用
    private Border BuildGanttTaskRow(TaskItem task, double totalW)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(_vm.AppSettingsService.GanttRowOpacity * 255), 0, 255);
        var ganttColor = ParseColor(_vm.AppSettingsService.GanttRowColor);
        var canvas = new Canvas { Width = totalW, Height = RowH };
        canvas.Children.Add(new Rectangle { Width = totalW, Height = RowH, Fill = new SolidColorBrush(Color.FromArgb(alpha, ganttColor.R, ganttColor.G, ganttColor.B)) });
        AddGanttRowLines(canvas, totalW);
        DrawBarOnCanvas(canvas, task.PlannedStartDate, task.PlannedEndDate, PlanBarY, BarH, PlanBrush_, $"予定: {task.PlannedStartDate:M/d}〜{task.PlannedEndDate:M/d}");
        DrawActualWorkOnCanvas(canvas, task.Id, ActBarY, BarH);
        canvas.Children.Add(new Line { X1 = 0, Y1 = RowH - 0.5, X2 = totalW, Y2 = RowH - 0.5, Stroke = _rowBorderBrush, StrokeThickness = 1 });
        var border = new Border { Height = RowH, Child = canvas, ClipToBounds = true };
        canvas.MouseLeftButtonDown += (_, e) => GanttRowCanvas_Click(task, e, canvas);
        border.ContextMenu = MakeTaskContextMenu(task);
        return border;
    }

    // タスク行（詳細モード）：左パネルと同じ外観
    private Border BuildDetailTaskRow(TaskItem task, ProjectData project, double totalW)
    {
        byte alpha = (byte)Math.Clamp((int)Math.Round(_vm.AppSettingsService.TaskRowOpacity * 255), 0, 255);
        var canvas = new Canvas { Width = totalW, Height = RowH };
        canvas.Children.Add(new Rectangle { Width = totalW, Height = RowH, Fill = new SolidColorBrush(Color.FromArgb(alpha, 47, 47, 47)) });
        // 縦区切り線
        foreach (var (cx, _, _) in DetailColumns)
            if (cx > 0)
                canvas.Children.Add(new Line { X1 = cx, Y1 = 0, X2 = cx, Y2 = RowH, Stroke = ColSepBrush_, StrokeThickness = 1 });
        canvas.Children.Add(new Line { X1 = 0, Y1 = RowH - 0.5, X2 = totalW, Y2 = RowH - 0.5, Stroke = _rowBorderBrush, StrokeThickness = 1 });

        var catName = project.Categories.FirstOrDefault(c => c.Id == task.CategoryId)?.Name ?? "";
        string period = (task.PlannedStartDate.HasValue && task.PlannedEndDate.HasValue)
            ? $"{task.PlannedStartDate.Value:yyyy/MM/dd}〜{task.PlannedEndDate.Value:yyyy/MM/dd}" : "";
        string actualPeriod = (task.ActualStartDate.HasValue && task.ActualEndDate.HasValue)
            ? $"{task.ActualStartDate.Value:yyyy/MM/dd}〜{task.ActualEndDate.Value:yyyy/MM/dd}" : "";
        double totalHours = project.ActualWork.Where(a => a.TaskId == task.Id).Sum(a => a.Hours);
        string actualWork = totalHours > 0 ? $"{totalHours:F1}時間" : "";
        AddDetailCellOnCanvas(canvas, 0, catName,                totalW);
        AddDetailCellOnCanvas(canvas, 1, task.SubCategory ?? "", totalW);
        AddDetailCellOnCanvas(canvas, 2, task.Environment  ?? "", totalW);
        AddDetailCellOnCanvas(canvas, 3, period,                 totalW, new FontFamily("Consolas"), 10, centered: true);
        AddDetailCellOnCanvas(canvas, 4, actualPeriod,           totalW, new FontFamily("Consolas"), 10, centered: true);
        AddDetailCellOnCanvas(canvas, 5, actualWork,             totalW, centered: true);
        AddDetailCellOnCanvas(canvas, 6, task.Tags         ?? "", totalW);
        AddDetailCellOnCanvas(canvas, 7, task.Description  ?? "", totalW);
        AddDetailCellOnCanvas(canvas, 8, task.Notes        ?? "", totalW);

        var border = new Border { Height = RowH, Child = canvas, ClipToBounds = true };
        border.MouseLeftButtonDown += (_, _) => ShowDetailPanel(task);
        border.ContextMenu = MakeTaskContextMenu(task);
        return border;
    }

    // 空行（AddTask行・右パネル）
    private Border BuildGanttAddRow(double totalW)
    {
        var canvas = new Canvas { Width = totalW, Height = RowH };
        canvas.Children.Add(new Rectangle { Width = totalW, Height = RowH, Fill = new SolidColorBrush(Color.FromArgb(18, 100, 100, 100)) });
        AddGanttRowLines(canvas, totalW);
        canvas.Children.Add(new Line { X1 = 0, Y1 = RowH - 0.5, X2 = totalW, Y2 = RowH - 0.5, Stroke = _rowBorderBrush, StrokeThickness = 1 });
        return new Border { Height = RowH, Child = canvas, ClipToBounds = true };
    }

    // ガントモード行共通：縦線＋週末ハイライト＋今日線
    private void AddGanttRowLines(Canvas canvas, double totalW)
    {
        if (!_isGanttMode) return;
        double todayX = (DateTime.Today.Date - _viewStart.Date).TotalDays * ColW;
        for (int i = 0; i < _viewDays; i++)
        {
            var d = _viewStart.AddDays(i);
            double x = i * ColW;
            bool isWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            bool isHoliday = JapaneseHolidays.IsHoliday(d);
            if (isWeekend || isHoliday)
            {
                var shade = new Rectangle { Width = ColW, Height = RowH, Fill = isHoliday ? HolidayBrush_ : WeekendBrush_ };
                Canvas.SetLeft(shade, x); canvas.Children.Add(shade);
            }
            if (i > 0)
                canvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = RowH, Stroke = GridBrush_, StrokeThickness = 1 });
        }
        if (todayX >= 0 && todayX <= totalW)
        {
            var todayLine = new Rectangle { Width = 2, Height = RowH, Fill = TodayBrush_, Opacity = 0.8 };
            Canvas.SetLeft(todayLine, todayX);
            Panel.SetZIndex(todayLine, 10);
            canvas.Children.Add(todayLine);
        }
    }

    private void DrawBarOnCanvas(Canvas canvas, DateTime? start, DateTime? end, double y, double h, SolidColorBrush brush, string tooltip)
    {
        if (!start.HasValue || !end.HasValue) return;
        double totalW = ColW * _viewDays;
        double x = (start.Value.Date - _viewStart.Date).TotalDays * ColW;
        double w = Math.Max(((end.Value.Date - start.Value.Date).TotalDays + 1) * ColW, ColW * 0.5);
        if (x + w < 0 || x > totalW) return;
        double cx = Math.Max(x, 0);
        double cw = Math.Min(x + w, totalW) - cx;
        var rect = new Rectangle { Width = cw, Height = h, Fill = brush, RadiusX = 3, RadiusY = 3 };
        ToolTipService.SetToolTip(rect, tooltip);
        Canvas.SetLeft(rect, cx); Canvas.SetTop(rect, y);
        canvas.Children.Add(rect);
    }

    private void DrawActualWorkOnCanvas(Canvas canvas, string taskId, double y, double h)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return;
        foreach (var entry in project.ActualWork.Where(a => a.TaskId == taskId))
        {
            double x = (entry.Date.Date - _viewStart.Date).TotalDays * ColW;
            if (x + ColW < 0 || x > ColW * _viewDays) continue;
            double barH = Math.Max(h * (entry.Hours / 24.0), 2);
            var rect = new Rectangle { Width = ColW - 2, Height = barH, Fill = ActBrush_, RadiusX = 2, RadiusY = 2 };
            ToolTipService.SetToolTip(rect, $"{entry.Date:M/d}: {entry.Hours:F1}h");
            Canvas.SetLeft(rect, x + 1); Canvas.SetTop(rect, y + (h - barH));
            canvas.Children.Add(rect);
        }
    }

    private void AddDetailCellOnCanvas(Canvas canvas, int colIndex, string text, double totalW,
        FontFamily? font = null, double fontSize = 11, bool centered = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        var (cx, cw, _) = DetailColumns[colIndex];
        bool isLast = colIndex == DetailColumns.Length - 1;
        double w = Math.Max(isLast ? totalW - cx - 8 : centered ? cw : cw - 8, 0);
        if (w <= 0) return;
        var tb = new TextBlock
        {
            Text = text, FontSize = fontSize, Foreground = TextSecBrush,
            Width = w, TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (font != null) tb.FontFamily = font;
        if (centered) tb.TextAlignment = TextAlignment.Center;
        Canvas.SetLeft(tb, centered ? cx : cx + 4);
        Canvas.SetTop(tb, (RowH - fontSize * 1.35) / 2.0);
        canvas.Children.Add(tb);
    }

    private void GanttRowCanvas_Click(TaskItem task, MouseButtonEventArgs e, Canvas canvas)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return;
        var pos = e.GetPosition(canvas);
        int dayIdx = (int)(pos.X / ColW);
        if (dayIdx < 0 || dayIdx >= _viewDays) return;
        var date = _viewStart.AddDays(dayIdx).Date;

        if (_isScheduleMode)
        {
            HandleScheduleClick(task, date);
            return;
        }

        var existing = project.ActualWork.FirstOrDefault(a => a.TaskId == task.Id && a.Date.Date == date);
        double? hours = AskActualHours(existing?.Hours ?? 0, date, task.Name);
        if (hours == null) return;
        if (existing != null) { if (hours.Value <= 0) project.ActualWork.Remove(existing); else existing.Hours = hours.Value; }
        else if (hours.Value > 0) project.ActualWork.Add(new TKer.Models.ActualWorkEntry { TaskId = task.Id, Date = date, Hours = hours.Value });
        _vm.ProjectService.SaveProject();
        BuildUI();
    }

    // ══════════════════════════════════════════════════════
    // 予定設定モード
    // ══════════════════════════════════════════════════════

    private void ScheduleTask_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null)
        {
            AppDialog.ShowInfo("タスクを選択してください", "操作", Window.GetWindow(this));
            return;
        }
        ActivateScheduleMode(_selectedTask);
    }

    private void ActivateScheduleMode(TaskItem task)
    {
        _isScheduleMode         = true;
        _scheduleClickCount     = 0;
        _scheduleFirstClickDate = null;

        ScheduleInputPanel.Visibility = Visibility.Visible;
        var hintBlue = new SolidColorBrush(Color.FromRgb(35, 131, 226));
        var hintDim  = new SolidColorBrush(Color.FromRgb(120, 119, 116));
        _suppressDateChanged        = true;
        TxtPlannedStart.Text        = task.PlannedStartDate.HasValue ? task.PlannedStartDate.Value.ToString("yyyy/MM/dd") : "クリックして開始日を設定";
        TxtPlannedEnd.Text          = task.PlannedEndDate.HasValue   ? task.PlannedEndDate.Value.ToString("yyyy/MM/dd")   : "クリックして終了日を設定";
        TxtPlannedStart.Foreground  = hintBlue;
        TxtPlannedEnd.Foreground    = hintDim;
        _suppressDateChanged        = false;
    }

    private void HandleScheduleClick(TaskItem task, DateTime date)
    {
        if (_scheduleClickCount == 0)
        {
            _scheduleFirstClickDate = date;
            _scheduleClickCount     = 1;
            _suppressDateChanged    = true;
            TxtPlannedStart.Text    = date.ToString("yyyy/MM/dd");
            TxtPlannedEnd.Text      = "クリックして終了日を設定";
            TxtPlannedStart.Foreground = new SolidColorBrush(Color.FromRgb(35, 131, 226));
            TxtPlannedEnd.Foreground   = new SolidColorBrush(Color.FromRgb(35, 131, 226));
            _suppressDateChanged    = false;
        }
        else
        {
            var start = _scheduleFirstClickDate!.Value;
            var end   = date;
            if (end < start) (start, end) = (end, start);

            task.PlannedStartDate = start;
            task.PlannedEndDate   = end;
            _vm.ProjectService.SaveProject();

            _isScheduleMode         = false;
            _scheduleClickCount     = 0;
            _scheduleFirstClickDate = null;

            ScheduleInputPanel.Visibility = Visibility.Collapsed;
            BuildUI();
        }
    }

    private void UpdateScheduleInputFields(TaskItem task)
    {
        if (ScheduleInputPanel.Visibility != Visibility.Visible) return;
        var fg = new SolidColorBrush(Color.FromRgb(207, 207, 207));
        _suppressDateChanged = true;
        TxtPlannedStart.Text       = task.PlannedStartDate.HasValue ? task.PlannedStartDate.Value.ToString("yyyy/MM/dd") : "";
        TxtPlannedEnd.Text         = task.PlannedEndDate.HasValue   ? task.PlannedEndDate.Value.ToString("yyyy/MM/dd")   : "";
        TxtPlannedStart.Foreground = fg;
        TxtPlannedEnd.Foreground   = fg;
        _suppressDateChanged = false;
    }

    private void PlannedDate_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressDateChanged || _selectedTask == null) return;
        var task = _selectedTask;
        bool changed = false;

        if (sender == TxtPlannedStart)
        {
            if (string.IsNullOrWhiteSpace(TxtPlannedStart.Text))
            { task.PlannedStartDate = null; changed = true; }
            else if (DateTime.TryParseExact(TxtPlannedStart.Text, new[] { "yyyy/MM/dd", "yyyy-MM-dd", "MM/dd", "M/d" },
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
            { task.PlannedStartDate = d; changed = true; }
        }
        else if (sender == TxtPlannedEnd)
        {
            if (string.IsNullOrWhiteSpace(TxtPlannedEnd.Text))
            { task.PlannedEndDate = null; changed = true; }
            else if (DateTime.TryParseExact(TxtPlannedEnd.Text, new[] { "yyyy/MM/dd", "yyyy-MM-dd", "MM/dd", "M/d" },
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d))
            { task.PlannedEndDate = d; changed = true; }
        }

        if (changed)
        {
            _vm.ProjectService.SaveProject();
            BuildUI();
        }
    }

    // ══════════════════════════════════════════════════════
    // 右クリックコンテキストメニュー
    // ══════════════════════════════════════════════════════

    private ContextMenu MakeTaskContextMenu(TaskItem task)
    {
        var menuStyle = (Style)Application.Current.Resources["DarkContextMenu"];
        var itemStyle = (Style)Application.Current.Resources["SubMenuItemStyle"];
        var sepStyle  = (Style)Application.Current.Resources["DarkMenuSeparator"];

        var menu = new ContextMenu { Style = menuStyle };

        MenuItem MakeItem(string header, string shortcut, RoutedEventHandler handler)
        {
            var grid = new Grid { MinWidth = 190 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nameTb = new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameTb, 0);
            grid.Children.Add(nameTb);
            var keyTb = new TextBlock
            {
                Text = shortcut, FontSize = 11, Foreground = TextDimBrush_,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0)
            };
            Grid.SetColumn(keyTb, 2);
            grid.Children.Add(keyTb);
            var mi = new MenuItem { Header = grid, Style = itemStyle };
            mi.Click += handler;
            return mi;
        }

        menu.Items.Add(MakeItem("編集", "F2", (_, _) =>
        {
            _selectedTask = task;
            OpenEditDialog(task);
        }));
        menu.Items.Add(MakeItem("追加", "Ctrl+Shift+;", (_, _) =>
        {
            var cat = _vm.ProjectService.CurrentProject?.Categories.FirstOrDefault(c => c.Id == task.CategoryId);
            if (cat != null) AddTaskInCategory(cat);
        }));
        menu.Items.Add(MakeItem("削除", "Ctrl+-", (_, _) =>
        {
            _selectedTask = task;
            DeleteTask(task);
            ClearSelection();
        }));
        menu.Items.Add(new Separator { Style = sepStyle });
        menu.Items.Add(MakeItem("コメント", "Ctrl+K", (_, _) =>
        {
            _selectedTask = task;
            ShowComments(task);
        }));
        menu.Items.Add(MakeItem("予定設定", "Ctrl+H", (_, _) =>
        {
            _selectedTask = task;
            ActivateScheduleMode(task);
        }));
        menu.Items.Add(new Separator { Style = sepStyle });
        menu.Items.Add(MakeItem("✅ 進捗達成条件", "", (_, _) =>
        {
            var dlg = new Views.Dialogs.ProgressConditionDialog(task) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
                _vm.ProjectService.MarkDirtyAndSave();
        }));

        return menu;
    }

    private double? AskActualHours(double current, DateTime date, string taskName)
    {
        var bg  = new SolidColorBrush(Color.FromRgb(32, 32, 32));
        var fg  = new SolidColorBrush(Color.FromRgb(207, 207, 207));
        var dim = new SolidColorBrush(Color.FromRgb(80, 80, 80));

        var win = new Window
        {
            Title = "実績入力", Width = 300, Height = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize,
            Background = bg
        };
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(new TextBlock
        {
            Text = $"{taskName}  {date:M月d日}",
            Foreground = fg, TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4)
        });
        sp.Children.Add(new TextBlock
        {
            Text = "実績時間 (0〜24h、0で削除)",
            Foreground = new SolidColorBrush(Color.FromRgb(120, 119, 116)),
            FontSize = 11, Margin = new Thickness(0, 0, 0, 10)
        });
        var txt = new TextBox
        {
            Text = current > 0 ? current.ToString("F1") : "",
            Background = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            Foreground = fg, BorderBrush = dim,
            Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 12)
        };
        sp.Children.Add(txt);
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "保存", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(6, 0, 0, 0) };
        var cancel = new Button { Content = "キャンセル", Padding = new Thickness(10, 4, 10, 4) };
        btnPanel.Children.Add(cancel);
        btnPanel.Children.Add(ok);
        sp.Children.Add(btnPanel);
        win.Content = sp;

        double? result = null;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(txt.Text)) { result = 0; win.DialogResult = true; return; }
            if (double.TryParse(txt.Text, out double h) && h >= 0 && h <= 24)
                { result = h; win.DialogResult = true; }
            else AppDialog.ShowInfo("0〜24の数値を入力してください", "入力エラー");
        };
        cancel.Click += (_, _) => win.DialogResult = false;
        win.Loaded += (_, _) => { txt.Focus(); txt.SelectAll(); };
        win.PreviewKeyDown += (_, e2) => { if (e2.Key == Key.Escape) { win.DialogResult = false; e2.Handled = true; } };
        win.ShowDialog();
        return result;
    }

    private void BarScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        => HeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);

    // ══════════════════════════════════════════════════════
    // View Toggle
    // ══════════════════════════════════════════════════════

    private void ViewToggleBtn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isAnimating) return;
        _isGanttMode = !_isGanttMode;
        bool toDetail = !_isGanttMode;

        // Toggle thumb animation
        var thumbAnim = new ThicknessAnimation
        {
            To = toDetail ? new Thickness(22, 0, 0, 0) : new Thickness(2, 0, 0, 0),
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        ToggleThumb.BeginAnimation(MarginProperty, thumbAnim);

        // Toggle bg color animation
        var colorAnim = new ColorAnimation
        {
            To = toDetail ? Color.FromRgb(35, 131, 226) : Color.FromRgb(11, 110, 153),
            Duration = TimeSpan.FromMilliseconds(200)
        };
        _toggleBg.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);

        // Switch view with fade
        _isAnimating = true;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            ApplyViewMode(toDetail);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (_, _) => _isAnimating = false;
            MainScroll.BeginAnimation(OpacityProperty, fadeIn);
        };
        MainScroll.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ApplyViewMode(bool detail)
    {
        // ナビゲーションはガントモードのみ表示
        GanttNavPanel.Visibility      = detail ? Visibility.Collapsed : Visibility.Visible;
        // ヘッダー・バースクロール・列ヘッダーは両モードで同じ構造を使う
        HeaderScroll.Visibility       = Visibility.Visible;
        BarScroll.Visibility          = Visibility.Visible;
        ColumnHeaderNormal.Visibility = Visibility.Visible;
        ColumnHeaderDetail.Visibility = Visibility.Collapsed; // 使用しない
        // 列幅は両モードとも480px固定
        TaskTreeColDef.Width    = new GridLength(480);
        HeaderTaskTreeCol.Width = new GridLength(480);
        BuildUI();
    }

    // ══════════════════════════════════════════════════════
    // Filter / Navigation
    // ══════════════════════════════════════════════════════

    private void Filter_Changed(object sender, RoutedEventArgs e) => BuildUI();

    private void ToggleSearch_Click(object sender, RoutedEventArgs e)
    {
        if (SearchSection.Visibility == Visibility.Collapsed)
        {
            SearchSection.Visibility = Visibility.Visible;
            SearchSection.BeginAnimation(HeightProperty, null);
            SearchSection.Height = double.NaN;
            SearchSection.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double targetH = SearchSection.DesiredSize.Height > 0 ? SearchSection.DesiredSize.Height : 50;
            SearchSection.Height = 0;
            var anim = new DoubleAnimation(0, targetH, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            anim.Completed += (_, _) =>
            {
                SearchSection.BeginAnimation(HeightProperty, null);
                SearchSection.Height = double.NaN;
            };
            SearchSection.BeginAnimation(HeightProperty, anim);
            SearchBox.Focus();
        }
        else
        {
            double currentH = SearchSection.ActualHeight > 0 ? SearchSection.ActualHeight : 50;
            var anim = new DoubleAnimation(currentH, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            anim.Completed += (_, _) =>
            {
                SearchSection.BeginAnimation(HeightProperty, null);
                SearchSection.Visibility = Visibility.Collapsed;
            };
            SearchSection.BeginAnimation(HeightProperty, anim);
        }
    }

    private double GetMaxComboBoxWidth(ComboBox cb)
    {
        var tb = new TextBlock { FontFamily = new FontFamily("Yu Gothic UI"), FontSize = 13 };
        double maxW = 0;
        foreach (var item in cb.Items.OfType<ComboBoxItem>())
        {
            tb.Text = item.Content as string ?? "";
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            maxW = Math.Max(maxW, tb.DesiredSize.Width);
        }
        return maxW + 48; // padding(10+10) + borders + arrow
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) { AppDialog.ShowInfo("タスクを選択してください", "操作", Window.GetWindow(this)); return; }
        OpenEditDialog(_selectedTask);
    }

    private void EditComment_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) { AppDialog.ShowInfo("タスクを選択してください", "操作", Window.GetWindow(this)); return; }
        ShowComments(_selectedTask);
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) { AppDialog.ShowInfo("タスクを選択してください", "操作", Window.GetWindow(this)); return; }
        DeleteTask(_selectedTask);
        ClearSelection();
    }

    private void ExportWbs_Click(object sender, RoutedEventArgs e)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null)
        {
            AppDialog.ShowInfo("プロジェクトを開いてください", "エラー", Window.GetWindow(this));
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title    = "WBS Excel出力",
            Filter   = "Excelファイル (*.xlsx)|*.xlsx",
            FileName = $"WBS_{project.Settings.ProjectName}_{DateTime.Now:yyyyMMdd}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            ExportWbsToExcel(project, dlg.FileName);
            AppDialog.ShowInfo("Excelエクスポートが完了しました。", "WBS出力完了", Window.GetWindow(this));
            // 出力ファイルをExcelで開く
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.ShowInfo($"エクスポート中にエラーが発生しました:\n{ex.Message}",
                "エクスポートエラー", Window.GetWindow(this));
        }
    }

    private static void ExportWbsToExcel(ProjectData project, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("WBS");

        // ── タイトル ──
        ws.Cell(1, 1).Value = project.Settings.ProjectName;
        ws.Cell(1, 1).Style.Font.Bold     = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Cell(2, 1).Value = $"出力日時: {DateTime.Now:yyyy/MM/dd HH:mm}";
        ws.Cell(2, 1).Style.Font.FontSize   = 10;
        ws.Cell(2, 1).Style.Font.FontColor  = XLColor.Gray;

        // ── 列ヘッダー（4行目） ──
        const int HdrRow = 4;
        var headers = new[]
        {
            "No.", "カテゴリー", "タスク名", "省略形", "中分類", "環境",
            "担当者", "優先度", "ステータス", "タグ", "予定開始日", "予定終了日", "説明", "備考"
        };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(HdrRow, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold       = true;
            cell.Style.Font.FontColor  = XLColor.White;
            cell.Style.Fill.BackgroundColor     = XLColor.FromHtml("#323238");
            cell.Style.Alignment.Horizontal     = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical       = XLAlignmentVerticalValues.Center;
        }
        ws.Row(HdrRow).Height = 22;

        // ── データ行 ──
        int row    = HdrRow + 1;
        int taskNo = 0;

        foreach (var cat in project.Categories)
        {
            // カテゴリー行
            var catBg = TintCategoryColor(cat.Color, 0.55);
            for (int c = 1; c <= headers.Length; c++)
                ws.Cell(row, c).Style.Fill.BackgroundColor = catBg;
            ws.Cell(row, 1).Value = cat.Name;
            ws.Cell(row, 1).Style.Font.Bold     = true;
            ws.Cell(row, 1).Style.Font.FontSize = 11;
            ws.Range(row, 1, row, headers.Length).Merge();
            ws.Row(row).Height = 18;
            row++;

            // タスク行
            var tasks = project.Tasks.Where(t => t.CategoryId == cat.Id).ToList();
            for (int ti = 0; ti < tasks.Count; ti++)
            {
                var task  = tasks[ti];
                taskNo++;
                var rowBg = (ti % 2 == 0) ? XLColor.White : XLColor.FromHtml("#F5F5F8");

                ws.Cell(row, 1).Value  = taskNo;
                ws.Cell(row, 2).Value  = cat.Name;
                ws.Cell(row, 3).Value  = task.Name;
                ws.Cell(row, 4).Value  = task.NameShort;
                ws.Cell(row, 5).Value  = task.SubCategory;
                ws.Cell(row, 6).Value  = task.Environment;
                ws.Cell(row, 7).Value  = task.Assignee;
                ws.Cell(row, 8).Value  = task.Priority;
                ws.Cell(row, 9).Value  = task.Status;
                ws.Cell(row, 10).Value = task.Tags;
                if (task.PlannedStartDate.HasValue)
                {
                    ws.Cell(row, 11).Value = task.PlannedStartDate.Value;
                    ws.Cell(row, 11).Style.DateFormat.Format = "yyyy/MM/dd";
                }
                if (task.PlannedEndDate.HasValue)
                {
                    ws.Cell(row, 12).Value = task.PlannedEndDate.Value;
                    ws.Cell(row, 12).Style.DateFormat.Format = "yyyy/MM/dd";
                }
                ws.Cell(row, 13).Value = task.Description;
                ws.Cell(row, 14).Value = task.Notes;

                // 行背景
                for (int c = 1; c <= headers.Length; c++)
                    ws.Cell(row, c).Style.Fill.BackgroundColor = rowBg;

                // ステータス色
                ws.Cell(row, 9).Style.Font.FontColor = task.Status switch
                {
                    "完了"      => XLColor.FromHtml("#529E72"),
                    "対応中"    => XLColor.FromHtml("#2383E2"),
                    "レビュー中" => XLColor.FromHtml("#BF8B00"),
                    _           => XLColor.Gray
                };
                ws.Cell(row, 9).Style.Font.Bold = true;

                // 優先度色
                ws.Cell(row, 8).Style.Font.FontColor = task.Priority switch
                {
                    "高" => XLColor.FromHtml("#E03E3E"),
                    "低" => XLColor.FromHtml("#0B6E99"),
                    _    => XLColor.FromHtml("#C25B00")
                };
                ws.Cell(row, 8).Style.Font.Bold = true;

                ws.Row(row).Height = 16;
                row++;
            }
        }

        // ── 罫線 ──
        if (row > HdrRow + 1)
        {
            var dataRange = ws.Range(HdrRow, 1, row - 1, headers.Length);
            dataRange.Style.Border.OutsideBorder      = XLBorderStyleValues.Medium;
            dataRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#888888");
            dataRange.Style.Border.InsideBorder       = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorderColor  = XLColor.FromHtml("#CCCCCC");
        }

        // ── 列幅（固定） ──
        ws.Column(1).Width  = 6;
        ws.Column(2).Width  = 18;
        ws.Column(3).Width  = 30;
        ws.Column(4).Width  = 14;
        ws.Column(5).Width  = 14;
        ws.Column(6).Width  = 12;
        ws.Column(7).Width  = 12;
        ws.Column(8).Width  = 8;
        ws.Column(9).Width  = 10;
        ws.Column(10).Width = 16;
        ws.Column(11).Width = 12;
        ws.Column(12).Width = 12;
        ws.Column(13).Width = 32;
        ws.Column(14).Width = 28;

        // 説明・備考はWrap
        for (int c = 13; c <= 14; c++)
            ws.Column(c).Style.Alignment.WrapText = true;

        // 縦方向中央揃え
        ws.Rows(HdrRow, row - 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // ヘッダー行を固定
        ws.SheetView.FreezeRows(HdrRow);

        wb.SaveAs(filePath);
    }

    /// <summary>カテゴリーカラーを白方向にtintRatio分ブレンドしてXLColorを返す</summary>
    private static XLColor TintCategoryColor(string? hexColor, double tintRatio)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hexColor ?? "#2383E2");
            byte r = (byte)(c.R + (255 - c.R) * tintRatio);
            byte g = (byte)(c.G + (255 - c.G) * tintRatio);
            byte b = (byte)(c.B + (255 - c.B) * tintRatio);
            return XLColor.FromHtml($"#{r:X2}{g:X2}{b:X2}");
        }
        catch
        {
            return XLColor.FromHtml("#D8E8F5");
        }
    }

    // ══════════════════════════════════════════════════════
    // Gantt Controls
    // ══════════════════════════════════════════════════════

    private void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddMonths(-1);
        _viewDays  = DateTime.DaysInMonth(_viewStart.Year, _viewStart.Month) + 14;
        BuildUI();
    }
    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddMonths(1);
        _viewDays  = DateTime.DaysInMonth(_viewStart.Year, _viewStart.Month) + 14;
        BuildUI();
    }
    private void PrevWeek_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddDays(-7);
        BuildUI();
    }
    private void NextWeek_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddDays(7);
        BuildUI();
    }
    private void PrevDay_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddDays(-1);
        BuildUI();
    }
    private void NextDay_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = _viewStart.AddDays(1);
        BuildUI();
    }
    private void Today_Click(object sender, RoutedEventArgs e)
    {
        _viewStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _viewDays  = 60;
        BuildUI();
        double todayX = (DateTime.Today - _viewStart.Date).TotalDays * ColW;
        BarScroll.ScrollToHorizontalOffset(Math.Max(todayX - 100, 0));
    }

    private void MonthLabel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return)
        {
            ParseAndNavigateMonth(MonthLabel.Text);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            MonthLabel.Text = _viewStart.ToString("yyyy年 M月");
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void MonthLabel_LostFocus(object sender, RoutedEventArgs e)
        => ParseAndNavigateMonth(MonthLabel.Text);

    private void ParseAndNavigateMonth(string text)
    {
        text = text.Trim();
        var fmts = new[] { "yyyy年 M月", "yyyy年M月", "yyyy年 MM月", "yyyy年MM月",
                           "yyyy/M", "yyyy/MM", "yyyy-M", "yyyy-MM" };
        if (DateTime.TryParseExact(text, fmts,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d))
        {
            _viewStart = new DateTime(d.Year, d.Month, 1);
            BuildUI();
        }
        else
        {
            MonthLabel.Text = _viewStart.ToString("yyyy年 M月");
        }
    }

    // ══════════════════════════════════════════════════════
    // Task Actions
    // ══════════════════════════════════════════════════════

    private void UpdateCatToolbarState()
    {
        bool hasSel = _selectedCategoryId != null;
        if (BtnCatEdit   != null) { BtnCatEdit.IsEnabled   = hasSel; BtnCatEdit.Opacity   = hasSel ? 1.0 : 0.35; }
        if (BtnCatDelete != null) { BtnCatDelete.IsEnabled = hasSel; BtnCatDelete.Opacity = hasSel ? 1.0 : 0.35; }
    }

    // ── カテゴリ操作 ──────────────────────────────────────

    public void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CategoryDialog(null) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            _vm.ProjectService.AddCategory(dlg.CategoryName, dlg.Description, dlg.Color);
            Refresh();
        }
    }

    private void EditCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategoryId == null) return;
        var cat = _vm.ProjectService.CurrentProject?.Categories.FirstOrDefault(c => c.Id == _selectedCategoryId);
        if (cat == null) return;
        var dlg = new CategoryDialog(cat) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        bool rename = dlg.RenameFolder && cat.FolderCreated;
        var updated = new Category
        {
            Id = cat.Id, Name = dlg.CategoryName,
            Description = dlg.Description, Color = dlg.Color,
            FolderPath = cat.FolderPath, FolderCreated = cat.FolderCreated,
        };
        try { _vm.ProjectService.UpdateCategory(updated, renameFolder: rename); Refresh(); }
        catch (Exception ex) { AppDialog.ShowError($"更新に失敗しました: {ex.Message}", "エラー", Window.GetWindow(this)); }
    }

    private void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCategoryId == null) return;
        var cat = _vm.ProjectService.CurrentProject?.Categories.FirstOrDefault(c => c.Id == _selectedCategoryId);
        if (cat == null) return;
        if (!AppDialog.Confirm($"カテゴリー「{cat.Name}」を削除しますか？\n配下のタスクも削除されます。", "削除確認", Window.GetWindow(this))) return;
        bool deleteFolder = AppDialog.Confirm("フォルダも一緒に削除しますか？", "フォルダ削除", Window.GetWindow(this));
        _vm.ProjectService.DeleteCategory(cat.Id, deleteFolder: deleteFolder);
        _selectedCategoryId = null;
        CbCategoryFilter.SelectedIndex = 0;
        Refresh();
    }

    private void MoveCategoryBy(int delta)
    {
        if (_selectedCategoryId == null) return;
        var cats = _vm.ProjectService.CurrentProject?.Categories;
        if (cats == null) return;
        var sorted = cats.OrderBy(c => c.Order).ToList();
        var selIdx = sorted.FindIndex(c => c.Id == _selectedCategoryId);
        if (selIdx < 0) return;
        var newIdx = selIdx + delta;
        if (newIdx < 0 || newIdx >= sorted.Count) return;
        (sorted[selIdx].Order, sorted[newIdx].Order) = (sorted[newIdx].Order, sorted[selIdx].Order);
        _vm.ProjectService.ReorderCategories(sorted.OrderBy(c => c.Order).Select(c => c.Id).ToList());
        Refresh();
    }

    private void OpenCategoryFolder()
    {
        if (_selectedCategoryId == null) return;
        var cat = _vm.ProjectService.CurrentProject?.Categories.FirstOrDefault(c => c.Id == _selectedCategoryId);
        if (cat == null) return;
        if (System.IO.Directory.Exists(cat.FolderPath))
            ShellHelper.OpenInExplorer(cat.FolderPath);
        else
            AppDialog.ShowWarning("フォルダが見つかりません", "エラー", Window.GetWindow(this));
    }

    public void TriggerAddDialog()   => AddTask_Click(this, new RoutedEventArgs());
    public void TriggerExportCsv()   => ExportCsv_Click(this, new RoutedEventArgs());
    public void TriggerExportWbs()   => ExportWbs_Click(this, new RoutedEventArgs());

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        var cats = _vm.ProjectService.CurrentProject?.Categories.ToList();
        if (cats == null || cats.Count == 0)
        {
            AppDialog.ShowInfo("先にカテゴリーを追加してください", "カテゴリー未設定", Window.GetWindow(this));
            return;
        }
        var dlg = new TaskDialog(null, cats, IsPersonalMode) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            _vm.ProjectService.AddTask(dlg.CategoryId, dlg.TaskName, dlg.TaskNameShort,
                dlg.SubCategory, dlg.Environment, dlg.Assignee, dlg.Priority, dlg.Status,
                dlg.PlannedStart, dlg.PlannedEnd, dlg.Description, dlg.Notes, dlg.Tags);
            BuildUI();
        }
    }

    private void AddTaskInCategory(Category cat)
    {
        var cats = _vm.ProjectService.CurrentProject?.Categories.ToList() ?? new();
        var dlg = new TaskDialog(null, cats, IsPersonalMode) { Owner = Window.GetWindow(this) };
        dlg.PresetCategory(cat.Id);
        if (dlg.ShowDialog() == true)
        {
            _vm.ProjectService.AddTask(dlg.CategoryId, dlg.TaskName, dlg.TaskNameShort,
                dlg.SubCategory, dlg.Environment, dlg.Assignee, dlg.Priority, dlg.Status,
                dlg.PlannedStart, dlg.PlannedEnd, dlg.Description, dlg.Notes, dlg.Tags);
            BuildUI();
        }
    }

    private void OpenEditDialog(TaskItem task)
    {
        var cats = _vm.ProjectService.CurrentProject?.Categories.ToList() ?? new();
        var dlg = new TaskDialog(task, cats, IsPersonalMode) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            task.Name = dlg.TaskName; task.NameShort = dlg.TaskNameShort;
            task.CategoryId = dlg.CategoryId; task.SubCategory = dlg.SubCategory;
            task.Environment = dlg.Environment; task.Assignee = dlg.Assignee;
            task.Priority = dlg.Priority; task.Status = dlg.Status;
            task.PlannedStartDate = dlg.PlannedStart; task.PlannedEndDate = dlg.PlannedEnd;
            task.Description = dlg.Description; task.Notes = dlg.Notes; task.Tags = dlg.Tags;
            task.DelayApproved = dlg.DelayApproved;
            _vm.ProjectService.UpdateTask(task);
            BuildUI();
        }
    }

    private void ShowComments(TaskItem task)
    {
        new CommentDialog(task, _vm.ProjectService) { Owner = Window.GetWindow(this) }.ShowDialog();
        BuildUI();
    }

    private static void OpenFolder(TaskItem task)
    {
        if (Directory.Exists(task.FolderPath))
            ShellHelper.OpenInExplorer(task.FolderPath);
    }

    private void DeleteTask(TaskItem task)
    {
        if (!AppDialog.Confirm($"「{task.Name}」を削除しますか？\nタスクフォルダも同時に削除されます。", "タスク削除", Window.GetWindow(this)))
            return;
        _vm.ProjectService.DeleteTask(task.Id, deleteFolder: true);
        BuildUI();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "CSVエクスポート", Filter = "CSVファイル (*.csv)|*.csv",
            FileName = $"tasks_{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dlg.ShowDialog() == true)
        {
            var tasks = _rows.Where(r => r.Kind == RowKind.Task && r.Task != null)
                             .Select(r => r.Task!).ToList();
            _vm.ProjectService.ExportToCsv(dlg.FileName, tasks);
            AppDialog.ShowInfo("CSVエクスポートが完了しました", "完了", Window.GetWindow(this));
        }
    }

    // ══════════════════════════════════════════════════════
    // Drag & Drop (長押し → 並び替え)
    // ══════════════════════════════════════════════════════

    private void ArmDrag(TaskItem task, Border border, Point startPosOnPage)
    {
        CancelDragArm();
        _dragCandidate       = task;
        _dragCandidateBorder = border;
        _dragStartPos        = startPosOnPage;

        _dragArmTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _dragArmTimer.Tick += (_, _) =>
        {
            CancelDragArm();
            if (_dragCandidate != null && Mouse.LeftButton == MouseButtonState.Pressed)
                BeginDrag();
        };
        _dragArmTimer.Start();
    }

    private void CancelDragArm()
    {
        _dragArmTimer?.Stop();
        _dragArmTimer = null;
    }

    private void Page_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            UpdateDrag(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (_dragCandidate != null && _dragArmTimer != null)
        {
            // ドラッグ開始前：大きく動いたら長押し判定を取り消し（クリック扱い）
            var cur = e.GetPosition(this);
            if (Math.Abs(cur.X - _dragStartPos.X) > 6 || Math.Abs(cur.Y - _dragStartPos.Y) > 6)
            {
                CancelDragArm();
                _dragCandidate       = null;
                _dragCandidateBorder = null;
            }
        }
    }

    private void Page_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            EndDrag(commit: true);
            e.Handled = true;
            return;
        }
        CancelDragArm();
        _dragCandidate       = null;
        _dragCandidateBorder = null;
    }

    private void BeginDrag()
    {
        if (_dragCandidate == null || _dragCandidateBorder == null) return;
        _isDragging = true;

        _dragCandidateBorder.Opacity = 0.35;

        _dragGhost = new Border
        {
            Width = 320, Height = RowH,
            Background = new SolidColorBrush(Color.FromArgb(235, 47, 47, 47)),
            BorderBrush = AccentBlueBrush_,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 0, 12, 0),
            Effect = new DropShadowEffect
            {
                BlurRadius = 14, ShadowDepth = 4,
                Opacity = 0.55, Color = Colors.Black
            },
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = _dragCandidate.Name,
                FontSize = 14, Foreground = TextPrimBrush,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
        OverlayCanvas.Children.Add(_dragGhost);

        _dropIndicator = new Rectangle
        {
            Height = 3, Width = 460,
            Fill = AccentBlueBrush_,
            RadiusX = 1, RadiusY = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        OverlayCanvas.Children.Add(_dropIndicator);

        Mouse.Capture(this, CaptureMode.SubTree);
        UpdateDrag(Mouse.GetPosition(this));
    }

    private void UpdateDrag(Point posOnPage)
    {
        if (_dragGhost != null)
        {
            Canvas.SetLeft(_dragGhost, posOnPage.X + 12);
            Canvas.SetTop (_dragGhost, posOnPage.Y - RowH / 2);
        }

        // ドロップ対象のタスク行を検出
        _dropTargetTask = null;
        var posInTree = TaskTree.IsLoaded ? this.TranslatePoint(posOnPage, TaskTree) : posOnPage;
        foreach (var (brd, tsk) in _dragTargets)
        {
            if (!brd.IsVisible) continue;
            if (tsk.Id == _dragCandidate?.Id) continue;
            Point brdOrigin;
            try { brdOrigin = brd.TranslatePoint(new Point(0, 0), TaskTree); }
            catch { continue; }
            double h = brd.ActualHeight;
            if (h <= 0) continue;
            if (posInTree.Y >= brdOrigin.Y && posInTree.Y < brdOrigin.Y + h)
            {
                _dropTargetTask = tsk;
                _dropBefore = posInTree.Y < brdOrigin.Y + h / 2;
                ShowDropIndicator(brd, _dropBefore);
                return;
            }
        }

        if (_dropIndicator != null) _dropIndicator.Visibility = Visibility.Collapsed;
    }

    private void ShowDropIndicator(Border targetRow, bool before)
    {
        if (_dropIndicator == null) return;
        try
        {
            var origin = targetRow.TranslatePoint(new Point(0, 0), this);
            double y = before ? origin.Y - 1 : origin.Y + targetRow.ActualHeight - 2;
            Canvas.SetLeft(_dropIndicator, origin.X);
            Canvas.SetTop (_dropIndicator, y);
            _dropIndicator.Width = Math.Max(targetRow.ActualWidth, 100);
            _dropIndicator.Visibility = Visibility.Visible;
        }
        catch
        {
            _dropIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void EndDrag(bool commit)
    {
        _isDragging = false;
        ReleaseMouseCapture();

        if (_dragCandidateBorder != null)
            _dragCandidateBorder.Opacity = 1.0;

        if (_dragGhost != null)      OverlayCanvas.Children.Remove(_dragGhost);
        if (_dropIndicator != null) OverlayCanvas.Children.Remove(_dropIndicator);
        _dragGhost     = null;
        _dropIndicator = null;

        var src = _dragCandidate;
        var dst = _dropTargetTask;
        bool before = _dropBefore;

        _dragCandidate       = null;
        _dragCandidateBorder = null;
        _dropTargetTask      = null;

        if (commit && src != null && dst != null && src.Id != dst.Id)
            ReorderTask(src, dst, before);
    }

    private void ReorderTask(TaskItem src, TaskItem dst, bool before)
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return;

        var list = project.Tasks;
        int origIdx = list.IndexOf(src);
        if (!list.Remove(src)) return;

        var oldCategoryId = src.CategoryId;
        var oldFolderPath = src.FolderPath;

        src.CategoryId = dst.CategoryId;

        // カテゴリが変わった場合はフォルダを移動
        if (oldCategoryId != src.CategoryId)
        {
            var (ok, err) = _vm.ProjectService.TryMoveTaskFolderToCategory(src, src.CategoryId);
            if (!ok)
            {
                // ロールバック
                src.CategoryId = oldCategoryId;
                src.FolderPath = oldFolderPath;
                list.Insert(origIdx, src);
                AppDialog.ShowError($"フォルダ移動に失敗しました。\n{err}", "移動エラー", Window.GetWindow(this));
                return;
            }
        }

        int dstIdx = list.IndexOf(dst);
        if (dstIdx < 0) dstIdx = list.Count;
        int insertAt = before ? dstIdx : dstIdx + 1;
        insertAt = Math.Clamp(insertAt, 0, list.Count);
        list.Insert(insertAt, src);

        _vm.ProjectService.SaveProject();
        BuildUI();
    }

    // ══════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════

    private static Color ParseColor(string? hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex ?? "#2383E2"); }
        catch { return Color.FromRgb(35, 131, 226); }
    }

    private static string GetFileIcon(string path)
    {
        return System.IO.Path.GetExtension(path).ToLower() switch
        {
            ".txt" or ".md"              => "📄",
            ".pdf"                       => "📕",
            ".xlsx" or ".xls" or ".csv"  => "📊",
            ".docx" or ".doc"            => "📝",
            ".pptx" or ".ppt"            => "📑",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "🖼️",
            ".zip" or ".7z" or ".rar"    => "📦",
            ".exe" or ".bat"             => "⚙️",
            _                            => "📄"
        };
    }
}
