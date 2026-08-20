using Sandbox.Rendering;
using static Sandbox.TerrainClipmap;

namespace Sandbox;

/// <summary>
/// Renders the terrain clipmap as instanced meshlets, frustum culled on the CPU.
/// </summary>
internal sealed class TerrainClipmapSceneObject : SceneCustomObject
{
	public int BlockSize;
	public float UnitsPerTexel;
	public float HeightScale;

	// A queued normal bake, run on the render thread before we draw so every pass this frame samples fresh data
	public CommandList PendingBake;

	private Frustum _cullFrustum;
	private Vector2 _clipCameraLocal;

	private Tier[] _tiers = [];

	private struct Tier
	{
		public Model Mesh;
		public Meshlet[] Meshlets;
		public CullGroup[] CullGroups;
		public GpuBuffer<Meshlet> FullBuffer;    // the whole layout, drawn by shadow passes
		public GpuBuffer<Meshlet> VisibleBuffer; // frustum-culled subset, refreshed for each camera view
		public Meshlet[] Visible;
		public int VisibleCount;
	}

	private readonly struct CullGroup
	{
		public readonly int Start;
		public readonly int Count;
		public readonly int Level;
		public readonly Vector2 MinimumBlockOffset;
		public readonly Vector2 MaximumBlockOffset;

		public CullGroup( int start, int count, int level, Vector2 minimumBlockOffset, Vector2 maximumBlockOffset )
		{
			Start = start;
			Count = count;
			Level = level;
			MinimumBlockOffset = minimumBlockOffset;
			MaximumBlockOffset = maximumBlockOffset;
		}
	}

	public TerrainClipmapSceneObject( SceneWorld world ) : base( world ) { }

	private static Tier GenerateTierMesh( Meshlet[] meshlets, int density, int blockSize, Material material )
	{
		var fullBuffer = new GpuBuffer<Meshlet>( meshlets.Length, GpuBuffer.UsageFlags.Structured, "TerrainMeshlets" );
		fullBuffer.SetData( meshlets );

		return new Tier
		{
			Mesh = Model.Builder.AddMesh( BuildBlockMesh( blockSize, density, material ) ).Create(),
			Meshlets = meshlets,
			CullGroups = BuildCullGroups( meshlets ),
			FullBuffer = fullBuffer,
			VisibleBuffer = new GpuBuffer<Meshlet>( meshlets.Length, GpuBuffer.UsageFlags.Structured, "TerrainMeshlets" ),
			Visible = new Meshlet[meshlets.Length],
		};
	}

	private static CullGroup[] BuildCullGroups( Meshlet[] meshlets )
	{
		const int maximumGroupSize = 16;
		var groups = new List<CullGroup>( (meshlets.Length + maximumGroupSize - 1) / maximumGroupSize );
		int start = 0;

		while ( start < meshlets.Length )
		{
			int level = meshlets[start].Level;
			int end = start + 1;
			var minimum = meshlets[start].BlockOffset;
			var maximum = minimum;

			while ( end < meshlets.Length && end - start < maximumGroupSize && meshlets[end].Level == level )
			{
				minimum = Vector2.Min( minimum, meshlets[end].BlockOffset );
				maximum = Vector2.Max( maximum, meshlets[end].BlockOffset );
				end++;
			}

			groups.Add( new CullGroup( start, end - start, level, minimum, maximum ) );
			start = end;
		}

		return [.. groups];
	}

	/// <summary>
	/// Build the render meshes from a meshlet layout. The first <paramref name="displacedLevels"/> LOD levels
	/// get a mesh subdivided <paramref name="density"/> times for displacement detail.
	/// </summary>
	public void Build( Meshlet[] meshlets, int blockSize, int density, int displacedLevels, Material material )
	{
		DisposeBuffers();
		BlockSize = blockSize;

		// Two tiers: near is subdivided for displacement, far isn't. Either can be empty,
		// and a zero-length buffer is invalid, so skip the empty ones.
		var nearMeshlets = meshlets.Where( m => m.Level < displacedLevels ).ToArray();
		var farMeshlets = meshlets.Where( m => m.Level >= displacedLevels ).ToArray();

		var tiers = new List<Tier>( 2 );

		if ( nearMeshlets.Length > 0 )
			tiers.Add( GenerateTierMesh( nearMeshlets, density, blockSize, material ) );
		if ( farMeshlets.Length > 0 )
			tiers.Add( GenerateTierMesh( farMeshlets, 1, blockSize, material ) );

		_tiers = [.. tiers];
	}

	internal void UpdateClipCamera( Vector3 cameraWorld )
	{
		var cameraLocal = Transform.PointToLocal( cameraWorld );
		_clipCameraLocal = new Vector2( cameraLocal.x, cameraLocal.y );
		Attributes.Set( "ClipCameraLocal", _clipCameraLocal );
	}

	private void UpdateView( Frustum frustum, Vector3 cameraWorld )
	{
		_cullFrustum = frustum;

		var cameraLocal = Transform.PointToLocal( cameraWorld );
		_clipCameraLocal = new Vector2( cameraLocal.x, cameraLocal.y );

		foreach ( ref var tier in _tiers.AsSpan() )
			Cull( ref tier );
	}

	public override void RenderSceneObject()
	{
		base.RenderSceneObject();

		// Run a queued normal bake once, before anything draws this frame
		var bake = PendingBake;
		PendingBake = null;
		bake?.ExecuteOnRenderThread();

		// Shadow passes need off-screen casters, so they draw the whole clipmap;
		// camera passes draw the culled subset. Graphics.Attributes is per-pass,
		// so these don't stomp other passes recording at the same time.
		bool shadow = Graphics.LayerType == SceneLayerType.Shadow;

		if ( !shadow )
			UpdateView(
				Graphics.Frustum.Scaled( Terrain.MeshletFrustumScale, Graphics.CameraPosition, Graphics.FieldOfView > 0.0f ),
				Graphics.CameraPosition );

		Attributes.MergeTo( Graphics.Attributes );
		Graphics.Attributes.Set( "ClipCameraLocal", _clipCameraLocal );
		Graphics.Attributes.Set( "TerrainShadowPass", shadow );

		foreach ( ref var tier in _tiers.AsSpan() )
		{
			int count = shadow ? tier.Meshlets.Length : tier.VisibleCount;
			if ( count <= 0 ) continue;

			Graphics.Attributes.Set( "TerrainMeshlets", shadow ? tier.FullBuffer : tier.VisibleBuffer );
			Graphics.DrawModelInstanced( tier.Mesh, count );
		}
	}

	private void Cull( ref Tier tier )
	{
		var terrainTransform = Transform;

		int vis = 0;
		foreach ( ref readonly var group in tier.CullGroups.AsSpan() )
		{
			float vertexStep = UnitsPerTexel * (1 << group.Level);
			float increment = vertexStep * 2.0f;
			var center = _clipCameraLocal.SnapToGrid( increment );

			if ( !_cullFrustum.IsInside( GetCullGroupAABB( in group, center, vertexStep, increment, terrainTransform ), partially: true ) )
				continue;

			int end = group.Start + group.Count;
			for ( int i = group.Start; i < end; i++ )
			{
				ref readonly var meshlet = ref tier.Meshlets[i];
				if ( _cullFrustum.IsInside( GetMeshletAABB( in meshlet, center, vertexStep, increment, terrainTransform ), partially: true ) )
					tier.Visible[vis++] = meshlet;
			}
		}

		if ( vis > 0 )
			tier.VisibleBuffer.SetData( tier.Visible.AsSpan( 0, vis ) );

		tier.VisibleCount = vis;
	}

	private BBox GetMeshletAABB( in Meshlet m, Vector2 center, float vertexStep, float increment, in Transform terrainTransform )
	{
		float ox = center.x + m.BlockOffset.x * vertexStep;
		float oy = center.y + m.BlockOffset.y * vertexStep;
		float ext = BlockSize * vertexStep;

		// Grow by one snap increment so sub-cell rounding / vertex displacement never culls an on-screen block
		var localBox = new BBox(
			new Vector3( ox - increment, oy - increment, -increment ),
			new Vector3( ox + ext + increment, oy + ext + increment, HeightScale + increment ) );

		return TransformBounds( localBox, terrainTransform );
	}

	private BBox GetCullGroupAABB( in CullGroup group, Vector2 center, float vertexStep, float increment, in Transform terrainTransform )
	{
		float minX = center.x + group.MinimumBlockOffset.x * vertexStep;
		float minY = center.y + group.MinimumBlockOffset.y * vertexStep;
		float maxX = center.x + (group.MaximumBlockOffset.x + BlockSize) * vertexStep;
		float maxY = center.y + (group.MaximumBlockOffset.y + BlockSize) * vertexStep;

		var localBox = new BBox(
			new Vector3( minX - increment, minY - increment, -increment ),
			new Vector3( maxX + increment, maxY + increment, HeightScale + increment ) );

		return TransformBounds( localBox, terrainTransform );
	}

	private static BBox TransformBounds( in BBox localBox, in Transform terrainTransform )
	{
		if ( terrainTransform.Rotation == Rotation.Identity && terrainTransform.Scale == Vector3.One )
			return new BBox( localBox.Mins + terrainTransform.Position, localBox.Maxs + terrainTransform.Position );

		return localBox.Transform( terrainTransform );
	}

	private void DisposeBuffers()
	{
		foreach ( var tier in _tiers )
		{
			tier.FullBuffer?.Dispose();
			tier.VisibleBuffer?.Dispose();
		}

		_tiers = [];
	}

	internal override void OnNativeDestroy()
	{
		DisposeBuffers();
		base.OnNativeDestroy();
	}
}
