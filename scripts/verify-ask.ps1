# TEST HARNESS ONLY - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Drives the ask window the way a person does: type a prompt, press Enter, read the answer.
#
# Runs with --stub-ai, so no key is needed, nothing goes out to the network and nothing is
# billed. The stub replaces only the client object; the rest of the path is the real thing -
# keystroke -> Enter handler -> await -> text on screen -> prompt box cleared.
#
# The HTTP and JSON layer is covered separately by AiClientTests, which feeds both clients
# fake responses (401, 404, 429, timeouts, broken JSON) without touching the network either.
#
# Elements are found by AutomationId, never by display text - Windows PowerShell reads .ps1
# as ANSI, so a Korean literal in this file would corrupt the whole script.

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$AE = [System.Windows.Automation.AutomationElement]
$Scope = [System.Windows.Automation.TreeScope]
$exe = "C:\Monster\Repo\PortKiller\src\PortKiller.App\bin\Debug\net9.0-windows\PortKillerPet.exe"
$out = @()
$proc = $null

function ById($root, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $root.FindFirst($Scope::Descendants, $cond)
}

function Value($element) {
    if ($null -eq $element) { return "" }
    return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
}

function Find-SmallWindow([int]$processId) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $processId)
    $wins = $AE::RootElement.FindAll($Scope::Children, $cond)
    foreach ($w in $wins) {
        $r = $w.Current.BoundingRectangle
        # the pet window covers the screen; the ask window is the small one
        if ($r.Width -gt 200 -and $r.Width -lt 900) { return $w }
    }
    return $null
}

try {
    $proc = Start-Process -FilePath $exe -ArgumentList "--open-ask", "--stub-ai" -PassThru
    Start-Sleep -Seconds 6

    $win = Find-SmallWindow $proc.Id
    if ($null -eq $win) { throw "ask window not found" }
    $r = $win.Current.BoundingRectangle
    $out += "ask window : $([int]$r.Width)x$([int]$r.Height)"

    $prompt = ById $win "PromptBox"
    if ($null -eq $prompt) { throw "prompt box not found" }
    $prompt.SetFocus()
    Start-Sleep -Milliseconds 500

    # real keystrokes, then a real Enter - that is the path under test
    [System.Windows.Forms.SendKeys]::SendWait("what is six times seven")
    Start-Sleep -Milliseconds 500
    $out += "typed      : '$(Value $prompt)'"

    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")

    $answer = ""
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 300
        $answer = Value (ById $win "AnswerText")
        if ($answer -like "*STUB ANSWER 42*") { break }
    }

    $status = (ById $win "StatusText").Current.Name
    $after = Value $prompt

    $out += "answer     : '$answer'"
    $out += "status line: '$status'"
    $out += "prompt box after send: '$after'"
    $out += ""

    $gotAnswer = $answer -like "*STUB ANSWER 42*"
    $carriedPrompt = $answer -like "*six times seven*"
    $promptCleared = [string]::IsNullOrWhiteSpace($after)
    $noError = [string]::IsNullOrWhiteSpace($status)

    $out += "enter sent the prompt and an answer came back : $gotAnswer"
    $out += "the prompt actually reached the client        : $carriedPrompt"
    $out += "prompt box cleared for the next question      : $promptCleared"
    $out += "no error line left on screen                  : $noError"
    $out += ""
    if ($gotAnswer -and $carriedPrompt -and $promptCleared -and $noError) { $out += "RESULT: PASS" }
    else { $out += "RESULT: FAIL" }
}
catch { $out += "ERROR: $_" }
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(3000) }
    $out += "app terminated"
}

$out | ForEach-Object { Write-Output $_ }
