using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TKer.Helpers;
using TKer.Models;
using TKer.ViewModels;
using TKer.Views.Dialogs;

namespace TKer.Views.Pages;

public partial class CategoryPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;
    private string? _selectedId;

    public CategoryPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } win)
            {
                win.KeyDown -= Window_KeyDown;
                win.KeyDown += Window_KeyDown;
                _keyDownWindow = win;
            }
        };
        Unloaded += (_, _) =>
        {
            if (_keyDownWindow is { } win)
                win.KeyDown -= Window_KeyDown;
            _keyDownWindow = null;
        };
    }

    private Window? _keyDownWindow;

    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Cat_Header"));
        ApplyFilter();
        ApplyBackground();
    }

    private void ApplyBackground()
    {
        UiThemeHelper.ApplyColorBackground(ContentBorder,
            _vm.AppSettingsService.CategoryListBgColor,
            _vm.AppSettingsService.CategoryListBgOpacity);
    }

    private void ApplyFilter()
    {
        var cats = _vm.ProjectService.CurrentProject?.Categories;
        bool hasSelection = !string.IsNullOrEmpty(_selectedId);
        BtnOpenFolder.IsEnabled     = hasSelection;
        BtnEditCategory.IsEnabled   = hasSelection;
        BtnDeleteCategory.IsEnabled = hasSelection;

        if (cats == null) { CategoryItemsControl.ItemsSource = null; BtnMoveUp.IsEnabled = BtnMoveDown.IsEnabled = false; return; }

        var q = SearchBox?.Text?.Trim().ToLower() ?? "";
        var sorted = cats.OrderBy(c => c.Order).ToList();
        var filtered = string.IsNullOrEmpty(q)
            ? sorted
            : sorted.Where(c =>
                c.Name.ToLower().Contains(q) ||
                c.Description.ToLower().Contains(q) ||
                c.Id.ToLower().Contains(q)).ToList();

        CategoryItemsControl.ItemsSource = filtered.Select((c, idx) => new
        {
            Cat        = c,
            IsSelected = c.Id == _selectedId,
            OrderLabel = $"#{c.Order}",
            IsFirst    = idx == 0,
            IsLast     = idx == filtered.Count - 1
        }).ToList();

        // 上下ボタンの有効/無効
        if (hasSelection)
        {
            var selIdx = filtered.FindIndex(c => c.Id == _selectedId);
            BtnMoveUp.IsEnabled   = selIdx > 0;
            BtnMoveDown.IsEnabled = selIdx >= 0 && selIdx < filtered.Count - 1;
        }
        else
        {
            BtnMoveUp.IsEnabled   = false;
            BtnMoveDown.IsEnabled = false;
        }
    }

    // ── キーボードショートカット ───────────────────────────
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (!IsVisible) return;

        bool ctrl  = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        bool alt   = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        if (ctrl && !shift && !alt)
        {
            switch (e.Key)
            {
                case Key.O:
                    OpenFolder_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
                case Key.F:
                    if (SearchSection.Visibility != Visibility.Visible)
                        ToggleSearch_Click(this, new RoutedEventArgs());
                    else { SearchBox.Focus(); SearchBox.SelectAll(); }
                    e.Handled = true; break;
                case Key.OemMinus:
                case Key.Subtract:
                    DeleteCategory_Click(this, new RoutedEventArgs());
                    e.Handled = true; break;
            }
        }
        else if (ctrl && shift && !alt)
        {
            if (e.Key == Key.OemSemicolon)
            {
                AddCategory_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            if (e.Key == Key.Up)
            {
                MoveUp_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                MoveDown_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.F2)
            {
                EditCategory_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && SearchSection.Visibility == Visibility.Visible)
            {
                ToggleSearch_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }

    // ── 検索 ──────────────────────────────────────────────
    private void ToggleSearch_Click(object sender, RoutedEventArgs e)
    {
        if (SearchSection.Visibility == Visibility.Collapsed)
        {
            SearchBarHelper.Open(SearchSection, SearchBox);
        }
        else
        {
            SearchBarHelper.Close(SearchSection);
            SearchBox.Text = "";
            ApplyFilter();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    // ── カード選択 ────────────────────────────────────────
    private void CategoryCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (((Border)sender).DataContext is not { } ctx) return;
        dynamic data = ctx;
        string id = (string)data.Cat.Id;
        _selectedId = _selectedId == id ? null : id;
        ApplyFilter();
    }

    // ── ツールバー ────────────────────────────────────────
    public void TriggerAddDialog() => AddCategory_Click(this, new RoutedEventArgs());

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CategoryDialog(null) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            _vm.ProjectService.AddCategory(dlg.CategoryName, dlg.Description, dlg.Color);
            Refresh();
        }
    }

    private void EditCategory_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedId))
        {
            AppDialog.ShowInfo("編集するカテゴリーを選択してください", "確認", Window.GetWindow(this));
            return;
        }
        var cat = _vm.ProjectService.CurrentProject?.Categories.FirstOrDefault(c => c.Id == _selectedId);
        if (cat == null) return;

        var dlg = new CategoryDialog(cat) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        bool rename = dlg.RenameFolder && cat.FolderCreated;
        var updated = new Category
        {
            Id = cat.Id, Name = dlg.CategoryName,
            Description = dlg.Description, Color = dlg.Color,
            FolderPath = cat.FolderPath, FolderCreated = cat.FolderCreated
        };
        try
        {
            _vm.ProjectService.UpdateCategory(updated, renameFolder: rename);
            Refresh();
        }
        catch (Exception ex)
        {
            AppDialog.ShowError($"更新に失敗しました: {ex.Message}", "エラー", Window.GetWindow(this));
        }
    }

    private void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedId))
        {
            AppDialog.ShowInfo("削除するカテゴリーを選択してください", "確認", Window.GetWindow(this));
            return;
        }
        var cat = _vm.ProjectService.CurrentProject?.Categories.FirstOrDefault(c => c.Id == _selectedId);
        if (cat == null) return;

        if (!AppDialog.Confirm($"カテゴリー「{cat.Name}」を削除しますか？\n配下のタスクも削除されます。", "削除確認", Window.GetWindow(this))) return;
        bool deleteFolder = AppDialog.Confirm("フォルダも一緒に削除しますか？\n（「キャンセル」でデータのみ削除）", "フォルダ削除", Window.GetWindow(this));
        _vm.ProjectService.DeleteCategory(cat.Id, deleteFolder: deleteFolder);
        _selectedId = null;
        Refresh();
    }

    // ── 優先度変更（上下移動） ──────────────────────────────
    private void MoveUp_Click(object sender, RoutedEventArgs e)   => MoveCategoryBy(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveCategoryBy(+1);

    private void MoveCategoryBy(int delta)
    {
        if (string.IsNullOrEmpty(_selectedId)) return;
        var cats = _vm.ProjectService.CurrentProject?.Categories;
        if (cats == null) return;

        var sorted = cats.OrderBy(c => c.Order).ToList();
        var selIdx = sorted.FindIndex(c => c.Id == _selectedId);
        if (selIdx < 0) return;
        var newIdx = selIdx + delta;
        if (newIdx < 0 || newIdx >= sorted.Count) return;

        // Swap Order values
        (sorted[selIdx].Order, sorted[newIdx].Order) = (sorted[newIdx].Order, sorted[selIdx].Order);

        // Reorder via service (handles folder renames async)
        var newOrder = sorted.OrderBy(c => c.Order).Select(c => c.Id).ToList();
        _vm.ProjectService.ReorderCategories(newOrder);
        Refresh();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedId))
        {
            AppDialog.ShowInfo("カテゴリーを選択してください", "確認", Window.GetWindow(this));
            return;
        }
        var cat = _vm.ProjectService.CurrentProject?.Categories.FirstOrDefault(c => c.Id == _selectedId);
        if (cat == null) return;

        if (Directory.Exists(cat.FolderPath))
            ShellHelper.OpenInExplorer(cat.FolderPath);
        else
            AppDialog.ShowWarning("フォルダが見つかりません", "エラー", Window.GetWindow(this));
    }
}
