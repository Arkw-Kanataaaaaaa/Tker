using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TKer.Models;

namespace TKer.Views.Dialogs;

/// <summary>カスタムテーブルの名前とカラム定義を編集するダイアログ。</summary>
public partial class TableColumnDialog : Window
{
    public string TableName { get; private set; } = string.Empty;
    public List<TableColumn> Columns { get; private set; } = new();

    private readonly List<(TextBox NameBox, ComboBox TypeBox)> _rows = new();

    /// <summary>既存テーブルがある場合はその値をフォームに反映して初期化する。</summary>
    public TableColumnDialog(CustomTable? existing = null)
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        if (existing != null)
        {
            TxtTableName.Text = existing.Name;
            foreach (var col in existing.Columns.OrderBy(c => c.Order))
                AddColumnRow(col.Name, col.Type);
        }
        UpdateNoColumnText();
        Loaded += (_, _) => TxtTableName.Focus();
    }

    private void AddColumn_Click(object sender, RoutedEventArgs e)
    {
        AddColumnRow("", "text");
        UpdateNoColumnText();
    }

    /// <summary>指定した名前と型でカラム入力行をリストに追加する。</summary>
    private void AddColumnRow(string name, string type)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var nameBox = new TextBox
        {
            Text = name,
            Style = (Style)FindResource("DarkTextBox"),
            Margin = new Thickness(0, 0, 6, 0)
        };
        Grid.SetColumn(nameBox, 0);

        var typeBox = new ComboBox
        {
            ItemsSource = new[] { "text", "number", "autonumber" },
            SelectedItem = type,
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            Foreground = new SolidColorBrush(Color.FromRgb(207, 207, 207)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60))
        };
        if (typeBox.SelectedItem == null) typeBox.SelectedIndex = 0;
        Grid.SetColumn(typeBox, 1);

        var delBtn = new Button
        {
            Content = "✕",
            Style = (Style)FindResource("SecondaryButton"),
            Padding = new Thickness(4, 2, 4, 2),
            FontSize = 11
        };
        Grid.SetColumn(delBtn, 2);

        var captured = (nameBox, typeBox);
        delBtn.Click += (_, _) =>
        {
            ColumnList.Items.Remove(grid);
            _rows.Remove(captured);
            UpdateNoColumnText();
        };

        grid.Children.Add(nameBox);
        grid.Children.Add(typeBox);
        grid.Children.Add(delBtn);

        ColumnList.Items.Add(grid);
        _rows.Add(captured);
    }

    /// <summary>カラムが0件のときの空欄メッセージ表示を更新する。</summary>
    private void UpdateNoColumnText()
    {
        NoColumnText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>入力値を検証してテーブル名とカラム定義を確定しダイアログを閉じる。</summary>
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtTableName.Text))
        {
            MessageBox.Show("テーブル名を入力してください", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_rows.Count == 0)
        {
            MessageBox.Show("列を1つ以上追加してください", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TableName = TxtTableName.Text.Trim();
        Columns = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.NameBox.Text))
            .Select((r, i) => new TableColumn
            {
                Name  = r.NameBox.Text.Trim(),
                Type  = r.TypeBox.SelectedItem?.ToString() ?? "text",
                Order = i
            })
            .ToList();

        if (Columns.Count == 0)
        {
            MessageBox.Show("有効な列名を入力してください", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
