using Sandbox;
using System.Collections.Concurrent;

internal static partial class NativeResourceCache
{
	/// <summary>
	/// Remove only indexes still owned by this model. Destruction may run on the
	/// finalizer thread while another native handle publishes a replacement.
	/// </summary>
	internal static void Remove( long key, Model expectedOwner )
	{
		ArgumentNullException.ThrowIfNull( expectedOwner );
		if ( WeakTable.TryGetValue( key, out var captured ) && ReferenceEquals( captured.Target, expectedOwner ) )
		{
			// The captured WeakReference is the CAS value, not just its target:
			// a later Add at this address must retain its own canonical entry.
			((ICollection<KeyValuePair<long, WeakReference>>)WeakTable).Remove( new( key, captured ) );
		}
		RemoveRetainedModel( retained, key, expectedOwner );
		RemoveRetainedModel( retainedPrevious, key, expectedOwner );
		// CallstackTable has no owner token. Do not delete a replacement's
		// diagnostic record by key. With tracking enabled it remains until the
		// normal diagnostic cache clear; tracking-off creates no such entries.
	}

	private static void RemoveRetainedModel( ConcurrentDictionary<long, object> values, long key, Model expectedOwner )
	{
		if ( values.TryGetValue( key, out var captured ) && ReferenceEquals( captured, expectedOwner ) )
			((ICollection<KeyValuePair<long, object>>)values).Remove( new( key, captured ) );
	}
}
