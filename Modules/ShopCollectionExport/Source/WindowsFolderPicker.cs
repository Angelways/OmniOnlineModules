using System.Runtime.InteropServices;

namespace OmniToolbox.ShopOwned;

internal static class WindowsFolderPicker
{
    public static Task<string?> SelectAsync(string initialDirectory, nint owner,
        string title, string acceptLabel, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Keep COM on its own STA. Cancellation closes only windows owned by this picker thread.
        var thread = new Thread(() =>
        {
            IFileDialog? dialog = null;
            IShellItem? initial = null;
            IShellItem? result = null;
            string? selected = null;
            Exception? error = null;
            var initialized = false;
            var threadId = GetCurrentThreadId();
            using var cancellationMonitor = new Timer(_ =>
            {
                if (cancellationToken.IsCancellationRequested)
                    EnumThreadWindows(threadId, (window, _) => { PostMessage(window, 0x0010, 0, 0); return true; }, 0);
            }, null, 0, 100);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2));
                initialized = true;
                var type = Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), true)!;
                dialog = (IFileDialog)Activator.CreateInstance(type)!;
                dialog.SetOptions(0x20 | 0x40 | 0x800 | 0x8);
                dialog.SetTitle(title);
                dialog.SetOkButtonLabel(acceptLabel);
                if (Directory.Exists(initialDirectory))
                {
                    var iid = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(initialDirectory, 0, ref iid, out initial) >= 0)
                        dialog.SetFolder(initial!);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var hr = dialog.Show(owner);
                if (hr != unchecked((int)0x800704C7))
                {
                    Marshal.ThrowExceptionForHR(hr);
                    dialog.GetResult(out result);
                    result.GetDisplayName(0x80058000, out var path);
                    try { selected = Marshal.PtrToStringUni(path); }
                    finally { Marshal.FreeCoTaskMem(path); }
                }
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                try
                {
                    if (result is not null) Marshal.FinalReleaseComObject(result);
                    if (initial is not null) Marshal.FinalReleaseComObject(initial);
                    if (dialog is not null) Marshal.FinalReleaseComObject(dialog);
                }
                catch (Exception ex) { error ??= ex; }
                finally { if (initialized) CoUninitialize(); }
            }
            cancellationMonitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
            // Complete only after native cleanup, so FeatureLifetime can safely drain this task.
            if (cancellationToken.IsCancellationRequested) completion.TrySetCanceled(cancellationToken);
            else if (error is not null) completion.TrySetException(error);
            else completion.TrySetResult(selected);
        }) { IsBackground = true, Name = "OmniShopOwned Folder Picker" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumWindowsCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

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
