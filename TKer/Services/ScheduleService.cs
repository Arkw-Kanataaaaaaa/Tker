using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// スケジュールイベントを %AppData%\TKer\schedule.json で管理するサービス。
/// </summary>
public class ScheduleService
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TKer");
    private static readonly string DataFile =
        Path.Combine(DataDir, "schedule.json");

    private List<ScheduleEvent> _events;

    public event EventHandler? DataChanged;

    public ScheduleService()
    {
        _events = Load();
    }

    // ── 読み取り ─────────────────────────────────────────
    public IReadOnlyList<ScheduleEvent> GetAll() => _events.AsReadOnly();

    public IReadOnlyList<ScheduleEvent> GetByMonth(int year, int month)
        => _events
            .Where(e => (e.StartTime.Year == year && e.StartTime.Month == month) ||
                        (e.EndTime.Year   == year && e.EndTime.Month   == month) ||
                        (e.StartTime      <= new DateTime(year, month, 1) &&
                         e.EndTime        >= new DateTime(year, month, DateTime.DaysInMonth(year, month))))
            .OrderBy(e => e.StartTime)
            .ToList();

    public IReadOnlyList<ScheduleEvent> GetByDate(DateTime date)
    {
        var d = date.Date;
        return _events
            .Where(e => e.IsAllDay
                ? e.StartTime.Date <= d && e.EndTime.Date >= d
                : e.StartTime.Date <= d && e.EndTime.Date >= d)
            .OrderBy(e => e.StartTime)
            .ToList();
    }

    // ── 追加 / 更新 / 削除 ───────────────────────────────
    public ScheduleEvent Add(string title, string description, string location,
        DateTime start, DateTime end, bool isAllDay, string color = "#3D7EFF", string? linkedTaskId = null)
    {
        var ev = new ScheduleEvent
        {
            Title       = title,
            Description = description,
            Location    = location,
            StartTime   = isAllDay ? start.Date : start,
            EndTime     = isAllDay ? end.Date.AddDays(1).AddSeconds(-1) : end,
            IsAllDay    = isAllDay,
            Color       = color,
            LinkedTaskId= linkedTaskId,
            CreatedAt   = DateTime.Now
        };
        _events.Add(ev);
        Save();
        DataChanged?.Invoke(this, EventArgs.Empty);
        return ev;
    }

    public void Update(ScheduleEvent updated)
    {
        var idx = _events.FindIndex(e => e.Id == updated.Id);
        if (idx < 0) return;
        _events[idx] = updated;
        Save();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        _events.RemoveAll(e => e.Id == id);
        Save();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── 永続化 ───────────────────────────────────────────
    private List<ScheduleEvent> Load()
    {
        try
        {
            if (File.Exists(DataFile))
            {
                var json = File.ReadAllText(DataFile);
                return JsonConvert.DeserializeObject<List<ScheduleEvent>>(json) ?? new();
            }
        }
        catch { /* 初回 or 破損 */ }
        return new();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.WriteAllText(DataFile, JsonConvert.SerializeObject(_events, Formatting.Indented));
        }
        catch { /* 保存失敗は無視 */ }
    }
}
