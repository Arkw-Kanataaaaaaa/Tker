using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;

namespace TKer.Views.Pages;

public partial class LogViewerPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;
    private readonly AppLogger     _logger = AppLogger.Instance;
    private bool _autoScroll = true;
    private List<LogEntry> _allEntries = new();

    public LogViewerPage(MainViewModel vm)
    {
        _vm = vm;

        // コンバーター登録
        Resources["LogLevelColorConverter"] = new LogLevelColorConverter();
        Resources["NullVis"] = new NullToVisibilityConverter();
        Resources["ColHeader"] = BuildColHeaderStyle();

        InitializeComponent();

        // リアルタイム更新
        _logger.EntryAdded += OnEntryAdded;

        Loaded += (_, _) => Refresh();
    }

    // ── IRefreshable ─────────────────────────────────────
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Log_Header"));
        _allEntries = _logger.GetAll().ToList();
        ApplyFilter();
        UpdateStatus();
        if (_autoScroll) ScrollToBottom();
    }

    // ── リアルタイム受信 ──────────────────────────────────
    private void OnEntryAdded(LogEntry entry)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LogList == null) return; // まだ初期化されていない

            _allEntries.Add(entry);
            // フィルターに合致する場合だけ追加
            if (MatchFilter(entry))
            {
                // ItemsSource を全再描画（シンプルに最新状態を反映）
                ApplyFilter();
                if (_autoScroll) ScrollToBottom();
            }
        }, DispatcherPriority.Background);
    }

    // ── フィルター適用 ────────────────────────────────────
    private void ApplyFilter()
    {
        // InitializeComponent() 途中に呼ばれることがあるため null ガード
        if (LogList == null || TxtSearch == null) return;

        var filtered = _allEntries.Where(MatchFilter).ToList();
        LogList.ItemsSource = filtered;
        UpdateStatus();
    }

    private bool MatchFilter(LogEntry e)
    {
        // コントロールが未初期化の場合は全件通過
        if (ChkDebug == null) return true;

        // レベルフィルター
        if (e.Level == AppLogLevel.DEBUG && ChkDebug.IsChecked != true) return false;
        if (e.Level == AppLogLevel.INFO  && ChkInfo.IsChecked  != true) return false;
        if (e.Level == AppLogLevel.WARN  && ChkWarn.IsChecked  != true) return false;
        if (e.Level == AppLogLevel.ERROR && ChkError.IsChecked != true) return false;
        if (e.Level == AppLogLevel.FATAL && ChkFatal.IsChecked != true) return false;

        // キーワード
        var kw = TxtSearch?.Text.Trim() ?? "";
        if (!string.IsNullOrEmpty(kw))
        {
            var hit = e.Message.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                      e.ScreenName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                      e.FunctionName.Contains(kw, StringComparison.OrdinalIgnoreCase);
            if (!hit) return false;
        }
        return true;
    }

    private void UpdateStatus()
    {
        if (TxtStatus == null || LogList == null) return;
        var shown = (LogList.ItemsSource as System.Collections.IList)?.Count ?? 0;
        TxtStatus.Text = $"表示: {shown} / 全 {_allEntries.Count} 件";
    }

    private void ScrollToBottom()
    {
        LogScroller?.ScrollToEnd();
    }

    // ── イベントハンドラー ────────────────────────────────
    private void TxtSearch_TextChanged(object s, TextChangedEventArgs e) => ApplyFilter();
    private void Filter_Changed(object s, RoutedEventArgs e)             => ApplyFilter();

    private void BtnRefresh_Click(object s, RoutedEventArgs e)   => Refresh();

    private void BtnClear_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("メモリ上のログをクリアしますか？\n（ファイルは消えません）",
            "確認", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        _allEntries.Clear();
        LogList.ItemsSource = null;
        UpdateStatus();
    }

    private void BtnOpenFolder_Click(object s, RoutedEventArgs e)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TKer", "logs");
        if (Directory.Exists(dir))
            Process.Start("explorer.exe", dir);
        else
            MessageBox.Show("ログフォルダがまだ存在しません。");
    }

    private void TxtAutoScroll_Click(object s, System.Windows.Input.MouseButtonEventArgs e)
    {
        _autoScroll = !_autoScroll;
        TxtAutoScroll.Text = $"自動スクロール: {(_autoScroll ? "ON" : "OFF")}";
        if (_autoScroll) ScrollToBottom();
    }

    // ── ヘルパー ──────────────────────────────────────────
    private static Style BuildColHeaderStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.0));
        style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)));
        return style;
    }
}

// ── ログレベル色コンバーター ──────────────────────────────
public class LogLevelColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AppLogLevel level)
        {
            return level switch
            {
                AppLogLevel.DEBUG => new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xB5)),
                AppLogLevel.INFO  => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                AppLogLevel.WARN  => new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)),
                AppLogLevel.ERROR => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
                AppLogLevel.FATAL => new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C)),
                _                 => new SolidColorBrush(Colors.White)
            };
        }
        return new SolidColorBrush(Colors.White);
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

// ── null → Visibility コンバーター ───────────────────────
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value == null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

