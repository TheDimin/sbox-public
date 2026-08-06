using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Editor;

/// <summary>
/// Small process-wide cache for parsed metadata documents. The limits are intentionally
/// independent of the number of assets so hydrating a large catalog cannot retain one JSON
/// document per asset.
/// </summary>
internal static class MetaDataDocumentCache
{
	internal const int MaximumEntries = 1_024;
	internal const long MaximumSourceBytes = 8 * 1024 * 1024;

	private static readonly object Sync = new();
	private static readonly Dictionary<string, Entry> Entries = new( StringComparer.OrdinalIgnoreCase );
	private static readonly LinkedList<string> Recency = new();
	private static long retainedSourceBytes;

	internal static int Count
	{
		get
		{
			lock ( Sync )
				return Entries.Count;
		}
	}

	internal static long RetainedSourceBytes
	{
		get
		{
			lock ( Sync )
				return retainedSourceBytes;
		}
	}

	internal static bool TryGet( string path, DateTime writeTimeUtc, long length, out JsonElement root )
	{
		lock ( Sync )
		{
			if ( !Entries.TryGetValue( path, out var entry ) )
			{
				root = default;
				return false;
			}

			if ( entry.WriteTimeUtc != writeTimeUtc || entry.Length != length )
			{
				RemoveEntry( entry );
				root = default;
				return false;
			}

			Recency.Remove( entry.Node );
			Recency.AddLast( entry.Node );
			root = entry.Root;
			return true;
		}
	}

	internal static void Store( string path, DateTime writeTimeUtc, long length, JsonElement root )
	{
		lock ( Sync )
		{
			if ( Entries.TryGetValue( path, out var existing ) )
				RemoveEntry( existing );

			if ( length > MaximumSourceBytes )
				return;

			var node = Recency.AddLast( path );
			var entry = new Entry( path, writeTimeUtc, length, root, node );
			Entries.Add( path, entry );
			retainedSourceBytes += length;

			while ( Entries.Count > MaximumEntries || retainedSourceBytes > MaximumSourceBytes )
			{
				var oldestPath = Recency.First?.Value;
				if ( oldestPath is null || !Entries.TryGetValue( oldestPath, out var oldest ) )
					break;

				RemoveEntry( oldest );
			}
		}
	}

	internal static void Remove( string path )
	{
		lock ( Sync )
		{
			if ( Entries.TryGetValue( path, out var entry ) )
				RemoveEntry( entry );
		}
	}

	private static void RemoveEntry( Entry entry )
	{
		Entries.Remove( entry.Path );
		Recency.Remove( entry.Node );
		retainedSourceBytes -= entry.Length;
	}

	private sealed record Entry(
		string Path,
		DateTime WriteTimeUtc,
		long Length,
		JsonElement Root,
		LinkedListNode<string> Node );
}
