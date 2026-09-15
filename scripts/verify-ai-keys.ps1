# TEST HARNESS ONLY - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Registers an AI key through the real settings window and then checks the claim the
# privacy document makes about it:
#
#   1. the window accepts the key and reports it as registered
#   2. the key is NOT readable in the file on disk (it is encrypted, not just hidden)
#   3. the key is NOT in save.json or ai.json
#   4. "clear all keys" really removes it
#
# A throwaway fake key is used. Never run this with a real one.

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$dir = "$env:APPDATA\PortKillerPet"
$keyFile = "$dir\keys.dat"
$fake = "FAKE-KEY-FOR-TESTING-0123456789"
$out = @()
$proc = $null
$backedUp = $false

function ById($root, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $root.FindFirst($Scope::Descendants, $cond)
}

function Find-SmallWindow([int]$processId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $wins = $AE::RootElement.FindAll($Scope::Children, $cond)
    foreach ($w in $wins) {
        $r = $w.Current.BoundingRectangle
        if ($r.Width -gt 200 -and $r.Width -lt 900) { return $w }
    }
    return $null
}

function Press($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function FileHasText([string]$path, [string]$needle) {
    if (-not (Test-Path $path)) { return $false }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    foreach ($enc in @([System.Text.Encoding]::UTF8, [System.Text.Encoding]::Unicode)) {
        if ($enc.GetString($bytes) -like "*$needle*") { return $true }
    }
    return $false
}

try {
    Get-Process PortKillerPet -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 1
    if (Test-Path $keyFile) { Rename-Item $keyFile "$keyFile.testbackup" -Force; $backedUp = $true }

    $proc = Start-Process -FilePath $exe -ArgumentList "--open-ai-settings" -PassThru
    Start-Sleep -Seconds 6

    $win = Find-SmallWindow $proc.Id
    if ($null -eq $win) { throw "settings window not found" }
    $out += "settings window : $([int]$win.Current.BoundingRectangle.Width)x$([int]$win.Current.BoundingRectangle.Height)"
    $out += "state before    : '$((ById $win 'GeminiState').Current.Name)'"

    # a PasswordBox has no ValuePattern on purpose (that is the point of one), so the key
    # has to be typed - which is also what a person does
    $box = ById $win "GeminiKey"
    if ($null -eq $box) { throw "gemini key box not found" }
    $box.SetFocus()
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait($fake)
    Start-Sleep -Milliseconds 400

    Press (ById $win "SaveButton")
    Start-Sleep -Seconds 2

    $out += "save state      : '$((ById $win 'SaveState').Current.Name)'"
    $out += "state after     : '$((ById $win 'GeminiState').Current.Name)'"

    $registered = (ById $win "GeminiState").Current.Name -notlike "*not*" -and
                  (ById $win "SaveState").Current.Name.Length -gt 0
    $fileExists = Test-Path $keyFile
    $keyInKeyFile = FileHasText $keyFile $fake
    $keyInSave = (FileHasText "$dir\save.json" $fake) -or (FileHasText "$dir\ai.json" $fake)

    $out += ""
    $out += "keys.dat exists            : $fileExists"
    $out += "key readable in keys.dat   : $keyInKeyFile   (must be False - it is encrypted)"
    $out += "key found in save/ai json  : $keyInSave   (must be False)"

    # --- clearing really clears ---
    Press (ById $win "ClearKeysButton")
    Start-Sleep -Milliseconds 700
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")   # confirm the message box
    Start-Sleep -Seconds 2
    $clearedState = (ById $win "GeminiState").Current.Name
    $fileGone = -not (Test-Path $keyFile)
    $out += "state after clear          : '$clearedState'"
    $out += "keys.dat removed by clear  : $fileGone"

    $out += ""
    $ok = $registered -and $fileExists -and (-not $keyInKeyFile) -and (-not $keyInSave) -and $fileGone
    if ($ok) { $out += "RESULT: PASS - key registered, stored encrypted, kept out of the save file, and removable" }
    else { $out += "RESULT: FAIL" }
}
catch { $out += "ERROR: $_" }
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
    if (Test-Path $keyFile) { Remove-Item $keyFile -Force }
    if ($backedUp -and (Test-Path "$keyFile.testbackup")) {
        Rename-Item "$keyFile.testbackup" $keyFile -Force
        $out += "original keys.dat restored"
    }
    $out += "app terminated"
}

$out | ForEach-Object { Write-Output $_ }
