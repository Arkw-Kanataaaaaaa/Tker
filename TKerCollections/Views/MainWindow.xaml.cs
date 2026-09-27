using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TKer.ViewModels;
using TKer.Views.Pages;

namespace TKer.Views;

/// <summary>アプリケーションのメインウィンドウ。コレクション画面同士の遷移のみを扱う。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private bool _isNavigating = false;

    /// <summary>メインウィンドウを初期化し、コレクション画面を表示する。</summary>
    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentView))
                NavigateToCurrentView();
        };

        NavigateToCurrentView();
    }

    // ── ウィンドウ操作 ─────────────────────────────────────
    private void WinMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void WinMaximize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;

    private void WinClose_Click(object sender, RoutedEventArgs e)
        => Close();

    /// <summary>ウィンドウ状態変化時に最大化アイコンを切り替え、最大化マージンを補正する。</summary>
    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (BtnMaximize == null || MaximizeIcon == null) return;
        MaximizeIcon.Data = (System.Windows.Media.Geometry)FindResource(
            WindowState == WindowState.Maximized ? "Bi.FullscreenExit" : "Bi.Square");

        // 最大化時、Windows はリサイズ境界分だけウィンドウをスクリーン外に配置するため
        // タイトルバー等の内容が上にずれて見える。その分を RootGrid のマージンで補正する。
        RootGrid.Margin = WindowState == WindowState.Maximized
            ? new Thickness(
                SystemParameters.WindowResizeBorderThickness.Left,
                SystemParameters.WindowResizeBorderThickness.Top,
                SystemParameters.WindowResizeBorderThickness.Right,
                SystemParameters.WindowResizeBorderThickness.Bottom)
            : new Thickness(0);
    }

    /// <summary>現在のビュー名に対応するページを生成し、フェードアニメーション付きで画面遷移する。</summary>
    private void NavigateToCurrentView()
    {
        if (_isNavigating) return;
        _isNavigating = true;

        Page page = _vm.CurrentView switch
        {
            "CollectionItems" => new CollectionItemsPage(_vm),
            _                 => new CollectionPage(_vm),
        };

        var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) =>
        {
            (page as IRefreshable)?.Refresh();
            MainFrame.Navigate(page);

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fadeIn.Completed += (_, _) => _isNavigating = false;
            MainFrame.BeginAnimation(OpacityProperty, fadeIn);
        };
        MainFrame.BeginAnimation(OpacityProperty, fadeOut);
    }
}

/// <summary>再遷移時にデータ再読み込みが必要なページが実装するインターフェース。</summary>
public interface IRefreshable { void Refresh(); }
