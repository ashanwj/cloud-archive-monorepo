#Requires -Version 5.1
<#
.SYNOPSIS
    Tests the CloudArchive upload API endpoint.
.PARAMETER FilePath
    Path to the file to upload. Defaults to a temporary text file created by this script.
.PARAMETER BaseUrl
    Base URL of the running API. Defaults to http://localhost:5000.
.EXAMPLE
    .\Test-Api.ps1
    .\Test-Api.ps1 -FilePath "C:\docs\report.txt"
    .\Test-Api.ps1 -BaseUrl "http://localhost:5123"
#>
param(
    [string]$FilePath  = "complex_test.txt",
    [string]$BaseUrl   = "http://localhost:54331"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# --- Create a temp file if none provided ---
if (-not $FilePath) {
    $FilePath = [System.IO.Path]::GetTempFileName() -replace '\.tmp$', '.txt'
    Set-Content -Path $FilePath -Value @"
Q4 revenue increased by 12% year-over-year, driven by cloud segment growth.
All three business units exceeded targets. EBITDA margin improved to 28%.
Headcount grew by 340 to reach 4,200 full-time employees globally.
"@
    Write-Host "Created temp file: $FilePath" -ForegroundColor DarkGray
}

if (-not (Test-Path $FilePath)) {
    Write-Error "File not found: $FilePath"
    exit 1
}

$fileName = [System.IO.Path]::GetFileName($FilePath)
$url      = "$BaseUrl/api/documents/upload"

Write-Host ""
Write-Host "Uploading '$fileName' to $url ..." -ForegroundColor Cyan

# --- Build multipart/form-data using .NET HttpClient (available in PS 5.1) ---
Add-Type -AssemblyName System.Net.Http

$client  = New-Object System.Net.Http.HttpClient
$content = New-Object System.Net.Http.MultipartFormDataContent

try {
    $fileStream    = [System.IO.File]::OpenRead($FilePath)
    $fileContent   = New-Object System.Net.Http.StreamContent($fileStream)
    $fileContent.Headers.ContentType =
        [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("text/plain")
    $content.Add($fileContent, "file", $fileName)

    $response = $client.PostAsync($url, $content).GetAwaiter().GetResult()
    $body     = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()

    Write-Host ""
    Write-Host "Status: $([int]$response.StatusCode) $($response.StatusCode)" -ForegroundColor $(
        if ($response.IsSuccessStatusCode) { "Green" } else { "Red" }
    )
    Write-Host ""

    # Pretty-print JSON if possible
    try {
        $parsed = $body | ConvertFrom-Json
        Write-Host ($parsed | ConvertTo-Json -Depth 5) -ForegroundColor $(
            if ($response.IsSuccessStatusCode) { "Green" } else { "Yellow" }
        )
    } catch {
        Write-Host $body
    }
}
finally {
    if ($fileStream) { $fileStream.Dispose() }
    $content.Dispose()
    $client.Dispose()
}
