# Lists every top-level UI Automation element before and during a notification, to see whether
# the popup appears at all and what it is called.
param([string]$Probe)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
function Snapshot {
    @($A::RootElement.FindAll($scope::Children, [System.Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { "{0} | {1} | pid {2}" -f $_.Current.ClassName, $_.Current.Name, $_.Current.ProcessId })
}
$before = Snapshot
$process = Start-Process -FilePath $Probe -PassThru -NoNewWindow
Start-Sleep -Milliseconds 2500
$during = Snapshot
'new while the notification is up:'
$during | Where-Object { $before -notcontains $_ } | ForEach-Object { "  $_" }
'gone while the notification is up:'
$before | Where-Object { $during -notcontains $_ } | ForEach-Object { "  $_" }
$process.WaitForExit(15000) | Out-Null
