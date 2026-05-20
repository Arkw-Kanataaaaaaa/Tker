using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TKer.Views;

/// <summary>サイドナビゲーション用のアイコン・ラベル・バッジを持つカスタムコントロール。</summary>
public partial class NavItem : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(NavItem),
            new PropertyMetadata("", OnPropsChanged));
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(NavItem),
            new PropertyMetadata("", OnPropsChanged));
    public static readonly DependencyProperty ViewProperty =
        DependencyProperty.Register(nameof(View), typeof(string), typeof(NavItem),
            new PropertyMetadata("", OnPropsChanged));
    public static readonly DependencyProperty CurrentViewProperty =
        DependencyProperty.Register(nameof(CurrentView), typeof(string), typeof(NavItem),
            new PropertyMetadata("", OnPropsChanged));
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.Register(nameof(Badge), typeof(int), typeof(NavItem),
            new PropertyMetadata(0, OnPropsChanged));
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(NavItem));
    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(NavItem));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string View { get => (string)GetValue(ViewProperty); set => SetValue(ViewProperty, value); }
    public string CurrentView { get => (string)GetValue(CurrentViewProperty); set => SetValue(CurrentViewProperty, value); }
    public int Badge { get => (int)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    public NavItem() { InitializeComponent(); }

    /// <summary>依存関係プロパティ変更時にビジュアルを更新するコールバック。</summary>
    private static void OnPropsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((NavItem)d).UpdateVisuals();

    /// <summary>アクティブ状態・バッジ数に応じてアイコン・ラベル・背景を更新する。</summary>
    private void UpdateVisuals()
    {
        IconBlock.Text = Icon;
        LabelBlock.Text = Label;

        bool isActive = !string.IsNullOrEmpty(View) && View == CurrentView;
        var blue = (SolidColorBrush)FindResource("AccentBlueBrush");
        var secondary = (SolidColorBrush)FindResource("TextSecondaryBrush");
        var transparent = Brushes.Transparent;

        if (isActive)
        {
            RootBorder.BorderBrush = blue;
            RootBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(26, 61, 126, 255));
            LabelBlock.Foreground = blue;
        }
        else
        {
            RootBorder.BorderBrush = transparent;
            RootBorder.Background = transparent;
            LabelBlock.Foreground = secondary;
        }

        if (Badge > 0)
        {
            BadgeBlock.Text = Badge.ToString();
            BadgeBlock.Visibility = Visibility.Visible;
        }
        else
        {
            BadgeBlock.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>ナビゲーション項目がクリックされたときにバインドされたコマンドを実行する。</summary>
    private void Root_Click(object sender, MouseButtonEventArgs e)
    {
        if (Command?.CanExecute(CommandParameter) == true)
            Command.Execute(CommandParameter);
    }
}
