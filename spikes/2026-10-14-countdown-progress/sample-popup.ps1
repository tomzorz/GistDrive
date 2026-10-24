# Starts the countdown probe and reads the notification popup through UI Automation every
# half second: does the progress bar on screen follow the data-bound updates?
param([string]$Probe, [int]$Seconds = 13)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]

# The popup's name is localized ("New notification" in English), so find it by window class
# and by the process that draws notifications.
$hosts = @(Get-Process -Name ShellExperienceHost -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
"notification hosts: $($hosts -join ', ')"
function Find-Popup {
    $windows = $A::RootElement.FindAll($scope::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, 'Windows.UI.Core.CoreWindow')))
    foreach ($w in $windows) { if ($hosts -contains $w.Current.ProcessId) { return $w } }
    return $null
}

$process = Start-Process -FilePath $Probe -PassThru -NoNewWindow
$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    $stamp = '{0:HH:mm:ss.fff}' -f (Get-Date)
    $popup = Find-Popup
    if ($null -eq $popup) {
        "$stamp no popup"
    }
    else {
        $bar = $popup.FindFirst($scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::ProgressBar)))
        if ($null -eq $bar) {
            "$stamp popup without a progress bar"
        }
        else {
            $value = 'n/a'
            try { $value = $bar.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value } catch { }
            $texts = @($popup.FindAll($scope::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name }) -join ' | '
            "$stamp popup '$($popup.Current.Name)': bar value $value; texts: $texts"
        }
    }
    Start-Sleep -Milliseconds 500
}
if (-not $process.HasExited) { $process.WaitForExit(5000) | Out-Null }
