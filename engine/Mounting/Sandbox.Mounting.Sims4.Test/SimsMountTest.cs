using Sandbox.Mounting;
using Sims4.Dbpf;
using Sims4.Dbpf.Enums;
using System.IO;
using IoDirectory = System.IO.Directory;
using DbpfResourceType = Sims4.Dbpf.Enums.ResourceType;

namespace Sims4MountTest;

[TestClass]
public class SimsMountTest
{
	[TestMethod]
	public void Initialize_WhenSteamAppMissing_DoesNotMarkMountInstalled()
	{
		using var host = new MountHost( new Configuration
		{
			SteamIntegration = new TestSteamIntegration( isInstalled: false, appDirectory: null )
		} );

		host.Initialize( typeof( SimsMount ) );

		var mount = host.GetSource( "sims4" ) as SimsMount;
		Assert.IsNotNull( mount );
		Assert.IsFalse( mount.IsInstalled );
	}

	[TestMethod]
	public void Initialize_WhenSteamAppInstalledButDirectoryMissing_DoesNotMarkMountInstalled()
	{
		var missingDirectory = Path.Combine( Path.GetTempPath(), $"sims4-mount-missing-{Guid.NewGuid():N}" );
		using var host = new MountHost( new Configuration
		{
			SteamIntegration = new TestSteamIntegration( isInstalled: true, appDirectory: missingDirectory )
		} );

		host.Initialize( typeof( SimsMount ) );

		var mount = host.GetSource( "sims4" ) as SimsMount;
		Assert.IsNotNull( mount );
		Assert.IsFalse( mount.IsInstalled );
	}

	[TestMethod]
	public void Initialize_WhenSteamAppInstalledAndDirectoryExists_MarksMountInstalled()
	{
		var gameDirectory = CreateTemporaryGameDirectory();
		try
		{
			using var host = new MountHost( new Configuration
			{
				SteamIntegration = new TestSteamIntegration( isInstalled: true, appDirectory: gameDirectory )
			} );

			host.Initialize( typeof( SimsMount ) );

			var mount = host.GetSource( "sims4" ) as SimsMount;
			Assert.IsNotNull( mount );
			Assert.IsTrue( mount.IsInstalled );
		}
		finally
		{
			IoDirectory.Delete( gameDirectory, recursive: true );
		}
	}

	[TestMethod]
	public async Task Mount_WhenInstalledAndFixturePackageExists_RegistersModelResources()
	{
		var fixturePackagePath = GetRealPackagePathOrInconclusive();
		EnsureFixtureHasModelRecordsOrInconclusive( fixturePackagePath );

		var gameDirectory = CreateTemporaryGameDirectoryWithFixture( fixturePackagePath );
		try
		{
			using var host = new MountHost( new Configuration
			{
				SteamIntegration = new TestSteamIntegration( isInstalled: true, appDirectory: gameDirectory )
			} );

			host.Initialize( typeof( SimsMount ) );
			await host.Mount( "sims4" );

			var mount = host.GetSource( "sims4" ) as SimsMount;
			Assert.IsNotNull( mount );
			Assert.IsTrue( mount.IsMounted );
			Assert.IsTrue( mount.Resources.Count > 0, "Expected at least one model resource to be mounted." );
			Assert.IsTrue( mount.Resources.Count <= 6, "Current mount implementation should register at most six model resources per package." );
			Assert.IsTrue( mount.Resources.All( x => x.Path.StartsWith( "mount://sims4/Models/", StringComparison.Ordinal ) ) );

			host.Unmount( "sims4" );
			Assert.IsFalse( mount.IsMounted );
		}
		finally
		{
			IoDirectory.Delete( gameDirectory, recursive: true );
		}
	}

	[TestMethod]
	public async Task Mount_WhenDataDirectoryMissing_DoesNotMarkMounted()
	{
		var gameDirectory = CreateTemporaryGameDirectory();
		IoDirectory.Delete( Path.Combine( gameDirectory, "Data" ), recursive: true );

		try
		{
			using var host = new MountHost( new Configuration
			{
				SteamIntegration = new TestSteamIntegration( isInstalled: true, appDirectory: gameDirectory )
			} );

			host.Initialize( typeof( SimsMount ) );
			await host.Mount( "sims4" );

			var mount = host.GetSource( "sims4" ) as SimsMount;
			Assert.IsNotNull( mount );
			Assert.IsFalse( mount.IsMounted );
			Assert.AreEqual( 0, mount.Resources.Count );
		}
		finally
		{
			IoDirectory.Delete( gameDirectory, recursive: true );
		}
	}

	private static void EnsureFixtureHasModelRecordsOrInconclusive( string packagePath )
	{
		using var package = DbpfPackage.Open( packagePath );
		if ( package.CountOf( DbpfResourceType.GEOM ) == 0 )
		{
			Assert.Inconclusive( "Fixture package has no GEOM resources. Provide a fixture that contains GEOM records." );
		}
	}

	private static string GetRealPackagePathOrInconclusive()
	{
		var fromEnv = Environment.GetEnvironmentVariable( "SIMS4_TEST_PACKAGE_PATH" );
		if ( !string.IsNullOrWhiteSpace( fromEnv ) && File.Exists( fromEnv ) )
			return fromEnv;

		var coffeeTableFixture = Path.Combine( @"C:\Users\DPHoo\Downloads\set", "miiko-harmony-coffee-table.package" );
		if ( File.Exists( coffeeTableFixture ) )
			return coffeeTableFixture;

		var chairFixture = Path.Combine( @"C:\Users\DPHoo\Downloads\set", "miiko-harmony-chair.package" );
		if ( File.Exists( chairFixture ) )
			return chairFixture;

		Assert.Inconclusive( "Real Sims 4 package not found. Set SIMS4_TEST_PACKAGE_PATH or configure a local fixture path." );
		return string.Empty;
	}

	private static string CreateTemporaryGameDirectory()
	{
		var root = Path.Combine( Path.GetTempPath(), $"sims4-mount-test-{Guid.NewGuid():N}" );
		IoDirectory.CreateDirectory( Path.Combine( root, "Data" ) );
		return root;
	}

	private static string CreateTemporaryGameDirectoryWithFixture( string fixturePackagePath )
	{
		var root = CreateTemporaryGameDirectory();
		var clientDir = Path.Combine( root, "Data", "Client" );
		IoDirectory.CreateDirectory( clientDir );

		var targetPath = Path.Combine( clientDir, "fixture.package" );
		File.Copy( fixturePackagePath, targetPath, overwrite: true );
		return root;
	}

	private sealed class TestSteamIntegration : ISteamIntegration
	{
		private readonly bool _isInstalled;
		private readonly string? _appDirectory;

		public TestSteamIntegration( bool isInstalled, string? appDirectory )
		{
			_isInstalled = isInstalled;
			_appDirectory = appDirectory;
		}

		public bool IsAppInstalled( long appid ) => _isInstalled;

		public bool IsDlcInstalled( long appid ) => false;

		public string GetAppDirectory( long appid ) => _appDirectory ?? string.Empty;
	}
}
