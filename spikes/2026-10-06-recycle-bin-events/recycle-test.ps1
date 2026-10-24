# Spike: does a FileSystemWatcher see a gist shortcut go to the Recycle Bin and come back?
# Deletes the way Explorer does (SHFileOperation with undo), restores through the Recycle Bin's
# own "undelete" verb, and prints every watcher event in order.
param([string[]]$Bases)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName Microsoft.VisualBasic
$shell = New-Object -ComObject Shell.Application

function Test-RoundTrip([string]$base) {
    $folder = Join-Path $base 'Secret'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $path = Join-Path $folder 'spike-notes.md.url'
    if (Test-Path $path) { Remove-Item $path -Force }
    [IO.File]::WriteAllText($path, "[InternetShortcut]`r`nURL=https://gist.github.com/example/0000000`r`n")

    $fsw = New-Object IO.FileSystemWatcher $folder
    $fsw.NotifyFilter = [IO.NotifyFilters]'FileName, DirectoryName, LastWrite, Size'
    $ids = @()
    foreach ($ev in 'Created', 'Deleted', 'Changed', 'Renamed') {
        $id = "fsw_${ev}_$([guid]::NewGuid().ToString('N'))"
        Register-ObjectEvent -InputObject $fsw -EventName $ev -SourceIdentifier $id | Out-Null
        $ids += $id
    }
    $fsw.EnableRaisingEvents = $true
    Start-Sleep -Milliseconds 500

    "== $folder"
    [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile($path,
        [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,
        [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin)
    Start-Sleep -Seconds 2
    "exists after delete: $(Test-Path $path)"

    $match = $null
    foreach ($i in $shell.NameSpace(10).Items()) {
        $from = $i.ExtendedProperty('System.Recycle.DeletedFrom')
        if ($from -eq $folder -and $i.Name -like 'spike-notes*') { $match = $i }
    }
    if ($null -eq $match) { 'not found in the Recycle Bin' }
    else {
        "in the Recycle Bin as: $($match.Path)"
        'verbs: ' + (($match.Verbs() | ForEach-Object { $_.Name }) -join ' | ')
        $match.InvokeVerb('undelete')
        Start-Sleep -Seconds 2
        "exists after restore: $(Test-Path $path)"
    }

    $fsw.EnableRaisingEvents = $false
    $events = foreach ($id in $ids) { Get-Event -SourceIdentifier $id -ErrorAction SilentlyContinue }
    'events:'
    $events | Sort-Object EventIdentifier | ForEach-Object {
        $a = $_.SourceEventArgs
        $old = if ($a -is [IO.RenamedEventArgs]) { " (old name $($a.OldName))" } else { '' }
        '  {0:HH:mm:ss.fff} {1} {2}{3}' -f $_.TimeGenerated, $a.ChangeType, $a.Name, $old
    }
    foreach ($id in $ids) {
        Unregister-Event -SourceIdentifier $id
        Remove-Event -SourceIdentifier $id -ErrorAction SilentlyContinue
    }
    $fsw.Dispose()
    if (Test-Path $path) { Remove-Item $path -Force }
}

foreach ($b in $Bases) { Test-RoundTrip $b }
