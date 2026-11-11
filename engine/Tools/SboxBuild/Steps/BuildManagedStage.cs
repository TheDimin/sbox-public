using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>Stages selected managed builds; it does not install or validate them in an editor.</summary>
internal sealed class BuildManagedStage
{
	private static readonly string[] Allowed = ["Sandbox.Gltf", "Sandbox.Engine", "Sandbox.SceneRenderer", "Sandbox.Tools", "Sandbox.AppSystem"];
	private const int LogLimit = 1024 * 1024;
	private sealed record ProjectResult( string Project, string ProjectSha256, string AssetsSha256, string[] Arguments,
		int ExitCode, double Seconds, string LogFile, bool LogTruncated, string OutputFile, string OutputSha256, string OutputMvid );
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

	internal ExitCode Run( string[] projects )
	{
		string stage = null;
		var results = new List<ProjectResult>();
		var baseline = new SortedDictionary<string, string>( StringComparer.Ordinal );
		var inputs = new SortedDictionary<string, string>( StringComparer.Ordinal );
		string error = null;
		var root = Path.GetFullPath( Directory.GetCurrentDirectory() );
		string[] selected = [];
		try
		{
			if ( !File.Exists( Path.Combine( root, "engine", "Sandbox-Engine.slnx" ) ) ) throw new InvalidOperationException( "Run from the s&box checkout root." );
			projects ??= [];
			if ( projects.Length == 0 ) projects = ["Sandbox.Gltf", "Sandbox.Engine"];
			if ( projects.Any( x => !Allowed.Contains( x, StringComparer.Ordinal ) ) || projects.Distinct( StringComparer.Ordinal ).Count() != projects.Length )
				throw new ArgumentException( "Projects must be unique and from the managed-stage allowlist." );
			selected = Allowed.Where( x => projects.Contains( x, StringComparer.Ordinal ) ).ToArray();
			var engine = Path.Combine( root, "engine" );
			var managed = Path.Combine( root, "game", "bin", "managed" );
			if ( !Directory.Exists( managed ) ) throw new DirectoryNotFoundException( "The existing managed baseline is missing." );
			foreach ( var project in selected )
			{
				var dir = Path.Combine( engine, project );
				RequireFile( Path.Combine( dir, project + ".csproj" ) );
				RequireFile( Path.Combine( dir, "obj", "project.assets.json" ) );
			}
			if ( selected.Contains( "Sandbox.Engine", StringComparer.Ordinal ) )
				RequireFile( Path.Combine( engine, "Tools", "CodeGen", "bin", OperatingSystem.IsWindows() ? "CodeGen.exe" : "CodeGen" ) );

			var stageParent = Path.Combine( root, "Temp", "gltf-validation", "staged-build" );
			RequireNoLinks( root, stageParent );
			Directory.CreateDirectory( stageParent );
			stage = Path.Combine( stageParent, Guid.NewGuid().ToString( "N" ) );
			if ( Directory.Exists( stage ) || File.Exists( stage ) ) throw new IOException( "Refusing to reuse a stage directory." );
			Directory.CreateDirectory( stage );
			var output = Path.Combine( stage, "output" );
			Directory.CreateDirectory( output );
			foreach ( var file in Directory.EnumerateFiles( managed ).Order( StringComparer.Ordinal ) )
			{
				var name = Path.GetFileName( file );
				if ( !(name.EndsWith( ".dll", StringComparison.OrdinalIgnoreCase ) || name.EndsWith( ".pdb", StringComparison.OrdinalIgnoreCase ) ||
					name.EndsWith( ".xml", StringComparison.OrdinalIgnoreCase ) || name.EndsWith( ".deps.json", StringComparison.OrdinalIgnoreCase ) ||
					name.EndsWith( ".runtimeconfig.json", StringComparison.OrdinalIgnoreCase )) ) continue;
				RequireFile( file );
				var destination = Path.Combine( output, name );
				File.Copy( file, destination, false );
				baseline.Add( name, Hash( destination ) );
				if ( baseline[name] != Hash( file ) ) throw new IOException( $"Baseline changed while copying: {name}" );
			}
			if ( baseline.Count == 0 ) throw new IOException( "The managed baseline contains no seed files." );
			foreach ( var project in selected )
			{
				var dir = Path.Combine( engine, project );
				foreach ( var file in SourceFiles( dir ) ) inputs.Add( Path.GetRelativePath( root, file ), Hash( file ) );
				var assets = Path.Combine( dir, "obj", "project.assets.json" );
				inputs.Add( Path.GetRelativePath( root, assets ), Hash( assets ) );
			}
			var codegenTargets = Path.Combine( engine, "CodeGen.Targets" );
			RequireFile( codegenTargets );
			inputs.Add( Path.GetRelativePath( root, codegenTargets ), Hash( codegenTargets ) );
			if ( selected.Contains( "Sandbox.Engine", StringComparer.Ordinal ) )
			{
				var codegen = Path.Combine( engine, "Tools", "CodeGen", "bin", OperatingSystem.IsWindows() ? "CodeGen.exe" : "CodeGen" );
				inputs.Add( Path.GetRelativePath( root, codegen ), Hash( codegen ) );
			}
			foreach ( var project in selected )
			{
				var csproj = Path.Combine( engine, project, project + ".csproj" );
				var assets = Path.Combine( engine, project, "obj", "project.assets.json" );
				var target = Path.Combine( output, project + ".dll" );
				// The file is our own seed copy. Its absence forces the build to produce an output.
				if ( File.Exists( target ) ) File.Delete( target );
				var arguments = new[] { "build", csproj, "--configuration", "Release", "--no-restore", "--nologo", "-m:1",
					"-p:BuildInParallel=false", "-p:BuildProjectReferences=false", "-p:UseSharedCompilation=false",
					"-p:OutDir=" + output + Path.DirectorySeparatorChar, "-p:TreatWarningsAsErrors=true" };
				var logPath = Path.Combine( stage, project + ".log" );
				var timer = Stopwatch.StartNew();
				var (code, log, truncated) = RunDotnet( arguments, root );
				timer.Stop();
				File.WriteAllText( logPath, log );
				var hash = File.Exists( target ) ? Hash( target ) : null;
				string mvid = null;
				string outputError = null;
				if ( hash is not null )
				{
					try { mvid = AssemblyMvid( target ); }
					catch ( Exception e ) when ( e is not OutOfMemoryException ) { outputError = e.Message; }
				}
				results.Add( new ProjectResult( project, Hash( csproj ), Hash( assets ), arguments, code, timer.Elapsed.TotalSeconds,
					Path.GetFileName( logPath ), truncated, hash is null ? null : Path.GetFileName( target ), hash, mvid ) );
				if ( code != 0 || hash is null || mvid is null ) throw new InvalidOperationException( $"{project} failed or did not produce a readable expected assembly ({outputError}); see {logPath}." );
			}
			foreach ( var input in inputs )
				if ( Hash( Path.Combine( root, input.Key ) ) != input.Value ) throw new IOException( "Build input changed during the stage: " + input.Key );
		}
		catch ( Exception e ) when ( e is not OutOfMemoryException ) { error = e.ToString(); }
		if ( stage is not null && Directory.Exists( stage ) )
		{
			try
			{
				var output = Path.Combine( stage, "output" );
				var final = Directory.EnumerateFiles( output ).Order( StringComparer.Ordinal ).ToDictionary( Path.GetFileName, Hash, StringComparer.Ordinal );
				File.WriteAllText( Path.Combine( stage, "manifest.json" ), JsonSerializer.Serialize( new
				{
					passed = error is null && results.Count == selected.Length,
					error,
					selected,
					projects = results,
					baseline_seed_sha256 = baseline,
					selected_project_inputs_sha256 = inputs,
					final_output_sha256 = final,
					caveats = "Only selected project outputs were built; baseline files were copied from the existing deployment. Shared obj and CodeGen intermediates may be updated. No restore, deployment, editor restart or runtime validation was performed."
				}, JsonOptions ) );
			}
			catch ( Exception e ) when ( e is not OutOfMemoryException ) { error ??= "Could not write stage manifest: " + e; }
		}
		if ( error is not null ) { Log.Error( error ); if ( stage is not null ) Log.Info( $"Stage: {stage}" ); return ExitCode.Failure; }
		Log.Info( $"Managed stage complete: {stage}" );
		return ExitCode.Success;
	}

	private static IEnumerable<string> SourceFiles( string project )
	{
		var directories = new Stack<string>();
		directories.Push( project );
		while ( directories.Count > 0 )
		{
			var directory = directories.Pop();
			foreach ( var child in Directory.EnumerateDirectories( directory ).Order( StringComparer.Ordinal ) )
			{
				if ( Path.GetFileName( child ) is "obj" or "bin" ) continue;
				if ( (File.GetAttributes( child ) & FileAttributes.ReparsePoint) != 0 ) throw new IOException( "Source directory must not be a symbolic link: " + child );
				directories.Push( child );
			}
			foreach ( var file in Directory.EnumerateFiles( directory ).Order( StringComparer.Ordinal ) )
			{
				if ( !file.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) && !file.EndsWith( ".csproj", StringComparison.OrdinalIgnoreCase ) ) continue;
				RequireFile( file );
				yield return file;
			}
		}
	}

	private static void RequireFile( string path )
	{
		if ( !File.Exists( path ) ) throw new FileNotFoundException( "Required build input is missing.", path );
		if ( (File.GetAttributes( path ) & FileAttributes.ReparsePoint) != 0 ) throw new IOException( "Build input must not be a symbolic link: " + path );
	}

	private static void RequireNoLinks( string root, string path )
	{
		var current = root;
		foreach ( var part in Path.GetRelativePath( root, path ).Split( Path.DirectorySeparatorChar ) )
		{
			if ( part is ".." or "." or "" ) throw new IOException( "Invalid stage path." );
			current = Path.Combine( current, part );
			if ( (File.Exists( current ) || Directory.Exists( current )) && (File.GetAttributes( current ) & FileAttributes.ReparsePoint) != 0 )
				throw new IOException( "Stage path contains a symbolic link: " + current );
		}
	}

	private static string Hash( string file ) { using var stream = File.OpenRead( file ); return Convert.ToHexStringLower( SHA256.HashData( stream ) ); }
	private static string AssemblyMvid( string file )
	{
		using var stream = File.OpenRead( file );
		using var pe = new PEReader( stream );
		var metadata = pe.GetMetadataReader();
		return metadata.GetGuid( metadata.GetModuleDefinition().Mvid ).ToString();
	}

	private static (int ExitCode, string Log, bool Truncated) RunDotnet( string[] args, string workingDirectory )
	{
		var info = new ProcessStartInfo( "dotnet" ) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		foreach ( var arg in args ) info.ArgumentList.Add( arg );
		using var process = Process.Start( info ) ?? throw new InvalidOperationException( "Unable to start dotnet." );
		var stdout = Capture( process.StandardOutput );
		var stderr = Capture( process.StandardError );
		process.WaitForExit();
		Task.WaitAll( stdout, stderr );
		return (process.ExitCode, "STDOUT\n" + stdout.Result.Text + "\nSTDERR\n" + stderr.Result.Text, stdout.Result.Truncated || stderr.Result.Truncated);
	}

	private static async Task<(string Text, bool Truncated)> Capture( StreamReader reader )
	{
		var result = new StringBuilder();
		var buffer = new char[4096];
		bool truncated = false;
		int length;
		while ( (length = await reader.ReadAsync( buffer )) != 0 )
		{
			var available = Math.Max( 0, LogLimit - result.Length );
			result.Append( buffer, 0, Math.Min( available, length ) );
			truncated |= available < length;
		}
		return (result.ToString(), truncated);
	}
}
