using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TKer.Views.Dialogs;

/// <summary>画像プレビューウィンドウ（Ctrl+スクロールでズーム、ドラッグで移動）。</summary>
public sealed class ImagePreviewWindow : Window
{
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly TranslateTransform _translate = new(0, 0);
    private readonly TextBlock _zoomLabel;
    private Point _dragStart;
    private Point _translateStart;
    private bool _isDragging;

    private const double MinScale = 0.1;
    private const double MaxScale = 10.0;
    private const double ZoomStep = 0.15;

    public ImagePreviewWindow(BitmapImage bitmap)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0));
        WindowState = WindowState.Maximized;
        Topmost = true;

        var transform = new TransformGroup();
        transform.Children.Add(_scale);
        transform.Children.Add(_translate);

        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = transform,
            MaxWidth = SystemParameters.PrimaryScreenWidth * 0.9,
            MaxHeight = SystemParameters.PrimaryScreenHeight * 0.9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 閉じるボタン（右上）
        var closeBtn = new Button
        {
            Content = "×",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Width = 44,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
        };
        closeBtn.Click += (_, _) => Close();

        // ズームラベル（右下）
        _zoomLabel = new TextBlock
        {
            Text = "100%",
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 12, 12),
        };

        var root = new Grid();
        root.Children.Add(image);
        root.Children.Add(closeBtn);
        root.Children.Add(_zoomLabel);
        Content = root;

        // ズーム（Ctrl+スクロール）
        root.PreviewMouseWheel += OnMouseWheel;

        // ドラッグ（パン）
        root.MouseLeftButtonDown += OnMouseDown;
        root.MouseMove += OnMouseMove;
        root.MouseLeftButtonUp += OnMouseUp;

        // Escape で閉じる
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };

    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl)) return;

        double factor = e.Delta > 0 ? (1 + ZoomStep) : (1 - ZoomStep);
        double newScale = Math.Clamp(_scale.ScaleX * factor, MinScale, MaxScale);
        _scale.ScaleX = newScale;
        _scale.ScaleY = newScale;
        _zoomLabel.Text = $"{(int)Math.Round(newScale * 100)}%";
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source == sender) { Close(); return; } // 背景クリックで閉じる
        _isDragging = true;
        _dragStart = e.GetPosition((IInputElement)sender);
        _translateStart = new Point(_translate.X, _translate.Y);
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition((IInputElement)sender);
        _translate.X = _translateStart.X + (pos.X - _dragStart.X);
        _translate.Y = _translateStart.Y + (pos.Y - _dragStart.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }
}
