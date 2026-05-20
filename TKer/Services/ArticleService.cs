using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// 記事（ドラフト）を %AppData%\TKer\articles.json で管理するサービス。
/// </summary>
public class ArticleService
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKer");
    private static readonly string DataFile =
        Path.Combine(DataDir, "articles.json");

    private static readonly string SettingsFile =
        Path.Combine(DataDir, "article_settings.json");

    private List<Article> _articles;
    private ArticleAppSettings _settings;

    public event EventHandler? DataChanged;

    public ArticleService()
    {
        _articles = Load();
        _settings = LoadSettings();
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
        Save();
        DataChanged?.Invoke(this, EventArgs.Empty);
        return a;
    }

    public void Save(Article article)
    {
        var idx = _articles.FindIndex(a => a.Id == article.Id);
        if (idx < 0)
        {
            _articles.Add(article);
        }
        else
        {
            article.UpdatedAt = DateTime.Now;
            _articles[idx] = article;
        }
        Save();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        _articles.RemoveAll(a => a.Id == id);
        Save();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── WordPress 設定 ────────────────────────────────────
    public ArticleAppSettings AppSettings => _settings;

    public void SaveWordPressConfig(WordPressConfig config)
    {
        _settings.WordPress = config;
        SaveSettings();
    }

    // ── 永続化 ───────────────────────────────────────────
    private List<Article> Load()
    {
        try
        {
            if (File.Exists(DataFile))
                return JsonConvert.DeserializeObject<List<Article>>(File.ReadAllText(DataFile)) ?? new();
        }
        catch { }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(DataFile, JsonConvert.SerializeObject(_articles, Formatting.Indented));
        }
        catch { }
    }

    private ArticleAppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonConvert.DeserializeObject<ArticleAppSettings>(File.ReadAllText(SettingsFile)) ?? new();
        }
        catch { }
        return new();
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(SettingsFile, JsonConvert.SerializeObject(_settings, Formatting.Indented));
        }
        catch { }
    }
}
