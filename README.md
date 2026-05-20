# TKer v1.2.0 — プロジェクト・タスク管理システム

WPF (.NET 8) 製デスクトップアプリ。  
複数プロジェクトの一元管理から成果物納品まで対応します。

---

## 画面構成（10画面）

| 画面 | 機能 |
|---|---|
| 🏠 ホーム | 全PJ横断アラート・プロジェクト一覧（ピン留め）・クイックナビ |
| 📊 ダッシュボード | 統計カード・最近のタスク・進捗バー・競合検知バナー |
| 📁 プロジェクトフォルダ | フォルダツリー・エクスプローラー連携 |
| 📂 カテゴリー管理 | CRUD・フォルダ自動生成・カラー設定・フォルダリネーム |
| ✅ タスク一覧 | CRUD・ソート・フィルター・CSVエクスポート・コメント・タグ |
| 📈 ガントチャート | 予定/実績バー・全体表示・完了非表示・スクロール同期 |
| 📅 カレンダー | 月表示・横一覧・セルクリック詳細・完了非表示 |
| 📦 成果物まとめ | 完了タスクの成果物を集約・ZIP・INDEX.md生成 |
| 🚀 環境構築 | キックファイル生成/実行 |
| ⚙️ プロジェクト設定 | 新規/開く/インポート/設定編集・現在PJ表示 |

---

## バージョン履歴

### v1.2.0（現行）— 改善・成果物まとめ追加
- 📦 **成果物まとめ機能** — 全タスク完了後に成果物を集約・ZIP・INDEX.md生成
- 🐛 カレンダーセルクリック未接続バグ修正
- 🐛 ガントチャートスクロール無限ループ防止
- 🐛 カテゴリー編集の楽観的更新問題修正（フォルダリネームオプション付き）
- 🔒 UTF-8 BOM 付きファイル保存
- 🔄 プロジェクト移動時の自動パス再マッピング
- ⚙️ アプリ設定ダイアログ（猶予日数・完了非表示デフォルト）
- 🗑️ タスク削除の専用ダイアログ（3択：データのみ/フォルダも/キャンセル）
- ⚡ ホーム画面サマリーの30秒キャッシュ
- 📊 ガント200件超で描画確認ダイアログ
- 🔢 最近使ったプロジェクト上限20件・LRU管理

### v1.1.0 — バグ修正・機能追加
- タスク一覧TagバインドのバグFix、CSVエクスポート、コメント機能
- キーボードショートカット（Ctrl+F/S/H）、タグ機能、遅延承認フラグ
- 自動保存(30秒)、バックアップ(.bak)、競合検知
- プロジェクト設定編集、削除時フォルダ削除オプション

### v1.0.0 — 初回リリース
- プロジェクト・カテゴリー・タスク管理（フォルダ自動生成）
- ガントチャート、カレンダー、複数プロジェクト対応
- 環境構築キックファイル、Excelインポート、ホーム画面

---

## ビルド手順

```powershell
# 必要: .NET 8 SDK, Visual Studio 2022

dotnet build TKer.sln -c Release
# または publish (単一ファイル)
dotnet publish TKer\TKer.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

---

## キーボードショートカット

| キー | 動作 |
|---|---|
| Ctrl+S | 保存 |
| Ctrl+H | ホームへ |
| Ctrl+F | タスク検索フォーカス |

---

## 成果物まとめ機能 詳細

### ユースケース
1. 全タスクを「完了」ステータスにする
2. 「📦 成果物まとめ」画面を開く
3. 出力先・オプションを設定して実行
4. 生成されたフォルダ/ZIPをクライアントへ納品

### 出力構成
```
成果物まとめ_20250621_1430/
├── INDEX.md                    ← ファイル一覧レポート（Markdown）
├── 基本設計/
│   ├── TSK0001_機能設計書/
│   │   └── 機能設計書_v1.0.docx
│   └── TSK0002_DB設計/
│       └── ER図.xlsx
├── 開発/
│   └── TSK0003_実装/
│       └── テスト結果.pdf
└── ...
成果物まとめ_20250621_1430.zip  ← ZIP（オプション）
```

### INDEX.md の内容
- プロジェクト名・生成日時・ファイル数・合計サイズ
- カテゴリー別・タスク別ファイル一覧
- タスク完了状況一覧表（ID / 担当者 / 予定終了 / 実績終了）

---

## フォルダ構成（ソース）

```
TKer.sln
├── TKer/                              WPF アプリ本体
│   ├── Models/Models.cs
│   ├── ViewModels/MainViewModel.cs
│   ├── Services/
│   │   ├── ProjectService.cs          CRUD・自動保存・CSV・パス再マッピング
│   │   ├── AppSettingsService.cs      複数PJ・アラート・キャッシュ
│   │   ├── DeliverableService.cs      成果物集約・ZIP・INDEX.md
│   │   └── SetupService.cs            環境構築キック
│   ├── Helpers/FolderPicker.cs
│   ├── Converters/Converters.cs
│   ├── Themes/AppTheme.xaml
│   └── Views/
│       ├── Pages/                     10画面
│       └── Dialogs/
│           ├── TaskDialog             タスク追加/編集
│           ├── TaskDeleteDialog       タスク削除（3択）
│           ├── CommentDialog          タスクコメント
│           ├── CategoryDialog         カテゴリー追加/編集（リネーム対応）
│           ├── FileListDialog         フォルダ内ファイル一覧
│           ├── AlertListDialog        全PJ横断アラート
│           ├── AppSettingsDialog      アプリ設定
│           ├── ProjectSettingsDialog  プロジェクト設定編集
│           ├── ManualDialog           操作マニュアル（11章）
│           └── ChangelogDialog        更新履歴
```

---

## NuGet パッケージ

| パッケージ | 用途 |
|---|---|
| CommunityToolkit.Mvvm | MVVM |
| ClosedXML | Excel インポート |
| Newtonsoft.Json | JSON 保存 |
| MaterialDesignThemes | ダークテーマ |

---

## プロジェクトデータ保存形式

```
プロジェクトフォルダ/
├── project_data.json      全データ（UTF-8 BOM付き）
├── project_data.json.bak  バックアップ（保存毎に更新）
├── CATXXX_カテゴリー名/
│   └── TSKXXXX_タスク省略名/  成果物格納フォルダ
├── 作業完了/              完了タスク移動先
├── _templates/
└── _docs/
```

%AppData%\TKer\settings.json — アプリ設定・最近使ったプロジェクト一覧
