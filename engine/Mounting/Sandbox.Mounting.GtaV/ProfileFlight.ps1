param( [Parameter(Mandatory)][int]$EditorId, [switch]$Low )
$ErrorActionPreference = 'Stop'
$cli = Join-Path $PSScriptRoot '../../../game/bin/managed/sbox-cli.exe'
$source = Get-Content (Join-Path $PSScriptRoot 'ProfileFlight.csx') -Raw
$mode = $Low.IsPresent.ToString().ToLowerInvariant()
$code = "const bool lowFlight = $mode;" + [Environment]::NewLine + $source
& $cli --connect $EditorId --call run_csharp (@{code=$code} | ConvertTo-Json -Compress)
if ($LASTEXITCODE) { throw "Editor flight failed ($LASTEXITCODE)" }
