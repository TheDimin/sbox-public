using NativeEngine;
using Sandbox.Engine;
using Sandbox.Engine.Utility.RayTrace;

namespace Sandbox;

/// <summary>
/// A model.
/// </summary>
public sealed partial class Model : Resource
{
	internal IModel native;
	internal bool procedural;
	private GltfModelLifetime gltfLifetime;

	internal void SetGltfLifetime( GlobalContext context, List<GltfMaterialLease<Material>> leases, List<Mesh> meshes )
	{
		if ( gltfLifetime is not null ) throw new InvalidOperationException( "glTF model lifetime has already been assigned." );
		gltfLifetime = new GltfModelLifetime( context, leases, meshes );
	}

	public override bool IsValid => native.IsValid;

	/// <summary>
	/// Private constructor, use <see cref="FromNative(IModel, bool, string)"/>
	/// </summary>
	private Model( IModel native, string name, bool procedural )
	{
		if ( native.IsNull ) throw new Exception( "Model pointer cannot be null!" );

		this.native = native;
		this.Name = name;
		this.procedural = procedural;

		RegisterWeakResourceId( Name, native.GetGuid() );
	}

	internal override void Destroy()
	{
		var gltf = System.Threading.Interlocked.Exchange( ref gltfLifetime, null );
		if ( gltf is not null )
		{
			var handle = native;
			native = default;
			using var ownerScope = new GlobalContext.GlobalContextScope( gltf.Context );
			MainThread.Queue( () =>
			{
				void Release()
				{
					using var scope = new GlobalContext.GlobalContextScope( gltf.Context );
					try
					{
						if ( !handle.IsNull )
						{
							var key = handle.GetBindingPtr().ToInt64();
							if ( NativeResourceCache.TryPeek<Model>( key, out var indexed ) && ReferenceEquals( indexed, this ) )
								NativeResourceCache.Remove( key );
							handle.DestroyStrongHandle();
						}
					}
					finally
					{
						try { GltfModelLoader.ReleaseMeshes( gltf.Meshes ); }
						finally
						{
							foreach ( var lease in gltf.Leases )
							{
								try { lease.Dispose(); }
								catch ( Exception e ) { Log.Warning( e, e.Message ); }
							}
						}
					}
				}

				if ( Graphics.IsActive ) EngineLoop.DisposeAtFrameEnd( Sandbox.Utility.DisposeAction.Create( Release ) );
				else Release();
			} );
			base.Destroy();
			return;
		}

		if ( !native.IsNull )
		{
			var path = ResourcePath;

			var n = native;
			native = default;

			MainThread.Queue( () =>
			{
				n.DestroyStrongHandle();
			} );
		}

		base.Destroy();
	}

	/// <summary>
	/// Called when the resource is reloaded. We should clear any cached values.
	/// </summary>
	internal override void OnReloaded()
	{
		_data?.Dispose();
		_data = null;

		_physics?.Dispose();
		_physics = default;

		_morphs?.Dispose();
		_morphs = null;

		_attachments?.Dispose();
		_attachments = default;

		_animationNames?.Clear();
		_animationNames = default;

		_sequenceNames?.Clear();
		_sequenceNames = default;

		_materials = default;

		_bones = default;
		_hitboxset = default;

		_parts?.Dispose();
		_parts = default;

		DataCache?.Clear();

		BaseModel = default;

		MeshInfo = null;

		IToolsDll.Current?.RunEvent( "model.reload", this );

		foreach ( var scene in Scene.All )
		{
			using var scope = scene.Push();

			var components = scene.GetAllComponents<IHasModel>();
			foreach ( var c in components )
			{
				if ( c.Model != this ) continue;

				c.OnModelReloaded();
			}
		}
	}

	/// <summary>
	/// Whether this model is an error model or invalid or not.
	/// </summary>
	public override bool IsError => native.IsNull || !native.IsStrongHandleValid() || native.IsError();

	/// <summary>
	/// Name of the model, usually being its file path.
	/// </summary>
	public string Name { get; internal set; }

	/// <summary>
	/// Whether this model is procedural, i.e. it was created at runtime via <see cref="ModelBuilder.Create"/>.
	/// </summary>
	public bool IsProcedural => procedural;

	/// <summary>
	/// Total number of meshes this model is made out of.
	/// </summary>
	public int MeshCount => native.GetNumMeshes();

	/// <summary>
	/// The highest LOD index used by this model, or -1 if the model has no effective LODs (all meshes enabled at all levels).
	/// </summary>
	internal int MaxLodLevel => native.ComputeMaxLODLevelUsedByModel();

	/// <summary>
	/// Returns the LOD level for a given screen size in pixels and object scale.
	/// Uses the same calculation as the standard rendering pipeline.
	/// </summary>
	internal int GetLodLevelForScreenSize( float screenWidthInPixels, float scale = 1f )
	{
		return native.LODLevelForScreenSize( screenWidthInPixels, scale );
	}

	/// <summary>
	/// Returns the switch distances for each LOD level, as defined by the model.
	/// </summary>
	internal float[] GetLodSwitchDistances()
	{
		var count = native.GetLODSwitchDistanceCount();
		var distances = new float[count];
		for ( int i = 0; i < count; i++ )
			distances[i] = native.GetLODSwitchDistance( i );
		return distances;
	}

	/// <summary>
	/// Trace against the triangles in this mesh
	/// </summary>
	public MeshTraceRequest Trace => new() { targetModel = this };

	/// <summary>
	/// Base model of this model if one was used.
	/// </summary>
	internal Model BaseModel
	{
		get => field ??= FromNative( native.GetBaseModel() );
		set;
	}
}

/// <summary>Native mesh and shared material ownership attached to a glTF model.</summary>
internal sealed record GltfModelLifetime( GlobalContext Context, List<GltfMaterialLease<Material>> Leases, List<Mesh> Meshes );

internal interface IHasModel
{
	/// <summary>
	/// The <see cref="Model"/> associated with this object.
	/// </summary>
	Model Model { get; }

	/// <summary>
	/// Called by the engine when the associated <see cref="Model"/> is reloaded.
	/// Implementing classes should clear or update any cached data that depends on the model.
	/// </summary>
	void OnModelReloaded();
}
