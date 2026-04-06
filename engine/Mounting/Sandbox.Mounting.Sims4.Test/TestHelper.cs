using System.IO;
using Microsoft.Win32;

namespace Sims4MountTest;

/// <summary>
/// Shared test utilities for locating Sims 4 package fixtures.
/// </summary>
internal static class TestHelper
{
	/// <summary>
	/// Locate a real .package file for integration tests.
	/// Checks SIMS4_TEST_PACKAGE_PATH env var first, then discovers via Steam.
	/// Calls Assert.Fail if no fixture is found.
	/// </summary>
	public static string GetPackagePath()
	{
		var fromEnv = Environment.GetEnvironmentVariable( "SIMS4_TEST_PACKAGE_PATH" );
		if ( !string.IsNullOrWhiteSpace( fromEnv ) && File.Exists( fromEnv ) )
			return fromEnv;

		var dataDir = FindSims4DataDir();
		if ( dataDir != null )
		{
			var fullBuild = Path.Combine( dataDir, "ClientFullBuild0.package" );
			if ( File.Exists( fullBuild ) )
				return fullBuild;
		}

		Assert.Fail(
			"Real Sims 4 package not found. Set SIMS4_TEST_PACKAGE_PATH or install The Sims 4 via Steam." );
		return string.Empty;
	}

	/// <summary>
	/// Find and open the first package that contains entries of the given resource type.
	/// Returns the package and path, or null if none found.
	/// Caller is responsible for disposing the returned package.
	/// </summary>
	public static (Sims4Reader.DbpfPackage package, string path)? FindPackageWithType( Sims4Reader.ResourceType type )
	{
		var paths = GetAllPackagePaths();
		if ( paths.Count == 0 )
		{
			// Fall back to single package
			var single = GetPackagePath();
			var pkg = Sims4Reader.DbpfPackage.Open( single );
			if ( pkg.FindAll( type ).Any( e => e.MemSize > 0 && e.FileSize > 0 ) )
				return (pkg, single);
			pkg.Dispose();
			return null;
		}

		foreach ( var path in paths )
		{
			var pkg = Sims4Reader.DbpfPackage.Open( path );
			if ( pkg.FindAll( type ).Any( e => e.MemSize > 0 && e.FileSize > 0 ) )
				return (pkg, path);
			pkg.Dispose();
		}

		return null;
	}

	/// <summary>
	/// Locate ALL .package files in the game data directories.
	/// Returns every FullBuild and DeltaBuild package. Used for comprehensive tests.
	/// </summary>
	public static List<string> GetAllPackagePaths()
	{
		var fromEnv = Environment.GetEnvironmentVariable( "SIMS4_TEST_PACKAGE_DIR" );
		if ( !string.IsNullOrWhiteSpace( fromEnv ) && Directory.Exists( fromEnv ) )
		{
			return Directory.EnumerateFiles( fromEnv, "*.package" )
				.Where( f => Path.GetFileName( f ).StartsWith( "Client" ) )
				.OrderBy( f => f )
				.ToList();
		}

		var dataDir = FindSims4DataDir();
		if ( dataDir != null && Directory.Exists( dataDir ) )
		{
			var packages = Directory.EnumerateFiles( dataDir, "*.package" )
				.Where( f =>
				{
					var name = Path.GetFileName( f );
					return name.StartsWith( "ClientFullBuild" ) || name.StartsWith( "ClientDeltaBuild" );
				} )
				.OrderBy( f => f )
				.ToList();

			if ( packages.Count > 0 )
				return packages;
		}

		return new List<string>();
	}

	/// <summary>
	/// Dynamically discover the Sims 4 Data/Client directory via Steam.
	/// Reads the Windows registry for Steam's install path, then parses
	/// libraryfolders.vdf to find all Steam library folders.
	/// </summary>
	private static string? FindSims4DataDir()
	{
		var steamPath = GetSteamInstallPath();
		if ( steamPath == null )
			return null;

		var libraryFolders = new List<string> { steamPath };

		// Parse libraryfolders.vdf for additional library paths
		var vdfPath = Path.Combine( steamPath, "steamapps", "libraryfolders.vdf" );
		if ( File.Exists( vdfPath ) )
		{
			foreach ( var line in File.ReadLines( vdfPath ) )
			{
				// Lines look like:  "path"		"D:\SteamLibrary"
				var trimmed = line.Trim();
				if ( !trimmed.StartsWith( "\"path\"", StringComparison.OrdinalIgnoreCase ) )
					continue;

				var parts = trimmed.Split( '"' );
				// Expected: "", "path", "", "", "D:\SteamLibrary", ""
				if ( parts.Length >= 5 && Directory.Exists( parts[4] ) )
					libraryFolders.Add( parts[4] );
			}
		}

		// Check each library folder for The Sims 4
		foreach ( var library in libraryFolders )
		{
			var dataDir = Path.Combine( library, "steamapps", "common", "The Sims 4", "Data", "Client" );
			if ( Directory.Exists( dataDir ) )
				return dataDir;
		}

		return null;
	}

	/// <summary>
	/// Read Steam's install directory from the Windows registry.
	/// </summary>
	private static string? GetSteamInstallPath()
	{
		string[] registryKeys =
		{
			@"SOFTWARE\WOW6432Node\Valve\Steam",
			@"SOFTWARE\Valve\Steam",
		};

		foreach ( var key in registryKeys )
		{
			using var regKey = Registry.LocalMachine.OpenSubKey( key );
			var path = regKey?.GetValue( "InstallPath" ) as string;
			if ( !string.IsNullOrWhiteSpace( path ) && Directory.Exists( path ) )
				return path;
		}

		// Try current user as fallback
		foreach ( var key in registryKeys )
		{
			using var regKey = Registry.CurrentUser.OpenSubKey( key );
			var path = regKey?.GetValue( "SteamPath" ) as string;
			if ( !string.IsNullOrWhiteSpace( path ) && Directory.Exists( path ) )
				return path;
		}

		return null;
	}
}
