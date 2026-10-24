# Spike: does an extra INI section in a .url file survive the ways Windows rewrites .url files?
# Rewriters tried: the shell's own InternetShortcut object (what the Properties dialog uses),
# WScript.Shell, a shell copy, and a shell rename.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$work = Join-Path $here 'work'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Spike {
  [ComImport, Guid("FBF23B40-E3F0-101B-8488-00AA003E56F8")]
  public class InternetShortcut {}

  [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("CABB0DA0-DA57-11CF-9974-0020AFD79762")]
  public interface IUniformResourceLocatorW {
    void SetURL([MarshalAs(UnmanagedType.LPWStr)] string pcszURL, int dwInFlags);
    void GetURL([MarshalAs(UnmanagedType.LPWStr)] out string ppszURL);
    void InvokeCommand(IntPtr purlici);
  }

  [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
  public interface IShellLinkW {
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
    void Resolve(IntPtr hwnd, int fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
  }

  public static class Url {
    public static string GetUrl(string path) {
      object o = new InternetShortcut();
      try {
        ((IPersistFile)o).Load(path, 0);
        string u;
        ((IUniformResourceLocatorW)o).GetURL(out u);
        return u;
      } finally { Marshal.ReleaseComObject(o); }
    }
    public static void SetUrl(string path, string url) {
      object o = new InternetShortcut();
      try {
        ((IPersistFile)o).Load(path, 2);
        ((IUniformResourceLocatorW)o).SetURL(url, 0);
        ((IPersistFile)o).Save(path, true);
      } finally { Marshal.ReleaseComObject(o); }
    }
    public static void SetIcon(string path, string iconPath, int index) {
      object o = new InternetShortcut();
      try {
        ((IPersistFile)o).Load(path, 2);
        ((IShellLinkW)o).SetIconLocation(iconPath, index);
        ((IPersistFile)o).Save(path, true);
      } finally { Marshal.ReleaseComObject(o); }
    }
  }
}
'@

$original = "[InternetShortcut]`r`nURL=https://gist.github.com/example/1111111`r`n[GistDrive]`r`nSourcePath=D:\somewhere\README.md`r`nVersion=1`r`n"
$path = Join-Path $work 'notes.md.url'
function Reset-File { [IO.File]::WriteAllText($path, $original) }
function Show-File([string]$label, [string]$p) {
    $text = [IO.File]::ReadAllText($p)
    "--- $label (custom section kept: $($text -match '\[GistDrive\]\s+SourcePath=D:\\somewhere\\README\.md'))"
    $text.TrimEnd()
}

Reset-File
Show-File 'original' $path
"InternetShortcut reads URL as: $([Spike.Url]::GetUrl($path))"

Reset-File
[Spike.Url]::SetUrl($path, 'https://gist.github.com/example/2222222')
Show-File 'InternetShortcut: SetURL + Save' $path

Reset-File
[Spike.Url]::SetIcon($path, '%SystemRoot%\System32\imageres.dll', 5)
Show-File 'InternetShortcut: SetIconLocation + Save' $path

Reset-File
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($path)
$sc.TargetPath = 'https://gist.github.com/example/3333333'
$sc.Save()
Show-File 'WScript.Shell: TargetPath + Save' $path

Reset-File
$shell = New-Object -ComObject Shell.Application
$dest = Join-Path $work 'copy'
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$shell.NameSpace($dest).CopyHere($path, 4 + 16 + 1024)
Start-Sleep -Seconds 2
Show-File 'Shell.Application: CopyHere' (Join-Path $dest 'notes.md.url')

# Rename through the shell with the name File Explorer shows (extension hidden).
$item = $shell.NameSpace($work).ParseName('notes.md.url')
"display name before rename: $($item.Name)"
$item.Name = 'ideas.md'
Start-Sleep -Seconds 1
'files after shell rename to "ideas.md": ' + ((Get-ChildItem $work -File | ForEach-Object { $_.Name }) -join ', ')
$renamed = Join-Path $work 'ideas.md.url'
if (Test-Path $renamed) { Show-File 'after shell rename' $renamed }
