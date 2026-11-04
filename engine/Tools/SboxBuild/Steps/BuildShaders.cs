using System.Text.RegularExpressions;
using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class BuildShaders( bool forced = false, bool seed = false, string[] shaders = null, int? jobs = null )
{
	internal ExitCode Run()
	{
		string[] selected;
		try
		{
			if ( jobs is <= 0 ) throw new ArgumentException( "Shader worker count must be positive." );
			selected = ResolveShaders( Path.GetFullPath( Path.Combine( Directory.GetCurrentDirectory(), "game" ) ) );
		}
		catch ( Exception ex ) when ( ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException )
		{
			Log.Error( $"Invalid shader selection: {ex.Message}" );
			return ExitCode.Failure;
		}
		// Compiled shaders aren't committed. Starting from the newest published ones means only the
		// shaders changed since then compile; without them, everything does.
		if ( seed )
		{
			if ( new DownloadPublicArtifacts( shadersOnly: true ).Run() == ExitCode.Success )
				GitHubActions.Note( "seeded from published shaders" );
			else
				Log.Warning( "Couldn't download the published shaders, compiling them all." );
		}

		return Compile( selected );
	}

	private string[] ResolveShaders( string gameDir )
	{
		var result = new List<string>();
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		foreach ( var shader in shaders ?? [] )
		{
			if ( string.IsNullOrWhiteSpace( shader ) || Path.IsPathRooted( shader ) )
				throw new ArgumentException( "Shader paths must be non-empty and relative to game/." );
			var full = Path.GetFullPath( Path.Combine( gameDir, shader ) );
			var relative = Path.GetRelativePath( gameDir, full );
			if ( relative == ".." || relative.StartsWith( ".." + Path.DirectorySeparatorChar, StringComparison.Ordinal ) || Path.IsPathRooted( relative ) )
				throw new ArgumentException( $"Shader path escapes game/: {shader}" );
			if ( !string.Equals( Path.GetExtension( full ), ".shader", comparison ) || !File.Exists( full ) )
				throw new ArgumentException( $"Shader does not exist or is not a .shader file: {shader}" );
			if ( !result.Contains( full, comparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal ) )
				result.Add( full );
		}
		return result.ToArray();
	}

	private ExitCode Compile( string[] selected ) => BuildDisplay.Run( "Build shaders", () =>
	{
		try
		{
			string rootDir = Directory.GetCurrentDirectory();
			string gameDir = Path.Combine( rootDir, "game" );
			// Named for the assembly, which is cased: Windows did not care, Linux does. The apphost
			// carries no extension there either.
			string shaderCompilerPath = Path.Combine( gameDir, "bin", "managed",
				OperatingSystem.IsWindows() ? "ShaderCompiler.exe" : "ShaderCompiler" );

			// Verify shader compiler exists
			if ( !File.Exists( shaderCompilerPath ) )
			{
				Log.Error( $"Error: Shader compiler executable not found at {shaderCompilerPath}" );
				return ExitCode.Failure;
			}

			return RunShaderCompiler( shaderCompilerPath, gameDir, selected );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Shader compilation failed with error: {ex}" );
			return ExitCode.Failure;
		}
	} );

	private ExitCode RunShaderCompiler( string shaderCompilerPath, string workingDirectory, string[] selected )
	{
		// ShaderCompiler compares its absolute enumerated paths to these exact arguments.
		string arguments = selected.Length == 0 ? "*" : string.Join( " ", selected.Select( QuoteArgument ) );
		if ( forced )
		{
			arguments += " -f";
		}
		if ( jobs.HasValue ) arguments += $" -j {jobs.Value}";

		var current = 0;
		var total = 0;
		var compiled = 0;
		var upToDate = 0;
		BuildDisplay.Status( "Compile shaders" );

		bool success = Utility.RunProcess(
			shaderCompilerPath,
			arguments,
			workingDirectory,
			onDataReceived: ( sender, e ) =>
			{
				if ( e.Data != null )
				{
					if ( !BuildDisplay.IsActive ) Log.Info( e.Data );

					var match = Regex.Match( e.Data, @"^\((\d+)/(\d+)\)\s*(.*)$" );
					if ( match.Success && int.TryParse( match.Groups[1].Value, out current )
						&& int.TryParse( match.Groups[2].Value, out total ) )
					{
						BuildDisplay.Detail( match.Groups[3].Value );
						BuildDisplay.Progress( Math.Max( 0, current - 1 ), total );
					}
					else if ( e.Data.Contains( "Compiled successfully in" )
						|| e.Data.Contains( "Skipped, up to date." )
						|| e.Data.Contains( "Compile failed." ) )
					{
						if ( e.Data.Contains( "Compiled successfully in" ) ) compiled++;
						else if ( e.Data.Contains( "Skipped, up to date." ) ) upToDate++;
						BuildDisplay.Progress( current, total );
					}
				}
			}
		);

		GitHubActions.Note( $"{compiled} compiled, {upToDate} up to date" );

		if ( !success )
		{
			Log.Error( $"Shader compiler failed" );
			return ExitCode.Failure;
		}
		if ( selected.Length > 0 && total != selected.Length )
		{
			Log.Error( $"Shader compiler enumerated {total} shaders for {selected.Length} selected files." );
			return ExitCode.Failure;
		}

		return ExitCode.Success;
	}

	// ProcessStartInfo.Arguments uses the standard backslash-and-quote argument rules.
	private static string QuoteArgument( string value )
	{
		var result = new System.Text.StringBuilder( value.Length + 2 );
		result.Append( '"' );
		var backslashes = 0;
		foreach ( var character in value )
		{
			if ( character == '\\' ) { backslashes++; continue; }
			if ( character == '"' )
			{
				result.Append( '\\', backslashes * 2 + 1 );
				result.Append( character );
			}
			else
			{
				result.Append( '\\', backslashes );
				result.Append( character );
			}
			backslashes = 0;
		}
		result.Append( '\\', backslashes * 2 );
		result.Append( '"' );
		return result.ToString();
	}
}
