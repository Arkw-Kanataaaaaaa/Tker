using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Markdig;
using Newtonsoft.Json;
using TKer.Helpers;
using TKer.Models;
using TKer.Services;
using TKer.ViewModels;

namespace TKer.Views.Pages;

/// <summary>記事の一覧・編集・プレビューおよびWordPress投稿を行うページ。</summary>
public partial class ArticlePage : Page, IRefreshable
{
    private readonly MainViewModel   _vm;
    private readonly ArticleService  _svc;

    private Article?  _current;
    private bool      _loading = false;   // UI リフレッシュ中は変更通知を抑制

    private static readonly MarkdownPipeline PIPELINE =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>記事ページを初期化してサービスを設定する。</summary>
    public ArticlePage(MainViewModel vm)
    {
        _vm  = vm;
        _svc = vm.ArticleService;
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    /// <summary>テーマを適用して記事一覧とWordPress設定を再読み込みする。</summary>
    public void Refresh()
    {
        UiThemeHelper.ApplySectionTheme(PageHeader, _vm.AppSettingsService.GetSectionTheme("Article_Header"));
        RefreshList();
        LoadWpSettings();
    }

    // ── 記事一覧 ──────────────────────────────────────────
    /// <summary>検索キーワードで絞り込んだ記事一覧をリストに表示する。</summary>
    private void RefreshList(string? keepId = null)
    {
        var q = SearchBox.Text.Trim().ToLower();
        var articles = _svc.GetAll()
            .Where(a => string.IsNullOrEmpty(q) ||
                        a.Title.ToLower().Contains(q) ||
                        a.Content.ToLower().Contains(q))
            .ToList();
        ArticleList.ItemsSource = articles;

        if (keepId != null)
            ArticleList.SelectedItem = articles.FirstOrDefault(a => a.Id == keepId);
        else if (_current != null)
            ArticleList.SelectedItem = articles.FirstOrDefault(a => a.Id == _current.Id);
    }

    private void ArticleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArticleList.SelectedItem is Article a)
            LoadArticle(a);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList(_current?.Id);

    // ── 記事ロード ────────────────────────────────────────
    /// <summary>指定した記事の内容をエディターフォームに読み込む。</summary>
    private void LoadArticle(Article a)
    {
        _loading = true;
        _current = a;

        TxtTitle.Text   = a.Title;
        TxtContent.Text = a.Content;
        TxtCategory.Text = a.Category;
        TxtTags.Text     = string.Join(", ", a.Tags);

        // プラットフォーム
        foreach (ComboBoxItem item in CmbPlatform.Items)
            if (item.Tag?.ToString() == a.Platform) { CmbPlatform.SelectedItem = item; break; }

        // ステータス
        foreach (ComboBoxItem item in CmbStatus.Items)
            if (item.Tag?.ToString() == a.Status) { CmbStatus.SelectedItem = item; break; }

        UpdateWordCount();
        TxtStatus.Text = $"更新: {a.UpdatedAt:yyyy/MM/dd HH:mm}";
        _loading = false;
    }

    // ── 変更ハンドラ ──────────────────────────────────────
    private void TxtTitle_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        _current.Title = TxtTitle.Text;
    }

    private void TxtContent_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        _current.Content = TxtContent.Text;
        UpdateWordCount();
    }

    private void Meta_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        _current.Category = TxtCategory.Text;
        _current.Tags = TxtTags.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private void CmbPlatform_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _current == null) return;
        _current.Platform = (CmbPlatform.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "note";
    }

    /// <summary>本文の文字数と行数を文字カウントラベルに反映する。</summary>
    private void UpdateWordCount()
    {
        var text = TxtContent.Text ?? "";
        TxtWordCount.Text = $"{text.Length} 文字 / {CountLines(text)} 行";
    }

    /// <summary>文字列の改行数から行数を返す。</summary>
    private static int CountLines(string s) => string.IsNullOrEmpty(s) ? 0 : s.Split('\n').Length;

    // ── タブ切替：プレビュー ───────────────────────────────
    /// <summary>タブ切替時にプレビュータブが選択されたらHTMLを描画する。</summary>
    private void EditorTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EditorTabs.SelectedIndex != 1) return;
        RenderPreview();
    }

    /// <summary>Markdownをhtmlに変換してプレビューブラウザに表示する。</summary>
    private void RenderPreview()
    {
        var md  = TxtContent.Text ?? "";
        var html = Markdown.ToHtml(md, PIPELINE);
        var full = $$"""
            <!DOCTYPE html><html><head>
            <meta charset="utf-8"/>
            <style>
            body { font-family: 'Yu Gothic UI', sans-serif; font-size:14px; color:#ddd; background:#1e1e2e; padding:20px; line-height:1.7; }
            h1,h2,h3 { color:#7ecfff; border-bottom:1px solid #444; padding-bottom:4px; }
            code { background:#2a2a3e; padding:2px 6px; border-radius:3px; font-family:Consolas; }
            pre  { background:#2a2a3e; padding:12px; border-radius:6px; overflow:auto; }
            blockquote { border-left:4px solid #3d7eff; margin:0; padding:0 16px; color:#aaa; }
            a { color:#7ecfff; }
            </style></head><body>{{html}}</body></html>
            """;
        PreviewBrowser.NavigateToString(full);
    }

    // ── ツールバー操作 ────────────────────────────────────
    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var a = _svc.Create();
        RefreshList(a.Id);
        LoadArticle(a);
        TxtTitle.Focus();
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e) => SaveCurrent();

    /// <summary>現在の記事のステータスを更新してサービスに保存する。</summary>
    private void SaveCurrent()
    {
        if (_current == null) return;
        _current.Status = (CmbStatus.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "draft";
        _svc.Save(_current);
        TxtStatus.Text = $"保存済み: {DateTime.Now:HH:mm:ss}";
        RefreshList(_current.Id);
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (MessageBox.Show($"「{_current.Title}」を削除しますか？", "削除確認",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _svc.Delete(_current.Id);
        _current = null;
        TxtTitle.Text   = "";
        TxtContent.Text = "";
        RefreshList();
    }

    // ── note 出力 ─────────────────────────────────────────
    private void BtnCopyNote_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        SaveCurrent();
        Clipboard.SetText(_current.Content);
        MessageBox.Show("クリップボードにコピーしました。\nnoteのエディターに貼り付けてください。",
            "コピー完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSaveText_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        SaveCurrent();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = SanitizeFilename(_current.Title) + ".md",
            Filter = "Markdown (*.md)|*.md|テキスト (*.txt)|*.txt|すべて (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, _current.Content, Encoding.UTF8);
            MessageBox.Show("保存しました", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ── WordPress ─────────────────────────────────────────
    /// <summary>保存済みWordPress設定をフォームに読み込む。</summary>
    private void LoadWpSettings()
    {
        var cfg = _svc.AppSettings.WordPress;
        TxtWpUrl.Text  = cfg.SiteUrl;
        TxtWpUser.Text = cfg.Username;
        PwWpPass.Password = cfg.AppPassword;
    }

    private void BtnSaveWp_Click(object sender, RoutedEventArgs e)
    {
        _svc.SaveWordPressConfig(new WordPressConfig
        {
            SiteUrl     = TxtWpUrl.Text.TrimEnd('/'),
            Username    = TxtWpUser.Text,
            AppPassword = PwWpPass.Password
        });
        MessageBox.Show("WordPress設定を保存しました", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnPostWp_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) { MessageBox.Show("記事を選択してください"); return; }
        SaveCurrent();

        var cfg = _svc.AppSettings.WordPress;
        if (string.IsNullOrEmpty(cfg.SiteUrl) || string.IsNullOrEmpty(cfg.Username))
        {
            MessageBox.Show("WordPress設定を入力してください", "設定不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            BtnPostWp.IsEnabled = false;
            using var client = new HttpClient();
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cfg.Username}:{cfg.AppPassword}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var html = Markdown.ToHtml(_current.Content ?? "", PIPELINE);
            var payload = JsonConvert.SerializeObject(new
            {
                title   = _current.Title,
                content = html,
                status  = _current.Status == "published" ? "publish" : "draft"
            });

            var response = await client.PostAsync(
                $"{cfg.SiteUrl}/wp-json/wp/v2/posts",
                new StringContent(payload, Encoding.UTF8, "application/json"));

            if (response.IsSuccessStatusCode)
                MessageBox.Show("WordPress に投稿しました！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                MessageBox.Show($"投稿に失敗しました: {response.StatusCode}\n{body}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"接続エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnPostWp.IsEnabled = true;
        }
    }

    /// <summary>ファイル名に使えない文字をアンダースコアに置換する。</summary>
    private static string SanitizeFilename(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "article" : name;
    }
}
