using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TKer.Helpers;
using TKer.Services;

namespace TKer.Views.Dialogs;

/// <summary>
/// ウィンドウレイアウト適用の結果を、アイコン+アプリ名+〇/× の表形式で
/// 表示する TKer 標準ダイアログ。
/// </summary>
public partial class WindowLayoutApplyResultDialog : Window
{
    /// <summary>タイトルと適用結果を指定してダイアログを構築する。</summary>
    public WindowLayoutApplyResultDialog(string title, ApplyResult result)
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => Close()));
        Title             = title;
        TitleBarText.Text = title;
        HeaderTitle.Text  = title;
        BuildRows(result);
    }

    /// <summary>結果エントリの一覧を行として組み立てて StackPanel に追加する。</summary>
    private void BuildRows(ApplyResult result)
    {
        var successBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x9E, 0x72)); // 緑
        var failBrush    = new SolidColorBrush(Color.FromRgb(0xE0, 0x3E, 0x3E)); // 赤
        var borderBrush  = (Brush)FindResource("BorderBrush");
        var textBrush    = (Brush)FindResource("TextPrimaryBrush");
        var dimBrush     = (Brush)FindResource("TextDimBrush");

        for (int i = 0; i < result.Entries.Count; i++)
        {
            var entry  = result.Entries[i];
            bool isLast = i == result.Entries.Count - 1;

            var row = new Border
            {
                Padding         = new Thickness(14, 8, 14, 8),
                BorderBrush     = borderBrush,
                BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });

            // アイコン
            var iconSrc = AppIconHelper.TryGetExeIcon(entry.ExePath);
            if (iconSrc != null)
            {
                var img = new Image
                {
                    Source = iconSrc, Width = 24, Height = 24,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(img, 0);
                grid.Children.Add(img);
            }

            // アプリ名
            var nameStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            nameStack.Children.Add(new TextBlock
            {
                Text         = string.IsNullOrEmpty(entry.Title)
                                 ? System.IO.Path.GetFileNameWithoutExtension(entry.ExePath)
                                 : entry.Title,
                FontSize     = 13, FontWeight = FontWeights.SemiBold,
                Foreground   = textBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (!string.IsNullOrEmpty(entry.ExePath))
                nameStack.Children.Add(new TextBlock
                {
                    Text         = System.IO.Path.GetFileName(entry.ExePath),
                    FontSize     = 10,
                    Foreground   = dimBrush,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin       = new Thickness(0, 2, 0, 0)
                });
            Grid.SetColumn(nameStack, 1);
            grid.Children.Add(nameStack);

            // 結果マーク
            var mark = new TextBlock
            {
                Text       = entry.Success ? "〇" : "×",
                FontSize   = 20, FontWeight = FontWeights.Bold,
                Foreground = entry.Success ? successBrush : failBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(mark, 2);
            grid.Children.Add(mark);

            row.Child = grid;
            EntryList.Children.Add(row);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
