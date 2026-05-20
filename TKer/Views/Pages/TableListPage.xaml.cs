using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClosedXML.Excel;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;

namespace TKer.Views.Pages;

public partial class TableListPage : Page, IRefreshable
{
    private readonly MainViewModel _vm;
    private CustomTable? _currentTable;
    private DataTable? _displayTable;
    private string _searchText = string.Empty;

    public TableListPage(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
    }

    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Table_Header"));
        if (!_vm.IsProjectLoaded)
        {
            ShowNoTable();
            return;
        }
        LoadTableSelector();
    }

    private void LoadTableSelector()
    {
        var tables = _vm.ProjectService.CurrentProject?.CustomTables ?? new List<CustomTable>();
        TableSelector.ItemsSource = tables;

        if (_currentTable != null)
        {
            var match = tables.FirstOrDefault(t => t.Id == _currentTable.Id);
            if (match != null) { TableSelector.SelectedItem = match; return; }
        }

        if (tables.Count > 0)
            TableSelector.SelectedIndex = 0;
        else
            ShowNoTable();
    }

    private void TableSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        _currentTable = TableSelector.SelectedItem as CustomTable;
        BuildGrid();
    }

    // ──────────────────────────────────────────────────
    // DataGrid 構築
    // ──────────────────────────────────────────────────
    private void BuildGrid()
    {
        if (_currentTable == null) { ShowNoTable(); return; }

        NoTableBanner.Visibility = Visibility.Collapsed;
        TableGrid.Visibility     = Visibility.Visible;

        // DataTable を再構築
        _displayTable = new DataTable();
        // 行IDは隠し列として保持（Tagで）
        _displayTable.Columns.Add("__RowId", typeof(string));

        foreach (var col in _currentTable.Columns.OrderBy(c => c.Order))
            _displayTable.Columns.Add(col.Id, typeof(string));

        // DataGrid 列を再構築
        TableGrid.Columns.Clear();
        foreach (var col in _currentTable.Columns.OrderBy(c => c.Order))
        {
            bool readOnly = col.Type == "autonumber";
            var dgCol = new DataGridTextColumn
            {
                Header   = col.Name,
                Binding  = new System.Windows.Data.Binding($"[{col.Id}]"),
                IsReadOnly = readOnly,
                Width    = col.Type == "autonumber"
                           ? new DataGridLength(60)
                           : new DataGridLength(1, DataGridLengthUnitType.Star),
                EditingElementStyle = readOnly ? null : (Style?)TryFindResource("DarkDataGridEditStyle")
            };
            TableGrid.Columns.Add(dgCol);
        }

        PopulateRows();
    }

    private void PopulateRows()
    {
        if (_currentTable == null || _displayTable == null) return;

        _displayTable.Rows.Clear();
        var filter = _searchText.ToLower();
        var colOrder = _currentTable.Columns.OrderBy(c => c.Order).ToList();

        foreach (var row in _currentTable.Rows)
        {
            // 検索フィルター
            if (!string.IsNullOrEmpty(filter))
            {
                bool hit = colOrder.Any(col =>
                    row.Cells.TryGetValue(col.Id, out var v) &&
                    (v ?? "").ToLower().Contains(filter));
                if (!hit) continue;
            }

            var dr = _displayTable.NewRow();
            dr["__RowId"] = row.Id;
            foreach (var col in colOrder)
                dr[col.Id] = row.Cells.TryGetValue(col.Id, out var val) ? val : "";
            _displayTable.Rows.Add(dr);
        }

        TableGrid.ItemsSource = _displayTable.DefaultView;
    }

    private void ShowNoTable()
    {
        NoTableBanner.Visibility = Visibility.Visible;
        TableGrid.Visibility     = Visibility.Collapsed;
        _currentTable = null;
        TableSelector.ItemsSource = null;
    }

    // ──────────────────────────────────────────────────
    // 新規テーブル
    // ──────────────────────────────────────────────────
    private void NewTable_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsProjectLoaded) { MessageBox.Show("プロジェクトを選択してください"); return; }

        var dlg = new TKer.Views.Dialogs.TableColumnDialog
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() != true) return;

        var table = _vm.ProjectService.CreateTable(dlg.TableName);
        table.Columns = dlg.Columns;
        _vm.ProjectService.SaveTableDefinition(table);

        _currentTable = table;
        LoadTableSelector();
    }

    // ──────────────────────────────────────────────────
    // テーブル列編集
    // ──────────────────────────────────────────────────
    private void EditTable_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTable == null) { MessageBox.Show("テーブルを選択してください"); return; }

        var dlg = new TKer.Views.Dialogs.TableColumnDialog(_currentTable)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() != true) return;

        _currentTable.Name = dlg.TableName;

        // 既存IDを保持しつつ列定義をマージ
        var oldCols = _currentTable.Columns.ToDictionary(c => c.Id);
        var newCols = new List<TableColumn>();
        foreach (var nc in dlg.Columns)
        {
            // 同名・同型の既存列があればIDを継承
            var existing = oldCols.Values.FirstOrDefault(oc =>
                oc.Name == nc.Name && oc.Type == nc.Type);
            if (existing != null)
            {
                existing.Order = nc.Order;
                newCols.Add(existing);
                oldCols.Remove(existing.Id);
            }
            else
            {
                newCols.Add(nc);
            }
        }
        _currentTable.Columns = newCols;
        _vm.ProjectService.SaveTableDefinition(_currentTable);
        BuildGrid();
    }

    // ──────────────────────────────────────────────────
    // テーブル削除
    // ──────────────────────────────────────────────────
    private void DeleteTable_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTable == null) { MessageBox.Show("テーブルを選択してください"); return; }

        var res = MessageBox.Show($"テーブル「{_currentTable.Name}」を削除しますか？\nデータはすべて失われます。",
            "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        _vm.ProjectService.DeleteTable(_currentTable.Id);
        _currentTable = null;
        LoadTableSelector();
    }

    // ──────────────────────────────────────────────────
    // 行操作
    // ──────────────────────────────────────────────────
    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTable == null) { MessageBox.Show("テーブルを選択してください"); return; }
        _vm.ProjectService.AddRow(_currentTable);
        PopulateRows();
        // 最終行にスクロール
        if (TableGrid.Items.Count > 0)
            TableGrid.ScrollIntoView(TableGrid.Items[TableGrid.Items.Count - 1]);
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTable == null) return;
        if (TableGrid.SelectedItems.Count == 0) { MessageBox.Show("削除する行を選択してください"); return; }

        var res = MessageBox.Show($"{TableGrid.SelectedItems.Count}行を削除しますか？",
            "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        var ids = new List<string>();
        foreach (DataRowView drv in TableGrid.SelectedItems)
            ids.Add(drv["__RowId"].ToString() ?? "");

        _vm.ProjectService.DeleteRows(_currentTable, ids);
        PopulateRows();
    }

    // ──────────────────────────────────────────────────
    // セル編集終了
    // ──────────────────────────────────────────────────
    private void TableGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (_currentTable == null || _displayTable == null) return;
        if (e.Row.Item is not DataRowView drv) return;

        var colId  = (string)TableGrid.Columns[e.Column.DisplayIndex].Header == null
                     ? "" : "";
        // DataGridTextColumn binding key = column.Id
        var dgCol  = TableGrid.Columns[e.Column.DisplayIndex] as DataGridTextColumn;
        if (dgCol?.Binding is not System.Windows.Data.Binding binding) return;
        var boundPath = binding.Path.Path; // "[colId]"
        var columnId  = boundPath.TrimStart('[').TrimEnd(']');

        var rowId = drv["__RowId"].ToString() ?? "";
        var element = e.EditingElement as TextBox;
        var value   = element?.Text ?? "";

        _vm.ProjectService.UpdateCell(_currentTable, rowId, columnId, value);
    }

    // ──────────────────────────────────────────────────
    // 検索
    // ──────────────────────────────────────────────────
    private void ToggleSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBar.Visibility = SearchBar.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        if (SearchBar.Visibility == Visibility.Visible)
            SearchBox.Focus();
        else
        {
            _searchText = "";
            SearchBox.Text = "";
            PopulateRows();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text;
        PopulateRows();
    }

    // ──────────────────────────────────────────────────
    // Excel出力
    // ──────────────────────────────────────────────────
    private void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTable == null) { MessageBox.Show("テーブルを選択してください"); return; }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title  = "Excelとして保存",
            Filter = "Excel ファイル (*.xlsx)|*.xlsx",
            FileName = $"{_currentTable.Name}_{DateTime.Now:yyyyMMdd}"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(_currentTable.Name.Length > 31
                ? _currentTable.Name[..31] : _currentTable.Name);

            var cols = _currentTable.Columns.OrderBy(c => c.Order).ToList();

            // ヘッダー
            for (int ci = 0; ci < cols.Count; ci++)
                ws.Cell(1, ci + 1).Value = cols[ci].Name;
            var headerRow = ws.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#2D2D2D");
            headerRow.Style.Font.FontColor = XLColor.White;

            // データ
            for (int ri = 0; ri < _currentTable.Rows.Count; ri++)
            {
                var row = _currentTable.Rows[ri];
                for (int ci = 0; ci < cols.Count; ci++)
                {
                    var val = row.Cells.TryGetValue(cols[ci].Id, out var v) ? v : "";
                    if (cols[ci].Type == "number" && double.TryParse(val, out var num))
                        ws.Cell(ri + 2, ci + 1).Value = num;
                    else
                        ws.Cell(ri + 2, ci + 1).Value = val;
                }
            }

            ws.Columns().AdjustToContents();
            wb.SaveAs(dlg.FileName);
            MessageBox.Show("Excelファイルを保存しました", "完了",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
