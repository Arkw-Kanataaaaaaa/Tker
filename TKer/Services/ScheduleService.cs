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
    private const string FILE_NAME = "schedule.json";

    private List<ScheduleEvent> _events;

    public event EventHandler? DataChanged;

    /// <summary>JSON ファイルからスケジュールイベントを読み込んで初期化する。</summary>
    public ScheduleService() => _events = JsonFileStore.Load<List<ScheduleEvent>>(FILE_NAME);

    // ── 読み取り ─────────────────────────────────────────
    /// <summary>全スケジュールイベントを返す。</summary>
    public IReadOnlyList<ScheduleEvent> GetAll() => _events.AsReadOnly();

    /// <summary>指定年月に該当するスケジュールイベントを返す。</summary>
    public IReadOnlyList<ScheduleEvent> GetByMonth(int year, int month)
        => _events
            .Where(e => (e.StartTime.Year == year && e.StartTime.Month == month) ||
                        (e.EndTime.Year   == year && e.EndTime.Month   == month) ||
                        (e.StartTime      <= new DateTime(year, month, 1) &&
                         e.EndTime        >= new DateTime(year, month, DateTime.DaysInMonth(year, month))))
            .OrderBy(e => e.StartTime)
            .ToList();

    /// <summary>指定日に該当するスケジュールイベントを返す。</summary>
    public IReadOnlyList<ScheduleEvent> GetByDate(DateTime date)
    {
        var d = date.Date;
        return _events
            .Where(e => e.StartTime.Date <= d && e.EndTime.Date >= d)
            .OrderBy(e => e.StartTime)
            .ToList();
    }

    // ── 追加 / 更新 / 削除 ───────────────────────────────
    /// <summary>新しいスケジュールイベントを追加して保存する。</summary>
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

    /// <summary>既存のスケジュールイベントを更新して保存する。</summary>
    public void Update(ScheduleEvent updated)
    {
        var idx = _events.FindIndex(e => e.Id == updated.Id);
        if (idx < 0) return;
        _events[idx] = updated;
        SaveAndNotify();
    }

    /// <summary>指定 ID のスケジュールイベントを削除して保存する。</summary>
    public void Delete(string id)
    {
        _events.RemoveAll(e => e.Id == id);
        SaveAndNotify();
    }

    // ── 永続化 ───────────────────────────────────────────
    /// <summary>データを JSON ファイルに保存して変更イベントを発火する。</summary>
    private void SaveAndNotify()
    {
        JsonFileStore.Save(FILE_NAME, _events);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
