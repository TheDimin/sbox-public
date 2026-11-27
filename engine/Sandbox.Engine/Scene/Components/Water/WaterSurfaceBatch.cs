using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>Draws streamed horizontal water tiles with one material and a common wave clock.
/// Each WaterBody retains its own volume, height queries and underwater effects.</summary>
public sealed class WaterSurfaceBatch : Component, Component.ExecuteInEditor
{
	[ConVar( "gtav_water_batch", Help = "Batch nearby GTA water surfaces for new world sessions." )]
	public static bool UseForGtaWorld { get; set; } = true;

	internal struct Tile
	{
		public Vector4 Bounds, Waves, Band0, Band1, Band2, Band3, Overlays;
	}

	readonly List<WaterBody> bodies = [];
	readonly List<WaterOverlay> overlays = [];
	readonly List<WaterOverlay.Data> projections = [];
	readonly List<Tile> tiles = [];
	GpuBuffer<Tile> tileBuffer;
	GpuBuffer<WaterOverlay.Data> foamBuffer;
	Tile[] uploadedTiles = [];
	WaterOverlay.Data[] uploadedFoam = [];
	Sandbox.Rendering.CommandList environmentCommands;
	WaterBody.EnvironmentBinding lastEnvironment;
	bool hasEnvironment;
	ModelRenderer renderer;
	bool dirty;
	internal void Add( WaterBody body ) { bodies.Add( body ); dirty = true; }
	internal void Remove( WaterBody body ) { bodies.Remove( body ); dirty = true; }

	protected override void OnPreRender()
	{
		if ( bodies.Count == 0 )
		{
			if ( renderer.IsValid() ) renderer.Model = null;
			return;
		}

		if ( !renderer.IsValid() )
		{
			renderer = GameObject.Components.Create<ModelRenderer>();
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			GameObject.Tags.Add( WaterBody.SurfaceTag );
		}

		if ( dirty )
		{
			renderer.Model = BuildMesh();
			dirty = false;
		}

		overlays.Clear();
		Scene.GetAll( overlays );
		overlays.Sort( ( a, b ) => a.SortLayer != b.SortLayer ? a.SortLayer.CompareTo( b.SortLayer ) : a.Id.CompareTo( b.Id ) );
		tiles.Clear();
		projections.Clear();
		foreach ( var body in bodies ) tiles.Add( body.BatchTile() );
		// Resolve each overlay owner once. The projections for each tile occupy one contiguous range.
		foreach ( var overlay in overlays )
		{
			if ( !overlay.Active || !overlay.Texture.IsValid() || overlay.Tint.a <= 0 ) continue;
			var body = overlay.Body;
			int index = bodies.IndexOf( body );
			if ( index < 0 ) continue;
			overlay.Texture.MarkUsed();
			var tile = tiles[index];
			int insert = 0;
			for ( int j = 0; j <= index; j++ ) insert += (int)tiles[j].Overlays.y;
			projections.Insert( insert, overlay.GetData( body ) );
			tile.Overlays.y++;
			tiles[index] = tile;
		}
		int offset = 0;
		for ( int i = 0; i < tiles.Count; i++ )
		{
			var t = tiles[i];
			t.Overlays.x = offset;
			offset += (int)t.Overlays.y;
			tiles[i] = t;
		}

		var environment = bodies[0].CurrentEnvironment();
		if ( !hasEnvironment || !environment.Same( lastEnvironment ) )
		{
			environmentCommands ??= new( "Batched water environment" );
			environmentCommands.Reset();
			WaterBody.BindEnvironment( environmentCommands, environment );
			renderer.ExecuteBefore = environmentCommands;
			lastEnvironment = environment;
			hasEnvironment = true;
		}
		Upload( ref tileBuffer, ref uploadedTiles, tiles );
		if ( projections.Count > 0 ) Upload( ref foamBuffer, ref uploadedFoam, projections );
		var a = renderer.Attributes;
		a.Set( "WaterBatchEnabled", 1 );
		a.Set( "WaterBatchTiles", tileBuffer );
		a.Set( "WaterOverlays", foamBuffer );
		a.Set( "WaterOverlayCount", projections.Count );
		a.Set( "WaveTime", (float)Time.NowDouble );
		a.Set( "DetailTime", bodies[0].DetailTime );
		a.Set( "WaveEdgeFade", 1f );
	}

	static void Upload<T>( ref GpuBuffer<T> buffer, ref T[] uploaded, List<T> data ) where T : unmanaged
	{
		var span = CollectionsMarshal.AsSpan( data );
		if ( uploaded.Length == span.Length && MemoryMarshal.AsBytes( span ).SequenceEqual( MemoryMarshal.AsBytes( uploaded.AsSpan() ) ) )
			return;
		if ( uploaded.Length != span.Length ) uploaded = new T[span.Length];
		span.CopyTo( uploaded );
		if ( buffer is null || buffer.ElementCount < data.Count )
		{
			buffer?.Dispose();
			buffer = new( (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)data.Count ) );
		}
		buffer.SetData( CollectionsMarshal.AsSpan( data ) );
	}

	Model BuildMesh()
	{
		var vertices = new List<Vertex>();
		var indices = new List<int>();
		var bounds = BBox.FromPositionAndSize( WorldTransform.PointToLocal( bodies[0].WorldPosition ), 0 );
		for ( int id = 0; id < bodies.Count; id++ )
		{
			var b = bodies[id];
			var origin = b.WorldPosition;
			var size = b.Size;
			var extent = new Vector3( size.x, size.y, 0 ) * 0.5f;
			bounds = bounds.AddPoint( WorldTransform.PointToLocal( origin - extent ) );
			bounds = bounds.AddPoint( WorldTransform.PointToLocal( origin + extent ) );
			int nx = Math.Clamp( (int)MathF.Ceiling( size.x / 128 ), 1, 32 );
			int ny = Math.Clamp( (int)MathF.Ceiling( size.y / 128 ), 1, 32 );
			for ( int side = 0; side < 2; side++ )
			{
				int first = vertices.Count;
				for ( int y = 0; y <= ny; y++ )
				for ( int x = 0; x <= nx; x++ )
				{
					var pos = origin + new Vector3( size.x * ((float)x / nx - 0.5f), size.y * ((float)y / ny - 0.5f), 0 );
					vertices.Add( new Vertex( WorldTransform.PointToLocal( pos ), side == 0 ? Vector3.Up : Vector3.Down, Vector3.Forward, new Vector4( MathF.Max( size.x / nx, size.y / ny ), 0, 0, 0 ) )
					{ TexCoord1 = new Vector4( id, side, 0, 0 ) } );
					if ( x == 0 || y == 0 ) continue;
					int d = vertices.Count - 1, c = d - 1, a = c - nx - 1, e = a + 1;
					indices.Add( a );
					indices.Add( side == 0 ? e : c );
					indices.Add( side == 0 ? c : e );
					indices.Add( e );
					indices.Add( side == 0 ? d : c );
					indices.Add( side == 0 ? c : d );
				}
			}
		}
		var mesh = new Mesh( bodies[0].ActiveMaterial );
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.Bounds = bounds.Grow( 512 );
		return Model.Builder.AddMesh( mesh ).Create();
	}

	protected override void OnDestroy()
	{
		tileBuffer?.Dispose();
		foamBuffer?.Dispose();
	}
}

public sealed partial class WaterBody
{
	internal WaterSurfaceBatch.Tile BatchTile()
	{
		UpdateWaves();
		return new()
		{
			Bounds = new Vector4( WorldPosition.x, WorldPosition.y, Size.x * 0.5f, Size.y * 0.5f ),
			Waves = new Vector4( WaveHeight, SharedSwellHeight, WaveEdgeFade, 0 ),
			Band0 = _waves[0], Band1 = _waves[1], Band2 = _waves[2], Band3 = _waves[3]
		};
	}
}
