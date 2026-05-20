using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TKer.Services;
using TKer.ViewModels;
using TKer.Helpers;

namespace TKer.Views.Pages;

public partial class DeliverablePage : Page, IRefreshable
{
    private readonly MainViewModel   _vm;
    private readonly DeliverableService _svc;
    private string? _lastOutputPath;
    private string? _lastZipPath;

    public DeliverablePage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = new DeliverableService(vm.ProjectService);
        InitializeComponent();
    }

    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Del_Header"));
        UpdateCompletionStatus();
        LoadCategories();
    }

    private void UpdateCompletionStatus()
    {
        var (allDone, done, total, incomplete) = _svc.CheckCompletion();
        TxtDoneCount.Text       = done.ToString();
        TxtIncompleteCount.Text = incomplete.Count.ToString();
        TxtProgressRate.Text    = total > 0 ? $"{done * 100 / total}%" : "0%";

        if (incomplete.Count > 0)
        {
            IncompleteBanner.Visibility = Visibility.Visible;
            IncompleteList.ItemsSource  = incomplete;
        }
        else
        {
            IncompleteBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadCategories()
    {
        var cats = new[] { new { Id = (string?)null, Name = "すべてのカテゴリー" } }
            .Concat(
                _vm.ProjectService.CurrentProject?.Categories
                    .Select(c => new { Id = (string?)c.Id, Name = c.Name })
                ?? Enumerable.Empty<dynamic>()
            ).ToList();

        CbCategory.ItemsSource      = cats;
        CbCategory.DisplayMemberPath = "Name";
        CbCategory.SelectedValuePath = "Id";
        CbCategory.SelectedIndex    = 0;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var picked = FolderPicker.Pick("成果物の出力先フォルダを選択");
        if (picked != null) TxtOutputPath.Text = picked;
    }

    private void Collect_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ProjectService.CurrentProject == null)
        {
            AppendLog("❌ プロジェクトが読み込まれていません");
            return;
        }

        var options = new DeliverableService.CollectOptions
        {
            OutputBasePath   = string.IsNullOrWhiteSpace(TxtOutputPath.Text) ? null : TxtOutputPath.Text,
            OutputFolderName = string.IsNullOrWhiteSpace(TxtFolderName.Text) ? "成果物まとめ" : TxtFolderName.Text,
            FilterCategoryId = CbCategory.SelectedValue as string,
            CompletedOnly    = ChkCompletedOnly.IsChecked == true,
            GroupByCategory  = ChkGroupByCategory.IsChecked == true,
            CreateZip        = ChkCreateZip.IsChecked == true,
            CreateIndex      = ChkCreateIndex.IsChecked == true,
            ExtensionFilter  = TxtExtFilter.Text.Trim()
        };

        AppendLog($"▶ 成果物まとめを開始します...");
        AppendLog($"  対象: {(options.CompletedOnly ? "完了タスクのみ" : "全タスク")}");
        if (!string.IsNullOrEmpty(options.ExtensionFilter))
            AppendLog($"  拡張子: {options.ExtensionFilter}");

        var result = _svc.Collect(options);
        _lastOutputPath = result.OutputPath;
        _lastZipPath    = result.ZipPath;

        if (result.Success)
        {
            AppendLog($"✅ 完了: {result.TotalFiles}ファイル ({FormatBytes(result.TotalBytes)})");
            AppendLog($"   出力先: {result.OutputPath}");
            if (result.ZipPath != null)  AppendLog($"   ZIP:    {result.ZipPath}");
            if (result.IndexPath != null) AppendLog($"   INDEX:  {result.IndexPath}");

            foreach (var err in result.Errors)
                AppendLog($"  ⚠️ {err}");

            ResultButtons.Visibility = Visibility.Visible;
            BtnOpenZip.Visibility    = result.ZipPath != null ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            AppendLog($"❌ エラー: {string.Join(", ", result.Errors)}");
        }
    }

    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastOutputPath) && System.IO.Directory.Exists(_lastOutputPath))
            ShellHelper.OpenInExplorer(_lastOutputPath);
    }

    private void OpenZip_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastZipPath) && System.IO.File.Exists(_lastZipPath))
            Process.Start(new ProcessStartInfo(_lastZipPath) { UseShellExecute = true });
    }

    private void AppendLog(string msg)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        var prev = LogBox.Text == "— 実行待ち —" ? "" : LogBox.Text + "\n";
        LogBox.Text = $"{prev}[{ts}] {msg}";
        LogBox.ScrollToEnd();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024        => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _             => $"{bytes / (1024.0 * 1024):F1} MB"
    };
}
