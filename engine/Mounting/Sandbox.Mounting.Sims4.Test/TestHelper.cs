using System.IO;

namespace Sims4MountTest;

/// <summary>
/// Shared test utilities for locating Sims 4 package fixtures.
/// </summary>
internal static class TestHelper
{
	private static readonly string[] FixturePaths =
	{
		@"E:\SteamLibrary\steamapps\common\The Sims 4\Data\Client\ClientFullBuild0.package",
	};

	private static readonly string[] GameDataDirs =
	{
		@"E:\SteamLibrary\steamapps\common\The Sims 4\Data\Client",
	};

	/// <summary>
	/// Locate a real .package file for integration tests.
	/// Checks SIMS4_TEST_PACKAGE_PATH env var first, then hardcoded fallbacks.
	/// Calls Assert.Inconclusive if no fixture is found.
	/// </summary>
	public static string GetPackagePath()
	{
		var fromEnv = Environment.GetEnvironmentVariable( "SIMS4_TEST_PACKAGE_PATH" );
		if ( !string.IsNullOrWhiteSpace( fromEnv ) && File.Exists( fromEnv ) )
			return fromEnv;

		foreach ( var path in FixturePaths )
		{
			if ( File.Exists( path ) )
				return path;
		}

		Assert.Fail(
			"Real Sims 4 package not found. Set SIMS4_TEST_PACKAGE_PATH or place a .package fixture." );
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

		foreach ( var dir in GameDataDirs )
		{
			if ( Directory.Exists( dir ) )
			{
				var packages = Directory.EnumerateFiles( dir, "*.package" )
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
		}

		return new List<string>();
	}
}
