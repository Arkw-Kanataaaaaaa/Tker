# TKer v1.1.0 — プロジェクト・タスク管理システム

WPF (.NET 8) 製 Windows デスクトップアプリ。
複数プロジェクトの一元管理から成果物納品まで対応します。

---

## 動作環境

- Windows 10 / 11
- .NET 8 ランタイム（self-contained publish の場合は不要）

---

## 画面一覧

| 画面 | 主な機能 |
|---|---|
| ホーム | 全プロジェクト横断アラート・プロジェクト一覧・ピン留め・クイックナビ |
| ダッシュボード | 統計カード・最近のタスク・進捗バー・競合検知バナー |
| プロジェクト一覧 | 新規作成/開く/インポート・フォルダツリー・エクスプローラー連携 |
| カテゴリー管理 | 追加/編集/削除・フォルダ自動生成・カラー設定・ドラッグ並び替え |
| タスク一覧 | 追加/編集/削除・ソート・フィルター・CSVエクスポート・WBSエクスポート・コメント・タグ・ガントスケジュール設定 |
| ガントチャート | 予定/実績バー・全体表示・完了タスク非表示・スクロール同期 |
| カレンダー | 月表示・イベント管理・セルクリック詳細・完了タスク非表示 |
| 成果物まとめ | 完了タスクの成果物を集約・ZIP圧縮・INDEX.md生成 |
| ToDo | 独立ToDoリスト管理 |
| コレクション | 汎用コレクション管理 |
| 記事 | Markdown プレビュー付きメモ管理 |
| ポモドーロ | 作業/休憩タイマー・セッション集計 |
| テーブルリスト | カスタム列テーブルデータ管理 |
| ログビューア | アプリログ参照 |
| UI カスタマイズ | ホームレイアウト・フォント・テーマ・セクション配色 |
| 環境構築 | キックファイル生成/実行 |
| アプリ設定 | 猶予日数・表示設定・ツールバーカスタマイズ |
| ショートカット一覧 | キーボードショートカット一覧表示 |

---

## キーボードショートカット

### タスク一覧

| キー | 動作 |
|---|---|
| Ctrl+F | 検索フォーカス |
| Ctrl+S | 保存 |
| Ctrl+- | 選択タスク削除 |
| Ctrl+P | CSVエクスポート |
| Ctrl+K | 選択タスクのコメント表示 |
| Ctrl+H | 選択タスクのスケジュール設定 |
| Ctrl+Shift+; | タスク追加 |
| Ctrl+Shift+P | WBSエクスポート |
| F2 | 選択タスク編集 |
| F3 | 選択カテゴリー編集 |

### プロジェクト一覧

| キー | 動作 |
|---|---|
| Ctrl+N | 新規プロジェクト |
| Ctrl+O | プロジェクトを開く |
| Ctrl+F | 検索フォーカス |
| Ctrl+A | 選択プロジェクトをアクティブに設定 |
| Ctrl+- | 選択プロジェクトを一覧から削除 |

### 共通

| キー | 動作 |
|---|---|
| Ctrl+S | 保存 |

---

## ビルド手順

```powershell
# 前提: .NET 8 SDK, Visual Studio 2022 以降

# デバッグビルド
dotnet build TKer.sln

# リリースビルド
dotnet build TKer.sln -c Release

# 単一ファイル発行 (フレームワーク依存)
dotnet publish TKer\TKer.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# 単一ファイル発行 (自己完結)
dotnet publish TKer\TKer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

---

## フォルダ構成（ソース）

```
TKer.sln
TKer/
    App.xaml
    Models/
        Models.cs                      全モデル定義・AppVersion
    ViewModels/
        MainViewModel.cs               ナビゲーション・コマンド・状態管理
    Services/
        ProjectService.cs              プロジェクトCRUD・自動保存・パス再マッピング
        AppSettingsService.cs          複数プロジェクト管理・アラート・サマリーキャッシュ
        DeliverableService.cs          成果物集約・ZIP・INDEX.md生成
        ScheduleService.cs             カレンダーイベント管理
        TodoService.cs                 ToDo管理
        ArticleService.cs              記事管理
        CollectionService.cs           コレクション管理
        FileWatcherService.cs          タスクフォルダ変更監視
        FolderIntegrityService.cs      フォルダ整合性チェック（バックグラウンド）
        FolderOperationQueue.cs        フォルダ操作キュー
        SetupService.cs                環境構築キックファイル管理
        AppLogger.cs                   アプリログ
        WidgetServiceProvider.cs       ウィジェット用サービスプロバイダ
    Helpers/
        FolderPicker.cs                Shell32 フォルダ選択ダイアログ
        ShellHelper.cs                 エクスプローラー起動ヘルパー
        JapaneseHolidays.cs            祝日判定（2024〜2027年）
        UiThemeHelper.cs               セクションテーマ適用
        SmoothScroll.cs                スムーズスクロール
    Converters/
        Converters.cs                  各種バインディングコンバーター
    Themes/
        AppTheme.xaml                  マテリアルデザインベーステーマ
    Views/
        MainWindow.xaml                メインウィンドウ・ナビゲーション
        Pages/                         各画面（18ファイル）
        Dialogs/
            TaskDialog                 タスク追加/編集
            TaskDeleteDialog           タスク削除（データのみ/フォルダも/キャンセル）
            CommentDialog              タスクコメント
            CategoryDialog             カテゴリー追加/編集（フォルダリネーム対応）
            CategoryTemplateDialog     カテゴリーテンプレート
            CollectionEditDialog       コレクション編集
            ScheduleEventDialog        カレンダーイベント追加/編集
            TableColumnDialog          テーブル列定義
            ProgressConditionDialog    進捗条件設定
            FileListDialog             フォルダ内ファイル一覧
            AlertListDialog            全プロジェクト横断アラート
            NewProjectDialog           新規プロジェクト作成
            AppSettingsDialog          アプリ設定
            ProjectSettingsDialog      プロジェクト設定編集
            QuickStatusDialog          クイックステータス変更
            ComponentThemeDialog       コンポーネントテーマ設定
            ToolbarCustomizeDialog     ツールバーカスタマイズ
            ManualDialog               操作マニュアル
            ChangelogDialog            更新履歴
```

---

## 使用パッケージ

| パッケージ | バージョン | 用途 |
|---|---|---|
| CommunityToolkit.Mvvm | 8.2.2 | MVVM基盤（RelayCommand / ObservableObject） |
| Newtonsoft.Json | 13.0.3 | プロジェクトデータのJSON保存 |
| ClosedXML | 0.102.2 | Excelインポート |
| MaterialDesignThemes | 4.9.0 | UIテーマ・コントロール |
| MaterialDesignColors | 2.1.4 | カラーパレット |
| Markdig | 0.38.0 | Markdownレンダリング（記事画面） |
| Hardcodet.NotifyIcon.Wpf | 1.1.0 | タスクトレイアイコン |

---

## プロジェクトデータ保存形式

```
プロジェクトフォルダ/
    project_data.json          全データ（UTF-8 BOM付き、アトミック書き込み）
    project_data.json.bak      バックアップ（保存のたびに更新）
    001_カテゴリー名/           カテゴリーフォルダ（Order番号_名前）
        TSK0001_タスク省略名/   タスクフォルダ（成果物格納）
    002_カテゴリー名/
    _templates/
    _docs/
```

アプリ設定: `%AppData%\TKer\settings.json` — アプリ設定・最近使ったプロジェクト一覧（最大20件）

---

## 成果物まとめ機能

### 手順

1. 対象タスクを「完了」ステータスにする
2. 「成果物まとめ」画面を開く
3. 出力先・オプションを設定して実行

### 出力構成

```
成果物まとめ_20250621_1430/
    INDEX.md                        ファイル一覧レポート（Markdown）
    基本設計/
        TSK0001_機能設計書/
            機能設計書_v1.0.docx
        TSK0002_DB設計/
            ER図.xlsx
    開発/
        TSK0003_実装/
            テスト結果.pdf
成果物まとめ_20250621_1430.zip      ZIP（オプション）
```

### INDEX.md の内容

- プロジェクト名・生成日時・ファイル数・合計サイズ
- カテゴリー別・タスク別ファイル一覧
- タスク完了状況一覧表（ID / 担当者 / 予定終了日 / 実績終了日）

---

## 更新履歴

### v1.1.0（現行）

- カテゴリーリネーム時に配下タスクのフォルダパスが更新されない不具合を修正
- プロジェクト移動後のパス再マッピングを先頭一致・大小無視に変更し誤置換を防止
- プロジェクトデータ保存をアトミック書き込み（一時ファイル経由）に変更
- ダッシュボードの「今週」集計が日曜日に翌週起点になる計算誤りを修正
- カレンダーでイベントのみの日に件数オーバーフロー表示が出ない不具合を修正
- ページ遷移のたびにイベント購読が蓄積するメモリリークを修正（KeyDown / DataChanged / タイマー）
- ポモドーロの当日累計分が作業時間設定変更で遡及再計算される不具合を修正
- エクスプローラー起動をスペース含みパスでも正しく動作するよう修正
- プロジェクトカードクリック時の RuntimeBinderException を修正
- obj / bin / .vs をバージョン管理から除外（.gitignore 追加）

### v1.0.0

- プロジェクト・カテゴリー・タスク管理（フォルダ自動生成）
- ガントチャート・カレンダー・複数プロジェクト対応
- 自動保存（30秒）・バックアップ・競合検知
- 環境構築キックファイル・Excelインポート
- ホーム画面・ダッシュボード・成果物まとめ
