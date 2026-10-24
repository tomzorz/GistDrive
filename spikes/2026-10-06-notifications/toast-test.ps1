# Spike: can an unpackaged process show a notification with buttons and a live countdown,
# without the Windows App SDK? Registers an AUMID in HKCU only (no Start menu shortcut), shows a
# toast with Cancel and Publish now, updates its progress bar through data binding, waits for a
# click through the in-process Activated event, then removes the toast and the registration.
#   -WaitSeconds  how long to wait for a click (run at the PC with a larger value to test clicks)
param([int]$WaitSeconds = 10)
$ErrorActionPreference = 'Stop'
$aumid = 'GistDrive.Spike'
$key = "HKCU:\Software\Classes\AppUserModelId\$aumid"

$null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
$null = [Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime]
$null = [Windows.UI.Notifications.NotificationData, Windows.UI.Notifications, ContentType = WindowsRuntime]
$null = [Windows.UI.Notifications.ToastActivatedEventArgs, Windows.UI.Notifications, ContentType = WindowsRuntime]
$null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]

'Windows App SDK runtimes installed: ' + ((Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime*' |
    ForEach-Object { "$($_.Name) $($_.Version)" } | Sort-Object -Unique) -join '; ')

$keyExisted = Test-Path $key
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayName -Value 'GistDrive Spike' -PropertyType String -Force | Out-Null
$ids = 'tActivated', 'tDismissed', 'tFailed'
try {
    $xmlText = @'
<toast scenario="reminder">
  <visual>
    <binding template="ToastGeneric">
      <text>Publishing notes.md</text>
      <text>GistDrive spike: a public upload waiting in the undo window</text>
      <progress value="{progressValue}" valueStringOverride="{progressValueString}" status="{progressStatus}"/>
    </binding>
  </visual>
  <actions>
    <action content="Cancel" arguments="action=cancel&amp;id=42" activationType="foreground"/>
    <action content="Publish now" arguments="action=now&amp;id=42" activationType="foreground"/>
  </actions>
</toast>
'@
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml($xmlText)
    $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
    $toast.Tag = 'spike'
    $toast.Group = 'gistdrive'
    # PowerShell 5.1 cannot index into the projected WinRT map, so build the data from a dictionary.
    function New-Data([hashtable]$values, [uint32]$sequence) {
        $dict = New-Object 'System.Collections.Generic.Dictionary[string,string]'
        foreach ($k in $values.Keys) { $dict.Add($k, [string]$values[$k]) }
        New-Object Windows.UI.Notifications.NotificationData -ArgumentList $dict, $sequence
    }
    $toast.Data = New-Data @{ progressValue = '1'; progressValueString = '30 s'; progressStatus = 'Waiting to publish' } 1

    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid)
    "notifier setting: $($notifier.Setting)"
    Register-ObjectEvent -InputObject $toast -EventName Activated -SourceIdentifier tActivated | Out-Null
    Register-ObjectEvent -InputObject $toast -EventName Dismissed -SourceIdentifier tDismissed | Out-Null
    Register-ObjectEvent -InputObject $toast -EventName Failed -SourceIdentifier tFailed | Out-Null
    $notifier.Show($toast)
    Start-Sleep -Milliseconds 800

    $history = [Windows.UI.Notifications.ToastNotificationManager]::History
    $shown = @($history.GetHistory($aumid))
    "history after Show: $($shown.Count) notification(s), tags: $(($shown | ForEach-Object { $_.Tag }) -join ',')"

    for ($s = 2; $s -le 4; $s++) {
        $remaining = 30 - ($s - 1) * 10
        $d = New-Data @{ progressValue = ($remaining / 30); progressValueString = "$remaining s" } $s
        "update to $remaining s: $($notifier.Update($d, 'spike', 'gistdrive'))"
        Start-Sleep -Milliseconds 500
    }

    "waiting up to $WaitSeconds s for a click"
    $null = Wait-Event -Timeout $WaitSeconds
    $events = foreach ($id in $ids) { Get-Event -SourceIdentifier $id -ErrorAction SilentlyContinue }
    if (@($events).Count -eq 0) { 'no event arrived' }
    foreach ($e in $events) {
        $payload = $e.SourceArgs[1]
        switch ($e.SourceIdentifier) {
            'tActivated' { "Activated with arguments: $(([Windows.UI.Notifications.ToastActivatedEventArgs]$payload).Arguments)" }
            'tDismissed' { "Dismissed, reason: $($payload.Reason)" }
            'tFailed' { "Failed, error code: $($payload.ErrorCode)" }
        }
    }

    $history.Remove('spike', 'gistdrive', $aumid)
    "history after Remove: $(@($history.GetHistory($aumid)).Count) notification(s)"
}
finally {
    foreach ($id in $ids) {
        Unregister-Event -SourceIdentifier $id -ErrorAction SilentlyContinue
        Remove-Event -SourceIdentifier $id -ErrorAction SilentlyContinue
    }
    if (-not $keyExisted) { Remove-Item -Path $key -Recurse -Force }
    "AUMID registration removed: $(-not (Test-Path $key))"
}
