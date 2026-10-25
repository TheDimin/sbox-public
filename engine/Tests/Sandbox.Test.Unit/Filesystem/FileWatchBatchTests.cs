using System.Collections.Generic;

namespace FilesystemTests;

[TestClass]
[DoNotParallelize]
public class FileWatchBatchTests
{
	private MemoryFileSystem files;
	private FileWatch watch;
	private readonly List<string> delivered = new();

	[TestInitialize]
	public void Initialize()
	{
		files = new MemoryFileSystem();
		watch = files.Watch();
		watch.OnChangedFile += delivered.Add;
	}

	[TestCleanup]
	public void Cleanup()
	{
		watch.Dispose();
		files.Dispose();
		FileWatch.Tick();
	}

	[TestMethod]
	public void QuietPeriodDefersShortBursts()
	{
		files.AddChangedFile( "/first.cs" );
		FileWatch.TimeSinceFirstChange = 0.2f;
		FileWatch.TimeSinceLastChange = 0;
		FileWatch.Tick();
		Assert.AreEqual( 0, delivered.Count );

		FileWatch.TimeSinceLastChange = 0.2f;
		FileWatch.Tick();
		CollectionAssert.AreEqual( new[] { "/first.cs" }, delivered );
	}

	[TestMethod]
	public void ContinuousChangesCannotStarveDelivery()
	{
		files.AddChangedFile( "/first.cs" );
		FileWatch.TimeSinceFirstChange = 1;
		files.AddChangedFile( "/second.cs" );
		FileWatch.TimeSinceLastChange = 0;
		FileWatch.Tick();
		CollectionAssert.AreEquivalent( new[] { "/first.cs", "/second.cs" }, delivered );

		// A new batch gets its own deadline instead of immediately firing again.
		files.AddChangedFile( "/third.cs" );
		FileWatch.Tick();
		Assert.AreEqual( 2, delivered.Count );
	}

	[TestMethod]
	public void ScopedSuppressionOverridesDeadlineWithoutDroppingChanges()
	{
		using ( FileWatch.Suppress() )
		{
			files.AddChangedFile( "/first.cs" );
			FileWatch.TimeSinceFirstChange = 1;
			FileWatch.TimeSinceLastChange = 0;
			FileWatch.Tick();
			Assert.AreEqual( 0, delivered.Count );
		}

		FileWatch.Tick();
		CollectionAssert.AreEqual( new[] { "/first.cs" }, delivered );
	}

	[TestMethod]
	public void TimedSuppressionDiscardsEvenAnActiveBurst()
	{
		try
		{
			FileWatch.SuppressWatchers = RealTime.Now + 10;
			files.AddChangedFile( "/first.cs" );
			FileWatch.TimeSinceLastChange = 0;
			FileWatch.Tick();
			Assert.AreEqual( 0, files.changedFiles.Count );
			Assert.IsFalse( FileWatch.WithChanges.Contains( files ) );
		}
		finally
		{
			FileWatch.SuppressWatchers = 0;
		}

		FileWatch.TimeSinceLastChange = 1;
		FileWatch.Tick();
		Assert.AreEqual( 0, delivered.Count );
	}
	[TestMethod]
	public void FailedWatcherCreationRetriesWithoutRecreatingTheSubscription()
	{
		using var failing = new RecoverableMemoryFileSystem();
		var fs = new BaseFileSystem( failing );
		var previousTime = RealTime.SimulatedNow;
		try
		{
			RealTime.SimulatedNow = 100;
			using var subscription = fs.Watch();
			var observed = new List<string>();
			subscription.OnChangedFile += observed.Add;
			fs.WriteAllText( "/missed.cs", "source" );
			failing.Fail = false;
			RealTime.SimulatedNow = 106;
			FileWatch.Tick();
			RealTime.SimulatedNow = 107;
			FileWatch.Tick();
			CollectionAssert.AreEqual( new[] { "/missed.cs" }, observed );
		}
		finally
		{
			fs.Dispose();
			RealTime.SimulatedNow = previousTime;
		}
	}

	[TestMethod]
	public void LastSubscriptionDisposalDropsItsPendingBatch()
	{
		files.AddChangedFile( "/old.cs" );
		watch.Dispose();
		watch = files.Watch();
		watch.OnChangedFile += delivered.Add;
		FileWatch.TimeSinceLastChange = 1;
		FileWatch.Tick();
		Assert.AreEqual( 0, delivered.Count );
	}

	[TestMethod]
	public void DiagnosticsIncludeLogicalCountsFiltersAndCallbackOwners()
	{
		using var diagnosticWatch = files.Watch( "*.cs" );
		diagnosticWatch.OnChangedFile += OnDiagnosticFileChange;
		var row = System.Linq.Enumerable.Single( BaseFileSystem.GetWatchDiagnostics(), value => value.Contains( nameof( OnDiagnosticFileChange ) ) );
		StringAssert.Contains( row, "subscriptions=2" );
		StringAssert.Contains( row, "*.cs" );
		StringAssert.Contains( row, nameof( FileWatchBatchTests ) );
	}

	static void OnDiagnosticFileChange( string file ) { }

	[TestMethod]
	public async System.Threading.Tasks.Task ResubscribingAfterFailureDoesNotLoseTheRetry()
	{
		using var backing = new RecoverableMemoryFileSystem();
		var fs = new BaseFileSystem( backing );
		var previousTime = RealTime.SimulatedNow;
		try
		{
			RealTime.SimulatedNow = 100;
			var failed = fs.Watch();
			failed.Dispose();
			backing.Fail = false;
			using var recovered = fs.Watch();
			var observed = new List<string>();
			recovered.OnChangedFile += observed.Add;
			fs.WriteAllText( "/new.cs", "source" );
			Assert.IsTrue( await FileWatch.TickUntilFileChanged( "/new.cs" ) );
			CollectionAssert.AreEqual( new[] { "/new.cs" }, observed );
		}
		finally
		{
			fs.Dispose();
			RealTime.SimulatedNow = previousTime;
		}
	}

	[TestMethod]
	public void DisposedFilesystemRejectsNewSubscriptions()
	{
		files.Dispose();
		Assert.ThrowsException<System.ObjectDisposedException>( () => files.Watch() );
	}

	sealed class RecoverableMemoryFileSystem : Zio.FileSystems.MemoryFileSystem
	{
		internal bool Fail = true;
		protected override Zio.IFileSystemWatcher WatchImpl( Zio.UPath path )
		{
			if ( Fail ) throw new System.IO.IOException( "simulated watcher exhaustion" );
			return base.WatchImpl( path );
		}
	}

}
