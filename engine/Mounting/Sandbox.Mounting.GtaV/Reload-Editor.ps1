#requires -Version 7.3
[CmdletBinding()]
param([Parameter(Mandatory)][int] $EditorProcessId)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Sandbox.Mounting.GtaV.csproj'
$game = (Resolve-Path (Join-Path $PSScriptRoot '../../../game')).Path
$cli = Join-Path $game 'bin/managed/sbox-cli.exe'
$build = Join-Path $PSScriptRoot ("obj/editor/" + [Guid]::NewGuid().ToString('N'))
& dotnet build $project -c Release --no-restore -p:BuildProjectReferences=false "-p:GameDir=$build"
if ($LASTEXITCODE) { throw 'Mount build failed. The editor still uses the previous build.' }

$literal = $build | ConvertTo-Json -Compress
$code = @"
var session = Editor.SceneEditorSession.Active;
var path = session?.Scene?.Source?.ResourcePath;
await Editor.EditorUtility.Mounting.ReloadDevelopmentBuild( "gtav", $literal );
if ( path?.StartsWith( "mount://gtav/", StringComparison.OrdinalIgnoreCase ) == true &&
     session.Scene.Source is SceneFile )
{
    session.Scene.Load( SceneFile.Load( path ) );
}
return Editor.EditorUtility.Mounting.Get( "gtav" ).GetType().Assembly.Location;
"@
$payload = @{ code = $code } | ConvertTo-Json -Compress
& $cli --connect $EditorProcessId --call run_csharp $payload
if ($LASTEXITCODE) { throw 'The editor could not load the new build.' }
