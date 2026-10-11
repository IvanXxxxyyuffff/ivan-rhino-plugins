Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
# 枚举所有 #32770 对话框，名字含 XNURBS/确认
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, '#32770')
$dialogs = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
foreach ($d in $dialogs) {
  $n = $d.Current.Name
  Write-Output ("DIALOG: '$n'")
  if ($n -match 'XNURBS|xn-runall|对照|确认') {
    $btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $btns = $d.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    foreach ($b in $btns) {
      Write-Output ("  BTN: '" + $b.Current.Name + "'")
      if ($b.Current.Name -match '确定|OK|是') {
        $inv = $null
        if ($b.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) { $inv.Invoke(); Write-Output '  -> CLICKED OK' }
      }
    }
  }
}
'scan done'