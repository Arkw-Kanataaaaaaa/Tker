using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Helpers;
using TKer.ViewModels;
using TKer.Views;

namespace TKer.Views.Pages;

public partial class DashboardPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;

    public DashboardPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
        // ④ Loadedは1回だけ登録
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        UpdateProgressBar();
    }

    private void GoToDeliverable_Click(object sender, RoutedEventArgs e)
        => _vm.NavigateToCommand.Execute("Deliverable");

    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Dash_Header"));
        if (_vm.ProjectService.CurrentProject == null) return;

        var tasks = _vm.ProjectService.CurrentProject.Tasks
            .OrderByDescending(t => t.UpdatedAt).Take(10).ToList();
        RecentTaskList.ItemsSource = tasks;

        // ⑥ 競合チェック：ファイルが外部更新されていれば通知
        bool reloaded = _vm.ProjectService.TryReloadIfNewer();
        if (reloaded)
            ReloadBanner.Visibility = Visibility.Visible;
        else
            ReloadBanner.Visibility = Visibility.Collapsed;

        UpdateProgressBar();
        UpdateActualsSummary();
    }

    private void UpdateActualsSummary()
    {
        var project = _vm.ProjectService.CurrentProject;
        if (project == null) return;

        var work = project.ActualWork;
        var today = DateTime.Today;
        var weekStart  = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var monthStart = new DateTime(today.Year, today.Month, 1);

        double totalH = work.Sum(w => w.Hours);
        double weekH  = work.Where(w => w.Date.Date >= weekStart).Sum(w => w.Hours);
        double monthH = work.Where(w => w.Date.Date >= monthStart).Sum(w => w.Hours);

        ActualSummaryLabel.Text = $"累計 {totalH:F1}h";

        ActualStatsGrid.Children.Clear();
        ActualStatsGrid.Children.Add(MakeStatCard("今週", $"{weekH:F1}h", Color.FromRgb(35, 131, 226)));
        ActualStatsGrid.Children.Add(MakeStatCard("今月", $"{monthH:F1}h", Color.FromRgb(82, 158, 114)));
        ActualStatsGrid.Children.Add(MakeStatCard("累計", $"{totalH:F1}h", Color.FromRgb(217, 115, 13)));

        var catMap = project.Categories.ToDictionary(c => c.Id, c => c.Name);
        ActualTaskList.ItemsSource = project.Tasks
            .Select(t => new
            {
                TaskName     = t.Name,
                CategoryName = catMap.GetValueOrDefault(t.CategoryId, ""),
                TotalHours   = work.Where(w => w.TaskId == t.Id).Sum(w => w.Hours)
            })
            .Where(x => x.TotalHours > 0)
            .OrderByDescending(x => x.TotalHours)
            .Take(20)
            .ToList();
    }

    private static Border MakeStatCard(string label, string value, Color accent)
    {
        var sp = new StackPanel { Margin = new Thickness(4) };
        sp.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(120, 119, 116))
        });
        sp.Children.Add(new TextBlock
        {
            Text = value, FontFamily = new FontFamily("Consolas"),
            FontSize = 24, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(accent), Margin = new Thickness(0, 4, 0, 0)
        });
        return new Border
        {
            Child = sp, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(Color.FromRgb(47, 47, 47)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B))
        };
    }

    private void UpdateProgressBar()
    {
        int total = _vm.TotalTasks;
        int done  = _vm.DoneTasks;
        double pct = total > 0 ? (double)done / total : 0;
        ProgressText.Text = $"{pct:P0}";

        // ④ ActualWidth が確定してから幅を計算
        if (ProgressBarBg.ActualWidth > 0)
        {
            ProgressBarFill.Width = ProgressBarBg.ActualWidth * pct;
        }
        else
        {
            ProgressBarBg.SizeChanged += (_, _) =>
                ProgressBarFill.Width = ProgressBarBg.ActualWidth * pct;
        }
    }

    private void DismissReload_Click(object sender, RoutedEventArgs e)
        => ReloadBanner.Visibility = Visibility.Collapsed;
}
