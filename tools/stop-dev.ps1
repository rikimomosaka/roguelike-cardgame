param(
    [Parameter(Mandatory = $true)]
    [string]$RootDirectory
)

$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath($RootDirectory)
$serverBin = [IO.Path]::GetFullPath((Join-Path $root 'src\Server\bin'))
$currentProcessId = $PID
$stopped = [System.Collections.Generic.HashSet[int]]::new()

function Stop-ProcessTree {
    param([int]$ProcessId)

    if ($ProcessId -le 0 -or $ProcessId -eq $currentProcessId -or !$stopped.Add($ProcessId)) {
        return
    }

    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        return
    }

    Write-Host "Stopping process tree PID $ProcessId ($($process.ProcessName))"
    & taskkill.exe /F /T /PID $ProcessId | Out-Null
}

# Process trees opened by dev.bat. Killing the cmd process also stops dotnet
# watch/Vite, preventing them from immediately spawning replacement children.
Get-Process -Name cmd -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle -like 'RoguelikeServer*' -or $_.MainWindowTitle -like 'RoguelikeClient*' } |
    ForEach-Object { Stop-ProcessTree $_.Id }

# A browser test or manual dotnet run may bind an arbitrary port. Match the
# executable path so unrelated Server.exe processes elsewhere are untouched.
$repoServers = @(
    Get-CimInstance Win32_Process -Filter "Name='Server.exe'" |
        Where-Object {
            $_.ExecutablePath -and
            [IO.Path]::GetFullPath($_.ExecutablePath).StartsWith(
                $serverBin,
                [StringComparison]::OrdinalIgnoreCase)
        }
)

foreach ($server in $repoServers) {
    $parent = Get-CimInstance Win32_Process -Filter "ProcessId=$($server.ParentProcessId)" -ErrorAction SilentlyContinue
    $targetProcessId = if ($parent -and $parent.Name -ieq 'dotnet.exe') {
        [int]$parent.ProcessId
    } else {
        [int]$server.ProcessId
    }
    Stop-ProcessTree $targetProcessId
}

# Keep the expected dev ports deterministic even if a previous process did not
# come from this repository.
$portOwners = @(
    Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -in 5114, 5173 } |
        Select-Object -ExpandProperty OwningProcess -Unique
)
foreach ($ownerProcessId in $portOwners) {
    Stop-ProcessTree ([int]$ownerProcessId)
}

$deadline = (Get-Date).AddSeconds(10)
do {
    $remainingServer = Get-CimInstance Win32_Process -Filter "Name='Server.exe'" |
        Where-Object {
            $_.ExecutablePath -and
            [IO.Path]::GetFullPath($_.ExecutablePath).StartsWith(
                $serverBin,
                [StringComparison]::OrdinalIgnoreCase)
        }
    $occupiedPort = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -in 5114, 5173 }

    if (!$remainingServer -and !$occupiedPort) {
        exit 0
    }

    Start-Sleep -Milliseconds 200
} while ((Get-Date) -lt $deadline)

throw 'Could not stop the previous development Server/Client processes within 10 seconds.'
