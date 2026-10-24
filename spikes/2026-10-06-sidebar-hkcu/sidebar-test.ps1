# Spike: do the HKCU steps of "Integrate a Cloud Storage Provider" alone give a sidebar entry?
# Writes only HKCU values (no HKLM SyncRootManager), checks the shell resolves the entry, opens an
# Explorer window and looks for the entry in its navigation pane through UI Automation.
#   -Keep        leave the entry in place afterwards (to look at it by hand)
#   -RemoveOnly  just remove a kept entry
param([switch]$Keep, [switch]$RemoveOnly)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Join-Path $here 'root'
$clsid = '{6A0E3D52-1B7C-4E0F-9C2A-5D8B47F31E66}'
$name = 'GistDrive Spike'
$clsKey = "HKCU\Software\Classes\CLSID\$clsid"
$nsKey = "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\$clsid"
$hideKey = 'HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'

Add-Type -Namespace Spike -Name Native -MemberDefinition @'
[DllImport("shell32.dll")] public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
'@

function Remove-Entry {
    reg delete $clsKey /f 2>&1 | Out-Null
    reg delete $nsKey /f 2>&1 | Out-Null
    reg delete $hideKey /v $clsid /f 2>&1 | Out-Null
    [Spike.Native]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
}
if ($RemoveOnly) { Remove-Entry; 'removed'; return }

foreach ($d in 'Public', 'Secret') { New-Item -ItemType Directory -Force -Path (Join-Path $root $d) | Out-Null }

try {
    # The twelve steps from the Microsoft page, HKCU only, in order.
    reg add $clsKey /ve /t REG_SZ /d $name /f | Out-Null
    reg add "$clsKey\DefaultIcon" /ve /t REG_EXPAND_SZ /d '%SystemRoot%\system32\imageres.dll,-1043' /f | Out-Null
    reg add $clsKey /v System.IsPinnedToNameSpaceTree /t REG_DWORD /d 0x1 /f | Out-Null
    reg add $clsKey /v SortOrderIndex /t REG_DWORD /d 0x42 /f | Out-Null
    reg add "$clsKey\InProcServer32" /ve /t REG_EXPAND_SZ /d '%systemroot%\system32\shell32.dll' /f | Out-Null
    reg add "$clsKey\Instance" /v CLSID /t REG_SZ /d '{0E5AAE11-A475-4c5b-AB00-C66DE400274E}' /f | Out-Null
    reg add "$clsKey\Instance\InitPropertyBag" /v Attributes /t REG_DWORD /d 0x11 /f | Out-Null
    reg add "$clsKey\Instance\InitPropertyBag" /v TargetFolderPath /t REG_EXPAND_SZ /d $root /f | Out-Null
    reg add "$clsKey\ShellFolder" /v FolderValueFlags /t REG_DWORD /d 0x28 /f | Out-Null
    reg add "$clsKey\ShellFolder" /v Attributes /t REG_DWORD /d 0xF080004D /f | Out-Null
    reg add $nsKey /ve /t REG_SZ /d $name /f | Out-Null
    reg add $hideKey /v $clsid /t REG_DWORD /d 0x1 /f | Out-Null
    [Spike.Native]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    'registered (HKCU only)'

    $shell = New-Object -ComObject Shell.Application
    $ns = $shell.NameSpace("shell:::$clsid")
    if ($null -eq $ns) { 'shell:::{clsid} did not resolve' }
    else {
        "shell:::{clsid} resolves, title '$($ns.Title)', path '$($ns.Self.Path)'"
        'items: ' + (($ns.Items() | ForEach-Object { $_.Name }) -join ', ')
    }

    # The navigation pane lists the desktop folder enumerated with SHCONTF_NAVIGATION_ENUM and shows
    # the items whose System.IsPinnedToNameSpaceTree is true. Ask the shell the same question.
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace Spike {
  [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E6-0000-0000-C000-000000000046")]
  public interface IShellFolder {
    void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attributes);
    [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IEnumIDList list);
  }
  [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F2-0000-0000-C000-000000000046")]
  public interface IEnumIDList {
    [PreserveSig] int Next(uint celt, out IntPtr pidl, out uint fetched);
    [PreserveSig] int Skip(uint celt);
    [PreserveSig] int Reset();
    [PreserveSig] int Clone(out IEnumIDList list);
  }
  [StructLayout(LayoutKind.Sequential)] public struct PROPERTYKEY { public Guid fmtid; public uint pid; }
  [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93")]
  public interface IShellItem2 {
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    void GetParent(out IntPtr ppsi);
    void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    void GetAttributes(uint mask, out uint attributes);
    void Compare(IntPtr psi, uint hint, out int order);
    void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
    void GetPropertyStoreWithCreateObject(int flags, IntPtr create, ref Guid riid, out IntPtr ppv);
    void GetPropertyStoreForKeys(IntPtr keys, uint count, int flags, ref Guid riid, out IntPtr ppv);
    void GetPropertyDescriptionList(ref PROPERTYKEY keyType, ref Guid riid, out IntPtr ppv);
    void Update(IntPtr pbc);
    void GetProperty(ref PROPERTYKEY key, IntPtr propvar);
    void GetCLSID(ref PROPERTYKEY key, out Guid clsid);
    void GetFileTime(ref PROPERTYKEY key, out long ft);
    void GetInt32(ref PROPERTYKEY key, out int value);
    void GetString(ref PROPERTYKEY key, [MarshalAs(UnmanagedType.LPWStr)] out string value);
    void GetUInt32(ref PROPERTYKEY key, out uint value);
    void GetUInt64(ref PROPERTYKEY key, out ulong value);
    [PreserveSig] int GetBool(ref PROPERTYKEY key, out int value);
  }
  public static class NavPane {
    [DllImport("shell32.dll")] static extern int SHGetDesktopFolder(out IShellFolder folder);
    [DllImport("shell32.dll")] static extern int SHCreateItemWithParent(IntPtr parentPidl, IShellFolder parent, IntPtr pidl, ref Guid riid, out IShellItem2 item);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] static extern int PSGetPropertyKeyFromName(string name, out PROPERTYKEY key);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pv);
    public static string[] Roots() {
      IShellFolder desktop;
      SHGetDesktopFolder(out desktop);
      IEnumIDList list;
      var result = new List<string>();
      if (desktop.EnumObjects(IntPtr.Zero, 0x20 | 0x1000, out list) != 0 || list == null) return result.ToArray();
      PROPERTYKEY pinned;
      PSGetPropertyKeyFromName("System.IsPinnedToNameSpaceTree", out pinned);
      Guid iid = typeof(IShellItem2).GUID;
      IntPtr pidl;
      uint fetched;
      while (list.Next(1, out pidl, out fetched) == 0 && fetched == 1) {
        IShellItem2 item;
        SHCreateItemWithParent(IntPtr.Zero, desktop, pidl, ref iid, out item);
        string name, parsing;
        item.GetDisplayName(0, out name);
        item.GetDisplayName(0x80028000, out parsing);
        int flag;
        string pin = item.GetBool(ref pinned, out flag) == 0 ? (flag != 0).ToString() : "unset";
        result.Add(name + " | " + parsing + " | pinned " + pin);
        CoTaskMemFree(pidl);
      }
      return result.ToArray();
    }
  }
}
'@
    'desktop items in navigation enumeration:'
    [Spike.NavPane]::Roots() | ForEach-Object { "  $_" }

    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $A = [Windows.Automation.AutomationElement]
    $windowName = Split-Path $here -Leaf
    Start-Process explorer.exe -ArgumentList "/n,`"$here`""
    $window = $null
    for ($t = 0; $t -lt 30 -and $null -eq $window; $t++) {
        Start-Sleep -Milliseconds 500
        $cond = New-Object Windows.Automation.PropertyCondition($A::ClassNameProperty, 'CabinetWClass')
        foreach ($w in $A::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $cond)) {
            if ($w.Current.Name -like "$windowName*") { $window = $w }
        }
    }
    if ($null -eq $window) { 'Explorer window not found through UI Automation' }
    else {
        "Explorer window found: '$($window.Current.Name)'"
        $treeCond = New-Object Windows.Automation.OrCondition(
            (New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::Tree)),
            (New-Object Windows.Automation.PropertyCondition($A::ClassNameProperty, 'SysTreeView32')))
        $tree = $null
        for ($t = 0; $t -lt 20 -and $null -eq $tree; $t++) {
            Start-Sleep -Milliseconds 500
            $tree = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $treeCond)
        }
        if ($null -eq $tree) {
            'navigation pane tree not found after 10 s; window children:'
            $window.FindAll([Windows.Automation.TreeScope]::Children, [Windows.Automation.Condition]::TrueCondition) |
                ForEach-Object { "  $($_.Current.ControlType.ProgrammaticName) '$($_.Current.Name)' class=$($_.Current.ClassName)" }
        }
        else {
            $itemCond = New-Object Windows.Automation.PropertyCondition($A::ControlTypeProperty, [Windows.Automation.ControlType]::TreeItem)
            $items = @()
            for ($t = 0; $t -lt 20 -and $items.Count -eq 0; $t++) {
                Start-Sleep -Milliseconds 500
                $items = @($tree.FindAll([Windows.Automation.TreeScope]::Descendants, $itemCond))
            }
            "navigation pane: $($items.Count) tree items, first ones: " + (($items | Select-Object -First 25 | ForEach-Object { $_.Current.Name }) -join ' | ')
            "entry '$name' in the navigation pane: $(@($items | Where-Object { $_.Current.Name -eq $name }).Count -gt 0)"
        }
        $wp = $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
        $wp.Close()
    }
}
finally {
    if (-not $Keep) { Remove-Entry; 'entry removed' } else { 'entry kept; run with -RemoveOnly to remove it' }
}
