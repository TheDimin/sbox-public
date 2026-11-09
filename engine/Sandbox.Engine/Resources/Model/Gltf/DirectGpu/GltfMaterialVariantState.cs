using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>A stable selected-scene draw identity, independent of material/variant indices.</summary>
internal readonly record struct GltfMaterialVariantSlot( int Node, int Mesh, int Primitive );

/// <summary>One immutable source-material binding. The asset owns its material lease.</summary>
internal sealed record GltfMaterialVariantBinding<T>( GltfMaterialVariantSlot Slot, int? MaterialIndex, T Material ) where T : class;

/// <summary>The active index and every draw binding are published as one immutable revision.</summary>
internal sealed class GltfMaterialVariantSnapshot<T> where T : class
{
	internal int? ActiveVariant { get; }
	internal long Revision { get; }
	internal IReadOnlyList<GltfMaterialVariantBinding<T>> Bindings { get; }
	internal GltfMaterialVariantSnapshot( int? variant, long revision, GltfMaterialVariantBinding<T>[] bindings )
	{
		ActiveVariant = variant; Revision = revision;
		Bindings = new ReadOnlyCollection<GltfMaterialVariantBinding<T>>( bindings );
	}
}

/// <summary>
/// Pure asset-owned lazy material cache. Borrows the document; the outer resource
/// owns its generation. Acquired leases are retained until the last instance is
/// gone and this owner is disposed. A borrowed material resolver supplies a lease
/// with a no-op release. No pose, vertex, UV, shader or override policy lives here.
/// </summary>
internal sealed class GltfMaterialVariantAsset<T> : IDisposable where T : class
{
	private readonly GltfDocument document;
	private readonly GltfMaterialVariantSlot[] slots;
	private readonly Func<int?, GltfMaterialLease<T>> acquire;
	private readonly Func<T, bool> isValid;
	private readonly Action verifyThread;
	private readonly Action<GltfMaterialVariantBinding<T>> validateBinding;
	private Dictionary<int, GltfMaterialLease<T>> cache = new();
	private int states;
	private bool transactionActive;
	private bool disposed;

	internal GltfMaterialVariantAsset( GltfDocument document, IReadOnlyList<GltfMaterialVariantSlot> slots,
		Func<int?, GltfMaterialLease<T>> acquire, Func<T, bool> isValid, Action verifyThread = null,
		Action<GltfMaterialVariantBinding<T>> validateBinding = null )
	{
		this.document = document ?? throw new ArgumentNullException( nameof( document ) );
		ArgumentNullException.ThrowIfNull( slots );
		this.slots = slots.ToArray();
		if ( this.slots.Any( x => x.Node < 0 || x.Mesh < 0 || x.Primitive < 0 ) || this.slots.Distinct().Count() != this.slots.Length )
			throw new ArgumentException( "Material draw slots must have unique nonnegative node/mesh/primitive identities.", nameof( slots ) );
		this.acquire = acquire ?? throw new ArgumentNullException( nameof( acquire ) );
		this.isValid = isValid ?? throw new ArgumentNullException( nameof( isValid ) );
		this.verifyThread = verifyThread;
		this.validateBinding = validateBinding;
	}

	internal GltfMaterialVariantState<T> CreateState()
	{
		VerifyAvailable(); var count = checked(states + 1);
		transactionActive = true;
		try
		{
			// Allocate the unpublished instance before acquiring any resources.
			var state = new GltfMaterialVariantState<T>( this );
			var prepared = Prepare( null, 0 );
			cache = prepared.Cache; state.Publish( prepared.Snapshot ); states = count;
			return state;
		}
		finally { transactionActive = false; }
	}

	internal void Select( GltfMaterialVariantState<T> state, int? variant )
	{
		VerifyAvailable(); state.VerifyLive();
		var before = state.Current;
		if ( before.ActiveVariant == variant ) return;
		var revision = checked(before.Revision + 1);
		transactionActive = true;
		try
		{
			var prepared = Prepare( variant, revision );
			cache = prepared.Cache;
			// Renderers capture Current once, then use that vector for every draw.
			state.Publish( prepared.Snapshot );
		}
		finally { transactionActive = false; }
	}

	private (Dictionary<int, GltfMaterialLease<T>> Cache, GltfMaterialVariantSnapshot<T> Snapshot) Prepare( int? variant, long revision )
	{
		// Resolve every row before acquiring anything. The reader owns mapping,
		// supported-extension and ordinary primitive/default fallback semantics.
		var root = document.Root;
		if ( variant is int requested ) ValidateVariantIndex( root, requested );
		var indices = new int?[slots.Length];
		for ( var i = 0; i < slots.Length; i++ ) indices[i] = document.GetMaterialForVariant( slots[i].Mesh, slots[i].Primitive, variant );
		var unique = indices.Distinct().ToArray();
		var staged = new Dictionary<int, GltfMaterialLease<T>>( unique.Length );
		try
		{
			foreach ( var index in unique )
			{
				var key = index ?? -1;
				if ( !cache.TryGetValue( key, out var lease ) )
				{
					lease = acquire( index ) ?? throw new InvalidOperationException( "Material acquisition returned no lease." );
					// Take ownership before Value/liveness inspection, which can throw.
					staged.Add( key, lease );
				}
				if ( !isValid( lease.Value ) ) throw new InvalidOperationException( "Resolved material is invalid." );
			}

			// Allocation and snapshot construction can still fail: keep them inside
			// the rollback boundary, before publishing cache or instance state.
			var next = new Dictionary<int, GltfMaterialLease<T>>( cache );
			foreach ( var pair in staged ) next.Add( pair.Key, pair.Value );
			var bindings = new GltfMaterialVariantBinding<T>[slots.Length];
			for ( var i = 0; i < slots.Length; i++ ) bindings[i] = new( slots[i], indices[i], next[indices[i] ?? -1].Value );
			if ( validateBinding is not null ) foreach ( var binding in bindings ) validateBinding( binding );
			return (next, new GltfMaterialVariantSnapshot<T>( variant, revision, bindings ));
		}
		catch ( Exception cause )
		{
			var cleanup = ReleaseAll( staged.Values );
			if ( cleanup.Count != 0 ) { cleanup.Insert( 0, cause ); throw new AggregateException( "Material selection failed and staged cleanup reported errors.", cleanup ); }
			throw;
		}
	}
	private static void ValidateVariantIndex( JsonElement root, int variant )
	{
		// ActiveVariant always names a real global variant, including an empty
		// selected scene or a caller using the extension's ordinary core fallback.
		if ( variant < 0 || !root.TryGetProperty( "extensions", out var extensions ) || extensions.ValueKind != JsonValueKind.Object ||
			!extensions.TryGetProperty( "KHR_materials_variants", out var extension ) || extension.ValueKind != JsonValueKind.Object ||
			!extension.TryGetProperty( "variants", out var variants ) || variants.ValueKind != JsonValueKind.Array || variant >= variants.GetArrayLength() )
			throw new GltfException( "Material variant index is out of range." );
		foreach ( var definition in variants.EnumerateArray() )
			if ( definition.ValueKind != JsonValueKind.Object || !definition.TryGetProperty( "name", out var name ) || name.ValueKind != JsonValueKind.String )
				throw new GltfException( "Material variant name must be a string." );
	}

	internal void ReleaseState( GltfMaterialVariantState<T> state )
	{
		VerifyAvailable(); state.VerifyLive();
		state.MarkDisposed(); states--;
	}
	private void VerifyAvailable()
	{
		verifyThread?.Invoke(); ObjectDisposedException.ThrowIf( disposed, this );
		if ( transactionActive ) throw new InvalidOperationException( "Recursive material selection, instance creation or disposal is not supported during acquisition." );
	}
	public void Dispose()
	{
		if ( disposed ) return;
		VerifyAvailable();
		if ( states != 0 ) throw new InvalidOperationException( "Dispose every material variant state before its shared asset." );
		disposed = true; var retired = cache; cache = null;
		var errors = ReleaseAll( retired.Values );
		if ( errors.Count != 0 ) throw new AggregateException( "Material cache disposal reported errors.", errors );
	}
	private static List<Exception> ReleaseAll( IEnumerable<GltfMaterialLease<T>> leases )
	{
		var errors = new List<Exception>();
		foreach ( var lease in leases ) try { lease.Dispose(); } catch ( Exception error ) { errors.Add( error ); }
		return errors;
	}
}

/// <summary>One instance's atomic selection vector; it never owns shared material resources.</summary>
internal sealed class GltfMaterialVariantState<T> : IDisposable where T : class
{
	private readonly GltfMaterialVariantAsset<T> owner;
	private GltfMaterialVariantSnapshot<T> current;
	private bool disposed;
	internal GltfMaterialVariantState( GltfMaterialVariantAsset<T> owner ) => this.owner = owner;
	internal GltfMaterialVariantSnapshot<T> Current { get { VerifyLive(); return Volatile.Read( ref current ); } }
	internal void SetVariant( int? variant ) { VerifyLive(); owner.Select( this, variant ); }
	internal void Publish( GltfMaterialVariantSnapshot<T> value ) => Volatile.Write( ref current, value );
	internal void VerifyLive() => ObjectDisposedException.ThrowIf( disposed, this );
	internal void MarkDisposed() => disposed = true;
	public void Dispose() { if ( !disposed ) owner.ReleaseState( this ); }
}
