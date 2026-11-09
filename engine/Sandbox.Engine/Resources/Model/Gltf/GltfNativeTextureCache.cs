using Sandbox.Internal;
using Sandbox.Engine;
using Sandbox.Gltf.DirectGpu;
using System.IO;
using System.Security.Cryptography;

namespace Sandbox;

/// <summary>
/// Shares compiled native texture resources while glTF model material leases use
/// them. Texture.Load owns GPU upload; this never creates procedural asset textures.
/// </summary>
internal static class GltfNativeTextureCache
{
	private sealed class Entry
	{
		internal Texture Texture;
		internal GlobalContext Context;
		internal RedirectFileSystem Redirect;
		internal string PhysicalPath;
		internal string Alias;
		internal int Users;
	}

	private static readonly Dictionary<GlobalContext, Dictionary<string, Entry>> contexts = new( ReferenceEqualityComparer.Instance );

	internal static GltfMaterialLease<Texture> Acquire( byte[] compiled )
		=> AcquireBatch( [compiled] )[0];

	/// <summary>
	/// Register private fresh aliases and try them without invalidating the
	/// database. Failed lookups retry under new aliases after one invalidation.
	/// Each returned lease is independently owned, including duplicates; failed
	/// batches roll back every newly created resource.
	/// </summary>
	internal static GltfMaterialLease<Texture>[] AcquireBatch( IReadOnlyList<byte[]> compiledResources, Func<string, Texture> load = null )
	{
		AssertMainThread();
		ArgumentNullException.ThrowIfNull( compiledResources );
		load ??= alias => Texture.Load( alias, false );
		if ( compiledResources.Count == 0 ) return [];
		var context = GlobalContext.Current;
		if ( !contexts.TryGetValue( context, out var entries ) )
			contexts.Add( context, entries = new( StringComparer.Ordinal ) );
		var hashes = new string[compiledResources.Count];
		var staged = new Dictionary<string, Entry>( StringComparer.Ordinal );
		try
		{
			for ( var i = 0; i < compiledResources.Count; i++ )
			{
				var compiled = compiledResources[i];
				ArgumentNullException.ThrowIfNull( compiled );
				var hash = hashes[i] = Convert.ToHexStringLower( SHA256.HashData( compiled ) );
				if ( entries.TryGetValue( hash, out var retired ) && !retired.Texture.IsValid )
					entries.Remove( hash ); // Outstanding leases retain the retired generation.
				if ( entries.ContainsKey( hash ) || staged.ContainsKey( hash ) ) continue;
				var directory = Path.Combine( Path.GetTempPath(), "sbox-gltf-textures", Environment.ProcessId.ToString() );
				// Fresh aliases preserve native negative/stale cache isolation.
				var id = Guid.NewGuid().ToString( "N" );
				var path = Path.Combine( directory, id + ".vtex_c" );
				var alias = "gltf-native-textures/" + id + ".vtex";
				var entry = new Entry { PhysicalPath = path, Context = context, Alias = alias };
				staged.Add( hash, entry );
				Directory.CreateDirectory( directory );
				File.WriteAllBytes( path, compiled );
				entry.Redirect = RedirectFileSystem.Create( directory );
				entry.Redirect.AddAbsFile( "/" + alias + "_c", path );
			}
			if ( staged.Count != 0 )
			{
				// These aliases are private GUIDs: each is registered before its
				// first load and never reused for a later generation. Avoid a global
				// database scan on the normal path. A previously queried name must
				// not use this shortcut: native negative lookups can remain stale.
				var retry = new List<Entry>();
				foreach ( var entry in staged.Values )
				{
					var texture = load( entry.Alias );
					if ( texture is null || !texture.IsValid || texture.IsError )
					{
						retry.Add( entry );
						continue; // Invalid/error textures can be borrowed native sentinels.
					}
					entry.Texture = texture;
				}
				if ( retry.Count != 0 )
				{
					// A failed lookup may have installed a negative native entry. Give
					// each retry a new private alias and refresh once before loading.
					foreach ( var entry in retry )
					{
						entry.Alias = "gltf-native-textures/" + Guid.NewGuid().ToString( "N" ) + ".vtex";
						entry.Redirect.AddAbsFile( "/" + entry.Alias + "_c", entry.PhysicalPath );
					}
					NativeEngine.g_pResourceSystem.InvalidateDatabase();
					foreach ( var entry in retry )
					{
						var texture = load( entry.Alias );
						if ( texture is null || !texture.IsValid || texture.IsError )
							throw new Gltf.GltfException( "Compiled glTF texture could not be loaded by the native resource system after refreshing the database." );
						entry.Texture = texture;
					}
				}
				foreach ( var (hash, entry) in staged )
				{
					entries.Add( hash, entry );
				}
			}
		}
		catch ( Exception original )
		{
			GltfGpuCleanup.Rollback( original, RollbackSteps() );
			throw;
		}
		IEnumerable<Action> RollbackSteps()
		{
			foreach ( var (hash, entry) in staged )
				yield return () =>
				{
					if ( entries.TryGetValue( hash, out var current ) && ReferenceEquals( current, entry ) ) entries.Remove( hash );
					Destroy( entry );
				};
			yield return () => { if ( entries.Count == 0 && contexts.TryGetValue( context, out var current ) && ReferenceEquals( current, entries ) ) contexts.Remove( context ); };
		}
		var leases = new GltfMaterialLease<Texture>[hashes.Length];
		for ( var i = 0; i < hashes.Length; i++ ) leases[i] = CreateLease( context, entries, hashes[i], entries[hashes[i]] );
		return leases;
	}

	private static GltfMaterialLease<Texture> CreateLease( GlobalContext context, Dictionary<string, Entry> entries, string hash, Entry entry )
	{
		entry.Users++;
		return new GltfMaterialLease<Texture>( entry.Texture, () =>
		{
			if ( --entry.Users != 0 ) return;
			if ( entries.TryGetValue( hash, out var current ) && ReferenceEquals( current, entry ) ) entries.Remove( hash );
			try { Destroy( entry ); }
			finally
			{
				if ( entries.Count == 0 && contexts.TryGetValue( context, out var currentEntries ) && ReferenceEquals( currentEntries, entries ) ) contexts.Remove( context );
			}
		}, AssertMainThread );
	}

	private static void Destroy( Entry entry )
	{
		using var ownerScope = new GlobalContext.GlobalContextScope( entry.Context );
		try { entry.Texture?.Dispose(); }
		finally
		{
			try { entry.Redirect?.Dispose(); }
			finally
			{
				if ( File.Exists( entry.PhysicalPath ) ) File.Delete( entry.PhysicalPath );
			}
		}
	}

	private static void AssertMainThread()
	{
		ThreadSafe.AssertIsMainThread();
		if ( Graphics.IsActive ) throw new InvalidOperationException( "glTF texture resources cannot be acquired or released during rendering." );
	}
}
