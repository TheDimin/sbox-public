using Sandbox.Rendering;
using System.Runtime.CompilerServices;

namespace Sandbox;

// Resource identities compare by reference; Revision is the observed texture DirtyVersion.
internal sealed record GltfMaterialTextureBinding( string Parameter, object TextureIdentity, int Revision, SamplerState Sampler, string SamplerParameter = null );

/// <summary>A snapshot of effective inputs. The owner supplies a fresh template revision after edits or reloads.</summary>
internal sealed class GltfMaterialKey : IEquatable<GltfMaterialKey>
{
	internal object Template { get; }
	internal int Revision { get; }
	private readonly KeyValuePair<string, int>[] features;
	private readonly KeyValuePair<string, Vector4>[] parameters;
	private readonly KeyValuePair<string, float>[] scalars;
	private readonly GltfMaterialTextureBinding[] textures;
	internal ReadOnlySpan<KeyValuePair<string, int>> Features => features;
	internal ReadOnlySpan<KeyValuePair<string, Vector4>> Parameters => parameters;
	internal ReadOnlySpan<KeyValuePair<string, float>> Scalars => scalars;
	internal ReadOnlySpan<GltfMaterialTextureBinding> Textures => textures;

	internal GltfMaterialKey( object template, int revision, IEnumerable<KeyValuePair<string, int>> features, IEnumerable<KeyValuePair<string, Vector4>> parameters, IEnumerable<GltfMaterialTextureBinding> textures, IEnumerable<KeyValuePair<string, float>> scalars = null )
	{
		Template = template ?? throw new ArgumentNullException( nameof( template ) );
		Revision = revision;
		this.features = (features ?? throw new ArgumentNullException( nameof( features ) )).OrderBy( x => x.Key, StringComparer.Ordinal ).ToArray();
		this.parameters = (parameters ?? throw new ArgumentNullException( nameof( parameters ) )).OrderBy( x => x.Key, StringComparer.Ordinal ).ToArray();
		this.scalars = (scalars ?? []).OrderBy( x => x.Key, StringComparer.Ordinal ).ToArray();
		this.textures = (textures ?? throw new ArgumentNullException( nameof( textures ) )).OrderBy( x => x?.Parameter, StringComparer.Ordinal ).ToArray();
		UniqueNames( this.features.Select( x => x.Key ) );
		UniqueNames( this.parameters.Select( x => x.Key ) );
		UniqueNames( this.scalars.Select( x => x.Key ) );
		UniqueNames( this.textures.Select( x => x?.Parameter ) );
		if ( this.textures.Any( x => x.TextureIdentity is null ) ) throw new ArgumentException( "Texture identities cannot be null.", nameof( textures ) );
		if ( this.parameters.Select( x => x.Key ).Intersect( this.scalars.Select( x => x.Key ), StringComparer.Ordinal ).Any() )
			throw new ArgumentException( "A material property cannot be both scalar and vector." );
		var valueNames = this.parameters.Select( x => x.Key ).Concat( this.scalars.Select( x => x.Key ) ).ToHashSet( StringComparer.Ordinal );
		if ( this.textures.Any( x => valueNames.Contains( x.Parameter ) ) )
			throw new ArgumentException( "A material property cannot be both a value and texture." );
		if ( this.scalars.Any( x => !float.IsFinite( x.Value ) ) || this.parameters.Any( x => !Finite( x.Value ) ) )
			throw new ArgumentException( "Material values must be finite." );
		var samplers = new Dictionary<string, SamplerState>( StringComparer.Ordinal );
		foreach ( var texture in this.textures )
		{
			var sampler = texture.Sampler;
			var color = sampler.BorderColor;
			if ( !float.IsFinite( sampler.MipLodBias ) || !float.IsFinite( color.r ) || !float.IsFinite( color.g ) || !float.IsFinite( color.b ) || !float.IsFinite( color.a ) )
				throw new ArgumentException( "Sampler values must be finite." );
			if ( texture.SamplerParameter is null ) continue;
			if ( texture.SamplerParameter.Length == 0 ) throw new ArgumentException( "Sampler parameter names cannot be empty." );
			if ( samplers.TryGetValue( texture.SamplerParameter, out var existing ) && existing != sampler )
				throw new ArgumentException( "A sampler parameter cannot have conflicting values." );
			samplers[texture.SamplerParameter] = sampler;
		}
	}

	private static bool Finite( Vector4 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z ) && float.IsFinite( value.w );

	private static void UniqueNames( IEnumerable<string> names )
	{
		var seen = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var name in names )
			if ( string.IsNullOrEmpty( name ) || !seen.Add( name ) ) throw new ArgumentException( "Material parameter names must be nonempty and unique within their category." );
	}

	public bool Equals( GltfMaterialKey other )
	{
		if ( other is null || !ReferenceEquals( Template, other.Template ) || Revision != other.Revision ||
			!features.SequenceEqual( other.features ) || !parameters.SequenceEqual( other.parameters ) || !scalars.SequenceEqual( other.scalars ) || textures.Length != other.textures.Length ) return false;
		for ( var i = 0; i < textures.Length; i++ )
		{
			var a = textures[i]; var b = other.textures[i];
			if ( a.Parameter != b.Parameter || !ReferenceEquals( a.TextureIdentity, b.TextureIdentity ) || a.Revision != b.Revision || a.Sampler != b.Sampler || a.SamplerParameter != b.SamplerParameter ) return false;
		}
		return true;
	}
	public override bool Equals( object obj ) => obj is GltfMaterialKey other && Equals( other );
	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add( RuntimeHelpers.GetHashCode( Template ) ); hash.Add( Revision );
		foreach ( var feature in features ) hash.Add( feature );
		foreach ( var parameter in parameters ) hash.Add( parameter );
		foreach ( var scalar in scalars ) hash.Add( scalar );
		foreach ( var texture in textures )
		{
			hash.Add( texture.Parameter, StringComparer.Ordinal ); hash.Add( RuntimeHelpers.GetHashCode( texture.TextureIdentity ) );
			hash.Add( texture.Revision ); hash.Add( texture.Sampler ); hash.Add( texture.SamplerParameter, StringComparer.Ordinal );
		}
		return hash.ToHashCode();
	}
}

internal sealed class GltfMaterialLease<T> : IDisposable where T : class
{
	private T value;
	private Action release;
	private readonly Action verifyThread;
	internal event Action<GltfMaterialLease<T>> Disposed;
	internal GltfMaterialLease( T value, Action release, Action verifyThread ) { this.value = value; this.release = release; this.verifyThread = verifyThread; }
	internal T Value => value ?? throw new ObjectDisposedException( nameof( GltfMaterialLease<T> ) );
	public void Dispose()
	{
		if ( release is not null ) verifyThread?.Invoke();
		var callback = System.Threading.Interlocked.Exchange( ref release, null );
		if ( callback is null ) return;
		value = null;
		try { callback(); }
		finally { Disposed?.Invoke( this ); Disposed = null; }
	}
}

// Material-specific lease bookkeeping. Native adapter calls are restricted to the main thread.
internal sealed class GltfMaterialCacheCore<T> : IDisposable where T : class
{
	private sealed class Entry { internal GltfMaterialKey Key; internal T Value; internal int Users; }
	private readonly Dictionary<GltfMaterialKey, Entry> entries;
	private readonly HashSet<GltfMaterialKey> creating;
	private readonly Func<GltfMaterialKey, T> create;
	private readonly Action<T> release;
	private readonly Func<T, bool> isAlive;
	private readonly Action verifyThread;
	private bool disposed;
	internal GltfMaterialCacheCore( Func<GltfMaterialKey, T> create, Action<T> release, Func<T, bool> isAlive, IEqualityComparer<GltfMaterialKey> comparer = null, Action verifyThread = null )
	{
		this.create = create ?? throw new ArgumentNullException( nameof( create ) );
		this.release = release ?? throw new ArgumentNullException( nameof( release ) );
		this.isAlive = isAlive ?? throw new ArgumentNullException( nameof( isAlive ) );
		this.verifyThread = verifyThread;
		entries = new Dictionary<GltfMaterialKey, Entry>( comparer );
		creating = new HashSet<GltfMaterialKey>( comparer );
	}
	internal GltfMaterialLease<T> Acquire( GltfMaterialKey key )
	{
		verifyThread?.Invoke();
		ObjectDisposedException.ThrowIf( disposed, this );
		ArgumentNullException.ThrowIfNull( key );
		if ( entries.TryGetValue( key, out var entry ) && !isAlive( entry.Value ) ) { entries.Remove( key ); entry = null; }
		if ( entry is null )
		{
			if ( !creating.Add( key ) ) throw new InvalidOperationException( "Recursive creation of the same glTF material is not supported." );
			T value = null;
			try
			{
				value = create( key ) ?? throw new InvalidOperationException( "Material creation returned null." );
				if ( !isAlive( value ) ) throw new InvalidOperationException( "Material creation returned an invalid resource." );
				ObjectDisposedException.ThrowIf( disposed, this );
				entry = new Entry { Key = key, Value = value };
				entries.Add( key, entry );
				value = null; // Ownership transfers to the entry only after publication.
			}
			finally
			{
				creating.Remove( key );
				if ( value is not null ) release( value );
			}
		}
		entry.Users++;
		return new GltfMaterialLease<T>( entry.Value, () => Release( entry ), verifyThread );
	}
	private void Release( Entry entry )
	{
		if ( --entry.Users != 0 ) return;
		if ( entries.TryGetValue( entry.Key, out var current ) && ReferenceEquals( current, entry ) ) entries.Remove( entry.Key );
		release( entry.Value );
	}
	internal void Clear() { verifyThread?.Invoke(); entries.Clear(); } // Leases keep retired entries alive.
	public void Dispose() { if ( disposed ) return; verifyThread?.Invoke(); disposed = true; entries.Clear(); }
}

/// <summary>
/// Process-scoped prototype, not game-context lifetime integration. Callers must treat
/// shared materials as immutable and acquire a different descriptor for edits. External
/// mutation via public Material APIs is not prevented by this prototype.
/// </summary>
internal sealed class GltfMaterialCache : IDisposable
{
	internal static GltfMaterialCache Shared { get; } = new();
	private readonly GltfMaterialCacheCore<Material> core = new( Create, x => x.Destroy(), x => x.IsValid, verifyThread: AssertMainThread );
	internal GltfMaterialLease<Material> Acquire( GltfMaterialKey key )
	{
		AssertMainThread();
		ArgumentNullException.ThrowIfNull( key );
		if ( key.Template is not Material { IsValid: true } ) throw new ArgumentException( "A valid stock material template is required.", nameof( key ) );
		foreach ( var binding in key.Textures )
			if ( binding.TextureIdentity is not Texture texture || !texture.IsValid || texture.DirtyVersion != binding.Revision )
				throw new InvalidOperationException( "A texture is invalid or changed since the material descriptor was captured." );
		return core.Acquire( key );
	}
	internal void Clear() { AssertMainThread(); core.Clear(); }
	public void Dispose() { AssertMainThread(); core.Dispose(); }
	private static void AssertMainThread()
	{
		ThreadSafe.AssertIsMainThread();
		if ( Graphics.IsActive ) throw new InvalidOperationException( "glTF material cache cannot be used during rendering." );
	}
	private static Material Create( GltfMaterialKey key )
	{
		if ( key.Template is not Material template || !template.IsValid ) throw new ArgumentException( "A valid stock material template is required." );
		Material result = null;
		try
		{
			result = template.CreateCopy();
			if ( result is null || !result.IsValid ) throw new InvalidOperationException( "Could not copy the stock material template." );
			foreach ( var feature in key.Features ) result.SetFeature( feature.Key, feature.Value );
			foreach ( var parameter in key.Parameters ) result.Set( parameter.Key, parameter.Value );
			foreach ( var scalar in key.Scalars ) result.Set( scalar.Key, scalar.Value );
			foreach ( var binding in key.Textures )
			{
				if ( !result.Set( binding.Parameter, (Texture)binding.TextureIdentity ) ) throw new InvalidOperationException( $"Could not bind material texture {binding.Parameter}." );
				if ( binding.SamplerParameter is not null ) result.Attributes.Set( binding.SamplerParameter, binding.Sampler );
			}
			return result;
		}
		catch { result?.Destroy(); throw; }
	}
}

[Expose]
internal sealed class GltfMaterialSceneSystem : GameObjectSystem<GltfMaterialSceneSystem>
{
	private readonly HashSet<GltfMaterialLease<Material>> leases = new();
	private bool disposed;
	private bool disposing;
	public GltfMaterialSceneSystem( Scene scene ) : base( scene ) { }
	internal GltfMaterialLease<Material> Acquire( GltfMaterialKey key )
	{
		ObjectDisposedException.ThrowIf( disposed || disposing, this );
		var lease = GltfMaterialCache.Shared.Acquire( key );
		leases.Add( lease );
		lease.Disposed += OnReleased;
		return lease;
	}
	private void OnReleased( GltfMaterialLease<Material> lease ) => leases.Remove( lease );
	public override void Dispose()
	{
		if ( disposed || disposing ) return;
		disposing = true;
		try
		{
			foreach ( var lease in leases.ToArray() ) lease.Dispose();
			base.Dispose();
			disposed = true;
		}
		finally { disposing = false; }
	}
}
