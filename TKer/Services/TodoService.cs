using System;
using System.Collections.Generic;
using System.Linq;
using TKer.Helpers;
using TKer.Models;

namespace TKer.Services;

/// <summary>
/// ToDo アイテムの CRUD を管理するサービス。
/// %AppData%\TKer\todos.json に保存される。
/// </summary>
public class TodoService
{
    private const string FileName = "todos.json";

    private List<TodoItem> _items;

    public event EventHandler? DataChanged;

    public TodoService() => _items = JsonFileStore.Load<List<TodoItem>>(FileName);

    // ── 読み取り ─────────────────────────────────────────
    public IReadOnlyList<TodoItem> GetAll()
        => _items.OrderBy(t => t.IsCompleted)
                 .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                 .ThenByDescending(t => t.CreatedAt)
                 .ToList();

    public IReadOnlyList<TodoItem> GetByTaskId(string taskId)
        => _items.Where(t => t.LinkedTaskId == taskId)
                 .OrderBy(t => t.IsCompleted)
                 .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                 .ToList();

    public IReadOnlyList<TodoItem> GetUnlinked()
        => _items.Where(t => t.LinkedTaskId == null)
                 .OrderBy(t => t.IsCompleted)
                 .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
                 .ToList();

    public IReadOnlyList<TodoItem> GetByDate(DateTime date)
        => _items.Where(t => t.DueDate.HasValue && t.DueDate.Value.Date == date.Date)
                 .ToList();

    // ── 追加 / 更新 / 削除 ───────────────────────────────
    public TodoItem Add(string title, string? notes = null,
        DateTime? dueDate = null, string color = "#3D7EFF",
        TodoRepeat repeat = TodoRepeat.None, string? linkedTaskId = null,
        string? mapInfo = null)
    {
        var item = new TodoItem
        {
            Title        = title,
            Notes        = notes,
            DueDate      = dueDate,
            Color        = color,
            Repeat       = repeat,
            LinkedTaskId = linkedTaskId,
            MapInfo      = mapInfo,
        };
        _items.Add(item);
        SaveAndNotify();
        return item;
    }

    public void Update(TodoItem updated)
    {
        var idx = _items.FindIndex(t => t.Id == updated.Id);
        if (idx < 0) return;
        updated.UpdatedAt = DateTime.Now;
        _items[idx] = updated;
        SaveAndNotify();
    }

    public void Delete(string id)
    {
        if (_items.RemoveAll(t => t.Id == id) > 0)
            SaveAndNotify();
    }

    public void Toggle(string id)
    {
        var item = _items.FirstOrDefault(t => t.Id == id);
        if (item == null) return;

        item.IsCompleted = !item.IsCompleted;
        item.CompletedAt = item.IsCompleted ? DateTime.Now : null;
        item.UpdatedAt   = DateTime.Now;

        if (item.IsCompleted && item.Repeat != TodoRepeat.None && item.DueDate.HasValue)
        {
            item.DueDate     = NextDueDate(item.DueDate.Value, item.Repeat);
            item.IsCompleted = false;
            item.CompletedAt = null;
        }

        SaveAndNotify();
    }

    // ── 繰り返し次期日計算 ───────────────────────────────
    private static DateTime NextDueDate(DateTime current, TodoRepeat repeat) => repeat switch
    {
        TodoRepeat.Daily   => current.AddDays(1),
        TodoRepeat.Weekday => NextWeekday(current),
        TodoRepeat.Weekly  => current.AddDays(7),
        TodoRepeat.Monthly => current.AddMonths(1),
        TodoRepeat.Yearly  => current.AddYears(1),
        _                  => current
    };

    private static DateTime NextWeekday(DateTime from)
    {
        var next = from.AddDays(1);
        while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next;
    }

    // ── 統計 ─────────────────────────────────────────────
    public int TotalCount     => _items.Count;
    public int CompletedCount => _items.Count(t => t.IsCompleted);
    public int PendingCount   => _items.Count(t => !t.IsCompleted);
    public int OverdueCount   => _items.Count(t => t.IsOverdue);

    // ── 永続化 ───────────────────────────────────────────
    private void SaveAndNotify()
    {
        JsonFileStore.Save(FileName, _items);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }
}
