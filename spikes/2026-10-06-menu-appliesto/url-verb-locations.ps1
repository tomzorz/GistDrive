# Diagnostic: which HKCU registry locations put an unconditional menu entry on a .url file?
# Registers one entry per location, lists every verb the shell reports for a .url file and a .txt
# file, then removes everything it created. Keys are written with reg.exe because the location
# "*" is a literal key name, and PowerShell's New-Item would treat it as a wildcard.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$dir = Join-Path $here 'outside'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$url = Join-Path $dir 'o.url'
$txt = Join-Path $dir 't.txt'
[IO.File]::WriteAllText($url, "[InternetShortcut]`r`nURL=https://gist.github.com/example/0000000`r`n")
[IO.File]::WriteAllText($txt, 'text')

$classes = 'HKCU\Software\Classes'
$locations = [ordered]@{
    'GDSa' = '*'
    'GDSb' = 'AllFilesystemObjects'
    'GDSc' = 'IE.AssocFile.URL'
    'GDSd' = 'SystemFileAssociations\.url'
    'GDSe' = 'InternetShortcut'
    'GDSf' = '.url'
}
$created = @()
foreach ($loc in $locations.Values) {
    foreach ($k in "$classes\$loc", "$classes\$loc\shell") {
        if (-not (Test-Path -LiteralPath "Registry::HKEY_CURRENT_USER\$($k.Substring(5))")) { $created += $k }
    }
}
try {
    foreach ($k in $locations.Keys) {
        $vk = "$classes\$($locations[$k])\shell\$k"
        reg add "$vk\command" /ve /t REG_SZ /d 'cmd.exe /c exit' /f | Out-Null
        reg add $vk /v MUIVerb /t REG_SZ /d "Spike $k $($locations[$k])" /f | Out-Null
    }
    $shell = New-Object -ComObject Shell.Application
    foreach ($p in $url, $txt) {
        $item = $shell.NameSpace($dir).ParseName((Split-Path $p -Leaf))
        "all verbs for $(Split-Path $p -Leaf): " + (@($item.Verbs() | ForEach-Object { $_.Name }) -join ' | ')
    }
}
finally {
    foreach ($k in $locations.Keys) { reg delete "$classes\$($locations[$k])\shell\$k" /f 2>&1 | Out-Null }
    [array]::Reverse($created)
    foreach ($k in $created) { reg delete $k /f 2>&1 | Out-Null }
    'cleanup done, keys created by this run removed: ' + ($created -join ', ')
}
