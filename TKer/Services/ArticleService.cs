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
    private const string ArticlesFile = "articles.json";
    private const string SettingsFile = "article_settings.json";

    private List<Article> _articles;
    private ArticleAppSettings _settings;

    public event EventHandler? DataChanged;

    public ArticleService()
    {
        _articles = JsonFileStore.Load<List<Article>>(ArticlesFile);
        _settings = JsonFileStore.Load<ArticleAppSettings>(SettingsFile);
    }

    // ── 記事 CRUD ─────────────────────────────────────────
    public IReadOnlyList<Article> GetAll()
        => _articles.OrderByDescending(a => a.UpdatedAt).ToList().AsReadOnly();

    public Article? GetById(string id) => _articles.FirstOrDefault(a => a.Id == id);

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

    public void Delete(string id)
    {
        _articles.RemoveAll(a => a.Id == id);
        SaveAndNotify();
    }

    // ── WordPress 設定 ────────────────────────────────────
    public ArticleAppSettings AppSettings => _settings;

    public void SaveWordPressConfig(WordPressConfig config)
    {
        _settings.WordPress = config;
        JsonFileStore.Save(SettingsFile, _settings);
    }

    // ── 永続化 ───────────────────────────────────────────
    private void SaveAndNotify()
    {
        JsonFileStore.Save(ArticlesFile, _articles);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
