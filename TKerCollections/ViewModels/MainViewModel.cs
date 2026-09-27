using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TKer.Models;
using TKer.Services;

namespace TKer.ViewModels;

/// <summary>アプリケーション全体のナビゲーションと状態を管理するメイン ViewModel（コレクション機能のみ）。</summary>
public partial class MainViewModel : ObservableObject
{
    // ── サービス ──────────────────────────────────
    /// <summary>アプリ設定の読み書きを担うサービス。</summary>
    public AppSettingsService AppSettingsService { get; }
    /// <summary>コレクション機能を担うサービス。</summary>
    public CollectionService  CollectionService  { get; }

    // ── ナビゲーション ────────────────────────────
    [ObservableProperty] private string _currentView = "Collection";

    /// <summary>コレクション詳細ページで表示するコレクション。</summary>
    public Collection? SelectedCollection { get; set; }

    /// <summary>各サービスを初期化するコンストラクター。</summary>
    public MainViewModel()
    {
        AppSettingsService = new AppSettingsService();
        CollectionService  = new CollectionService(AppSettingsService);
    }

    // ── ナビゲーション ────────────────────────────
    /// <summary>指定したビューに遷移する。</summary>
    [RelayCommand]
    public void NavigateTo(string view)
    {
        CurrentView = view;
    }
}
