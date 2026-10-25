using System.IO;
using Zio;
using Zio.FileSystems;

namespace Sandbox;

/// <summary>
/// Zio subscriptions retain their own filesystem/path mapping while identical physical roots
/// share a reference-counted Linux backend. No consumer callback runs under the registry lock.
/// </summary>
internal sealed class SharedPhysicalFileWatcher : Zio.FileSystems.FileSystemWatcher
{
	internal static readonly System.Threading.AsyncLocal<string> OwnerContext = new();
	static readonly object gate = new();
	static readonly Dictionary<string, Root> roots = new( StringComparer.Ordinal );
	static long created;
	static long disposed;
	static long failures;
	static long overflows;
	static long backendStarts;
	static long backendStops;

	readonly PhysicalFileSystem physical;
	readonly string nativePath;
	readonly string backendPath;
	readonly string[] excluded;
	readonly string key;
	readonly string owner;
	Root root;
	bool isDisposed;

	internal SharedPhysicalFileWatcher( PhysicalFileSystem physical, UPath path, IEnumerable<string> excludedFolders, string owner )
		: base( physical, path )
	{
		this.physical = physical;
		nativePath = System.IO.Path.TrimEndingDirectorySeparator( System.IO.Path.GetFullPath( physical.ConvertPathToInternal( path ) ) );
		excluded = excludedFolders.Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.OrdinalIgnoreCase ).ToArray();
		backendPath = LinuxDirectoryWatcher.CanonicalizePath( nativePath );
		key = backendPath;
		this.owner = $"{OwnerContext.Value ?? "physical"} ({owner})";
	}

	public override bool EnableRaisingEvents
	{
		get => base.EnableRaisingEvents;
		set
		{
			lock ( gate )
			{
				ObjectDisposedException.ThrowIf( isDisposed, this );
				if ( base.EnableRaisingEvents == value ) return;
				base.EnableRaisingEvents = value;
				if ( value )
				{
					if ( !roots.TryGetValue( key, out root ) )
					{
						root = new Root( backendPath, excluded );
						roots.Add( key, root );
					}

					root.Subscribers.Add( this );
					root.RefreshSubscribers();
					root.TryStart();
				}
				else
				{
					Release();
				}
			}
		}
	}

	void Release()
	{
		if ( root is null ) return;
		root.Subscribers.Remove( this );
		root.RefreshSubscribers();
		if ( root.Subscribers.Count == 0 )
		{
			root.Stop();
			roots.Remove( key );
			disposed++;
		}
		root = null;
	}

	protected override void Dispose( bool disposing )
	{
		if ( disposing )
		{
			lock ( gate )
			{
				if ( isDisposed ) return;
				base.EnableRaisingEvents = false;
				Release();
				isDisposed = true;
			}
		}
		base.Dispose( disposing );
	}

	bool Includes( string path, bool isDirectory )
	{
		if ( excluded.Length == 0 ) return true;
		var relative = path.AsSpan( backendPath.Length ).TrimStart( '/' );
		while ( !relative.IsEmpty )
		{
			var slash = relative.IndexOf( '/' );
			if ( slash < 0 && !isDirectory ) return true;
			var component = slash < 0 ? relative : relative[..slash];
			foreach ( var name in excluded )
				if ( component.Equals( name.AsSpan(), StringComparison.OrdinalIgnoreCase ) ) return false;
			if ( slash < 0 ) break;
			relative = relative[(slash + 1)..];
		}
		return true;
	}

	void Receive( FileSystemEventArgs args )
	{
		if ( !EnableRaisingEvents ) return;
		var isDirectory = args switch
		{
			LinuxDirectoryWatcher.ChangeEvent change => change.IsDirectory,
			LinuxDirectoryWatcher.RenameEvent rename => rename.IsDirectory,
			_ => Directory.Exists( args.FullPath )
		};
		var included = Includes( args.FullPath, isDirectory );
		if ( args is RenamedEventArgs renameEvent )
		{
			var oldIncluded = Includes( renameEvent.OldFullPath, isDirectory );
			if ( included && oldIncluded )
				RaiseRenamed( new FileRenamedEventArgs( physical, Zio.WatcherChangeTypes.Renamed,
					physical.ConvertPathFromInternal( MapPath( args.FullPath ) ), physical.ConvertPathFromInternal( MapPath( renameEvent.OldFullPath ) ) ) );
			else if ( included )
				RaiseCreated( new FileChangedEventArgs( physical, Zio.WatcherChangeTypes.Created, physical.ConvertPathFromInternal( MapPath( args.FullPath ) ) ) );
			else if ( oldIncluded )
				RaiseDeleted( new FileChangedEventArgs( physical, Zio.WatcherChangeTypes.Deleted, physical.ConvertPathFromInternal( MapPath( renameEvent.OldFullPath ) ) ) );
			return;
		}
		if ( !included ) return;
		var changeEvent = new FileChangedEventArgs( physical, (Zio.WatcherChangeTypes)args.ChangeType, physical.ConvertPathFromInternal( MapPath( args.FullPath ) ) );
		switch ( args.ChangeType )
		{
			case System.IO.WatcherChangeTypes.Created: RaiseCreated( changeEvent ); break;
			case System.IO.WatcherChangeTypes.Deleted: RaiseDeleted( changeEvent ); break;
			default: RaiseChanged( changeEvent ); break;
		}
	}

	string MapPath( string path ) => nativePath == backendPath ? path
		: System.IO.Path.Combine( nativePath, System.IO.Path.GetRelativePath( backendPath, path ) );

	/// <summary>Called by FileWatch.Tick even when there are no pending file changes.</summary>
	internal static void Tick()
	{
		List<(SharedPhysicalFileWatcher[] Subscribers, Exception Error)> errors = null;
		List<Root> rescans = null;
		lock ( gate )
		{
			foreach ( var root in roots.Values )
			{
				root.TryStart();
				if ( root.Backend != null && root.RescanPending )
				{
					root.RescanPending = false;
					root.ChangesDuringScan = new HashSet<string>( StringComparer.Ordinal );
					(rescans ??= new()).Add( root );
				}
				if ( root.PendingError is null ) continue;
				(errors ??= new()).Add( (root.SubscriberSnapshot, root.PendingError) );
				root.PendingError = null;
			}
		}

		if ( rescans != null )
			foreach ( var root in rescans ) root.Rescan();

		foreach ( var entry in errors ?? Enumerable.Empty<(SharedPhysicalFileWatcher[] Subscribers, Exception Error)>() )
		{
			Log.Warning( $"File watching: {entry.Error.Message}" );
			foreach ( var subscriber in entry.Subscribers )
			{
				try
				{
					if ( subscriber.EnableRaisingEvents )
						subscriber.RaiseError( new FileSystemErrorEventArgs( entry.Error ) );
				}
				catch ( Exception e ) { Log.Warning( $"File watch error handler failed: {e.Message}" ); }
			}
		}
	}

	internal static void RequestRescan( string path )
	{
		lock ( gate )
		{
			foreach ( var root in roots.Values.Where( x => x.Path == path ) )
				root.RescanPending = true;
		}
	}

	internal static string[] GetDiagnostics()
	{
		lock ( gate )
		{
			return new[] { $"Physical watcher roots={roots.Count}, created={created}, disposed={disposed}, backend starts={backendStarts}, backend stops={backendStops}, failures={failures}, overflows={overflows}" }
				.Concat( roots.Values.Select( root => $"{root.Path}: subscriptions={root.Subscribers.Count}, directories={root.Backend?.DirectoryCount ?? 0}, events={root.Events}, rescans={root.Rescans}, recoveryEvents={root.RecoveryEvents}, state={(root.Backend is null ? "retry pending" : "watching")}, kernelExcluded=[{string.Join( ",", root.Excluded )}], owners=[{string.Join( ";", root.Subscribers.Select( x => $"{x.owner} excludes=[{string.Join( ",", x.excluded )}]" ) )}], lastError={root.LastError}" ) )
				.ToArray();
		}
	}

	sealed class Root
	{
		internal readonly string Path;
		internal string[] Excluded;
		internal readonly HashSet<SharedPhysicalFileWatcher> Subscribers = new();
		internal SharedPhysicalFileWatcher[] SubscriberSnapshot = Array.Empty<SharedPhysicalFileWatcher>();
		internal void RefreshSubscribers()
		{
			SubscriberSnapshot = Subscribers.ToArray();
			if ( SubscriberSnapshot.Length == 0 ) return;
			IEnumerable<string> exclusions = SubscriberSnapshot[0].excluded;
			foreach ( var subscriber in SubscriberSnapshot.Skip( 1 ) )
				exclusions = exclusions.Intersect( subscriber.excluded, StringComparer.OrdinalIgnoreCase );
			var desired = exclusions.Order( StringComparer.OrdinalIgnoreCase ).ToArray();
			if ( Excluded.SequenceEqual( desired, StringComparer.OrdinalIgnoreCase ) ) return;
			Excluded = desired;
			Stop();
			retryAt = 0;
			RescanPending = true;
			TryStart();
		}
		internal LinuxDirectoryWatcher Backend;
		internal Exception PendingError;
		internal string LastError;
		internal long Events;
		internal long Rescans;
		internal long RecoveryEvents;
		internal bool RescanPending;
		internal HashSet<string> ChangesDuringScan;
		HashSet<string> knownEntries = new( StringComparer.Ordinal );
		long retryAt;
		long generation;

		internal Root( string path, string[] excluded )
		{
			Path = path;
			Excluded = excluded;
			created++;
		}

		internal void TryStart()
		{
			if ( Backend != null || Stopwatch.GetTimestamp() < retryAt ) return;
			try
			{
				var version = ++generation;
				Backend = new LinuxDirectoryWatcher( Path, Excluded, args => OnBackendChange( args, version ), error => OnBackendError( error, version ) );
				backendStarts++;
				if ( LastError != null )
				{
					RescanPending = true;
					PendingError = new IOException( $"Watching resumed for {Path}; reconciling files after the interruption" );
				}
				knownEntries.UnionWith( Backend.InitialEntries );
			}
			catch ( IOException e )
			{
				OnError( e );
			}
			catch ( UnauthorizedAccessException e )
			{
				OnError( e );
			}
		}

		void OnBackendChange( FileSystemEventArgs args, long version ) => OnChange( args, remember: true, version );

		internal void Stop()
		{
			generation++;
			if ( Backend != null )
			{
				Backend.Dispose();
				backendStops++;
				Backend = null;
			}
		}

		void OnBackendError( Exception error, long version )
		{
			lock ( gate )
			{
				if ( version != generation ) return;
				OnError( error );
			}
		}

		void OnError( Exception error )
		{
			lock ( gate )
			{
				PendingError = error;
				LastError = error.Message;
				if ( error is InternalBufferOverflowException )
				{
					overflows++;
					RescanPending = true;
					return;
				}

				failures++;
				Stop();
				retryAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
			}
		}

		internal void Rescan()
		{
			try
			{
				LinuxDirectoryWatcher backend;
				long version;
				lock ( gate ) { backend = Backend; version = generation; }
				if ( backend is null ) return;
				var current = backend.ScanEntries().ToHashSet( StringComparer.Ordinal );
				string[] changed;
				lock ( gate )
				{
					if ( version != generation || !ReferenceEquals( backend, Backend ) ) return;
					changed = knownEntries.Union( current ).ToArray();
					current.UnionWith( ChangesDuringScan ?? Enumerable.Empty<string>() );
					knownEntries = current;
					ChangesDuringScan = null;
					Rescans++;
				}

				// Conservatively invalidate all surviving entries and previously known deletions.
				// Delivery goes through each subscription's usual mapping, filters and batching.
				foreach ( var path in changed )
					OnChange( new FileSystemEventArgs( current.Contains( path ) ? System.IO.WatcherChangeTypes.Changed : System.IO.WatcherChangeTypes.Deleted,
						System.IO.Path.GetDirectoryName( path ), System.IO.Path.GetFileName( path ) ), remember: false );
			}
			catch ( IOException e ) { OnError( e ); }
			catch ( UnauthorizedAccessException e ) { OnError( e ); }
		}

		string[] ForgetTree( string path, bool isDirectory )
		{
			knownEntries.Remove( path );
			if ( !isDirectory ) return Array.Empty<string>();
			var removed = knownEntries.Where( entry => entry.StartsWith( path + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal ) ).ToArray();
			foreach ( var entry in removed ) knownEntries.Remove( entry );
			return removed;
		}

		void OnChange( FileSystemEventArgs args ) => OnChange( args, remember: true );

		void OnChange( FileSystemEventArgs args, bool remember, long version = 0 )
		{
			SharedPhysicalFileWatcher[] subscribers;
			string[] removedEntries = Array.Empty<string>();
			lock ( gate )
			{
				if ( version != 0 && version != generation ) return;
				if ( remember )
				{
					Events++;
					if ( args.ChangeType == System.IO.WatcherChangeTypes.Deleted )
						removedEntries = ForgetTree( args.FullPath, args is LinuxDirectoryWatcher.ChangeEvent { IsDirectory: true } );
					else
						knownEntries.Add( args.FullPath );
					ChangesDuringScan?.Add( args.FullPath );
					if ( args is RenamedEventArgs rename )
					{
						removedEntries = ForgetTree( rename.OldFullPath, args is LinuxDirectoryWatcher.RenameEvent { IsDirectory: true } );
						ChangesDuringScan?.Add( rename.OldFullPath );
					}
				}
				else RecoveryEvents++;
				subscribers = SubscriberSnapshot;
			}

			// Moving a directory out of the tree produces no individual file deletions from
			// inotify. Invalidate its known children so *.cs and other file-only subscriptions
			// still notice that those files disappeared.
			foreach ( var path in removedEntries )
				OnChange( new FileSystemEventArgs( System.IO.WatcherChangeTypes.Deleted,
					System.IO.Path.GetDirectoryName( path ), System.IO.Path.GetFileName( path ) ), remember: false );

			foreach ( var subscriber in subscribers )
			{
				try { subscriber.Receive( args ); }
				catch ( Exception e ) { Log.Warning( $"File watch subscriber failed for {Path}: {e.Message}" ); }
			}
		}
	}
}
