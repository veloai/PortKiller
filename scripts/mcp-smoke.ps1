# CHECK TOOL - not shipped. ASCII only (Windows PowerShell reads .ps1 as ANSI).
#
# Talks to the pet's MCP server (127.0.0.1:47420) the way an MCP client would and checks
# that the guards hold:
#   - initialize / tools/list / tools/call work with the key
#   - no key -> 401, wrong key -> 401, a web page Origin -> 403, GET -> 405
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File mcp-smoke.ps1 [-Say "text"]

param([string]$Say = "MCP smoke test OK")

$ErrorActionPreference = "Stop"
$url = "http://127.0.0.1:47420/mcp"
$key = (Get-Content (Join-Path $env:APPDATA "PortKillerPet\mcp.key") -Raw).Trim()
$fails = 0

function Call([string]$body, [hashtable]$headers) {
    try {
        $r = Invoke-WebRequest -Uri $url -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($body)) `
            -ContentType "application/json" -Headers $headers -UseBasicParsing -TimeoutSec 10
        return @{ code = [int]$r.StatusCode; body = $r.Content }
    } catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($resp -eq $null) { return @{ code = -1; body = $_.Exception.Message } }
        return @{ code = [int]$resp.StatusCode; body = "" }
    }
}

function Expect([string]$name, $actual, $expected) {
    if ($actual -eq $expected) { Write-Output ("PASS " + $name) }
    else { Write-Output ("FAIL " + $name + " (got " + $actual + ", want " + $expected + ")"); $script:fails++ }
}

$auth = @{ Authorization = "Bearer " + $key }

$r = Call '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}' $auth
Expect "initialize 200" $r.code 200
Write-Output ("     " + $r.body)

$r = Call '{"jsonrpc":"2.0","method":"notifications/initialized"}' $auth
Expect "notification 202" $r.code 202

$r = Call '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' $auth
Expect "tools/list 200" $r.code 200
$names = ($r.body | ConvertFrom-Json).result.tools | ForEach-Object { $_.name }
Write-Output ("     tools: " + ($names -join ", "))

$sayJson = '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"pet_say","arguments":{"text":"' + $Say + '","mood":"happy"}}}'
$r = Call $sayJson $auth
Expect "pet_say 200" $r.code 200
Write-Output ("     " + $r.body)

$r = Call '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"dev_status","arguments":{}}}' $auth
Expect "dev_status 200" $r.code 200
Write-Output ("     summary: " + (($r.body | ConvertFrom-Json).result.content[0].text | ConvertFrom-Json).summary)

$r = Call '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"pet_status","arguments":{}}}' $auth
Expect "pet_status 200" $r.code 200

# --- guards ---
$r = Call '{"jsonrpc":"2.0","id":9,"method":"tools/list"}' @{}
Expect "no key -> 401" $r.code 401
$r = Call '{"jsonrpc":"2.0","id":9,"method":"tools/list"}' @{ Authorization = "Bearer wrong" }
Expect "wrong key -> 401" $r.code 401
$r = Call '{"jsonrpc":"2.0","id":9,"method":"tools/list"}' @{ Authorization = "Bearer " + $key; Origin = "https://evil.example" }
Expect "web origin -> 403" $r.code 403
try { Invoke-WebRequest -Uri $url -Method Get -Headers $auth -UseBasicParsing -TimeoutSec 5 | Out-Null; $code = 200 }
catch [System.Net.WebException] { $code = [int]$_.Exception.Response.StatusCode }
Expect "GET -> 405" $code 405

Write-Output ("failures: " + $fails)
