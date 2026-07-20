using System;
using System.Runtime.InteropServices;

namespace HmDesktopCalendar.Authentication;

public interface IRefreshTokenStore
{
    void Save(string token);
    string? Load();
    void Clear();
}

public sealed class WindowsCredentialTokenStore : IRefreshTokenStore
{
    private const string Target = "HmDesktopCalendar.RefreshToken";
    public void Save(string token)
    {
        IntPtr blob = Marshal.StringToCoTaskMemUni(token);
        try
        {
            var credential = new Credential { Type = 1, TargetName = Target,
                CredentialBlobSize = (uint)(token.Length * 2), CredentialBlob = blob,
                Persist = 2, UserName = Environment.UserName };
            if (!CredWrite(ref credential, 0)) throw new InvalidOperationException("Windows 자격 증명 저장에 실패했습니다.");
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }
    public string? Load()
    {
        if (!CredRead(Target, 1, 0, out IntPtr pointer)) return null;
        try { var c = Marshal.PtrToStructure<Credential>(pointer); return c.CredentialBlob == IntPtr.Zero ? null : Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2); }
        finally { CredFree(pointer); }
    }
    public void Clear() => CredDelete(Target, 1, 0);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Credential
    { public uint Flags, Type; public string TargetName; public string? Comment; public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten; public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist, AttributeCount; public IntPtr Attributes; public string? TargetAlias; public string UserName; }
    [DllImport("advapi32", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CredWrite(ref Credential credential,uint flags);
    [DllImport("advapi32", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32", CharSet=CharSet.Unicode)] private static extern bool CredDelete(string target,uint type,uint flags);
    [DllImport("advapi32")] private static extern void CredFree(IntPtr buffer);
}
