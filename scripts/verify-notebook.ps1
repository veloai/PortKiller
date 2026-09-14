# TEST HARNESS ONLY - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Opens the notebook via the --open-notebook smoke switch. The tray menu is not reachable
# automatically because Windows 11 hides new tray icons in the overflow flyout.
#
# Elements are located by AutomationId (WPF maps x:Name to it), never by display text,
# so this script stays free of non-ASCII characters.

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$out = @()

function Find-NotebookWindow([int]$processId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $wins = $AE::RootElement.FindAll($Scope::Children, $cond)
    foreach ($w in $wins) {
        $r = $w.Current.BoundingRectangle
        # the pet window covers the screen; the notebook is the small one
        if ($r.Width -gt 100 -and $r.Width -lt 900) { return $w }
    }
    return $null
}

function ById($root, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $root.FindFirst($Scope::Descendants, $cond)
}

function SetText($el, [string]$value) {
    $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Press($el) {
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function CountButtons($root) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    return $root.FindAll($Scope::Descendants, $cond).Count
}

function FirstTodo($root) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
    return $root.FindAll($Scope::Descendants, $cond).Item(0)
}

function FirstCheck($root) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::CheckBox)
    return $root.FindAll($Scope::Descendants, $cond).Item(0)
}

$proc = Start-Process -FilePath $exe -ArgumentList "--open-notebook" -PassThru
try {
    Start-Sleep -Seconds 4

    $win = Find-NotebookWindow $proc.Id
    if ($null -eq $win) { throw "notebook window not found" }
    $r = $win.Current.BoundingRectangle
    $out += "notebook window : $([int]$r.Width)x$([int]$r.Height) at $([int]$r.X),$([int]$r.Y)"

    # --- 1. first todo: type text, then tick the checkbox ---
    SetText (FirstTodo $win) "code review"
    Start-Sleep -Milliseconds 500

    $check = FirstCheck $win
    $tp = $check.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $tp.Toggle()
    Start-Sleep -Milliseconds 600
    $toggle = "$($tp.Current.ToggleState)"
    $hint = (ById $win "TodoHint").Current.Name
    $out += "todo toggle state : $toggle"
    $out += "todo hint text    : $hint"

    # --- 2. add a reminder ---
    $before = CountButtons $win
    SetText (ById $win "ReminderText") "standup"
    SetText (ById $win "ReminderTime") "23:59"
    Start-Sleep -Milliseconds 300
    Press (ById $win "AddReminder")
    Start-Sleep -Milliseconds 800
    $after = CountButtons $win
    $out += "buttons before/after add : $before / $after  (a delete button should appear)"

    # --- 2b. a bad time must be refused, not crash ---
    SetText (ById $win "ReminderText") "broken"
    SetText (ById $win "ReminderTime") "99:99"
    Press (ById $win "AddReminder")
    Start-Sleep -Milliseconds 600
    $afterBad = CountButtons $win
    $err = (ById $win "ReminderError").Current.Name
    $out += "buttons after bad time   : $afterBad  (should equal $after)"
    $out += "error message shown      : '$err'"

    # --- 3. pomodoro ---
    $clockBefore = (ById $win "PomodoroClock").Current.Name
    Press (ById $win "PomodoroButton")
    Start-Sleep -Milliseconds 900
    $clock1 = (ById $win "PomodoroClock").Current.Name
    $label = (ById $win "PomodoroButton").Current.Name
    Start-Sleep -Seconds 3
    $clock2 = (ById $win "PomodoroClock").Current.Name
    $out += "pomodoro clock : idle=$clockBefore  start=$clock1  +3s=$clock2"
    $out += "button label after start (non-ASCII shown as-is) : length=$($label.Length)"

    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, (New-Object System.Drawing.Size([int]$r.Width, [int]$r.Height)))
    $bmp.Save("$env:TEMP\pk-notebook.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()

    $todoOk = $toggle -eq "On"
    $addOk = $after -gt $before
    $badRefused = ($afterBad -eq $after) -and ($err.Length -gt 0)
    $clockOk = ($clock1 -ne "") -and ($clock2 -ne "") -and ($clock1 -ne $clock2)

    $out += ""
    $out += "todo checked        : $todoOk"
    $out += "reminder added      : $addOk"
    $out += "bad time refused    : $badRefused"
    $out += "clock counting down : $clockOk"
    $out += ""
    if ($todoOk -and $addOk -and $badRefused -and $clockOk) { $out += "RESULT: PASS" } else { $out += "RESULT: FAIL" }
}
catch { $out += "ERROR: $_" }
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
    $out += "app terminated"
}

$out | ForEach-Object { Write-Output $_ }
