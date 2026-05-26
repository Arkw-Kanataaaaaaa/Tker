using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using TKer.Models;
using TKer.Services;

namespace TKer.Views.Dialogs;

/// <summary>テンプレート一覧に表示するためのビューモデル行。</summary>
public class TemplateListItem
{
    public string Id              { get; init; } = "";
    public string Name            { get; init; } = "";
    public bool IsBuiltIn         { get; init; }
    public bool IsUserCreated     => !IsBuiltIn;
    public string CategoryCountLabel => $"{ColorDots.Count}カテゴリー";
    public List<Color> ColorDots  { get; init; } = new();
}

/// <summary>カテゴリー作成行の編集用データクラス。</summary>
public class EditableCategoryRow : INotifyPropertyChanged
{
    private string _name        = "";
    private string _description = "";
    private string _hexColor    = "#3D7EFF";

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string HexColor
    {
        get => _hexColor;
        set
        {
            _hexColor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewColor));
        }
    }

    public Color PreviewColor
    {
        get
        {
            try { return (Color)ColorConverter.ConvertFromString(_hexColor); }
            catch { return Colors.Gray; }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>カテゴリーテンプレートの一覧表示・作成・削除を行うダイアログ。</summary>
public partial class CategoryTemplateManagerDialog : Window
{
    private readonly CategoryTemplateService _service;
    private string _searchText = "";

    private static readonly string[] PRESET_COLORS =
    {
        "#5C6BC0", "#26A69A", "#2383E2", "#F57C00", "#388E3C",
        "#E91E63", "#7B1FA2", "#1565C0", "#C62828", "#E65100",
        "#3D7EFF", "#6A1B9A", "#AD1457", "#0277BD", "#2E7D32",
    };

    public CategoryTemplateManagerDialog(CategoryTemplateService service)
    {
        InitializeComponent();
        _service = service;

        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => Close()));

        RefreshList();
    }

    // ── 一覧表示 ──────────────────────────────────────────

    private void RefreshList()
    {
        var items = BuildListItems();
        ApplySearch(items);
    }

    private List<TemplateListItem> BuildListItems()
    {
        var result = new List<TemplateListItem>();

        // 組み込みテンプレート
        foreach (var kv in CategoryTemplateDialog.BuiltInTemplates)
        {
            result.Add(new TemplateListItem
            {
                Id       = $"__builtin_{kv.Key}",
                Name     = kv.Key,
                IsBuiltIn = true,
                ColorDots = kv.Value.Select(TryParseColor).ToList(),
            });
        }

        // ユーザー作成テンプレート
        foreach (var p in _service.UserPresets)
        {
            result.Add(new TemplateListItem
            {
                Id       = p.Id,
                Name     = p.Name,
                IsBuiltIn = false,
                ColorDots = p.Categories.Select(c => TryParseColor(c.Color)).ToList(),
            });
        }

        return result;
    }

    private void ApplySearch(List<TemplateListItem> items)
    {
        var filtered = string.IsNullOrWhiteSpace(_searchText)
            ? items
            : items.Where(i => i.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        TemplateList.ItemsSource = filtered;
    }

    private static Color TryParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
    }

    private static Color TryParseColor(TemplateCategoryItem item) => TryParseColor(item.Color);

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text;
        ApplySearch(BuildListItems());
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string id) return;

        var result = MessageBox.Show("このテンプレートを削除しますか？", "確認",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        _service.Delete(id);
        RefreshList();
    }

    // ── パネル切り替え ────────────────────────────────────

    private void ShowCreatePanel_Click(object sender, RoutedEventArgs e)
    {
        TxtTemplateName.Text = "";

        var rows = new ObservableCollection<EditableCategoryRow>();
        rows.Add(NewRow());
        CategoryRows.ItemsSource = rows;

        PanelList.Visibility   = Visibility.Collapsed;
        PanelCreate.Visibility = Visibility.Visible;
    }

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        PanelCreate.Visibility = Visibility.Collapsed;
        PanelList.Visibility   = Visibility.Visible;
        RefreshList();
    }

    // ── 作成フォーム ──────────────────────────────────────

    private void AddCategoryRow_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryRows.ItemsSource is ObservableCollection<EditableCategoryRow> rows)
            rows.Add(NewRow());
    }

    private void RemoveCategoryRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.Tag is not EditableCategoryRow row) return;
        if (CategoryRows.ItemsSource is ObservableCollection<EditableCategoryRow> rows)
            rows.Remove(row);
    }

    private void SaveTemplate_Click(object sender, RoutedEventArgs e)
    {
        var name = TxtTemplateName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("テンプレート名を入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTemplateName.Focus();
            return;
        }

        var rows = (CategoryRows.ItemsSource as ObservableCollection<EditableCategoryRow>)?.ToList()
                   ?? new();
        var validRows = rows.Where(r => !string.IsNullOrWhiteSpace(r.Name)).ToList();
        if (validRows.Count == 0)
        {
            MessageBox.Show("少なくとも1つカテゴリー名を入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var preset = new CategoryPreset
        {
            Name       = name,
            Categories = validRows.Select(r => new CategoryPresetItem
            {
                Name        = r.Name.Trim(),
                Description = r.Description.Trim(),
                Color       = NormalizeHex(r.HexColor),
            }).ToList(),
        };

        _service.Add(preset);

        PanelCreate.Visibility = Visibility.Collapsed;
        PanelList.Visibility   = Visibility.Visible;
        RefreshList();
    }

    // ── ヘルパー ──────────────────────────────────────────

    private EditableCategoryRow NewRow()
    {
        var color = PRESET_COLORS[
            (CategoryRows.ItemsSource is ObservableCollection<EditableCategoryRow> r ? r.Count : 0)
            % PRESET_COLORS.Length];
        return new EditableCategoryRow { HexColor = color };
    }

    private static string NormalizeHex(string hex)
    {
        var s = hex.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        try
        {
            ColorConverter.ConvertFromString(s);
            return s.ToUpperInvariant();
        }
        catch { return "#3D7EFF"; }
    }
}
