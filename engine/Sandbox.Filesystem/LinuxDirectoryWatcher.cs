using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox;

/// <summary>
/// A Linux directory tree watch. Unlike FileSystemWatcher, excluded directories are never
/// registered with inotify. The worker owns the descriptor until it has stopped using it.
/// </summary>
internal sealed class LinuxDirectoryWatcher : IDisposable
{
	const uint Modify = 0x2;
	const uint Attrib = 0x4;
	const uint MovedFrom = 0x40;
	const uint MovedTo = 0x80;
	const uint Create = 0x100;
	const uint Delete = 0x200;
	const uint DeleteSelf = 0x400;
	const uint MoveSelf = 0x800;
	const uint Unmount = 0x2000;
	const uint Overflow = 0x4000;
	const uint Ignored = 0x8000;
	const uint IsDirectory = 0x40000000;
	const uint OnlyDirectory = 0x01000000;
	const uint DontFollow = 0x02000000;
	const uint Events = Modify | Attrib | MovedFrom | MovedTo | Create | Delete | DeleteSelf | MoveSelf | Unmount;

	readonly string root;
	readonly HashSet<string> excluded;
	readonly Dictionary<string, int> directories = new( StringComparer.Ordinal );
	readonly Dictionary<int, string> descriptors = new();
	readonly HashSet<string> relocatedDirectories = new( StringComparer.Ordinal );
	readonly Dictionary<uint, (string Path, long Time, bool IsDirectory)> moves = new();
	int stopping;
	readonly Thread worker;
	readonly int fd;
	readonly object disposalGate = new();
	int wakeFd = -1;
	readonly Action<FileSystemEventArgs> onChange;
	readonly Action<Exception> onError;
	int directoryCount;

	internal string[] InitialEntries { get; }

	internal int DirectoryCount => Volatile.Read( ref directoryCount );

	internal LinuxDirectoryWatcher( string path, IEnumerable<string> excludedFolders,
		Action<FileSystemEventArgs> onChange, Action<Exception> onError )
	{
		root = Path.TrimEndingDirectorySeparator( Path.GetFullPath( path ) );
		excluded = new HashSet<string>( excludedFolders, StringComparer.OrdinalIgnoreCase );
		this.onChange = onChange;
		this.onError = onError;
		fd = inotify_init1( 0x800 | 0x80000 ); // O_NONBLOCK | O_CLOEXEC
		if ( fd < 0 )
			throw NativeError( "create inotify instance", root );

		try
		{
			wakeFd = eventfd( 0, 0x800 | 0x80000 );
			if ( wakeFd < 0 ) throw NativeError( "create watcher cancellation event", root );
			ReconcileDirectories();
			InitialEntries = ScanEntries();
			worker = new Thread( Run ) { IsBackground = true, Name = "sbox file watch" };
			worker.Start();
		}
		catch
		{
			close( fd );
			if ( wakeFd >= 0 ) close( wakeFd );
			throw;
		}
	}

	void ReconcileDirectories( bool notifyExistingEntries = false )
	{
		if ( !Directory.Exists( root ) )
			throw new DirectoryNotFoundException( root );

		var desired = new HashSet<string>( StringComparer.Ordinal );
		var pending = new Stack<string>();
		pending.Push( root );
		while ( pending.TryPop( out var directory ) )
		{
			if ( Volatile.Read( ref stopping ) != 0 ) return;
			desired.Add( directory );
			try
			{
				foreach ( var child in Directory.EnumerateDirectories( directory ) )
				{
					if ( excluded.Contains( Path.GetFileName( child ) ) ) continue;
					try
					{
						if ( (File.GetAttributes( child ) & FileAttributes.ReparsePoint) != 0 ) continue;
					}
					catch ( FileNotFoundException ) { continue; }
					catch ( DirectoryNotFoundException ) { continue; }
					pending.Push( child );
				}
			}
			catch ( DirectoryNotFoundException ) when ( directory != root )
			{
				desired.Remove( directory );
			}
			catch ( UnauthorizedAccessException ) when ( directory != root )
			{
				desired.Remove( directory );
			}
		}

		// Refresh every desired path: the same set of names can now refer to different
		// inodes after a directory swap or atomic replacement. Register before removing
		// obsolete descriptors, since a moved inode can keep its descriptor at a new path.
		var nextDirectories = new Dictionary<string, int>( StringComparer.Ordinal );
		var nextDescriptors = new Dictionary<int, string>();
		var added = new List<string>();
		foreach ( var path in desired )
		{
			if ( Volatile.Read( ref stopping ) != 0 ) return;
			var wd = inotify_add_watch( fd, path, Events | OnlyDirectory | (path == root ? 0u : DontFollow) );
			if ( wd < 0 )
			{
				var error = Marshal.GetLastPInvokeError();
				if ( path != root && error is 2 or 13 ) continue; // Removed or inaccessible child.
				throw NativeError( "register directory watch", path );
			}

			if ( !directories.TryGetValue( path, out var previous ) || previous != wd || relocatedDirectories.Contains( path ) ) added.Add( path );
			nextDirectories[path] = wd;
			nextDescriptors[wd] = path;
		}

		relocatedDirectories.Clear();
		foreach ( var wd in descriptors.Keys.Where( x => !nextDescriptors.ContainsKey( x ) ) )
			inotify_rm_watch( fd, wd );
		directories.Clear();
		descriptors.Clear();
		foreach ( var entry in nextDirectories ) directories.Add( entry.Key, entry.Value );
		foreach ( var entry in nextDescriptors ) descriptors.Add( entry.Key, entry.Value );

		Volatile.Write( ref directoryCount, directories.Count );
		if ( !notifyExistingEntries ) return;

		// Files can be created before a new directory's watch is registered. Announce its
		// existing entries after registration so consumers cannot miss that initial burst.
		foreach ( var path in added )
		{
			try
			{
				foreach ( var entry in Directory.EnumerateFileSystemEntries( path ) )
					Emit( WatcherChangeTypes.Changed, entry, Directory.Exists( entry ) );
			}
			catch ( DirectoryNotFoundException ) { }
		}
	}

	internal string[] ScanEntries()
	{
		var entries = new List<string>();
		var pending = new Stack<string>();
		pending.Push( root );
		while ( pending.TryPop( out var directory ) )
		{
			try
			{
				foreach ( var entry in Directory.EnumerateFileSystemEntries( directory ) )
				{
					FileAttributes attributes;
					try { attributes = File.GetAttributes( entry ); }
					catch ( FileNotFoundException ) { continue; }
					catch ( DirectoryNotFoundException ) { continue; }
					var isDirectory = (attributes & FileAttributes.Directory) != 0;
					if ( isDirectory && excluded.Contains( Path.GetFileName( entry ) ) ) continue;
					entries.Add( entry );
					if ( isDirectory && (attributes & FileAttributes.ReparsePoint) == 0 )
						pending.Push( entry );
				}
			}
			catch ( DirectoryNotFoundException ) when ( directory != root ) { }
			catch ( UnauthorizedAccessException ) when ( directory != root ) { }

		}
		return entries.ToArray();
	}

	void Run()
	{
		var buffer = new byte[64 * 1024];
		var pollFds = new[] { new PollDescriptor { Descriptor = fd, Events = 1 }, new PollDescriptor { Descriptor = wakeFd, Events = 1 } };
		try
		{
			while ( Volatile.Read( ref stopping ) == 0 )
			{
				// Sleep indefinitely when idle. A cancellation event wakes the worker without
				// closing a descriptor that poll/read may still be using.
				var ready = poll( pollFds, 2, moves.Count > 0 ? 100 : -1 );
				if ( ready < 0 )
				{
					if ( Marshal.GetLastPInvokeError() == 4 ) continue; // EINTR
					throw NativeError( "poll inotify", root );
				}

				if ( Volatile.Read( ref stopping ) != 0 ) break;
				if ( (pollFds[0].ReturnedEvents & (8 | 16 | 32)) != 0 )
					throw new IOException( $"inotify descriptor stopped accepting events for {root}" );
				var reconcile = false;
				if ( (pollFds[0].ReturnedEvents & 1) != 0 )
				{
					var size = read( fd, buffer, (nuint)buffer.Length );
					if ( size < 0 )
					{
						if ( Marshal.GetLastPInvokeError() is 11 or 4 ) continue; // EAGAIN, EINTR
						throw NativeError( "read inotify", root );
					}

					if ( size == 0 ) throw new IOException( $"inotify descriptor closed for {root}" );

					reconcile = DecodeEvents( buffer, (int)size, HandleNotification, error =>
					{
						moves.Clear();
						onError( error );
					}, root );
				}

				foreach ( var move in moves.Where( x => Stopwatch.GetElapsedTime( x.Value.Time ).TotalMilliseconds >= 100 ).ToArray() )
				{
					moves.Remove( move.Key );
					Emit( WatcherChangeTypes.Deleted, move.Value.Path, move.Value.IsDirectory );
				}

				if ( reconcile ) ReconcileDirectories( notifyExistingEntries: true );
			}
		}
		catch ( Exception e )
		{
			if ( Volatile.Read( ref stopping ) == 0 ) onError( e );
		}
		finally
		{
			lock ( disposalGate )
			{
				close( fd );
				close( wakeFd );
				wakeFd = -1;
			}
			Volatile.Write( ref directoryCount, 0 );
		}
	}

	internal readonly record struct Notification( int Descriptor, uint Mask, uint Cookie, string Name );

	internal static bool DecodeEvents( byte[] buffer, int size, Func<Notification, bool> onNotification, Action<Exception> onError, string root )
	{
		if ( size < 0 || size > buffer.Length ) throw new InvalidDataException( "Invalid inotify read length" );
		var reconcile = false;
		for ( var offset = 0; offset < size; )
		{
			if ( size - offset < 16 ) throw new InvalidDataException( "Truncated inotify event header" );
			var wd = BitConverter.ToInt32( buffer, offset );
			var mask = BitConverter.ToUInt32( buffer, offset + 4 );
			var cookie = BitConverter.ToUInt32( buffer, offset + 8 );
			var length = BitConverter.ToUInt32( buffer, offset + 12 );
			if ( length > size - offset - 16 ) throw new InvalidDataException( "Truncated inotify event name" );
			var end = Array.IndexOf( buffer, (byte)0, offset + 16, (int)length );
			var name = Encoding.UTF8.GetString( buffer, offset + 16, end < 0 ? (int)length : end - offset - 16 );
			offset += 16 + (int)length;
			if ( (mask & Overflow) != 0 )
			{
				onError( new InternalBufferOverflowException( $"inotify event queue overflowed for {root}; rescanning is required" ) );
				reconcile = true;
			}
			else if ( (mask & Unmount) != 0 )
			{
				onError( new IOException( $"Filesystem unmounted for {root}; watching must be re-established" ) );
				reconcile = true;
			}
			else
			{
				reconcile |= onNotification( new Notification( wd, mask, cookie, name ) );
			}
		}
		return reconcile;
	}

	bool HandleNotification( Notification notification )
	{
		var (wd, mask, cookie, name) = notification;
		var reconcile = false;
		if ( !descriptors.TryGetValue( wd, out var directory ) ) return reconcile;
		if ( (mask & Ignored) != 0 )
		{
			descriptors.Remove( wd );
			if ( directories.TryGetValue( directory, out var current ) && current == wd ) directories.Remove( directory );
			reconcile = true;
			return reconcile;
		}

		if ( (mask & (DeleteSelf | MoveSelf | Unmount)) != 0 ) reconcile = true;
		if ( name.Length == 0 ) return reconcile;
		var path = Path.Combine( directory, name );
		if ( (mask & IsDirectory) != 0 && excluded.Contains( name ) ) return reconcile;
		if ( (mask & IsDirectory) != 0 && (mask & (Create | Delete | MovedFrom | MovedTo | Attrib)) != 0 ) reconcile = true;

		if ( (mask & MovedFrom) != 0 )
			moves[cookie] = (path, Stopwatch.GetTimestamp(), (mask & IsDirectory) != 0);
		else if ( (mask & MovedTo) != 0 )
		{
			if ( moves.Remove( cookie, out var old ) )
			{
				if ( old.IsDirectory ) RebaseDirectory( old.Path, path );
				onChange( new RenameEvent( root, Path.GetRelativePath( root, path ), Path.GetRelativePath( root, old.Path ), old.IsDirectory ) );
			}
			else
				Emit( WatcherChangeTypes.Created, path, (mask & IsDirectory) != 0 );
		}
		else if ( (mask & Create) != 0 ) Emit( WatcherChangeTypes.Created, path, (mask & IsDirectory) != 0 );
		else if ( (mask & Delete) != 0 ) Emit( WatcherChangeTypes.Deleted, path, (mask & IsDirectory) != 0 );
		else if ( (mask & (Modify | Attrib)) != 0 ) Emit( WatcherChangeTypes.Changed, path, (mask & IsDirectory) != 0 );
		return reconcile;
	}

	void RebaseDirectory( string oldPath, string newPath )
	{
		var moved = directories.Where( x => x.Key == oldPath || x.Key.StartsWith( oldPath + Path.DirectorySeparatorChar, StringComparison.Ordinal ) ).ToArray();
		foreach ( var entry in moved ) directories.Remove( entry.Key );
		foreach ( var entry in moved )
		{
			var path = newPath + entry.Key[oldPath.Length..];
			directories[path] = entry.Value;
			descriptors[entry.Value] = path;
			relocatedDirectories.Add( path );
		}
	}

	internal sealed class ChangeEvent : FileSystemEventArgs
	{
		internal bool IsDirectory { get; }
		internal ChangeEvent( WatcherChangeTypes type, string path, bool isDirectory )
			: base( type, Path.GetDirectoryName( path ), Path.GetFileName( path ) ) => IsDirectory = isDirectory;
	}

	internal sealed class RenameEvent : RenamedEventArgs
	{
		internal bool IsDirectory { get; }
		internal RenameEvent( string root, string name, string oldName, bool isDirectory )
			: base( WatcherChangeTypes.Renamed, root, name, oldName ) => IsDirectory = isDirectory;
	}

	void Emit( WatcherChangeTypes type, string path, bool isDirectory ) => onChange( new ChangeEvent( type, path, isDirectory ) );

	public void Dispose()
	{
		if ( Interlocked.Exchange( ref stopping, 1 ) != 0 ) return;
		lock ( disposalGate )
		{
			if ( wakeFd < 0 ) return;
			ulong value = 1;
			while ( write( wakeFd, ref value, 8 ) < 0 && Marshal.GetLastPInvokeError() == 4 ) { }
		}
	}

	internal static string CanonicalizePath( string path )
	{
		var suffix = new Stack<string>();
		var candidate = path;
		while ( true )
		{
			var resolved = realpath( candidate, IntPtr.Zero );
			if ( resolved != IntPtr.Zero )
			{
				try
				{
					var canonical = Marshal.PtrToStringUTF8( resolved );
					while ( suffix.TryPop( out var part ) ) canonical = Path.Combine( canonical, part );
					return canonical;
				}
				finally { free( resolved ); }
			}

			var parent = Path.GetDirectoryName( candidate );
			if ( parent is null ) return path;
			suffix.Push( Path.GetFileName( candidate ) );
			candidate = parent;
		}
	}

	static IOException NativeError( string operation, string path )
	{
		var error = Marshal.GetLastPInvokeError();
		return new IOException( $"Couldn't {operation} for {path}: {new Win32Exception( error ).Message} (errno {error})" );
	}

	[StructLayout( LayoutKind.Sequential )]
	struct PollDescriptor
	{
		public int Descriptor;
		public short Events;
		public short ReturnedEvents;
	}

	[DllImport( "libc", SetLastError = true )]
	static extern IntPtr realpath( [MarshalAs( UnmanagedType.LPUTF8Str )] string path, IntPtr resolved );
	[DllImport( "libc" )]
	static extern void free( IntPtr address );
	[DllImport( "libc", SetLastError = true )]
	static extern int inotify_init1( int flags );
	[DllImport( "libc", SetLastError = true )]
	static extern int inotify_add_watch( int fd, [MarshalAs( UnmanagedType.LPUTF8Str )] string path, uint mask );
	[DllImport( "libc", SetLastError = true )]
	static extern int inotify_rm_watch( int fd, int wd );
	[DllImport( "libc", SetLastError = true )]
	static extern nint read( int fd, [Out] byte[] buffer, nuint count );
	[DllImport( "libc", SetLastError = true )]
	static extern int poll( [In, Out] PollDescriptor[] descriptors, nuint count, int timeout );
	[DllImport( "libc", SetLastError = true )]
	static extern int eventfd( uint initialValue, int flags );
	[DllImport( "libc", SetLastError = true )]
	static extern nint write( int fd, ref ulong value, nuint count );
	[DllImport( "libc" )]
	static extern int close( int fd );
}
