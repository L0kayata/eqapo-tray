using System.Runtime.InteropServices;

namespace EqApoTray.Services;

// Win32 GetOpenFileNameW wrapper. Replaces Windows.Storage.Pickers.FileOpenPicker
// because the latter throws COMException in unpackaged WinUI 3 apps under various
// activation-context conditions. The classic dialog has no such activation
// requirements and works the same way packaged or not.
internal static partial class Win32FileDialog
{
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR   = 0x00000008;
    private const int OFN_EXPLORER      = 0x00080000;
    private const int OFN_HIDEREADONLY  = 0x00000004;
    private const int MaxPathChars      = 32768;

    // Filter format: pairs of "Display\0Pattern" separated by \0, terminated with \0.
    // e.g. "Text (*.txt)\0*.txt\0All (*.*)\0*.*\0"
    public static string? PickFile(IntPtr ownerHwnd, string title, string filter, string? initialDir)
    {
        var fileBuffer    = Marshal.AllocHGlobal(MaxPathChars * sizeof(char));
        var filterPtr     = Marshal.StringToHGlobalUni(filter);
        var titlePtr      = Marshal.StringToHGlobalUni(title);
        var initialDirPtr = string.IsNullOrEmpty(initialDir)
            ? IntPtr.Zero
            : Marshal.StringToHGlobalUni(initialDir);

        try
        {
            // Empty file name buffer.
            Marshal.WriteInt16(fileBuffer, 0);

            var ofn = new OpenFileName
            {
                lStructSize     = Marshal.SizeOf<OpenFileName>(),
                hwndOwner       = ownerHwnd,
                lpstrFilter     = filterPtr,
                lpstrFile       = fileBuffer,
                nMaxFile        = MaxPathChars,
                lpstrInitialDir = initialDirPtr,
                lpstrTitle      = titlePtr,
                Flags           = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER | OFN_HIDEREADONLY,
            };

            return GetOpenFileNameW(ref ofn) ? Marshal.PtrToStringUni(fileBuffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
            Marshal.FreeHGlobal(filterPtr);
            Marshal.FreeHGlobal(titlePtr);
            if (initialDirPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(initialDirPtr);
            }
        }
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileNameW(ref OpenFileName ofn);

    // All string fields are IntPtr (manually marshalled) so the struct stays
    // blittable — required for the [LibraryImport] source generator and AOT.
    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileName
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }
}
