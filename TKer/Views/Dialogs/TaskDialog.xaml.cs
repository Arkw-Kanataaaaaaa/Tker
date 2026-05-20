using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TKer.Models;

namespace TKer.Views.Dialogs;

public partial class TaskDialog : Window
{
    public string CategoryId { get; private set; } = "";
    public string TaskName { get; private set; } = "";
    public string TaskNameShort { get; private set; } = "";
    public string SubCategory { get; private set; } = "";
    public string Environment { get; private set; } = "";
    public string Assignee { get; private set; } = "";
    public string Priority { get; private set; } = "中";
    public string Status { get; private set; } = "未着手";
    public DateTime? PlannedStart { get; private set; }
    public DateTime? PlannedEnd { get; private set; }
    public string Description { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public string Tags { get; private set; } = "";
    public bool DelayApproved { get; private set; } = false;

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == System.Windows.Input.Key.Escape) { DialogResult = false; e.Handled = true; }
    }

    public TaskDialog(TaskItem? existing, List<Category> categories, bool isPersonalMode = false)
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));
        CbCategory.ItemsSource = categories;
        if (isPersonalMode)
            CbStatusReview.Visibility = System.Windows.Visibility.Collapsed;

        if (existing != null)
        {
            Title = "タスク編集";
            CbCategory.SelectedValue = existing.CategoryId;
            TxtName.Text = existing.Name;
            TxtNameShort.Text = existing.NameShort;
            TxtSubCategory.Text = existing.SubCategory;
            TxtEnvironment.Text = existing.Environment;
            TxtAssignee.Text = existing.Assignee;
            SetComboByText(CbPriority, existing.Priority);
            SetComboByText(CbStatus, existing.Status);
            DpPlanStart.SelectedDate = existing.PlannedStartDate;
            DpPlanEnd.SelectedDate = existing.PlannedEndDate;
            TxtDescription.Text = existing.Description;
            TxtNotes.Text = existing.Notes;
        }
        else if (categories.Count > 0)
        {
            CbCategory.SelectedIndex = 0;
        }
    }

    private static void SetComboByText(System.Windows.Controls.ComboBox cb, string text)
    {
        foreach (System.Windows.Controls.ComboBoxItem item in cb.Items)
            if (item.Content as string == text) { cb.SelectedItem = item; return; }
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtName.Text))
        { MessageBox.Show("タスク名を入力してください"); return; }
        if (CbCategory.SelectedValue == null)
        { MessageBox.Show("カテゴリーを選択してください"); return; }

        CategoryId = CbCategory.SelectedValue as string ?? "";
        TaskName = TxtName.Text;
        TaskNameShort = string.IsNullOrWhiteSpace(TxtNameShort.Text)
            ? (TxtName.Text.Length > 20 ? TxtName.Text[..20] : TxtName.Text)
            : TxtNameShort.Text;
        SubCategory = TxtSubCategory.Text;
        Environment = TxtEnvironment.Text;
        Assignee = TxtAssignee.Text;
        Priority = ((CbPriority.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string) ?? "中";
        Status = ((CbStatus.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string) ?? "未着手";
        PlannedStart = DpPlanStart.SelectedDate;
        PlannedEnd = DpPlanEnd.SelectedDate;
        Description = TxtDescription.Text;
        Notes = TxtNotes.Text;
        Tags = TxtTags.Text;
        DelayApproved = ChkDelayApproved.IsChecked == true;
        DialogResult = true;
    }

    public void PresetCategory(string categoryId)
    {
        CbCategory.SelectedValue = categoryId;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
