using System.Runtime.InteropServices;

namespace PowerX.App.Services;

/// <summary>
/// The classic Win32 open-file dialog (<c>GetOpenFileNameW</c>). The modern WinUI
/// <c>FileOpenPicker</c> silently fails in an unpackaged app running elevated, which PowerX always
/// is, so we use the plain comdlg32 dialog instead.
/// </summary>
internal static class NativeFileDialog
{
    public static string? PickFile(nint ownerHwnd, string title = "Choose a file")
    {
        const int bufChars = 4096;
        nint buffer = Marshal.AllocHGlobal(bufChars * sizeof(char));
        // A COMDLG filter is a double-null-terminated multi-string; an LPWStr marshaller would
        // stop at the first embedded null, so build it by hand.
        nint filter = Marshal.StringToHGlobalUni("All files\0*.*\0\0");
        try
        {
            for (int i = 0; i < bufChars; i++) Marshal.WriteInt16(buffer, i * 2, 0);

            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = ownerHwnd,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = bufChars,
                lpstrTitle = title,
                Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER | OFN_DONTADDTORECENT,
            };

            return GetOpenFileNameW(ref ofn)
                ? Marshal.PtrToStringUni(buffer)
                : null;   // cancelled or an error; CommDlgExtendedError() has the code
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(filter);
        }
    }

    /// <summary>The classic Win32 save-file dialog. Returns the chosen path (extension appended if
    /// missing) or null if cancelled.</summary>
    public static string? SaveFile(nint ownerHwnd, string suggestedName, string defExt, string title = "Save")
    {
        const int bufChars = 4096;
        nint buffer = Marshal.AllocHGlobal(bufChars * sizeof(char));
        nint filter = Marshal.StringToHGlobalUni($"{defExt.ToUpperInvariant()} file\0*.{defExt}\0All files\0*.*\0\0");
        try
        {
            byte[] seed = System.Text.Encoding.Unicode.GetBytes(suggestedName + "\0");
            Marshal.Copy(seed, 0, buffer, Math.Min(seed.Length, bufChars * 2));

            var ofn = new OPENFILENAME
            {
                lStructSize = Marshal.SizeOf<OPENFILENAME>(),
                hwndOwner = ownerHwnd,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = buffer,
                nMaxFile = bufChars,
                lpstrTitle = title,
                lpstrDefExt = defExt,
                Flags = OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER | OFN_DONTADDTORECENT | OFN_OVERWRITEPROMPT,
            };
            return GetSaveFileNameW(ref ofn) ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(filter);
        }
    }

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_EXPLORER = 0x00080000;
    private const int OFN_DONTADDTORECENT = 0x02000000;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public nint lpstrFilter;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;
        public int nMaxFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int flagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileNameW(ref OPENFILENAME ofn);
}
