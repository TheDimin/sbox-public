namespace Editor;

/// <summary>
/// Keeps recently used thumbnails resident within a pixel-memory budget. Eviction only
/// releases the cache's reference; widgets can continue to use an image they already hold.
/// </summary>
internal sealed class ThumbnailMemoryCache<TKey, TValue>( long maximumBytes ) where TKey : notnull where TValue : class
{
	private readonly Dictionary<TKey, Entry> entries = new();
	private readonly LinkedList<TKey> recency = new();

	internal int Count => entries.Count;
	internal long RetainedBytes { get; private set; }

	internal bool TryGet( TKey key, out TValue value )
	{
		if ( !entries.TryGetValue( key, out var entry ) )
		{
			value = null;
			return false;
		}

		recency.Remove( entry.Node );
		recency.AddLast( entry.Node );
		value = entry.Value;
		return true;
	}

	internal void Store( TKey key, TValue value, long bytes )
	{
		Remove( key );
		if ( value is null || bytes <= 0 || bytes > maximumBytes )
			return;

		while ( RetainedBytes > maximumBytes - bytes && recency.First is { } oldest )
			Remove( oldest.Value );

		entries.Add( key, new Entry( value, bytes, recency.AddLast( key ) ) );
		RetainedBytes += bytes;
	}

	internal void Remove( TKey key )
	{
		if ( !entries.Remove( key, out var entry ) )
			return;

		recency.Remove( entry.Node );
		RetainedBytes -= entry.Bytes;
	}

	private sealed record Entry( TValue Value, long Bytes, LinkedListNode<TKey> Node );
}
