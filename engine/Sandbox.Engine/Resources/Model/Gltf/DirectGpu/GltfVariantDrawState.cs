using System.Collections.ObjectModel;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu;

internal sealed record GltfVariantDrawBinding<T>( GltfMaterialVariantSlot Slot, int? MaterialIndex, T Value ) where T : class;

internal sealed class GltfVariantDrawSnapshot<T> where T : class
{
	internal int? ActiveVariant { get; }
	internal long Revision { get; }
	internal IReadOnlyList<GltfVariantDrawBinding<T>> Bindings { get; }
	internal GltfVariantDrawSnapshot( int? variant, long revision, GltfVariantDrawBinding<T>[] bindings )
	{
		ActiveVariant = variant; Revision = revision; Bindings = Array.AsReadOnly( bindings );
	}
}

/// <summary>
/// Asset-owned, transactional packets keyed by source primitive and material,
/// not material alone. A packet may contain a material lease and the exact
/// immutable UV descriptor route for that primitive. Instanced rows sharing the
/// same primitive share packets, while each instance selects its own vector.
/// </summary>
internal sealed class GltfVariantDrawAsset<T> : IDisposable where T : class
{
	private readonly record struct Key( int Mesh, int Primitive, int Material );
	private readonly GltfDocument document;
	private readonly GltfMaterialVariantSlot[] slots;
	private readonly Func<GltfMaterialVariantSlot, int?, GltfMaterialLease<T>> acquire;
	private readonly Func<T, bool> valid;
	private readonly Action verifyThread;
	private readonly Func<IReadOnlyList<(GltfMaterialVariantSlot Slot, int? Material)>, IDisposable> prepareBatch;
	private Dictionary<Key, GltfMaterialLease<T>> cache = new();
	private int states;
	private bool disposed, transaction;

	internal GltfVariantDrawAsset( GltfDocument document, IReadOnlyList<GltfMaterialVariantSlot> slots,
		Func<GltfMaterialVariantSlot, int?, GltfMaterialLease<T>> acquire, Func<T, bool> valid, Action verifyThread = null,
		Func<IReadOnlyList<(GltfMaterialVariantSlot Slot, int? Material)>, IDisposable> prepareBatch = null )
	{
		this.document = document ?? throw new ArgumentNullException( nameof( document ) );
		ArgumentNullException.ThrowIfNull( slots ); this.slots = slots.ToArray();
		if ( this.slots.Any( x => x.Node < 0 || x.Mesh < 0 || x.Primitive < 0 ) ) throw new ArgumentException( "Draw identities must be nonnegative.", nameof( slots ) );
		this.acquire = acquire ?? throw new ArgumentNullException( nameof( acquire ) );
		this.valid = valid ?? throw new ArgumentNullException( nameof( valid ) ); this.verifyThread = verifyThread; this.prepareBatch = prepareBatch;
	}

	internal GltfVariantDrawState<T> CreateState()
	{
		Verify(); var count = checked(states + 1); transaction = true;
		try
		{
			var state = new GltfVariantDrawState<T>( this ); var prepared = Prepare( null, 0 );
			cache = prepared.Cache; state.Publish( prepared.Snapshot ); states = count; return state;
		}
		finally { transaction = false; }
	}

	internal void Select( GltfVariantDrawState<T> state, int? variant, Action<GltfVariantDrawSnapshot<T>> preflight = null )
	{
		Verify(); state.Verify(); var current = state.Current;
		if ( current.ActiveVariant == variant ) return;
		transaction = true;
		try
		{
			var prepared = Prepare( variant, checked(current.Revision + 1) );
			try { preflight?.Invoke( prepared.Snapshot ); }
			catch ( Exception cause )
			{
				var staged = prepared.Cache.Where( x => !cache.ContainsKey( x.Key ) ).Select( x => x.Value );
				var errors = Release( staged );
				if ( errors.Count != 0 ) { errors.Insert( 0, cause ); throw new AggregateException( "Draw packet preflight failed and rollback reported errors.", errors ); }
				throw;
			}
			cache = prepared.Cache; state.Publish( prepared.Snapshot );
		}
		finally { transaction = false; }
	}

	private (Dictionary<Key, GltfMaterialLease<T>> Cache, GltfVariantDrawSnapshot<T> Snapshot) Prepare( int? variant, long revision )
	{
		if ( variant is int index && (index < 0 || index >= document.ReadMaterialVariantNames().Count) )
			throw new GltfException( "Material variant index is out of range." );
		var indices = slots.Select( x => document.GetMaterialForVariant( x.Mesh, x.Primitive, variant ) ).ToArray();
		var staged = new Dictionary<Key, GltfMaterialLease<T>>();
		IDisposable batch = null;
		try
		{
			if ( prepareBatch is not null )
			{
				var pending = new List<(GltfMaterialVariantSlot Slot, int? Material)>(); var seen = new HashSet<Key>();
				for ( var i = 0; i < slots.Length; i++ )
				{
					var slot = slots[i]; var key = new Key( slot.Mesh, slot.Primitive, indices[i] ?? -1 );
					if ( !cache.ContainsKey( key ) && seen.Add( key ) ) pending.Add( (slot, indices[i]) );
				}
				batch = prepareBatch( pending );
			}
			var bindings = new GltfVariantDrawBinding<T>[slots.Length];
			for ( var i = 0; i < slots.Length; i++ )
			{
				var slot = slots[i]; var key = new Key( slot.Mesh, slot.Primitive, indices[i] ?? -1 );
				if ( !cache.TryGetValue( key, out var lease ) && !staged.TryGetValue( key, out lease ) )
				{
					lease = acquire( slot, indices[i] ) ?? throw new InvalidOperationException( "Draw packet acquisition returned no lease." );
					staged.Add( key, lease );
				}
				if ( !valid( lease.Value ) ) throw new InvalidOperationException( "Resolved draw packet is invalid." );
				bindings[i] = new( slot, indices[i], lease.Value );
			}
			var next = new Dictionary<Key, GltfMaterialLease<T>>( cache );
			foreach ( var pair in staged ) next.Add( pair.Key, pair.Value );
			batch?.Dispose(); batch = null;
			return (next, new GltfVariantDrawSnapshot<T>( variant, revision, bindings ));
		}
		catch ( Exception cause )
		{
			var errors = Release( staged.Values );
			try { batch?.Dispose(); } catch ( Exception error ) { errors.Add( error ); }
			if ( errors.Count != 0 ) { errors.Insert( 0, cause ); throw new AggregateException( "Draw packet selection failed and rollback reported errors.", errors ); }
			throw;
		}
	}

	internal void ReleaseState( GltfVariantDrawState<T> state ) { Verify(); state.Verify(); state.MarkDisposed(); states--; }
	private void Verify()
	{
		verifyThread?.Invoke(); ObjectDisposedException.ThrowIf( disposed, this );
		if ( transaction ) throw new InvalidOperationException( "Recursive draw packet acquisition or disposal is not supported." );
	}
	public void Dispose()
	{
		if ( disposed ) return; Verify();
		if ( states != 0 ) throw new InvalidOperationException( "Dispose every draw selection before its shared asset." );
		disposed = true; var old = cache; cache = null; var errors = Release( old.Values );
		if ( errors.Count != 0 ) throw new AggregateException( "Draw packet disposal reported errors.", errors );
	}
	private static List<Exception> Release( IEnumerable<GltfMaterialLease<T>> leases )
	{
		var errors = new List<Exception>(); foreach ( var lease in leases ) try { lease.Dispose(); } catch ( Exception error ) { errors.Add( error ); }
		return errors;
	}
}

internal sealed class GltfVariantDrawState<T> : IDisposable where T : class
{
	private readonly GltfVariantDrawAsset<T> owner;
	private GltfVariantDrawSnapshot<T> current;
	private bool disposed;
	internal GltfVariantDrawState( GltfVariantDrawAsset<T> owner ) => this.owner = owner;
	internal GltfVariantDrawSnapshot<T> Current { get { Verify(); return Volatile.Read( ref current ); } }
	internal void SetVariant( int? variant, Action<GltfVariantDrawSnapshot<T>> preflight = null ) { Verify(); owner.Select( this, variant, preflight ); }
	internal void Publish( GltfVariantDrawSnapshot<T> value ) => Volatile.Write( ref current, value );
	internal void Verify() => ObjectDisposedException.ThrowIf( disposed, this );
	internal void MarkDisposed() => disposed = true;
	public void Dispose() { if ( !disposed ) owner.ReleaseState( this ); }
}
