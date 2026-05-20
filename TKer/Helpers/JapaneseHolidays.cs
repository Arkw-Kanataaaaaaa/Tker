using System;
using System.Collections.Generic;

namespace TKer.Helpers;

/// <summary>
/// 日本の祝日・休日判定を提供するヘルパー。
/// </summary>
public static class JapaneseHolidays
{
    private static readonly Dictionary<DateTime, string> _holidays = new()
    {
        // 2024
        { new DateTime(2024, 1, 1),  "元日" },
        { new DateTime(2024, 1, 8),  "成人の日" },
        { new DateTime(2024, 2, 11), "建国記念日" },
        { new DateTime(2024, 2, 12), "振替休日" },
        { new DateTime(2024, 2, 23), "天皇誕生日" },
        { new DateTime(2024, 3, 20), "春分の日" },
        { new DateTime(2024, 4, 29), "昭和の日" },
        { new DateTime(2024, 5, 3),  "憲法記念日" },
        { new DateTime(2024, 5, 4),  "みどりの日" },
        { new DateTime(2024, 5, 5),  "こどもの日" },
        { new DateTime(2024, 5, 6),  "振替休日" },
        { new DateTime(2024, 7, 15), "海の日" },
        { new DateTime(2024, 8, 11), "山の日" },
        { new DateTime(2024, 8, 12), "振替休日" },
        { new DateTime(2024, 9, 16), "敬老の日" },
        { new DateTime(2024, 9, 22), "振替休日" },
        { new DateTime(2024, 9, 23), "秋分の日" },
        { new DateTime(2024, 10, 14),"スポーツの日" },
        { new DateTime(2024, 11, 3), "文化の日" },
        { new DateTime(2024, 11, 4), "振替休日" },
        { new DateTime(2024, 11, 23),"勤労感謝の日" },
        // 2025
        { new DateTime(2025, 1, 1),  "元日" },
        { new DateTime(2025, 1, 13), "成人の日" },
        { new DateTime(2025, 2, 11), "建国記念日" },
        { new DateTime(2025, 2, 23), "天皇誕生日" },
        { new DateTime(2025, 2, 24), "振替休日" },
        { new DateTime(2025, 3, 20), "春分の日" },
        { new DateTime(2025, 4, 29), "昭和の日" },
        { new DateTime(2025, 5, 3),  "憲法記念日" },
        { new DateTime(2025, 5, 4),  "みどりの日" },
        { new DateTime(2025, 5, 5),  "こどもの日" },
        { new DateTime(2025, 5, 6),  "振替休日" },
        { new DateTime(2025, 7, 21), "海の日" },
        { new DateTime(2025, 8, 11), "山の日" },
        { new DateTime(2025, 9, 15), "敬老の日" },
        { new DateTime(2025, 9, 23), "秋分の日" },
        { new DateTime(2025, 10, 13),"スポーツの日" },
        { new DateTime(2025, 11, 3), "文化の日" },
        { new DateTime(2025, 11, 23),"勤労感謝の日" },
        { new DateTime(2025, 11, 24),"振替休日" },
        // 2026
        { new DateTime(2026, 1, 1),  "元日" },
        { new DateTime(2026, 1, 12), "成人の日" },
        { new DateTime(2026, 2, 11), "建国記念日" },
        { new DateTime(2026, 2, 23), "天皇誕生日" },
        { new DateTime(2026, 3, 20), "春分の日" },
        { new DateTime(2026, 4, 29), "昭和の日" },
        { new DateTime(2026, 5, 3),  "憲法記念日" },
        { new DateTime(2026, 5, 4),  "みどりの日" },
        { new DateTime(2026, 5, 5),  "こどもの日" },
        { new DateTime(2026, 5, 6),  "振替休日" },
        { new DateTime(2026, 7, 20), "海の日" },
        { new DateTime(2026, 8, 11), "山の日" },
        { new DateTime(2026, 9, 21), "敬老の日" },
        { new DateTime(2026, 9, 22), "国民の休日" },
        { new DateTime(2026, 9, 23), "秋分の日" },
        { new DateTime(2026, 10, 12),"スポーツの日" },
        { new DateTime(2026, 11, 3), "文化の日" },
        { new DateTime(2026, 11, 23),"勤労感謝の日" },
        // 2027
        { new DateTime(2027, 1, 1),  "元日" },
        { new DateTime(2027, 1, 11), "成人の日" },
        { new DateTime(2027, 2, 11), "建国記念日" },
        { new DateTime(2027, 2, 23), "天皇誕生日" },
        { new DateTime(2027, 3, 21), "春分の日" },
        { new DateTime(2027, 4, 29), "昭和の日" },
        { new DateTime(2027, 5, 3),  "憲法記念日" },
        { new DateTime(2027, 5, 4),  "みどりの日" },
        { new DateTime(2027, 5, 5),  "こどもの日" },
        { new DateTime(2027, 7, 19), "海の日" },
        { new DateTime(2027, 8, 11), "山の日" },
        { new DateTime(2027, 9, 20), "敬老の日" },
        { new DateTime(2027, 9, 23), "秋分の日" },
        { new DateTime(2027, 10, 11),"スポーツの日" },
        { new DateTime(2027, 11, 3), "文化の日" },
        { new DateTime(2027, 11, 23),"勤労感謝の日" },
    };

    /// <summary>指定日が祝日かどうかを返す。</summary>
    public static bool IsHoliday(DateTime date) => _holidays.ContainsKey(date.Date);

    /// <summary>指定日の祝日名を返す。祝日でない場合は null。</summary>
    public static string? GetName(DateTime date) => _holidays.TryGetValue(date.Date, out var n) ? n : null;

    /// <summary>指定日が土日または祝日かどうかを返す。</summary>
    public static bool IsRestDay(DateTime date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || IsHoliday(date);
}
