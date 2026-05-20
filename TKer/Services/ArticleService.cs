using System;
using System.Collections.Generic;
using System.Linq;
using TKer.Helpers;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// 記事（ドラフト）を %AppData%\TKer\articles.json で管理するサービス。
/// </summary>
public class ArticleService
{
    private const string ARTICLES_FILE = "articles.json";
    private const string SETTINGS_FILE = "article_settings.json";

    private List<Article> _articles;
    private ArticleAppSettings _settings;

    public event EventHandler? DataChanged;

    /// <summary>JSON ファイルから記事データと設定を読み込んで初期化する。</summary>
    public ArticleService()
    {
        _articles = JsonFileStore.Load<List<Article>>(ARTICLES_FILE);
        _settings = JsonFileStore.Load<ArticleAppSettings>(SETTINGS_FILE);
    }

    // ── 記事 CRUD ─────────────────────────────────────────
    /// <summary>全記事を更新日降順で返す。</summary>
    public IReadOnlyList<Article> GetAll()
        => _articles.OrderByDescending(a => a.UpdatedAt).ToList().AsReadOnly();

    /// <summary>指定 ID の記事を返す。見つからない場合は null。</summary>
    public Article? GetById(string id) => _articles.FirstOrDefault(a => a.Id == id);

    /// <summary>新しい記事を作成して保存する。</summary>
    public Article Create(string title = "新しい記事")
    {
        var a = new Article
        {
            Title     = title,
            Content   = "",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };
        _articles.Add(a);
        SaveAndNotify();
        return a;
    }

    /// <summary>記事を保存する。存在しない場合は新規追加する。</summary>
    public void Save(Article article)
    {
        var idx = _articles.FindIndex(a => a.Id == article.Id);
        if (idx < 0)
            _articles.Add(article);
        else
        {
            article.UpdatedAt = DateTime.Now;
            _articles[idx] = article;
        }
        SaveAndNotify();
    }

    /// <summary>指定 ID の記事を削除して保存する。</summary>
    public void Delete(string id)
    {
        _articles.RemoveAll(a => a.Id == id);
        SaveAndNotify();
    }

    // ── WordPress 設定 ────────────────────────────────────
    /// <summary>現在のアプリ設定を返す。</summary>
    public ArticleAppSettings AppSettings => _settings;

    /// <summary>WordPress 接続設定を保存する。</summary>
    public void SaveWordPressConfig(WordPressConfig config)
    {
        _settings.WordPress = config;
        JsonFileStore.Save(SETTINGS_FILE, _settings);
    }

    // ── 永続化 ───────────────────────────────────────────
    /// <summary>記事データを JSON ファイルに保存して変更イベントを発火する。</summary>
    private void SaveAndNotify()
    {
        JsonFileStore.Save(ARTICLES_FILE, _articles);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
