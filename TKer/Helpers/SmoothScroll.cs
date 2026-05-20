using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace TKer.Helpers;

/// <summary>
/// 全 ScrollViewer に慣性スクロールを付与する。
/// App.xaml.cs の OnStartup で Register() を呼ぶだけで有効になる。
/// </summary>
public static class SmoothScroll
{
    private const double FRICTION       = 0.15;  // 1フレームで縮まる割合（小さいほど滑らか）
    private const double STOP_THRESHOLD = 0.5;   // px 以下で停止
    private const double WHEEL_SCALE    = 1.2;   // マウスホイール 1 ノッチあたりのスクロール量係数
    private const int    FRAME_MS       = 16;    // ~60fps

    private static readonly Dictionary<ScrollViewer, ScrollState> _states = new();

    /// <summary>全 ScrollViewer に対してマウスホイールイベントハンドラーを登録する。</summary>
    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnWheel),
            handledEventsToo: true);
    }

    /// <summary>マウスホイールイベントを処理し、慣性スクロール状態を更新する。</summary>
    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.ScrollableHeight <= 0 && sv.ScrollableWidth <= 0) return;

        e.Handled = true;

        double delta = -e.Delta * WHEEL_SCALE;

        if (!_states.TryGetValue(sv, out var state))
        {
            state = new ScrollState(sv);
            _states[sv] = state;
        }

        state.AddDelta(delta);
    }

    /// <summary>
    /// 個々の ScrollViewer に対するスクロール状態と慣性タイマーを保持するクラス。
    /// </summary>
    private sealed class ScrollState
    {
        private readonly ScrollViewer _sv;
        private readonly DispatcherTimer _timer;
        private double _targetOffset;

        /// <summary>指定 ScrollViewer のスクロール状態を初期化する。</summary>
        public ScrollState(ScrollViewer sv)
        {
            _sv = sv;
            _targetOffset = sv.VerticalOffset;
            _timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(FRAME_MS),
                DispatcherPriority.Render,
                Tick,
                sv.Dispatcher);
        }

        /// <summary>スクロール目標位置にデルタ値を加算してタイマーを起動する。</summary>
        public void AddDelta(double delta)
        {
            _targetOffset = Math.Clamp(
                _targetOffset + delta,
                0,
                _sv.ScrollableHeight);

            if (!_timer.IsEnabled)
                _timer.Start();
        }

        /// <summary>フレームごとに呼ばれる指数減衰スクロール処理。</summary>
        private void Tick(object? sender, EventArgs e)
        {
            double current = _sv.VerticalOffset;
            double diff    = _targetOffset - current;

            if (Math.Abs(diff) < STOP_THRESHOLD)
            {
                _sv.ScrollToVerticalOffset(_targetOffset);
                _timer.Stop();
                _states.Remove(_sv);
                return;
            }

            // 指数減衰（EaseOut 相当）
            _sv.ScrollToVerticalOffset(current + diff * FRICTION * (FRAME_MS / 8.0));
        }
    }
}
