using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TKer.Models;

namespace TKer.Views.Dialogs;

/// <summary>コレクションの新規作成・編集を行うダイアログ。</summary>
public partial class CollectionEditDialog : Window
{
    private readonly Collection?          _existing;
    private readonly List<CollectionField> _fields = new();

    /// <summary>OK 押下後に確定したコレクション情報</summary>
    public Collection? Result { get; private set; }

    /// <summary>既存コレクションがある場合はその値をフォームに反映して初期化する。</summary>
    public CollectionEditDialog(Collection? existing = null)
    {
        InitializeComponent();
        _existing = existing;

        if (existing != null)
        {
            Title         = "コレクションを編集";
            BtnOk.Content = "保存";

            TxtName.Text       = existing.Name;
            TxtDesc.Text       = existing.Description;
            TxtFolder.Text     = existing.FolderPath;
            TxtDataFormat.Text = existing.ItemFormat;
            _fields.AddRange(existing.Fields.Select(f => new CollectionField
            {
                Id = f.Id, Name = f.Name, FieldType = f.FieldType, Order = f.Order
            }));
        }

        Loaded += (_, _) =>
        {
            TxtName.Focus();
            RefreshFieldList();
        };
    }

    // ══════ フィールド一覧 ══════

    /// <summary>フィールド一覧パネルを現在のフィールドリストで再構築する。</summary>
    private void RefreshFieldList()
    {
        FieldListPanel.Children.Clear();

        if (_fields.Count == 0)
        {
            FieldListPanel.Children.Add(new TextBlock
            {
                Text       = "フィールドが未定義です",
                FontSize   = 12,
                Foreground = Brush("TextDimBrush"),
                Margin     = new Thickness(0, 0, 0, 8),
            });
            return;
        }

        foreach (var field in _fields)
            FieldListPanel.Children.Add(BuildFieldRow(field));
    }

    /// <summary>フィールドの種別に応じたアイコンとバッジを含む行UIを構築する。</summary>
    private UIElement BuildFieldRow(CollectionField field)
    {
        var (icon, label, badgeColor) = field.FieldType switch
        {
            "画像"  => ("🖼", "画像",  Color.FromArgb(200, 120, 60, 200)),
            "リンク" => ("🔗", "リンク", Color.FromArgb(200, 30, 140, 80)),
            _       => ("📝", "文字列", Color.FromArgb(200, 35, 100, 200)),
        };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });

        // アイコン
        var iconTb = new TextBlock
        {
            Text = icon, FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(iconTb, 0); g.Children.Add(iconTb);

        // フィールド名
        var nameTb = new TextBlock
        {
            Text      = field.Name, FontSize = 13,
            Foreground = Brush("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(6, 0, 0, 0),
        };
        Grid.SetColumn(nameTb, 1); g.Children.Add(nameTb);

        // 形式バッジ
        var badge = new Border
        {
            Background        = new SolidColorBrush(badgeColor),
            CornerRadius      = new CornerRadius(4),
            Padding           = new Thickness(7, 2, 7, 2),
            Margin            = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock
        {
            Text = label, FontSize = 11, Foreground = Brushes.White,
        };
        Grid.SetColumn(badge, 2); g.Children.Add(badge);

        // 削除ボタン
        var del = new Button
        {
            Content         = "✕", FontSize = 12,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground      = Brush("TextDimBrush"),
            Cursor          = Cursors.Hand,
            Padding         = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var cap = field;
        del.Click += (_, _) => { _fields.Remove(cap); RefreshFieldList(); };
        Grid.SetColumn(del, 3); g.Children.Add(del);

        var row = new Border
        {
            Background      = Brush("BgSecondaryBrush"),
            BorderBrush     = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10, 7, 10, 7),
            Margin          = new Thickness(0, 0, 0, 5),
            Child           = g,
        };
        return row;
    }

    // ══════ イベント ══════

    /// <summary>新規フィールド名と種別を入力して一覧に追加する。</summary>
    private void AddField_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtNewFieldName.Text.Trim();
        if (string.IsNullOrEmpty(name)) { TxtNewFieldName.Focus(); return; }

        var type = (CbFieldType.SelectedItem as ComboBoxItem)?.Tag as string ?? "文字列";

        _fields.Add(new CollectionField
        {
            Name      = name,
            FieldType = type,
            Order     = _fields.Count,
        });

        TxtNewFieldName.Text = "";
        TxtNewFieldName.Focus();
        RefreshFieldList();
    }

    /// <summary>フォルダー選択ダイアログでコレクションフォルダを設定する。</summary>
    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "コレクションフォルダを選択",
        };
        if (dlg.ShowDialog() == true)
            TxtFolder.Text = dlg.FolderName;
    }

    /// <summary>入力を検証してコレクションオブジェクトを生成しダイアログを確定する。</summary>
    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        {
            TxtName.Focus();
            return;
        }

        // 順序を更新
        for (int i = 0; i < _fields.Count; i++)
            _fields[i].Order = i;

        Result = new Collection
        {
            Id          = _existing?.Id          ?? Guid.NewGuid().ToString("N")[..8],
            Name        = TxtName.Text.Trim(),
            Icon        = _existing?.Icon        ?? "📁",
            Description = TxtDesc.Text.Trim(),
            FolderPath  = TxtFolder.Text.Trim(),
            ItemFormat  = TxtDataFormat.Text.Trim(),
            Fields      = _fields.ToList(),
            Items       = _existing?.Items       ?? new(),
            CreatedAt   = _existing?.CreatedAt   ?? DateTime.Now,
            UpdatedAt   = DateTime.Now,
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ══════ ユーティリティ ══════

    /// <summary>アプリケーションリソースからブラシを取得する。見つからない場合は透明を返す。</summary>
    private static Brush Brush(string key)
    {
        try { return (Brush)Application.Current.Resources[key]; }
        catch { return System.Windows.Media.Brushes.Transparent; }
    }
}
