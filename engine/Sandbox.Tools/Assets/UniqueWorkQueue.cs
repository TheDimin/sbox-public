namespace Editor;

/// <summary>
/// A FIFO work queue that keeps at most one pending entry for each item.
/// Existing items can be reprioritized without linearly searching the queue.
/// </summary>
internal sealed class UniqueWorkQueue<T> where T : notnull
{
	private readonly LinkedList<T> queue = new();
	private readonly Dictionary<T, LinkedListNode<T>> nodes;

	internal UniqueWorkQueue( IEqualityComparer<T> comparer = null )
	{
		nodes = new Dictionary<T, LinkedListNode<T>>( comparer );
	}

	internal int Count => queue.Count;

	internal bool Contains( T item ) => nodes.ContainsKey( item );

	internal bool Enqueue( T item, bool addIfMissing = true )
	{
		if ( nodes.Remove( item, out var existing ) )
		{
			queue.Remove( existing );
		}
		else if ( !addIfMissing )
		{
			return false;
		}

		nodes.Add( item, queue.AddLast( item ) );
		return true;
	}

	internal void EnqueueFirst( T item )
	{
		if ( nodes.Remove( item, out var existing ) )
		{
			queue.Remove( existing );
		}

		nodes.Add( item, queue.AddFirst( item ) );
	}

	internal bool Remove( T item )
	{
		if ( !nodes.Remove( item, out var node ) )
			return false;

		queue.Remove( node );
		return true;
	}

	internal bool TryDequeue( out T item )
	{
		var first = queue.First;
		if ( first is null )
		{
			item = default;
			return false;
		}

		item = first.Value;
		queue.RemoveFirst();
		nodes.Remove( item );
		return true;
	}
}
