# Spike: can a registry menu entry be limited to one folder with AppliesTo?
# File entries go under HKCU\Software\Classes\SystemFileAssociations\.url\shell, because on this
# machine .url files belong to the ProgID IE.AssocFile.URL (per-user UserChoice), so entries under
# InternetShortcut\shell never apply. Folder entries (for gist folders) go under Directory\shell.
# Each entry gets a different AQS condition; the shell reports which entries each test item gets.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Join-Path $here 'root'
$secret = Join-Path $root 'Secret'
$public = Join-Path $root 'Public'
$outside = Join-Path $here 'outside'
$gistFolder = Join-Path $secret 'multi file gist'
$outsideFolder = Join-Path $outside 'some folder'
foreach ($d in $secret, $public, $outside, $gistFolder, $outsideFolder) { New-Item -ItemType Directory -Force -Path $d | Out-Null }
$body = "[InternetShortcut]`r`nURL=https://gist.github.com/example/0000000`r`n"
$files = [ordered]@{
    'secret file'      = Join-Path $secret 's.md.url'
    'public file'      = Join-Path $public 'p.md.url'
    'outside file'     = Join-Path $outside 'o.url'
    'gist folder file' = Join-Path $gistFolder 'g.md.url'
}
foreach ($f in $files.Values) { [IO.File]::WriteAllText($f, $body) }
$folders = [ordered]@{
    'gist folder'    = $gistFolder
    'outside folder' = $outsideFolder
}

$choice = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.url\UserChoice' -ErrorAction SilentlyContinue).ProgId
".url UserChoice ProgId: $choice"

# url-verb-locations.ps1 showed that only the active ProgID and AllFilesystemObjects reach .url files.
$fileBase = 'HKCU:\Software\Classes\AllFilesystemObjects'
$dirBase = 'HKCU:\Software\Classes\Directory'
$created = @()
foreach ($k in $fileBase, "$fileBase\shell", "$dirBase\shell") { if (-not (Test-Path $k)) { $created += $k } }

$fileConditions = [ordered]@{
    'GDS0' = $null
    'GDS1' = "System.ItemFolderPathDisplay:=""$secret"""
    'GDS2' = "System.ItemFolderPathDisplay:~<""$root"""
    'GDS3' = "System.ItemPathDisplay:~<""$secret\"""
    'GDS4' = "System.ParsingPath:~<""$root\"""
    'GDS5' = "System.ItemFolderPathDisplay:=""$secret"" AND System.FileExtension:="".url"""
    'GDS6' = "System.ItemPathDisplay:~<""$root\"" AND System.FileExtension:="".url"""
}
$dirConditions = [ordered]@{
    'GDSD0' = $null
    'GDSD1' = "System.ItemFolderPathDisplay:=""$secret"""
    'GDSD2' = "System.ItemPathDisplay:~<""$root\"""
}

function Add-Entry([string]$base, [string]$k, [string]$condition) {
    $vk = "$base\shell\$k"
    New-Item -Path "$vk\command" -Force | Out-Null
    Set-ItemProperty -Path $vk -Name 'MUIVerb' -Value "Spike $k"
    if ($condition) { Set-ItemProperty -Path $vk -Name 'AppliesTo' -Value $condition }
    Set-ItemProperty -Path "$vk\command" -Name '(default)' -Value 'cmd.exe /c exit'
}

try {
    foreach ($k in $fileConditions.Keys) { Add-Entry $fileBase $k $fileConditions[$k] }
    foreach ($k in $dirConditions.Keys) { Add-Entry $dirBase $k $dirConditions[$k] }
    'conditions:'
    foreach ($k in $fileConditions.Keys) { "  $k  $($fileConditions[$k])" }
    foreach ($k in $dirConditions.Keys) { "  $k  $($dirConditions[$k])" }

    $shell = New-Object -ComObject Shell.Application
    function Get-SpikeEntries([string]$path) {
        $item = $shell.NameSpace((Split-Path $path)).ParseName((Split-Path $path -Leaf))
        @($item.Verbs() | ForEach-Object { $_.Name } | Where-Object { $_ -like 'Spike*' }) -join ', '
    }
    'entries per item:'
    foreach ($label in $files.Keys) { '  {0,-17} {1}' -f $label, (Get-SpikeEntries $files[$label]) }
    foreach ($label in $folders.Keys) { '  {0,-17} {1}' -f $label, (Get-SpikeEntries $folders[$label]) }
}
finally {
    foreach ($k in $fileConditions.Keys) { Remove-Item -Path "$fileBase\shell\$k" -Recurse -Force -ErrorAction SilentlyContinue }
    foreach ($k in $dirConditions.Keys) { Remove-Item -Path "$dirBase\shell\$k" -Recurse -Force -ErrorAction SilentlyContinue }
    [array]::Reverse($created)
    foreach ($k in $created) { Remove-Item -Path $k -Recurse -Force -ErrorAction SilentlyContinue }
    'cleanup done, keys created by this run removed: ' + ($created -join ', ')
}
