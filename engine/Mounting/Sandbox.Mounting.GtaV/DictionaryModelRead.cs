using System.Threading;
using System.Threading.Tasks;

namespace GtaV;

/// <summary>One worker reads a dictionary and services the model requests that arrive before it finishes.</summary>
internal sealed class DictionaryModelRead( uint file )
{
	internal sealed class Member( Entry entry )
	{
		internal readonly Entry Entry = entry;
		internal readonly TaskCompletionSource<PreparedModelData> Completion = new( TaskCreationOptions.RunContinuationsAsynchronously );
		internal Member Next;
	}

	static readonly Member Closed = new( default );
	Member incoming;
	Member ready;

	internal uint File => file;
	internal bool IsClosed => ReferenceEquals( Volatile.Read( ref incoming ), Closed );

	// The streamer owns submissions. The worker closes the inbox atomically when it reaches the end.
	internal bool TryAdd( Member member )
	{
		Member observed;
		do
		{
			observed = Volatile.Read( ref incoming );
			if ( ReferenceEquals( observed, Closed ) ) return false;
			member.Next = observed;
		}
		while ( !ReferenceEquals( Interlocked.CompareExchange( ref incoming, member, observed ), observed ) );
		return true;
	}

	internal Member Take()
	{
		while ( ready is null )
		{
			var observed = Volatile.Read( ref incoming );
			if ( ReferenceEquals( observed, Closed ) ) return null;
			var replacement = observed is null ? Closed : null;
			if ( !ReferenceEquals( Interlocked.CompareExchange( ref incoming, replacement, observed ), observed ) ) continue;
			if ( observed is null ) return null;

			// Restore submission order after detaching the producer's list.
			while ( observed is not null )
			{
				var next = observed.Next;
				observed.Next = ready;
				ready = observed;
				observed = next;
			}
		}

		var member = ready;
		ready = member.Next;
		member.Next = null;
		return member;
	}
}
