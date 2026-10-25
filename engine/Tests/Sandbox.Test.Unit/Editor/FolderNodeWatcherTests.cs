using System;
using System.IO;
using System.Runtime.CompilerServices;

// Only the watcher partial is linked into this test assembly. This small node stand-in
// records Dirty calls; the watcher implementation and its OS watchers are the shipped code.
namespace Editor
{
	partial class FolderNode
	{
		internal int DirtyCount;
		public void Dirty() => DirtyCount++;
		internal void WatchFolder( string path ) => DirectoryWatcher.Watch( path, this );
		internal static void TickWatchers() => DirectoryWatcher.Tick();
		internal static void SweepWatchers() => DirectoryWatcher.Sweep();
		internal static int WatcherCount => DirectoryWatcher.RootCount;
		internal static void QueueChange( string path ) => DirectoryWatcher.QueueTestChange( path );
		internal static void FailWatchers() => DirectoryWatcher.FailTestWatchers();
		internal static void ResetWatchers() => DirectoryWatcher.ResetTestWatchers();

		static partial class DirectoryWatcher
		{
			internal static void QueueTestChange( string path ) => OnChanged( null,
				new FileSystemEventArgs( WatcherChangeTypes.Created, Path.GetDirectoryName( path ), Path.GetFileName( path ) ) );

			internal static void FailTestWatchers()
			{
				foreach ( var root in Roots.ToArray() ) OnError( root.Watcher, new ErrorEventArgs( new InternalBufferOverflowException( "test overflow" ) ) );
			}

			internal static void ResetTestWatchers()
			{
				lock ( Nodes )
				{
					foreach ( var root in Roots ) root.Watcher.Dispose();
					Roots.Clear();
					Nodes.Clear();
					PendingFolders.Clear();
				}
			}
		}
	}
}

namespace Editor.Tests
{
	[TestClass]
	[DoNotParallelize]
	public class FolderNodeWatcherTests
	{
		string root;

		[TestInitialize]
		public void Initialize()
		{
			FolderNode.ResetWatchers();
			root = Path.Combine( Path.GetTempPath(), "sbox-folder-watch-tests", Guid.NewGuid().ToString( "N" ) );
			Directory.CreateDirectory( root );
		}

		[TestCleanup]
		public void Cleanup()
		{
			FolderNode.ResetWatchers();
			Directory.Delete( root, true );
		}

		[TestMethod]
		public void ParentRootReplacesChildAndCoalescesNotificationsOnTick()
		{
			var child = Path.Combine( root, "child" );
			Directory.CreateDirectory( child );
			var childNode = new FolderNode();
			childNode.WatchFolder( child );
			var parentNode = new FolderNode();
			parentNode.WatchFolder( root );
			Assert.AreEqual( 1, FolderNode.WatcherCount );
			FolderNode.QueueChange( Path.Combine( child, "first.cs" ) );
			FolderNode.QueueChange( Path.Combine( child, "second.cs" ) );
			Assert.AreEqual( 0, childNode.DirtyCount );
			FolderNode.TickWatchers();
			Assert.AreEqual( 1, childNode.DirtyCount );
			Assert.AreEqual( 0, parentNode.DirtyCount );
			GC.KeepAlive( parentNode );
			GC.KeepAlive( childNode );
		}

		[TestMethod]
		public void DeadNodesReleaseTheirRootEvenWithoutFilesystemEvents()
		{
			var weak = CreateNode( root );
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
			Assert.IsFalse( weak.TryGetTarget( out _ ) );
			FolderNode.SweepWatchers();
			Assert.AreEqual( 0, FolderNode.WatcherCount );
		}

		[MethodImpl( MethodImplOptions.NoInlining )]
		static WeakReference<FolderNode> CreateNode( string path )
		{
			var node = new FolderNode();
			node.WatchFolder( path );
			return new WeakReference<FolderNode>( node );
		}

		[TestMethod]
		public void FailedCreationRetainsNodesForRetry()
		{
			var missing = Path.Combine( root, "missing" );
			var node = new FolderNode();
			node.WatchFolder( missing );
			Assert.AreEqual( 0, FolderNode.WatcherCount );
			Directory.CreateDirectory( missing );
			FolderNode.SweepWatchers();
			Assert.AreEqual( 1, FolderNode.WatcherCount );
			FolderNode.QueueChange( Path.Combine( missing, "file.cs" ) );
			FolderNode.TickWatchers();
			Assert.AreEqual( 1, node.DirtyCount );
		}

		[TestMethod]
		public void OverflowDirtiesNodesAndRecreatesCoverage()
		{
			var node = new FolderNode();
			node.WatchFolder( root );
			FolderNode.FailWatchers();
			Assert.AreEqual( 0, FolderNode.WatcherCount );
			FolderNode.TickWatchers();
			Assert.AreEqual( 1, node.DirtyCount );
			FolderNode.SweepWatchers();
			Assert.AreEqual( 1, FolderNode.WatcherCount );
			GC.KeepAlive( node );
		}
		[TestMethod]
		public void MissingRootIsRetriedEvenWithoutAnErrorEvent()
		{
			var node = new FolderNode();
			node.WatchFolder( root );
			Directory.Delete( root );
			FolderNode.SweepWatchers();
			Assert.AreEqual( 0, FolderNode.WatcherCount );
			Directory.CreateDirectory( root );
			FolderNode.SweepWatchers();
			Assert.AreEqual( 1, FolderNode.WatcherCount );
			GC.KeepAlive( node );
		}

	}
}
