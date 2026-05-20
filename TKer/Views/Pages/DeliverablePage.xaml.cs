using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TKer.Services;
using TKer.ViewModels;
using TKer.Helpers;

namespace TKer.Views.Pages;

/// <summary>プロジェクト成果物の収集・まとめ処理を行うページ。</summary>
public partial class DeliverablePage : Page, IRefreshable
{
    private readonly MainViewModel   _vm;
    private readonly DeliverableService _svc;
    private string? _lastOutputPath;
    private string? _lastZipPath;

    /// <summary>成果物ページを初期化してサービスを設定する。</summary>
    public DeliverablePage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = new DeliverableService(vm.ProjectService);
        InitializeComponent();
    }

    /// <summary>テーマを適用して完了状況とカテゴリー一覧を更新する。</summary>
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Del_Header"));
        UpdateCompletionStatus();
        LoadCategories();
    }

    /// <summary>タスク完了率と未完了リストをUIに反映する。</summary>
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

    /// <summary>プロジェクトのカテゴリー一覧をコンボボックスに設定する。</summary>
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

    /// <summary>出力先フォルダー選択ダイアログを開いてパスを設定する。</summary>
    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var picked = FolderPicker.Pick("成果物の出力先フォルダを選択");
        if (picked != null) TxtOutputPath.Text = picked;
    }

    /// <summary>設定オプションに基づいて成果物収集処理を実行する。</summary>
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

    /// <summary>出力フォルダーをエクスプローラーで開く。</summary>
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastOutputPath) && System.IO.Directory.Exists(_lastOutputPath))
            ShellHelper.OpenInExplorer(_lastOutputPath);
    }

    /// <summary>生成されたZIPファイルをシェルで開く。</summary>
    private void OpenZip_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastZipPath) && System.IO.File.Exists(_lastZipPath))
            Process.Start(new ProcessStartInfo(_lastZipPath) { UseShellExecute = true });
    }

    /// <summary>タイムスタンプ付きでログボックスにメッセージを追記する。</summary>
    private void AppendLog(string msg)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        var prev = LogBox.Text == "— 実行待ち —" ? "" : LogBox.Text + "\n";
        LogBox.Text = $"{prev}[{ts}] {msg}";
        LogBox.ScrollToEnd();
    }

    /// <summary>バイト数を読みやすい単位の文字列に変換する。</summary>
    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024        => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _             => $"{bytes / (1024.0 * 1024):F1} MB"
    };
}
