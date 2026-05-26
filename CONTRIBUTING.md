# 開発ガイドライン

## 目次

1. [命名規則](#命名規則)
2. [コメント規則](#コメント規則)
3. [ブランチ戦略](#ブランチ戦略)
4. [コミットメッセージ](#コミットメッセージ)

---

## 命名規則

### C# コード

| 対象 | 規則 | 例 |
|---|---|---|
| クラス・メソッド・プロパティ | `PascalCase` | `ProjectListPage`, `ShowInfoPanel()` |
| ローカル変数・引数 | `lowerCamelCase` | `projectName`, `useFolderManagement` |
| プライベートフィールド | `_lowerCamelCase` | `_selectedPath`, `_isFolderMode` |
| 定数（`const` / `static readonly`） | `UPPER_SNAKE_CASE` | `ALL_COLUMNS`, `PANEL_WIDTH`, `MUTEX_NAME` |

#### 定数の例

```csharp
// ✅ 正しい
private static readonly string[] ALL_COLUMNS = { "名前", "更新日時", "種類", "サイズ" };
private const double DETAIL_CANVAS_MIN_W = 400.0;

// ❌ 古い書き方（修正対象）
private static readonly string[] AllColumns = { "名前", "更新日時", "種類", "サイズ" };
private const double DetailCanvasMinW = 400.0;
```

### ブランチ名

| 規則 | 例 |
|---|---|
| `UPPER_SNAKE_CASE` を使用 | `FEATURE_FIX`, `PROJECT_LIST_FIX` |
| スラッシュによる階層は **1段のみ** | `FEATURE_FIX/PROJECT_LIST_FIX` は NG |
| 親ブランチと派生ブランチの関係は名前に含めない | 派生は単独の名前にする |

> **注意:** Git の仕様上、`FEATURE_FIX` というブランチが存在する状態で `FEATURE_FIX/PROJECT_LIST_FIX` は作成できません（ref パスの衝突）。派生ブランチは独立した名前にしてください。

---

## コメント規則

### XML ドキュメントコメント

すべてのクラス・パブリックメソッド・非自明なプライベートメソッドに **日本語の `/// <summary>` コメント** を1行で記述します。

```csharp
/// <summary>プロジェクト一覧を表示・管理するページ。</summary>
public partial class ProjectListPage : Page, IRefreshable
{
    /// <summary>ViewModelを受け取り初期化する。</summary>
    public ProjectListPage(MainViewModel vm) { ... }

    /// <summary>検索条件に基づいてプロジェクト一覧を絞り込んで表示する。</summary>
    private void ApplyFilter() { ... }
}
```

### コメントを書かなくていいケース

- 1行のデリゲート（`=> Refresh()`, `=> BuildList()` など自明な処理）
- 自動生成コード
- 処理内容がメソッド名だけで明らかなもの

```csharp
// ✅ コメント不要（自明な1ライナー）
private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveCategoryBy(-1);
private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
```

### インラインコメント

「**なぜ**その処理をするのか」が非自明な場合のみ記述します。「何をしているか」はコードを読めば分かるため不要です。

```csharp
// ✅ 理由が非自明なので書く
// 一時名を経由してリネームすることで、同一ドライブ内での名前衝突を回避する
Directory.Move(oldPath, tempPath);
Directory.Move(tempPath, finalPath);

// ❌ コードを読めば分かるので不要
// プロジェクト名を設定する
project.Name = name;
```

---

## ブランチ戦略

```
main
 └─ FEATURE_FIX          ← 機能修正をまとめる親ブランチ
     └─ PROJECT_LIST_FIX ← プロジェクト管理画面の修正（派生ブランチ）
     └─ （次の派生）      ← 他画面の修正はここから派生
```

### ルール

| ルール | 内容 |
|---|---|
| **作業ブランチ** | 画面・機能単位で派生ブランチを切る |
| **マージ先** | 派生ブランチ → 親ブランチ（`FEATURE_FIX`）へ PR |
| **main へのマージ** | 親ブランチを十分レビュー後に main へ |
| **変更スコープ** | 派生ブランチでは担当画面のファイルのみ変更する |
| **共通ファイルの変更** | `Models/`, `Services/` など共通ファイルを変更する場合は **事前に報告** してから対応 |

### 派生ブランチの命名

```
{画面名 or 機能名}_FIX   （バグ修正・改善）
{画面名 or 機能名}_FEAT  （新機能追加）
```

例:
- `PROJECT_LIST_FIX` — プロジェクト管理画面の修正
- `TASK_LIST_FEAT` — タスク管理画面の新機能

---

## コミットメッセージ

### 形式

```
{画面名 or 対象}: {変更内容の要約}

【詳細（任意）】
- 変更点1
- 変更点2
```

### 例

```
プロジェクト管理画面: フォルダ/詳細モード切替・リサイズ・追加フォームのインライン化

- 縦ツールバーをフォルダモードとプロジェクト詳細モードに分離
- プロジェクトリストと右パネルの間にGridSplitterを追加
- プロジェクト追加をダイアログからインラインフォームに変更
```

```
フォルダ整合の脆弱性対応

【ProjectService】
- LoadProject: PFTMP_*残留一時フォルダを起動時に削除
- DeleteTask: フォルダ削除をモデル保存後に実行
```

### 言語

コミットメッセージは **日本語** で記述します。

---

## その他

### 変更してはいけないファイル（ブランチスコープ外）

各派生ブランチでは担当画面に関係するファイルのみ変更します。以下のような共通ファイルを変更する場合は事前に確認が必要です。

- `Models/Models.cs`
- `Services/ProjectService.cs`
- `Services/AppSettingsService.cs`
- `Views/MainWindow.xaml(.cs)`
- `App.xaml(.cs)`

### 不要ファイルの削除

参照されていないページ・ダイアログは積極的に削除します。削除前に以下を確認してください。

1. `grep` でプロジェクト全体から参照を検索
2. ナビゲーションキー（`"GanttPage"` 等）での呼び出しがないことを確認
3. 他ブランチで使用中でないことを確認
