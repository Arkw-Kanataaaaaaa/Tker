using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TKer.Helpers;

/// <summary>
/// System.Windows.Forms に依存しないフォルダ選択ダイアログ
/// Shell32 の IFileOpenDialog (Vista以降) を直接呼び出す
/// </summary>
public static class FolderPicker
{
    /// <summary>フォルダ選択ダイアログを表示し、選択されたパスを返す。キャンセル時は null。</summary>
    public static string? Pick(string title = "フォルダを選択")
    {
        try
        {
            // Vista 以降の IFileOpenDialog を使用（Forms不要）
            var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM);
            dialog.SetTitle(title);

            var owner = Application.Current?.MainWindow;
            var handle = owner != null
                ? new WindowInteropHelper(owner).Handle
                : IntPtr.Zero;

            var hr = dialog.Show(handle);
            if (hr != 0) return null; // キャンセル

            dialog.GetResult(out var item);
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
            return path;
        }
        catch
        {
            return null;
        }
    }

    // ── COM定義 ────────────────────────────────────
    /// <summary>IFileOpenDialog の COM クラスファクトリ用 RCW クラス。</summary>
    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    /// <summary>Shell32 の IFileOpenDialog COM インターフェース定義。</summary>
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        /// <summary>ダイアログを表示する。</summary>
        [PreserveSig] int Show(IntPtr parent);
        /// <summary>ファイルフィルターを設定する。</summary>
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        /// <summary>ファイルタイプインデックスを設定する。</summary>
        void SetFileTypeIndex(uint iFileType);
        /// <summary>ファイルタイプインデックスを取得する。</summary>
        void GetFileTypeIndex(out uint piFileType);
        /// <summary>イベントハンドラーを登録する。</summary>
        void Advise(IntPtr pfde, out uint pdwCookie);
        /// <summary>イベントハンドラーの登録を解除する。</summary>
        void Unadvise(uint dwCookie);
        /// <summary>ダイアログオプションを設定する。</summary>
        void SetOptions(FOS fos);
        /// <summary>ダイアログオプションを取得する。</summary>
        void GetOptions(out FOS pfos);
        /// <summary>デフォルトフォルダを設定する。</summary>
        void SetDefaultFolder(IShellItem psi);
        /// <summary>初期表示フォルダを設定する。</summary>
        void SetFolder(IShellItem psi);
        /// <summary>現在表示中のフォルダを取得する。</summary>
        void GetFolder(out IShellItem ppsi);
        /// <summary>現在の選択アイテムを取得する。</summary>
        void GetCurrentSelection(out IShellItem ppsi);
        /// <summary>ファイル名を設定する。</summary>
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        /// <summary>ファイル名を取得する。</summary>
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        /// <summary>ダイアログタイトルを設定する。</summary>
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        /// <summary>OK ボタンのラベルを設定する。</summary>
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        /// <summary>ファイル名ラベルを設定する。</summary>
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        /// <summary>選択結果のシェルアイテムを取得する。</summary>
        void GetResult(out IShellItem ppsi);
        /// <summary>ダイアログにショートカット場所を追加する。</summary>
        void AddPlace(IShellItem psi, int alignment);
        /// <summary>デフォルト拡張子を設定する。</summary>
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        /// <summary>ダイアログを閉じる。</summary>
        void Close(int hr);
        /// <summary>クライアント GUID を設定する。</summary>
        void SetClientGuid(ref Guid guid);
        /// <summary>クライアントデータをクリアする。</summary>
        void ClearClientData();
        /// <summary>フィルターを設定する。</summary>
        void SetFilter(IntPtr pFilter);
        /// <summary>複数選択結果を取得する。</summary>
        void GetResults(out IShellItemArray ppenum);
        /// <summary>選択中のアイテム一覧を取得する。</summary>
        void GetSelectedItems(out IShellItemArray ppsai);
    }

    /// <summary>Shell32 の IShellItem COM インターフェース定義。</summary>
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        /// <summary>指定ハンドラーにバインドする。</summary>
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        /// <summary>親シェルアイテムを取得する。</summary>
        void GetParent(out IShellItem ppsi);
        /// <summary>指定形式の表示名を取得する。</summary>
        void GetDisplayName(SIGDN sigdnName,
            [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        /// <summary>属性を取得する。</summary>
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        /// <summary>別シェルアイテムと比較する。</summary>
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    /// <summary>シェルアイテム配列の COM インターフェース定義。</summary>
    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray { }

    /// <summary>IFileOpenDialog に渡すオプションフラグ。</summary>
    [Flags]
    private enum FOS : uint
    {
        FOS_PICKFOLDERS    = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
    }

    /// <summary>IShellItem.GetDisplayName に渡す表示名形式。</summary>
    private enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000,
    }
}
