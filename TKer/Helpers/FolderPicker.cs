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
    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IShellItemArray ppenum);
        void GetSelectedItems(out IShellItemArray ppsai);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName,
            [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray { }

    [Flags]
    private enum FOS : uint
    {
        FOS_PICKFOLDERS    = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
    }

    private enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000,
    }
}
