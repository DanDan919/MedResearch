param(
    [string] $RuntimeRoot = 'E:\MedResearch-local-q4',
    [string] $PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [int] $Port = 55434
)

$ErrorActionPreference = 'Stop'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Use a nonprivileged dedicated port.' }
$root = [IO.Path]::GetFullPath($RuntimeRoot)
$repository = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if ($root.TrimEnd('\') -eq $repository.TrimEnd('\') -or $root.StartsWith($repository.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Local runtime must be outside the repository, never existing user PGDATA.'
}
foreach ($name in @('initdb', 'pg_ctl', 'createdb', 'psql')) {
    if (!(Test-Path -LiteralPath (Join-Path $PostgresBin "$name.exe"))) { throw "Missing PostgreSQL executable: $name" }
}
$configPath = Join-Path $root 'local-runtime.json'
$data = Join-Path $root 'pgdata'
$fresh = !(Test-Path -LiteralPath $root)
if (!$fresh -and !(Test-Path -LiteralPath $configPath)) { throw 'Existing unmarked runtime directory; refusing to initialize.' }
if ($fresh) {
    if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { throw 'Dedicated PostgreSQL port is already occupied.' }
    New-Item -ItemType Directory -Path $root | Out-Null
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
    $configuration = [ordered]@{ Purpose = 'medresearch-quick-q4-local-v1'; Port = $Port; Database = 'medresearch_quick_q4'; Role = 'q4_local'; Password = $password }
    [IO.File]::WriteAllText($configPath, ($configuration | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    & icacls.exe $root /inheritance:r /grant:r "*${sid}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE) { throw 'Unable to protect local runtime configuration.' }
    $pwfile = Join-Path $root 'init-password.txt'
    [IO.File]::WriteAllText($pwfile, $password, [Text.UTF8Encoding]::new($false))
    try {
        & (Join-Path $PostgresBin 'initdb.exe') -D $data -U q4_local --auth-host=scram-sha-256 --auth-local=scram-sha-256 "--pwfile=$pwfile" --encoding=UTF8 --locale=C
        if ($LASTEXITCODE) { throw 'Dedicated initdb failed; existing user cluster is untouched.' }
    } finally {
        # The password remains only in the protected local configuration, not process arguments.
        Remove-Item -LiteralPath $pwfile -ErrorAction SilentlyContinue
    }
} else {
    $configuration = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    if ($configuration.Purpose -ne 'medresearch-quick-q4-local-v1' -or $configuration.Port -ne $Port -or !(Test-Path -LiteralPath (Join-Path $data 'PG_VERSION'))) {
        throw 'Runtime identity mismatch or incomplete initialization; no automatic repair/reset.'
    }
}
& (Join-Path $PostgresBin 'pg_ctl.exe') -D $data status *> $null
if ($LASTEXITCODE) {
    if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { throw 'Dedicated port is owned by another server.' }
    & (Join-Path $PostgresBin 'pg_ctl.exe') -D $data -l (Join-Path $root 'postgresql.log') -o "-h 127.0.0.1 -p $Port" -w start
    if ($LASTEXITCODE) { throw 'Dedicated PostgreSQL startup failed.' }
}
$env:PGPASSWORD = $configuration.Password
try {
    if ($fresh) {
        & (Join-Path $PostgresBin 'createdb.exe') -h 127.0.0.1 -p $Port -U q4_local $configuration.Database
        if ($LASTEXITCODE) { throw 'Dedicated database creation failed.' }
    }
    & (Join-Path $PostgresBin 'psql.exe') -X -h 127.0.0.1 -p $Port -U q4_local -d $configuration.Database -v ON_ERROR_STOP=1 -c 'SELECT current_database(), version();'
    if ($LASTEXITCODE) { throw 'Dedicated PostgreSQL verification failed.' }
} finally { $env:PGPASSWORD = $null }
Write-Output "Dedicated local PostgreSQL ready on 127.0.0.1:$Port. Protected configuration: $configPath"
Write-Output 'No existing user database, role, port 5432 or PGDATA was modified. Migrations are a separate explicit operation.'
