using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace FilesystemTests;

[TestClass]
public class LinuxDirectoryWatcherTests
{
	string root;
	LinuxDirectoryWatcher watcher;
	readonly ConcurrentQueue<FileSystemEventArgs> changes = new();
	readonly ConcurrentQueue<Exception> errors = new();

	[TestInitialize]
	public void Initialize()
	{
		if ( !OperatingSystem.IsLinux() ) Assert.Inconclusive( "Linux inotify backend" );
		root = Path.Combine( Path.GetTempPath(), "sbox-linux-watch-tests", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
	}

	[TestCleanup]
	public async Task Cleanup()
	{
		watcher?.Dispose();
		if ( watcher != null ) await Until( () => watcher.DirectoryCount == 0 );
		if ( root != null && Directory.Exists( root ) ) Directory.Delete( root, true );
	}

	void Start( params string[] excluded ) => watcher = new LinuxDirectoryWatcher( root, excluded, changes.Enqueue, errors.Enqueue );

	static async Task Until( Func<bool> condition )
	{
		var timer = Stopwatch.StartNew();
		while ( !condition() && timer.Elapsed.TotalSeconds < 5 ) await Task.Delay( 10 );
		Assert.IsTrue( condition(), "Expected watcher state was not reached within five seconds" );
	}

	[TestMethod]
	public async Task ExcludedTreesNeverConsumeDirectoryWatches()
	{
		Directory.CreateDirectory( Path.Combine( root, "obj", "nested" ) );
		Directory.CreateDirectory( Path.Combine( root, "Code" ) );
		Start( "OBJ" );
		Assert.AreEqual( 2, watcher.DirectoryCount );

		Directory.CreateDirectory( Path.Combine( root, "Code", "obj", "nested" ) );
		File.WriteAllText( Path.Combine( root, "Code", "obj", "nested", "ignored.cs" ), "ignored" );
		File.WriteAllText( Path.Combine( root, "Code", "kept.cs" ), "kept" );
		await Until( () => changes.Any( x => x.Name == "kept.cs" ) );
		Assert.AreEqual( 2, watcher.DirectoryCount );
		Assert.IsFalse( changes.Any( x => x.FullPath.Contains( "/obj/", StringComparison.OrdinalIgnoreCase ) ) );
		Assert.AreEqual( 0, errors.Count );
	}

	[TestMethod]
	public async Task NewDirectoriesCatchFilesCreatedBeforeRegistration()
	{
		Start();
		var directory = Path.Combine( root, "new", "nested" );
		Directory.CreateDirectory( directory );
		var file = Path.Combine( directory, "already-created.cs" );
		File.WriteAllText( file, "source" );
		await Until( () => changes.Any( x => x.FullPath == file ) );
		await Until( () => watcher.DirectoryCount == 3 );
		Assert.AreEqual( 0, errors.Count );
	}

	[TestMethod]
	public async Task RenamesPreserveBothPathsAcrossDirectories()
	{
		var left = Path.Combine( root, "left" );
		var right = Path.Combine( root, "right" );
		Directory.CreateDirectory( left );
		Directory.CreateDirectory( right );
		var oldPath = Path.Combine( left, "source.cs" );
		var newPath = Path.Combine( right, "renamed.cs" );
		File.WriteAllText( oldPath, "source" );
		Start();
		File.Move( oldPath, newPath );
		await Until( () => changes.OfType<RenamedEventArgs>().Any( x => x.FullPath == newPath && x.OldFullPath == oldPath ) );
		Assert.AreEqual( 0, errors.Count );
	}

	[TestMethod]
	public async Task RenamedDirectoryKeepsRecursiveCoverage()
	{
		var original = Path.Combine( root, "original", "nested" );
		Directory.CreateDirectory( original );
		Start();
		Directory.Move( Path.Combine( root, "original" ), Path.Combine( root, "renamed" ) );
		var file = Path.Combine( root, "renamed", "nested", "source.cs" );
		File.WriteAllText( file, "source" );
		await Until( () => changes.Any( x => x.FullPath == file ) );
		await Until( () => watcher.DirectoryCount == 3 );
		File.AppendAllText( file, "changed" );
		await Until( () => changes.Any( x => x.FullPath == file && x.ChangeType == WatcherChangeTypes.Changed ) );
		Assert.AreEqual( 0, errors.Count );
	}

	[TestMethod]
	public async Task MovingOutOfTheTreeEventuallyReportsDeletion()
	{
		var oldPath = Path.Combine( root, "source.cs" );
		var outside = root + ".outside";
		File.WriteAllText( oldPath, "source" );
		Start();
		try
		{
			File.Move( oldPath, outside );
			await Until( () => changes.Any( x => x.FullPath == oldPath && x.ChangeType == WatcherChangeTypes.Deleted ) );
		}
		finally
		{
			File.Delete( outside );
		}
		Assert.AreEqual( 0, errors.Count );
	}

	[TestMethod]
	public async Task RemovingRootReportsFailureAndReleasesWatches()
	{
		Start();
		Directory.Delete( root );
		await Until( () => errors.Any( x => x is DirectoryNotFoundException ) );
		await Until( () => watcher.DirectoryCount == 0 );
		watcher.Dispose();
		watcher.Dispose();
	}

	[TestMethod]
	public async Task SymlinkTreesAreNotTraversed()
	{
		Directory.CreateDirectory( Path.Combine( root, "actual", "nested" ) );
		Directory.CreateSymbolicLink( Path.Combine( root, "alias" ), Path.Combine( root, "actual" ) );
		Start();
		Assert.AreEqual( 3, watcher.DirectoryCount );
		watcher.Dispose();
		await Until( () => watcher.DirectoryCount == 0 );
	}
	[TestMethod]
	public void KernelOverflowRecordRequestsRecoveryWithoutDroppingFollowingRecords()
	{
		var records = new[] {
			Record( 7, 0x40, 12, "old-名前.cs" ),
			Record( -1, 0x4000, 0, "" ),
			Record( 8, 0x80, 12, "new-名前.cs" )
		}.SelectMany( x => x ).ToArray();
		var notifications = new System.Collections.Generic.List<LinuxDirectoryWatcher.Notification>();
		var failures = new System.Collections.Generic.List<Exception>();
		var reconcile = LinuxDirectoryWatcher.DecodeEvents( records, records.Length, value => { notifications.Add( value ); return false; }, failures.Add, root );
		Assert.IsTrue( reconcile );
		Assert.AreEqual( 1, failures.Count );
		Assert.IsInstanceOfType<InternalBufferOverflowException>( failures[0] );
		StringAssert.Contains( failures[0].Message, root );
		Assert.AreEqual( 2, notifications.Count );
		Assert.AreEqual( "old-名前.cs", notifications[0].Name );
		Assert.AreEqual( "new-名前.cs", notifications[1].Name );
		Assert.AreEqual( notifications[0].Cookie, notifications[1].Cookie );
	}

	[TestMethod]
	public void TruncatedKernelRecordsFailInsteadOfReadingPastTheBuffer()
	{
		var record = Record( 1, 0x100, 0, "source.cs" );
		Assert.ThrowsException<InvalidDataException>( () => LinuxDirectoryWatcher.DecodeEvents( record, 8, _ => false, _ => { }, root ) );
		Assert.ThrowsException<InvalidDataException>( () => LinuxDirectoryWatcher.DecodeEvents( record, record.Length - 1, _ => false, _ => { }, root ) );
	}

	[TestMethod]
	public void KernelUnmountRecordRequestsRecoveryWithAVisibleFailure()
	{
		var record = Record( 7, 0x2000, 0, "" );
		var failures = new System.Collections.Generic.List<Exception>();
		var reconcile = LinuxDirectoryWatcher.DecodeEvents( record, record.Length, _ => false, failures.Add, root );
		Assert.IsTrue( reconcile );
		Assert.AreEqual( 1, failures.Count );
		Assert.IsInstanceOfType<IOException>( failures[0] );
		StringAssert.Contains( failures[0].Message, "unmounted" );
		StringAssert.Contains( failures[0].Message, root );
	}

	[TestMethod]
	public async Task DirectorySwapsKeepWatchesOnTheCurrentInodes()
	{
		var left = Path.Combine( root, "left" );
		var right = Path.Combine( root, "right" );
		var temporary = Path.Combine( root, "temporary" );
		Directory.CreateDirectory( Path.Combine( left, "nested" ) );
		Directory.CreateDirectory( Path.Combine( right, "nested" ) );
		var pauseFile = Path.Combine( root, "pause.cs" );
		File.WriteAllText( pauseFile, "initial" );
		using var entered = new System.Threading.ManualResetEventSlim();
		using var resume = new System.Threading.ManualResetEventSlim();
		var paused = 0;
		watcher = new LinuxDirectoryWatcher( root, Array.Empty<string>(), change =>
		{
			if ( change.Name == "pause.cs" && System.Threading.Interlocked.Exchange( ref paused, 1 ) == 0 )
			{
				entered.Set();
				resume.Wait( TimeSpan.FromSeconds( 10 ) );
			}
			changes.Enqueue( change );
		}, errors.Enqueue );
		try
		{
			File.AppendAllText( pauseFile, "pause" );
			Assert.IsTrue( entered.Wait( TimeSpan.FromSeconds( 5 ) ) );
			Directory.Move( left, temporary );
			Directory.Move( right, left );
			Directory.Move( temporary, right );
			resume.Set();
			await Until( () => changes.OfType<RenamedEventArgs>().Any( x => x.OldFullPath == temporary && x.FullPath == right ) );
			File.WriteAllText( Path.Combine( root, "barrier.cs" ), "barrier" );
			await Until( () => changes.Any( x => x.Name == "barrier.cs" ) );
			changes.Clear();
			var leftFile = Path.Combine( left, "nested", "left-edit.cs" );
			var rightFile = Path.Combine( right, "nested", "right-edit.cs" );
			File.WriteAllText( leftFile, "left" );
			File.WriteAllText( rightFile, "right" );
			await Until( () => changes.Any( x => x.FullPath == leftFile ) && changes.Any( x => x.FullPath == rightFile ) );
			Assert.AreEqual( 0, errors.Count );
		}
		finally
		{
			resume.Set();
			watcher.Dispose();
			await Until( () => watcher.DirectoryCount == 0 );
		}
	}

	[TestMethod]
	public async Task InaccessibleChildDoesNotDisableTheReadableTree()
	{
		if ( !OperatingSystem.IsLinux() ) return;
		var locked = Path.Combine( root, "locked" );
		Directory.CreateDirectory( locked );
		File.SetUnixFileMode( locked, UnixFileMode.None );
		try
		{
			Start();
			var file = Path.Combine( root, "readable.cs" );
			File.WriteAllText( file, "source" );
			await Until( () => changes.Any( x => x.FullPath == file ) );
			Assert.AreEqual( 0, errors.Count );
			File.SetUnixFileMode( locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
			await Until( () => watcher.DirectoryCount == 2 );
			var restored = Path.Combine( locked, "restored.cs" );
			File.WriteAllText( restored, "source" );
			await Until( () => changes.Any( x => x.FullPath == restored ) );
		}
		finally
		{
			File.SetUnixFileMode( locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
		}
	}

	static byte[] Record( int descriptor, uint mask, uint cookie, string name )
	{
		var bytes = System.Text.Encoding.UTF8.GetBytes( name );
		var length = name.Length == 0 ? 0 : (bytes.Length + 4) & ~3;
		var result = new byte[16 + length];
		BitConverter.GetBytes( descriptor ).CopyTo( result, 0 );
		BitConverter.GetBytes( mask ).CopyTo( result, 4 );
		BitConverter.GetBytes( cookie ).CopyTo( result, 8 );
		BitConverter.GetBytes( (uint)length ).CopyTo( result, 12 );
		bytes.CopyTo( result, 16 );
		return result;
	}

}
