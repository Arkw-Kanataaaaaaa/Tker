using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace TKer.Views.Dialogs;

public class TemplateCategoryItem : INotifyPropertyChanged
{
    private bool _isSelected = true;
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3D7EFF";
    public string Description { get; set; } = "";

    public Color ColorBrushColor =>
        (Color)ColorConverter.ConvertFromString(Color);

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class CategoryTemplateDialog : Window
{
    public List<TemplateCategoryItem> SelectedCategories { get; private set; } = new();

    private static readonly Dictionary<string, List<TemplateCategoryItem>> Templates = new()
    {
        ["ソフトウェア開発"] = new()
        {
            new() { Name = "要件定義",   Color = "#5C6BC0", Description = "要件収集・分析・仕様策定" },
            new() { Name = "設計",       Color = "#26A69A", Description = "アーキテクチャ・詳細設計" },
            new() { Name = "実装",       Color = "#2383E2", Description = "コーディング・単体テスト" },
            new() { Name = "テスト",     Color = "#F57C00", Description = "結合テスト・システムテスト" },
            new() { Name = "デプロイ",   Color = "#388E3C", Description = "リリース・本番環境反映" },
            new() { Name = "運用・保守", Color = "#6D4C41", Description = "監視・障害対応・改善" },
        },
        ["Webプロジェクト"] = new()
        {
            new() { Name = "企画・要件", Color = "#7B1FA2", Description = "コンセプト・要件定義" },
            new() { Name = "UIデザイン", Color = "#E91E63", Description = "ワイヤーフレーム・デザイン" },
            new() { Name = "フロントエンド", Color = "#1565C0", Description = "HTML/CSS/JS実装" },
            new() { Name = "バックエンド",   Color = "#2E7D32", Description = "API・DB設計・実装" },
            new() { Name = "インフラ",   Color = "#4527A0", Description = "サーバー・CI/CD構築" },
            new() { Name = "SEO・分析", Color = "#E65100", Description = "SEO対策・GA設定" },
        },
        ["一般プロジェクト"] = new()
        {
            new() { Name = "計画",   Color = "#1976D2", Description = "プロジェクト計画策定" },
            new() { Name = "準備",   Color = "#00796B", Description = "リソース・環境準備" },
            new() { Name = "実行",   Color = "#F57C00", Description = "メインタスク実行" },
            new() { Name = "確認",   Color = "#C62828", Description = "品質確認・レビュー" },
            new() { Name = "完了",   Color = "#388E3C", Description = "成果物納品・クローズ" },
        },
        ["ゲーム開発"] = new()
        {
            new() { Name = "企画",      Color = "#6A1B9A", Description = "ゲームデザイン・仕様" },
            new() { Name = "グラフィック", Color = "#AD1457", Description = "キャラ・背景・UI素材" },
            new() { Name = "サウンド",  Color = "#0277BD", Description = "BGM・SE制作" },
            new() { Name = "プログラム", Color = "#2E7D32", Description = "ゲームロジック実装" },
            new() { Name = "デバッグ",  Color = "#E65100", Description = "バグ修正・バランス調整" },
            new() { Name = "リリース",  Color = "#37474F", Description = "ストア申請・公開" },
        },
        ["データ分析"] = new()
        {
            new() { Name = "データ収集",  Color = "#1565C0", Description = "データソース取得・整備" },
            new() { Name = "前処理",      Color = "#00695C", Description = "クレンジング・変換" },
            new() { Name = "探索的分析",  Color = "#6A1B9A", Description = "EDA・可視化" },
            new() { Name = "モデル構築",  Color = "#E65100", Description = "機械学習・統計モデル" },
            new() { Name = "評価・検証",  Color = "#C62828", Description = "精度評価・チューニング" },
            new() { Name = "レポート",    Color = "#33691E", Description = "結果まとめ・報告" },
        },
    };

    /// <param name="customPresets">AppSettingsから渡すカスタムプリセット（null可）</param>
    public CategoryTemplateDialog(IEnumerable<TKer.Models.CategoryPreset>? customPresets = null)
    {
        InitializeComponent();
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            SystemCommands.CloseWindowCommand, (_, _) => { DialogResult = false; }));

        // 組み込みテンプレート + カスタムプリセットを統合
        var allKeys = Templates.Keys.ToList();
        if (customPresets != null)
        {
            foreach (var p in customPresets)
            {
                if (!Templates.ContainsKey($"[カスタム] {p.Name}"))
                {
                    Templates[$"[カスタム] {p.Name}"] = p.Categories.Select(c =>
                        new TemplateCategoryItem { Name = c.Name, Color = c.Color, Description = c.Description }).ToList();
                }
            }
        }

        CmbTemplate.ItemsSource  = Templates.Keys.ToList();
        CmbTemplate.SelectedIndex = 0;
    }

    private void CmbTemplate_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CmbTemplate.SelectedItem is string key && Templates.TryGetValue(key, out var items))
        {
            var copies = items.Select(i => new TemplateCategoryItem
            {
                Name = i.Name, Color = i.Color, Description = i.Description, IsSelected = true
            }).ToList();
            CategoryItems.ItemsSource = copies;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in GetItems()) item.IsSelected = true;
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in GetItems()) item.IsSelected = false;
    }

    private IEnumerable<TemplateCategoryItem> GetItems()
        => CategoryItems.ItemsSource as IEnumerable<TemplateCategoryItem>
           ?? Enumerable.Empty<TemplateCategoryItem>();

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        SelectedCategories = GetItems().Where(i => i.IsSelected).ToList();
        if (SelectedCategories.Count == 0)
        { MessageBox.Show("少なくとも1つ選択してください"); return; }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
