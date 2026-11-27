using System;
using System.Collections.Generic;
using System.IO;

namespace GtaV.ManagedReader;

/// <summary>Reads texture members in graphics stream order and keeps small prefixes for later requests.</summary>
internal sealed class TextureDictionaryReader( ArchiveContent content ) : IDisposable
{
	const int MaxBatchMembers = 16;
	const int MaxBatchBytes = 64 * 1024 * 1024;
	const int MaxIdleReaders = 4;
	readonly object gate = new();
	readonly LinkedList<((int Archive, int Entry) Key, RscDictionary Reader)> idle = new();
	bool disposed;

	internal (uint Hash, string Name)[] ReadMembers( in ArchiveIndex.FileEntry entry )
	{
		using var dictionary = new RscDictionary( content, entry );
		var names = new (uint Hash, string Name)[dictionary.Members.Length];
		bool textures = entry.Extension == ".ytd";
		for ( int i = 0; i < names.Length; i++ )
		{
			var member = dictionary.Members[i];
			names[i] = (member.Hash, textures ? dictionary.TextureName( member ) : string.Empty);
		}
		return names;
	}

	internal Dictionary<uint, ArraySegment<byte>> ReadTextures( in ArchiveIndex.FileEntry entry, ReadOnlySpan<uint> members, out long inflated )
	{
		if ( members.Length is < 1 or > MaxBatchMembers ) throw new ArgumentOutOfRangeException( nameof(members) );
		var key = (entry.Archive, entry.Entry);
		var dictionary = Take( key );
		ulong before = dictionary?.InflatedBytes ?? 0;
		bool retained = false;
		try
		{
#pragma warning disable CA2000 // The finally block disposes the reader unless the cache takes ownership.
			dictionary ??= new RscDictionary( content, entry );
#pragma warning restore CA2000
			Span<RscDictionary.Texture> textures = stackalloc RscDictionary.Texture[members.Length];
			long total = 0;
			for ( int i = 0; i < members.Length; i++ )
			{
				if ( members[..i].Contains( members[i] ) ) throw new InvalidDataException( "Duplicate texture request." );
				textures[i] = dictionary.TextureAt( members[i] );
				total += (long)textures[i].Dds.HeaderSize + textures[i].Length;
			}
			if ( total > Array.MaxLength || (members.Length > 1 && total + 8 * members.Length > MaxBatchBytes) )
				throw new InvalidDataException( "Texture batch is too large." );
			textures.Sort();
			var bytes = GC.AllocateUninitializedArray<byte>( (int)total );
			var result = new Dictionary<uint, ArraySegment<byte>>( textures.Length );
			int offset = 0;
			foreach ( ref readonly var texture in textures )
			{
				int length = texture.Dds.HeaderSize + texture.Length;
				dictionary.ReadTexture( texture, bytes.AsSpan( offset, length ) );
				result.Add( texture.Hash, new ArraySegment<byte>( bytes, offset, length ) );
				offset += length;
			}
			inflated = checked((long)(dictionary.InflatedBytes - before));
			retained = dictionary.RetainPrefix && Return( key, dictionary );
			return result;
		}
		catch ( EndOfStreamException e )
		{
			throw new InvalidDataException( "Texture dictionary is truncated.", e );
		}
		finally
		{
			if ( !retained ) dictionary?.Dispose();
		}
	}

	RscDictionary Take( (int Archive, int Entry) key )
	{
		lock ( gate )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			for ( var node = idle.First; node is not null; node = node.Next )
			{
				if ( node.Value.Key != key ) continue;
				idle.Remove( node );
				return node.Value.Reader;
			}
			return null;
		}
	}

	bool Return( (int Archive, int Entry) key, RscDictionary reader )
	{
		RscDictionary evicted = null;
		lock ( gate )
		{
			if ( disposed ) return false;
			idle.AddFirst( (key, reader) );
			if ( idle.Count > MaxIdleReaders )
			{
				evicted = idle.Last.Value.Reader;
				idle.RemoveLast();
			}
		}
		evicted?.Dispose();
		return true;
	}

	public void Dispose()
	{
		lock ( gate )
		{
			if ( disposed ) return;
			disposed = true;
			foreach ( var (_, reader) in idle ) reader.Dispose();
			idle.Clear();
		}
	}
}
