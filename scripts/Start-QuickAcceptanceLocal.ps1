param(
    [string] $RuntimeRoot = 'E:\MedResearch-local-q4',
    [int] $ApiPort = 5254,
    [int] $WebPort = 3004
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$configPath = Join-Path $RuntimeRoot 'local-runtime.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if ($config.Purpose -ne 'medresearch-quick-q4-local-v1' -or $config.Database -ne 'medresearch_quick_q4' -or $config.Role -ne 'q4_local') {
    throw 'Initialize the dedicated Q4 database first; no user database is permitted.'
}
foreach ($port in @($ApiPort, $WebPort)) {
    if ($port -lt 1024 -or $port -gt 65535 -or (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)) {
        throw 'Use free nonprivileged local ports. Existing services will not be stopped.'
    }
}
if ($ApiPort -eq $WebPort) { throw 'API and web ports must differ.' }
$apiDll = Join-Path $root 'tests/MedResearch.WebReleaseTests/bin/Release/net10.0/MedResearch.WebReleaseTests.dll'
$web = Join-Path $root 'frontend/apps/web'
$next = Join-Path $web 'node_modules/next/dist/bin/next'
if (!(Test-Path -LiteralPath $apiDll) -or !(Test-Path -LiteralPath $next)) {
    throw 'Build the release acceptance host and install frozen frontend dependencies first.'
}
$dotnet = (Get-Command dotnet.exe).Source
$node = (Get-Command node.exe).Source
$variables = @{
    MEDRESEARCH_QUICK_ACCEPTANCE = 'true'; MEDRESEARCH_QUICK_LOCAL_CONFIG = $configPath
    MEDRESEARCH_QUICK_API_PORT = "$ApiPort"; MEDRESEARCH_QUICK_FAKE_SCENARIO = 'normal'
    MEDRESEARCH_QUICK_ARTIFACTS = (Join-Path $RuntimeRoot 'demo-artifacts')
    ConnectionStrings__MedResearch = "Host=127.0.0.1;Port=$($config.Port);Database=medresearch_quick_q4;Username=q4_local;Password=$($config.Password);Timeout=2;Command Timeout=3"
    ASPNETCORE_ENVIRONMENT = 'Development'; ASPNETCORE_URLS = "http://127.0.0.1:$ApiPort"
    Authentication__Mode = 'DevelopmentLocal'; Authentication__DevelopmentSubject = 'q4-local-demo'
    Database__ApplyMigrationsOnStartup = 'false'; ResearchProcessing__Enabled = 'false'
    QuickResearch__Enabled = 'true'; QuickResearch__Retrieval__Enabled = 'true'; QuickResearch__Answer__Enabled = 'true'
    QuickResearch__PollIntervalSeconds = '1'; QuickResearch__Answer__TimeoutSeconds = '2'
    'Logging__LogLevel__Microsoft.EntityFrameworkCore' = 'Warning'
    WEB_AUTH_MODE = 'DevelopmentLocal'; WEB_AUTH_ORIGIN = "http://127.0.0.1:$WebPort"
    MEDRESEARCH_API_INTERNAL_URL = "http://127.0.0.1:$ApiPort"; NEXT_TELEMETRY_DISABLED = '1'
}
$previous = @{}
$api = $null
try {
    foreach ($key in $variables.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $variables[$key], 'Process')
    }
    $api = Start-Process -FilePath $dotnet -ArgumentList @($apiDll, '--quick-api') -WorkingDirectory $root -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $RuntimeRoot 'demo-api.log') -RedirectStandardError (Join-Path $RuntimeRoot 'demo-api-error.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        if ($api.HasExited) { throw 'Test API exited; inspect protected local runtime logs.' }
        try { $ready = (Invoke-WebRequest "http://127.0.0.1:$ApiPort/health/ready" -TimeoutSec 2).StatusCode -eq 200 } catch { $ready = $false }
        if (!$ready) { Start-Sleep -Milliseconds 250 }
    } while (!$ready -and [DateTime]::UtcNow -lt $deadline)
    if (!$ready) { throw 'Dedicated PostgreSQL readiness failed; existing services were not changed.' }
    $frontend = Start-Process -FilePath $node -ArgumentList @($next, 'dev', '--hostname', '127.0.0.1', '--port', "$WebPort") `
        -WorkingDirectory $web -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $RuntimeRoot 'demo-web.log') `
        -RedirectStandardError (Join-Path $RuntimeRoot 'demo-web-error.log')
    Write-Output "TEST ONLY: fake scientific sources and fake LLM. Deep worker disabled. No live/paid requests."
    Write-Output "Web: http://127.0.0.1:$WebPort/quick-research/new; API readiness: http://127.0.0.1:$ApiPort/health/ready"
    Write-Output "Demo question: Does sleep deprivation affect memory in adults? API PID: $($api.Id); web PID: $($frontend.Id)"
} catch {
    if ($api -and !$api.HasExited) { Stop-Process -Id $api.Id }
    throw
} finally {
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
    $config = $null; $variables.Clear()
}
