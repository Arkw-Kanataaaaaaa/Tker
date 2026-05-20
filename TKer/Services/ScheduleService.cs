using System;
using System.Collections.Generic;
using System.Linq;
using TKer.Helpers;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// スケジュールイベントを %AppData%\TKer\schedule.json で管理するサービス。
/// </summary>
public class ScheduleService
{
    private const string FileName = "schedule.json";

    private List<ScheduleEvent> _events;

    public event EventHandler? DataChanged;

    public ScheduleService() => _events = JsonFileStore.Load<List<ScheduleEvent>>(FileName);

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
            .Where(e => e.StartTime.Date <= d && e.EndTime.Date >= d)
            .OrderBy(e => e.StartTime)
            .ToList();
    }

    // ── 追加 / 更新 / 削除 ───────────────────────────────
    public ScheduleEvent Add(string title, string description, string location,
        DateTime start, DateTime end, bool isAllDay, string color = "#3D7EFF", string? linkedTaskId = null)
    {
        var ev = new ScheduleEvent
        {
            Title        = title,
            Description  = description,
            Location     = location,
            StartTime    = isAllDay ? start.Date : start,
            EndTime      = isAllDay ? end.Date.AddDays(1).AddSeconds(-1) : end,
            IsAllDay     = isAllDay,
            Color        = color,
            LinkedTaskId = linkedTaskId,
            CreatedAt    = DateTime.Now
        };
        _events.Add(ev);
        SaveAndNotify();
        return ev;
    }

    public void Update(ScheduleEvent updated)
    {
        var idx = _events.FindIndex(e => e.Id == updated.Id);
        if (idx < 0) return;
        _events[idx] = updated;
        SaveAndNotify();
    }

    public void Delete(string id)
    {
        _events.RemoveAll(e => e.Id == id);
        SaveAndNotify();
    }

    // ── 永続化 ───────────────────────────────────────────
    private void SaveAndNotify()
    {
        JsonFileStore.Save(FileName, _events);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
