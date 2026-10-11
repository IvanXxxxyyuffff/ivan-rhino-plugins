Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32770')
$dialogs = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
foreach ($d in $dialogs) {
  Write-Output ("DIALOG: " + $d.Current.Name + " | " + $d.Current.BoundingRectangle.ToString())
  $btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
  $btns = $d.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
  foreach ($b in $btns) {
    Write-Output ("  BTN: '" + $b.Current.Name + "'")
    if ($b.Current.Name -match '确定|OK|是|Yes') {
      $inv = $null
      if ($b.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); Write-Output "  -> CLICKED" }
    }
  }
}
'dialog scan done'