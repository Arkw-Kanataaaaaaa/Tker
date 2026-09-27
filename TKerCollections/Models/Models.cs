using System;
using System.Collections.Generic;

namespace TKer.Models;

// ══════════════════════════════════════════════
//  アプリ全体設定
// ══════════════════════════════════════════════

/// <summary>アプリ全体設定（コレクション機能のみを保持する最小構成）。</summary>
public class AppSettings
{
    /// <summary>登録済みコレクションのファイルパス一覧。</summary>
    public List<string> CollectionFilePaths { get; set; } = new();
}

// ══════════════════════════════════════════════
//  コレクション
// ══════════════════════════════════════════════

/// <summary>コレクションのアイテムに付属させる情報フィールドの定義</summary>
public class CollectionField
{
    /// <summary>フィールドの一意識別子。</summary>
    public string Id        { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>フィールド名。</summary>
    public string Name      { get; set; } = "";
    /// <summary>文字列 | ファイル | リンク</summary>
    public string FieldType { get; set; } = "文字列";
    /// <summary>フィールドの表示順序。</summary>
    public int    Order     { get; set; } = 0;
}

/// <summary>コレクション（任意のアイテムをまとめるグループ）</summary>
public class Collection
{
    /// <summary>コレクションの一意識別子。</summary>
    public string Id          { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>コレクション名。</summary>
    public string Name        { get; set; } = "";
    /// <summary>コレクションのアイコン（絵文字）。</summary>
    public string Icon        { get; set; } = "📁";
    /// <summary>コレクションの説明文。</summary>
    public string Description { get; set; } = "";
    /// <summary>関連するフォルダのパス。</summary>
    public string FolderPath  { get; set; } = "";
    /// <summary>アイテムの主データ形式: "文字列" | "ファイル"</summary>
    public string ItemFormat  { get; set; } = "文字列";
    /// <summary>コレクションのフィールド定義一覧。</summary>
    public List<CollectionField> Fields { get; set; } = new();
    /// <summary>コレクションに含まれるアイテムの一覧。</summary>
    public List<CollectionItem>  Items  { get; set; } = new();
    /// <summary>表紙画像（Base64エンコード）。</summary>
    public string CoverImageData { get; set; } = string.Empty;
    /// <summary>コレクションの作成日時。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>コレクションの最終更新日時。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>コレクション内の1件のアイテム</summary>
public class CollectionItem
{
    /// <summary>アイテムの一意識別子。</summary>
    public string Id      { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>アイテム名。</summary>
    public string Name    { get; set; } = "";
    /// <summary>フィールドID → 値 の動的データ</summary>
    public Dictionary<string, string> FieldValues { get; set; } = new();
    /// <summary>アイテムの追加日時。</summary>
    public DateTime AddedAt { get; set; } = DateTime.Now;
}
