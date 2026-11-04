using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>Builds and runs the isolated tools-mode material child-compile experiment.</summary>
internal sealed class TestGltfNativeMaterial
{
	internal ExitCode Run()
	{
		try
		{
			var root = Path.GetFullPath( "." );
			var project = Path.Combine( root, "engine", "Tools", "GltfNativeValidation", "GltfNativeValidation.csproj" );
			if ( !File.Exists( project ) ) throw new InvalidOperationException( "Run this command from the repository root." );
			if ( !Utility.RunDotnetCommand( root, $"build --configuration Release --nologo -m:1 -p:BuildInParallel=false {Quote( project )}" ) ) return ExitCode.Failure;

			var parent = Path.Combine( root, "Temp", "gltf-validation", "native-compiler" );
			var runDirectory = Path.Combine( parent, Guid.NewGuid().ToString( "N" ) );
			RequireNoLinks( runDirectory );
			Directory.CreateDirectory( runDirectory );
			RequireNoLinks( runDirectory );
			Console.WriteLine( $"Native material probe directory: {runDirectory}" );
			var executable = Path.Combine( root, "game", "bin", "managed", OperatingSystem.IsWindows() ? "GltfNativeValidation.exe" : "GltfNativeValidation" );
			var arguments = $"--repo-root {Quote( root )} --run-dir {Quote( runDirectory )}";
			return Utility.RunProcess( executable, arguments, root ) ? ExitCode.Success : ExitCode.Failure;
		}
		catch ( Exception e )
		{
			Console.Error.WriteLine( "Native material probe failed: " + e );
			return ExitCode.Failure;
		}
	}

	private static string Quote( string path )
	{
		if ( path.Contains( '"' ) || path.Contains( '\n' ) || path.Contains( '\r' ) ) throw new InvalidOperationException( "Unsupported character in probe path." );
		return '"' + path + '"';
	}

	private static void RequireNoLinks( string path )
	{
		var root = Path.GetPathRoot( path );
		var current = root;
		foreach ( var component in path[root.Length..].Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
		{
			current = Path.Combine( current, component );
			try
			{
				if ( (File.GetAttributes( current ) & FileAttributes.ReparsePoint) != 0 ) throw new InvalidOperationException( $"Probe path contains a link: {current}" );
			}
			catch ( FileNotFoundException ) { }
			catch ( DirectoryNotFoundException ) { }
		}
	}
}
