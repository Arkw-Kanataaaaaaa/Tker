using System;
using TKer.Services;

namespace TKer.Services;

/// <summary>
/// ウィジェットが必要とするサービスをまとめたシンプルなコンテナ。
/// メインプロセス・ウィジェット専用プロセスの両方で使用する。
/// </summary>
public class WidgetServiceProvider
{
    public AppSettingsService AppSettingsService { get; }
    public ScheduleService    ScheduleService    { get; }
    public ProjectService     ProjectService     { get; }
    public TodoService        TodoService        { get; }

    /// <summary>
    /// ページ遷移コールバック。
    /// メインプロセスでは MainViewModel.NavigateToCommand を呼ぶ。
    /// ウィジェット専用プロセスではメインアプリを起動する。
    /// </summary>
    public Action<string> NavigateTo { get; }

    public WidgetServiceProvider(
        AppSettingsService appSettings,
        ScheduleService    schedule,
        ProjectService     project,
        TodoService        todo,
        Action<string>     navigateTo)
    {
        AppSettingsService = appSettings;
        ScheduleService    = schedule;
        ProjectService     = project;
        TodoService        = todo;
        NavigateTo         = navigateTo;
    }
}
