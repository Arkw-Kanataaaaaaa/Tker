using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>ユーザー定義カテゴリーテンプレートを %AppData%\TKer\category_templates.json で管理するサービス。</summary>
public class CategoryTemplateService
{
    private static readonly string DIR =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKer");
    private static readonly string FILE = Path.Combine(DIR, "category_templates.json");

    private List<CategoryPreset> _presets;

    public CategoryTemplateService() { _presets = Load(); }

    public IReadOnlyList<CategoryPreset> UserPresets => _presets;

    public void Add(CategoryPreset preset)
    {
        _presets.Add(preset);
        Persist();
    }

    public void Delete(string id)
    {
        _presets.RemoveAll(p => p.Id == id);
        Persist();
    }

    public void Update(CategoryPreset preset)
    {
        var idx = _presets.FindIndex(p => p.Id == preset.Id);
        if (idx >= 0) _presets[idx] = preset;
        else _presets.Add(preset);
        Persist();
    }

    private List<CategoryPreset> Load()
    {
        try
        {
            if (File.Exists(FILE))
            {
                var json = File.ReadAllText(FILE);
                return JsonConvert.DeserializeObject<List<CategoryPreset>>(json) ?? new();
            }
        }
        catch { }
        return new();
    }

    private void Persist()
    {
        Directory.CreateDirectory(DIR);
        File.WriteAllText(FILE, JsonConvert.SerializeObject(_presets, Formatting.Indented));
    }
}
