using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FilesystemTests;

[TestClass]
[DoNotParallelize]
public class SharedPhysicalFileWatcherTests
{
	string root;
	readonly List<BaseFileSystem> filesystems = new();

	[TestInitialize]
	public void Initialize()
	{
		if ( !OperatingSystem.IsLinux() ) Assert.Inconclusive( "Linux shared watcher backend" );
		root = Path.Combine( Path.GetTempPath(), "sbox-shared-watch-tests", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var fs in filesystems ) fs.Dispose();
		FileWatch.Tick();
		if ( Directory.Exists( root ) ) Directory.Delete( root, true );
	}

	T Own<T>( T filesystem ) where T : BaseFileSystem
	{
		filesystems.Add( filesystem );
		return filesystem;
	}

	string[] RootDiagnostics() => SharedPhysicalFileWatcher.GetDiagnostics().Skip( 1 ).Where( x => x.StartsWith( root, StringComparison.Ordinal ) ).ToArray();

	static async Task Until( Func<bool> condition, double timeout = 5 )
	{
		var timer = Stopwatch.StartNew();
		while ( !condition() && timer.Elapsed.TotalSeconds < timeout )
		{
			FileWatch.Tick();
			await Task.Delay( 10 );
		}
		Assert.IsTrue( condition(), "Expected watcher state was not reached" );
	}

	[TestMethod]
	public async Task AggregatesSharePhysicalRootAndKeepVirtualPathsAfterOneDisposes()
	{
		var assets = Path.Combine( root, "Assets" );
		Directory.CreateDirectory( assets );
		var project = Own( new LocalFileSystem( root ) );
		var localAssets = Own( new LocalFileSystem( assets ) );
		var sub = Own( project.CreateSubSystem( "/Assets" ) );
		var first = Own( new AggregateFileSystem() );
		var second = Own( new AggregateFileSystem() );
		first.Mount( sub );
		second.Mount( localAssets );
		using var a = first.Watch();
		using var b = second.Watch();
		var left = new List<string>();
		var right = new List<string>();
		a.OnChangedFile += left.Add;
		b.OnChangedFile += right.Add;
		Assert.AreEqual( 1, RootDiagnostics().Length );
		StringAssert.Contains( RootDiagnostics()[0], "subscriptions=2" );

		File.WriteAllText( Path.Combine( assets, "source.cs" ), "first" );
		await Until( () => left.Contains( "/source.cs" ) && right.Contains( "/source.cs" ) );
		first.Dispose();
		StringAssert.Contains( RootDiagnostics().Single(), "subscriptions=1" );
		right.Clear();
		File.WriteAllText( Path.Combine( assets, "second.cs" ), "second" );
		await Until( () => right.Contains( "/second.cs" ) );
		Assert.IsFalse( left.Contains( "/second.cs" ) );
		second.Dispose();
		Assert.AreEqual( 0, RootDiagnostics().Length );
	}

	[TestMethod]
	public void LastLogicalSubscriptionReleasesTheBackend()
	{
		var fs = Own( new LocalFileSystem( root ) );
		var first = fs.Watch( "*.cs" );
		var second = fs.Watch( "*.razor" );
		Assert.AreEqual( 1, RootDiagnostics().Length );
		first.Dispose();
		Assert.AreEqual( 1, RootDiagnostics().Length );
		second.Dispose();
		Assert.AreEqual( 0, RootDiagnostics().Length );
		using var third = fs.Watch();
		Assert.AreEqual( 1, RootDiagnostics().Length );
	}

	[TestMethod]
	public async Task ExclusionProfilesDoNotRemoveCoverageRequiredByAnotherSubscriber()
	{
		Directory.CreateDirectory( Path.Combine( root, "obj", "nested" ) );
		var compiler = Own( new LocalFileSystem( root ) );
		var assets = Own( new LocalFileSystem( root ) );
		compiler.ConfigureWatch( "test compiler", new[] { "obj" } );
		using var a = compiler.Watch();
		using var b = assets.Watch();
		var sourceChanges = new List<string>();
		var assetChanges = new List<string>();
		a.OnChangedFile += sourceChanges.Add;
		b.OnChangedFile += assetChanges.Add;
		Assert.AreEqual( 1, RootDiagnostics().Length );
		StringAssert.Contains( RootDiagnostics().Single(), "directories=3" );
		StringAssert.Contains( RootDiagnostics().Single(), "test compiler" );
		File.WriteAllText( Path.Combine( root, "obj", "nested", "generated.cs" ), "source" );
		await Until( () => assetChanges.Contains( "/obj/nested/generated.cs" ) );
		Assert.AreEqual( 0, sourceChanges.Count );
		b.Dispose();
		FileWatch.Tick();
		StringAssert.Contains( RootDiagnostics().Single(), "directories=1" );
	}

	[TestMethod]
	public async Task MissingRootRecoversWithoutRecreatingSubscriptions()
	{
		var fs = Own( new LocalFileSystem( root ) );
		Directory.Delete( root );
		using var watch = fs.Watch();
		var changes = new List<string>();
		watch.OnChangedFile += changes.Add;
		Assert.IsTrue( RootDiagnostics().Single().Contains( "retry pending" ) );
		Directory.CreateDirectory( root );
		await Until( () => RootDiagnostics().Single().Contains( "state=watching" ), 12 );
		File.WriteAllText( Path.Combine( root, "recovered.cs" ), "source" );
		await Until( () => changes.Contains( "/recovered.cs" ) );
	}
	[TestMethod]
	public async Task RecoveryRescansChangesAndDeletionsDuringAnInterruption()
	{
		var oldFile = Path.Combine( root, "old.cs" );
		File.WriteAllText( oldFile, "old" );
		var fs = Own( new LocalFileSystem( root ) );
		using var watch = fs.Watch();
		var changes = new List<string>();
		watch.OnChangedFile += changes.Add;
		var offline = root + ".offline";
		try
		{
			Directory.Move( root, offline );
			await Until( () => RootDiagnostics().Single().Contains( "retry pending" ) );
			Directory.CreateDirectory( root );
			File.WriteAllText( Path.Combine( root, "new.cs" ), "new" );
			await Until( () => changes.Contains( "/old.cs" ) && changes.Contains( "/new.cs" ), 12 );
			Assert.IsFalse( File.Exists( oldFile ) );
			StringAssert.Contains( RootDiagnostics().Single(), "rescans=1" );
		}
		finally
		{
			if ( Directory.Exists( offline ) ) Directory.Delete( offline, true );
		}
	}

	[TestMethod]
	public async Task ExplicitRescanRetainsFiltersAndVirtualPathMapping()
	{
		File.WriteAllText( Path.Combine( root, "source.cs" ), "source" );
		File.WriteAllText( Path.Combine( root, "other.txt" ), "other" );
		var fs = Own( new LocalFileSystem( root ) );
		using var watch = fs.Watch( "*.cs" );
		var changes = new List<string>();
		watch.OnChangedFile += changes.Add;
		SharedPhysicalFileWatcher.RequestRescan( root );
		await Until( () => changes.Contains( "/source.cs" ) );
		Assert.IsFalse( changes.Contains( "/other.txt" ) );
		StringAssert.Contains( RootDiagnostics().Single(), "rescans=1" );
	}

	[TestMethod]
	public async Task SymbolicRootAliasesShareBackendAndKeepTheirOwnVirtualPaths()
	{
		var actual = Path.Combine( root, "actual" );
		var alias = Path.Combine( root, "alias" );
		Directory.CreateDirectory( actual );
		Directory.CreateSymbolicLink( alias, actual );
		var first = Own( new LocalFileSystem( actual ) );
		var second = Own( new LocalFileSystem( alias ) );
		using var a = first.Watch();
		using var b = second.Watch();
		var left = new List<string>();
		var right = new List<string>();
		a.OnChangedFile += left.Add;
		b.OnChangedFile += right.Add;
		Assert.AreEqual( 1, RootDiagnostics().Length );
		File.WriteAllText( Path.Combine( actual, "source.cs" ), "source" );
		await Until( () => left.Contains( "/source.cs" ) && right.Contains( "/source.cs" ) );
		StringAssert.Contains( RootDiagnostics().Single(), "subscriptions=2" );
		Assert.AreEqual( 2, BaseFileSystem.GetWatchDiagnostics().Count( x => x.Contains( "subscriptions=1" ) ) );
	}

	[TestMethod]
	public async Task MovingDirectoryOutInvalidatesFileOnlySubscriptions()
	{
		var directory = Path.Combine( root, "source" );
		Directory.CreateDirectory( Path.Combine( directory, "nested" ) );
		File.WriteAllText( Path.Combine( directory, "first.cs" ), "first" );
		File.WriteAllText( Path.Combine( directory, "nested", "second.cs" ), "second" );
		var fs = Own( new LocalFileSystem( root ) );
		using var watch = fs.Watch( "*.cs" );
		var changes = new List<string>();
		watch.OnChangedFile += changes.Add;
		var outside = root + ".outside";
		try
		{
			Directory.Move( directory, outside );
			await Until( () => changes.Contains( "/source/first.cs" ) && changes.Contains( "/source/nested/second.cs" ) );
		}
		finally
		{
			if ( Directory.Exists( outside ) ) Directory.Delete( outside, true );
		}
	}

	[TestMethod]
	public async Task DefaultWatchIncludesFilesWithoutAnExtension()
	{
		var fs = Own( new LocalFileSystem( root ) );
		using var watch = fs.Watch();
		var changes = new List<string>();
		watch.OnChangedFile += changes.Add;
		File.WriteAllText( Path.Combine( root, "README" ), "source" );
		await Until( () => changes.Contains( "/readme" ) );
	}

	[TestMethod]
	public async Task SharedCoverageMapsRenamesAcrossAConsumersExclusionBoundary()
	{
		Directory.CreateDirectory( Path.Combine( root, "obj" ) );
		var excludedPath = Path.Combine( root, "obj", "source.cs" );
		var includedPath = Path.Combine( root, "source.cs" );
		File.WriteAllText( excludedPath, "source" );
		var compiler = Own( new LocalFileSystem( root ) );
		compiler.ConfigureWatch( "compiler", new[] { "obj" } );
		var assets = Own( new LocalFileSystem( root ) );
		using var sourceWatch = compiler.Watch( "*.cs" );
		using var assetWatch = assets.Watch();
		var changes = new List<string>();
		sourceWatch.OnChangedFile += changes.Add;
		FileWatch.Tick();

		File.Move( excludedPath, includedPath );
		await Until( () => changes.Contains( "/source.cs" ) );
		Assert.IsFalse( changes.Contains( "/obj/source.cs" ) );
		changes.Clear();
		File.Move( includedPath, excludedPath );
		await Until( () => changes.Contains( "/source.cs" ) );
		Assert.IsFalse( changes.Contains( "/obj/source.cs" ) );
		Assert.AreEqual( 1, RootDiagnostics().Length );
	}

	[TestMethod]
	public void DynamicMountIdentifiesTheWatchingAggregateInDiagnostics()
	{
		var aggregate = Own( new AggregateFileSystem() );
		using var watch = aggregate.Watch();
		var identity = BaseFileSystem.GetWatchDiagnostics().Single( x => x.StartsWith( "AggregateFileSystem#", StringComparison.Ordinal ) ).Split( ':' )[0];
		aggregate.Mount( Own( new LocalFileSystem( root ) ) );
		StringAssert.Contains( RootDiagnostics().Single(), identity );
	}

	[TestMethod]
	public async Task DirectoryRenameNotifiesSubscriptionsMatchingOnlyTheNewPath()
	{
		var oldPath = Path.Combine( root, "old" );
		Directory.CreateDirectory( oldPath );
		File.WriteAllText( Path.Combine( oldPath, "source.cs" ), "source" );
		var fs = Own( new LocalFileSystem( root ) );
		using var watch = fs.Watch( "/new/*.cs" );
		var observed = new List<string>();
		watch.OnChangedFile += observed.Add;
		Directory.Move( oldPath, Path.Combine( root, "new" ) );
		await Until( () => observed.Contains( "/new/source.cs" ) );
	}

}
