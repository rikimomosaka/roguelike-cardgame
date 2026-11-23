param(
    [Parameter(Mandatory = $true)]
    [string]$Url,

    [int]$TimeoutSeconds = 120
)

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
Write-Host "Waiting for $Url"

do {
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 2
        if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
            Write-Host "Ready: HTTP $($response.StatusCode)"
            exit 0
        }
    } catch {
        # The Server may still be compiling. Retry until the timeout expires.
    }

    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $deadline)

Write-Error "Timed out waiting for $Url after $TimeoutSeconds seconds. Check the RoguelikeServer window for build errors."
exit 1
