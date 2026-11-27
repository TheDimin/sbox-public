using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GtaV.ManagedReader;

/// <summary>Reads bounded RPF entries with shared file handles and a bounded directory cache.</summary>
internal sealed class ArchiveContent( string directory, ArchiveIndex index ) : IDisposable
{
	const int DirectoryCacheLimit = 256;
	readonly string root = Path.GetFullPath( directory );
	readonly Lazy<RpfKeys> keys = new( () => new RpfKeys( RpfKeys.DefaultDirectory ) );
	readonly ReaderWriterLockSlim lifetime = new();
	readonly object gate = new();
	readonly Dictionary<int, Lazy<SafeFileHandle>> files = new();
	readonly Dictionary<int, LinkedListNode<(int Id, Lazy<RpfArchive> Archive)>> directories = new();
	readonly LinkedList<(int Id, Lazy<RpfArchive> Archive)> recent = new();
	bool disposed;

	internal byte[] Extract( in ArchiveIndex.FileEntry entry )
	{
		lifetime.EnterReadLock();
		try
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			return Open( entry.Archive, 0 ).Extract( entry.Entry );
		}
		finally { lifetime.ExitReadLock(); }
	}

	internal Stream OpenResourceStream( in ArchiveIndex.FileEntry entry, out RscHeader header )
	{
		lifetime.EnterReadLock();
		try
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			return Open( entry.Archive, 0 ).OpenResourceStream( entry.Entry, out header );
		}
		finally { lifetime.ExitReadLock(); }
	}

	RpfArchive Open( int id, int depth )
	{
		if ( depth >= 64 ) throw new InvalidDataException( "Too many nested RPF archives." );
		LinkedListNode<(int Id, Lazy<RpfArchive> Archive)> current;
		lock ( gate )
		{
			if ( directories.TryGetValue( id, out current ) ) recent.Remove( current );
			else
			{
				current = new( (id, new( () => ReadDirectory( id, depth ) )) );
				directories.Add( id, current );
			}
			recent.AddFirst( current );
			if ( recent.Count > DirectoryCacheLimit )
			{
				directories.Remove( recent.Last.Value.Id );
				recent.RemoveLast();
			}
		}
		try { return current.Value.Archive.Value; }
		catch
		{
			lock ( gate )
			{
				if ( directories.GetValueOrDefault( id ) == current )
				{
					directories.Remove( id );
					recent.Remove( current );
				}
			}
			throw;
		}
	}

	RpfArchive ReadDirectory( int id, int depth )
	{
		var archive = index.Archives[id];
		if ( archive.Parent >= 0 ) return Open( archive.Parent, depth + 1 ).OpenChild( archive.ParentEntry );
		Lazy<SafeFileHandle> lazy;
		lock ( gate )
		{
			if ( !files.TryGetValue( id, out lazy ) ) files.Add( id, lazy = new( () => OpenFile( archive.Path ) ) );
		}
		SafeFileHandle file;
		try { file = lazy.Value; }
		catch
		{
			lock ( gate )
				if ( files.GetValueOrDefault( id ) == lazy ) files.Remove( id );
			throw;
		}
		return new RpfArchive( file, 0, RandomAccess.GetLength( file ), Path.GetFileName( archive.Path ), keys.Value );
	}

	SafeFileHandle OpenFile( string path )
	{
		string full = Path.GetFullPath( path, root );
		string relative = Path.GetRelativePath( root, full );
		if ( Path.IsPathRooted( relative ) || relative == ".." || relative.StartsWith( ".." + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
			throw new InvalidDataException( "RPF path escapes the install folder." );
		return File.OpenHandle( full, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess );
	}

	public void Dispose()
	{
		lifetime.EnterWriteLock();
		try
		{
			if ( disposed ) return;
			disposed = true;
			foreach ( var file in files.Values )
				if ( file.IsValueCreated ) file.Value.Dispose();
			files.Clear();
			directories.Clear();
			recent.Clear();
		}
		finally { lifetime.ExitWriteLock(); }
	}
}
