using System.Runtime.InteropServices;

namespace OmniToolbox.ShopOwned;

internal static class WindowsFolderPicker
{
    public static Task<string?> SelectAsync(string initialDirectory, nint owner)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // The native Shell dialog needs STA; never block Dalamud's framework/render thread.
        var thread = new Thread(() =>
        {
            IFileDialog? dialog = null;
            IShellItem? initial = null;
            IShellItem? result = null;
            var initialized = false;
            try
            {
                Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2));
                initialized = true;
                var type = Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), true)!;
                dialog = (IFileDialog)Activator.CreateInstance(type)!;
                dialog.SetOptions(0x20 | 0x40 | 0x800 | 0x8); // PICKFOLDERS, FORCEFILESYSTEM, PATHMUSTEXIST, NOCHANGEDIR
                dialog.SetTitle("选择商城收录导出文件夹");
                dialog.SetOkButtonLabel("选择文件夹");
                if (Directory.Exists(initialDirectory))
                {
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(initialDirectory, 0, ref iid, out initial) >= 0)
                        dialog.SetFolder(initial!);
                }
                var hr = dialog.Show(owner);
                if (hr == unchecked((int)0x800704C7)) { completion.SetResult(null); return; }
                Marshal.ThrowExceptionForHR(hr);
                dialog.GetResult(out result);
                result.GetDisplayName(0x80058000, out var path); // SIGDN_FILESYSPATH
                try { completion.SetResult(Marshal.PtrToStringUni(path)); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            catch (Exception ex) { completion.TrySetException(ex); }
            finally
            {
                if (result is not null) Marshal.FinalReleaseComObject(result);
                if (initial is not null) Marshal.FinalReleaseComObject(initial);
                if (dialog is not null) Marshal.FinalReleaseComObject(dialog);
                if (initialized) CoUninitialize();
            }
        }) { IsBackground = true, Name = "OmniShopOwned Folder Picker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    // IFileDialog inherits IModalWindow. Preserve native vtable order, including unused slots.
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(nint owner);
        void SetFileTypes(uint count, nint filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(nint events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName(out nint name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
        void AddPlace(IShellItem item, uint alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(nint filter);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint displayName, out nint name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
}
