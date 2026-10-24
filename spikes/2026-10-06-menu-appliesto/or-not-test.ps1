# Follow-up: do OR and NOT work in AppliesTo? GistDrive's folder entries need "a folder directly in
# Secret or Public, but not _failed". Registers Directory entries with those conditions, asks the
# shell which folders get them, then removes them.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Join-Path $here 'root'
$secret = Join-Path $root 'Secret'
$public = Join-Path $root 'Public'
$folders = [ordered]@{
    'secret gist folder' = Join-Path $secret 'multi file gist'
    'public gist folder' = Join-Path $public 'another gist'
    'secret _failed'     = Join-Path $secret '_failed'
    'outside folder'     = Join-Path $here 'outside\some folder'
}
foreach ($d in $folders.Values) { New-Item -ItemType Directory -Force -Path $d | Out-Null }

$base = 'HKCU:\Software\Classes\Directory\shell'
$conditions = [ordered]@{
    'GDSN' = "System.ItemFolderPathDisplay:=""$secret"" AND NOT System.ItemNameDisplay:=""_failed"""
    'GDSO' = "System.ItemFolderPathDisplay:=""$secret"" OR System.ItemFolderPathDisplay:=""$public"""
    'GDSB' = "(System.ItemFolderPathDisplay:=""$secret"" OR System.ItemFolderPathDisplay:=""$public"") AND NOT System.ItemNameDisplay:=""_failed"""
}
try {
    foreach ($k in $conditions.Keys) {
        New-Item -Path "$base\$k\command" -Force | Out-Null
        Set-ItemProperty -Path "$base\$k" -Name 'MUIVerb' -Value "Spike $k"
        Set-ItemProperty -Path "$base\$k" -Name 'AppliesTo' -Value $conditions[$k]
        Set-ItemProperty -Path "$base\$k\command" -Name '(default)' -Value 'cmd.exe /c exit'
    }
    foreach ($k in $conditions.Keys) { "  $k  $($conditions[$k])" }
    $shell = New-Object -ComObject Shell.Application
    foreach ($label in $folders.Keys) {
        $path = $folders[$label]
        $item = $shell.NameSpace((Split-Path $path)).ParseName((Split-Path $path -Leaf))
        '  {0,-19} {1}' -f $label, (@($item.Verbs() | ForEach-Object { $_.Name } | Where-Object { $_ -like 'Spike*' }) -join ', ')
    }
}
finally {
    foreach ($k in $conditions.Keys) { Remove-Item -Path "$base\$k" -Recurse -Force -ErrorAction SilentlyContinue }
    'cleanup done'
}
