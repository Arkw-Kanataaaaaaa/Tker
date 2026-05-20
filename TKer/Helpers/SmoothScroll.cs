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
    private const double Friction    = 0.15;  // 1フレームで縮まる割合（小さいほど滑らか）
    private const double StopThreshold = 0.5; // px 以下で停止
    private const double WheelScale  = 1.2;   // マウスホイール 1 ノッチあたりのスクロール量係数
    private const int    FrameMs     = 16;    // ~60fps

    private static readonly Dictionary<ScrollViewer, ScrollState> _states = new();

    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnWheel),
            handledEventsToo: true);
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.ScrollableHeight <= 0 && sv.ScrollableWidth <= 0) return;

        e.Handled = true;

        double delta = -e.Delta * WheelScale;

        if (!_states.TryGetValue(sv, out var state))
        {
            state = new ScrollState(sv);
            _states[sv] = state;
        }

        state.AddDelta(delta);
    }

    private sealed class ScrollState
    {
        private readonly ScrollViewer _sv;
        private readonly DispatcherTimer _timer;
        private double _targetOffset;

        public ScrollState(ScrollViewer sv)
        {
            _sv = sv;
            _targetOffset = sv.VerticalOffset;
            _timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(FrameMs),
                DispatcherPriority.Render,
                Tick,
                sv.Dispatcher);
        }

        public void AddDelta(double delta)
        {
            _targetOffset = Math.Clamp(
                _targetOffset + delta,
                0,
                _sv.ScrollableHeight);

            if (!_timer.IsEnabled)
                _timer.Start();
        }

        private void Tick(object? sender, EventArgs e)
        {
            double current = _sv.VerticalOffset;
            double diff    = _targetOffset - current;

            if (Math.Abs(diff) < StopThreshold)
            {
                _sv.ScrollToVerticalOffset(_targetOffset);
                _timer.Stop();
                _states.Remove(_sv);
                return;
            }

            // 指数減衰（EaseOut 相当）
            _sv.ScrollToVerticalOffset(current + diff * Friction * (FrameMs / 8.0));
        }
    }
}
