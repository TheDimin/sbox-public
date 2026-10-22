namespace Sandbox.Navigation.Generation;

/// <summary>
/// Reuses scratch state during a build, then bounds idle retention. Checked-out items are never trimmed.
/// </summary>
internal sealed class GenerationPool<T>( Func<T> create, Action<T> release, Func<T, long> getSize, long byteBudget ) : IDisposable where T : class
{
	internal const long IdleMilliseconds = 30_000;
	private readonly object _lock = new();
	private readonly Queue<T> _items = new();
	private long _retainedBytes;
	private long _lastReturn;

	internal long RetainedBytes
	{
		get { lock ( _lock ) return _retainedBytes; }
	}

	internal T Get()
	{
		lock ( _lock )
		{
			if ( _items.TryDequeue( out var item ) )
			{
				_retainedBytes -= getSize( item );
				return item;
			}
		}

		return create();
	}

	internal void Return( T item ) => Return( item, Environment.TickCount64 );

	internal void Return( T item, long now )
	{
		lock ( _lock )
		{
			_items.Enqueue( item );
			_retainedBytes += getSize( item );
			_lastReturn = now;
		}
	}

	internal void Trim( long now )
	{
		List<T> removed = null;
		lock ( _lock )
		{
			if ( now - _lastReturn < IdleMilliseconds ) return;

			while ( _retainedBytes > byteBudget && _items.TryDequeue( out var item ) )
			{
				_retainedBytes -= getSize( item );
				(removed ??= new()).Add( item );
			}
		}

		if ( removed is null ) return;
		foreach ( var item in removed ) release( item );
	}

	public void Dispose()
	{
		List<T> removed;
		lock ( _lock )
		{
			removed = new( _items );
			_items.Clear();
			_retainedBytes = 0;
		}

		foreach ( var item in removed ) release( item );
	}
}
