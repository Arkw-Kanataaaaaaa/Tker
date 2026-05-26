using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
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

/// <summary>カテゴリー行の編集用データクラス。</summary>
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

/// <summary>カテゴリーテンプレートの一覧表示・参照・作成・編集を行うダイアログ。</summary>
public partial class CategoryTemplateManagerDialog : Window
{
    private readonly CategoryTemplateService _service;
    private string _searchText = "";
    private string? _editingPresetId;

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

    // ── 一覧 ──────────────────────────────────────────────

    private void RefreshList()
    {
        var items = BuildListItems();
        ApplySearch(items);
    }

    private List<TemplateListItem> BuildListItems()
    {
        var result = new List<TemplateListItem>();

        foreach (var kv in CategoryTemplateDialog.BuiltInTemplates)
        {
            result.Add(new TemplateListItem
            {
                Id        = $"__builtin_{kv.Key}",
                Name      = kv.Key,
                IsBuiltIn = true,
                ColorDots = kv.Value.Select(c => TryParseColor(c.Color)).ToList(),
            });
        }

        foreach (var p in _service.UserPresets)
        {
            result.Add(new TemplateListItem
            {
                Id        = p.Id,
                Name      = p.Name,
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

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text;
        ApplySearch(BuildListItems());
    }

    private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
    {
        // 行クリックイベントに伝播させない
        e.Handled = true;

        if (sender is not FrameworkElement fe || fe.Tag is not string id) return;
        var name = (fe.DataContext as TemplateListItem)?.Name ?? "";

        if (!AppDialog.Confirm("このカスタムテンプレートを削除しますか？",
                               "カテゴリーテンプレートの削除", this,
                               confirmLabel: "削除", dangerConfirm: true,
                               heading: $"「{name}」 削除の確認")) return;

        _service.Delete(id);
        RefreshList();
    }

    private void TemplateRow_Click(object sender, MouseButtonEventArgs e)
    {
        // ボタン上のクリックは無視（削除ボタンなど）
        var src = e.OriginalSource as DependencyObject;
        while (src != null)
        {
            if (src is System.Windows.Controls.Button) return;
            src = VisualTreeHelper.GetParent(src);
        }

        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not TemplateListItem item) return;

        ShowDetailPanel(item);
    }

    // ── 詳細/編集パネル ──────────────────────────────────

    private void ShowDetailPanel(TemplateListItem item)
    {
        _editingPresetId = item.IsBuiltIn ? null : item.Id;

        if (item.IsBuiltIn)
        {
            // 参照モード
            DetailTitle.Text          = "テンプレート参照";
            DetailNameView.Text       = item.Name;
            DetailNameView.Visibility = Visibility.Visible;
            DetailNameEdit.Visibility = Visibility.Collapsed;

            var rows = GetBuiltInRows(item.Name);
            DetailViewRows.ItemsSource = rows;
            DetailViewRows.Visibility  = Visibility.Visible;
            DetailEditRows.Visibility  = Visibility.Collapsed;

            DetailAddRowBtn.Visibility = Visibility.Collapsed;
            DetailSaveBtn.Visibility   = Visibility.Collapsed;
            DetailCancelBtn.Content    = "閉じる";
        }
        else
        {
            // 編集モード
            DetailTitle.Text          = "テンプレート編集";
            DetailNameEdit.Text       = item.Name;
            DetailNameEdit.Visibility = Visibility.Visible;
            DetailNameView.Visibility = Visibility.Collapsed;

            var preset = _service.UserPresets.FirstOrDefault(p => p.Id == item.Id);
            var rows   = new ObservableCollection<EditableCategoryRow>(
                preset?.Categories.Select(c => new EditableCategoryRow
                {
                    Name        = c.Name,
                    Description = c.Description,
                    HexColor    = c.Color,
                }) ?? Enumerable.Empty<EditableCategoryRow>());

            DetailEditRows.ItemsSource = rows;
            DetailEditRows.Visibility  = Visibility.Visible;
            DetailViewRows.Visibility  = Visibility.Collapsed;

            DetailAddRowBtn.Visibility = Visibility.Visible;
            DetailSaveBtn.Visibility   = Visibility.Visible;
            DetailCancelBtn.Content    = "キャンセル";
        }

        PanelList.Visibility   = Visibility.Collapsed;
        PanelDetail.Visibility = Visibility.Visible;
    }

    private List<EditableCategoryRow> GetBuiltInRows(string templateName)
    {
        if (!CategoryTemplateDialog.BuiltInTemplates.TryGetValue(templateName, out var items))
            return new();
        return items.Select(c => new EditableCategoryRow
        {
            Name        = c.Name,
            Description = c.Description,
            HexColor    = c.Color,
        }).ToList();
    }

    private void AddDetailRow_Click(object sender, RoutedEventArgs e)
    {
        if (DetailEditRows.ItemsSource is ObservableCollection<EditableCategoryRow> rows)
            rows.Add(NewDetailRow(rows.Count));
    }

    private void RemoveDetailRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.Tag is not EditableCategoryRow row) return;
        if (DetailEditRows.ItemsSource is ObservableCollection<EditableCategoryRow> rows)
            rows.Remove(row);
    }

    private void SaveDetailTemplate_Click(object sender, RoutedEventArgs e)
    {
        var name = DetailNameEdit.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("テンプレート名を入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            DetailNameEdit.Focus();
            return;
        }

        var rows = (DetailEditRows.ItemsSource as ObservableCollection<EditableCategoryRow>)?.ToList()
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
            Id         = _editingPresetId ?? Guid.NewGuid().ToString("N")[..8],
            Name       = name,
            Categories = validRows.Select(r => new CategoryPresetItem
            {
                Name        = r.Name.Trim(),
                Description = r.Description.Trim(),
                Color       = NormalizeHex(r.HexColor),
            }).ToList(),
        };

        _service.Update(preset);

        PanelDetail.Visibility = Visibility.Collapsed;
        PanelList.Visibility   = Visibility.Visible;
        RefreshList();
    }

    // ── 作成パネル ────────────────────────────────────────

    private void ShowCreatePanel_Click(object sender, RoutedEventArgs e)
    {
        TxtTemplateName.Text = "";

        var rows = new ObservableCollection<EditableCategoryRow> { NewRow() };
        CategoryRows.ItemsSource = rows;

        PanelList.Visibility   = Visibility.Collapsed;
        PanelCreate.Visibility = Visibility.Visible;
    }

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

    // ── 共通 ─────────────────────────────────────────────

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        PanelCreate.Visibility = Visibility.Collapsed;
        PanelDetail.Visibility = Visibility.Collapsed;
        PanelList.Visibility   = Visibility.Visible;
        RefreshList();
    }

    private EditableCategoryRow NewRow()
    {
        var count = (CategoryRows.ItemsSource as ObservableCollection<EditableCategoryRow>)?.Count ?? 0;
        return new EditableCategoryRow { HexColor = PRESET_COLORS[count % PRESET_COLORS.Length] };
    }

    private EditableCategoryRow NewDetailRow(int index)
        => new() { HexColor = PRESET_COLORS[index % PRESET_COLORS.Length] };

    private static Color TryParseColor(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
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
