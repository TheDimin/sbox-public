using System;
using System.Linq;
using System.Collections.Generic;
using Sandbox;

namespace GtaV;

/// <summary>Static ocean batches. Split a batch when a detailed surface becomes ready.
/// Ordinary scene objects let the renderer keep its render thread.</summary>
internal sealed class CoastalSurface : IDisposable
{
	readonly SceneWorld world;
	readonly Shoreline[] coast;
	readonly Dictionary<Shoreline, int> indices = new();
	readonly Node[] roots;
	readonly Node[] rootForRegion;
	readonly Material material = Material.Load( "materials/default/white.vmat" ).CreateCopy();

	sealed class Node( int start, int end )
	{
		internal readonly int Start = start, End = end;
		internal int Hidden;
		internal SceneObject Object;
		internal Node Left, Right;
	}

	internal CoastalSurface( Scene scene, Shoreline[] regions )
	{
		// Use the broad, crossed ocean scales in metres for the distant surface.
		// One pair of mipmapped textures serves all far batches in the scene.
		const int side = 64;
		var color = new byte[side * side * 4];
		var normal = new byte[color.Length];
		for ( int y = 0; y < side; y++ )
		for ( int x = 0; x < side; x++ )
		{
			float cross = MathF.Tau * y / side;
			float along = MathF.Tau * x / side;
			float slope = 0.60f * MathF.Cos( along + 1.4f * MathF.Sin( cross ) )
				+ 0.26f * MathF.Cos( 3 * along - 2 * cross ) + 0.14f * MathF.Sin( 5 * cross + along );
			int i = (x + y * side) * 4;
			byte shade = (byte)(219 + 25 * slope);
			color[i] = color[i + 1] = color[i + 2] = shade;
			color[i + 3] = 255;
			normal[i] = (byte)(128 - 9 * slope);
			normal[i + 1] = (byte)(128 - 7 * MathF.Cos( cross ));
			normal[i + 2] = normal[i + 3] = 255;
		}
		material.Set( "g_tColor", Texture.Create( side, side ).WithData( color ).WithMips().Finish() );
		material.Set( "g_tNormal", Texture.Create( side, side ).WithData( normal ).WithMips().Finish() );
		material.Set( "g_vTexCoordScrollSpeed", new Vector2( 0.015f, -0.015f ) );
		world = scene.SceneWorld;
		// A compact spatial batch can be culled as a unit. Keep each water level separate.
		var groups = regions.GroupBy( r => (r.Bounds.Center.z,
			MathF.Floor( r.Bounds.Center.x / 1024 ), MathF.Floor( r.Bounds.Center.y / 1024 )) ).ToArray();
		coast = new Shoreline[regions.Length];
		roots = new Node[groups.Length];
		rootForRegion = new Node[coast.Length];
		int index = 0;
		for ( int group = 0; group < groups.Length; group++ )
		{
			int start = index;
			foreach ( var region in groups[group].OrderBy( r => r.Bounds.Center.x ).ThenBy( r => r.Bounds.Center.y ) )
			{
				coast[index] = region;
				indices.Add( region, index++ );
			}
			var root = roots[group] = new Node( start, index );
			Array.Fill( rootForRegion, root, start, index - start );
			Draw( root, true );
		}
	}

	void Draw( Node node, bool visible )
	{
		if ( visible && node.Object is null )
		{
			var vertices = new List<Vertex>();
			var triangles = new List<int>();
			var bounds = coast[node.Start].Bounds;
			for ( int i = node.Start; i < node.End; i++ )
			{
				var box = coast[i].Bounds;
				bounds = bounds.AddBBox( box );
				var a = box.Mins * PacketReader.UnitsPerMetre;
				var b = box.Maxs * PacketReader.UnitsPerMetre;
				int k = vertices.Count;
				void Corner( float x, float y ) => vertices.Add( new Vertex( new Vector3( x, y, a.z ), Vector3.Up, Vector3.Forward, new Vector2( y / (448f * PacketReader.UnitsPerMetre), x / (512f * PacketReader.UnitsPerMetre) ) ) );
				Corner( a.x, a.y );
				Corner( a.x, b.y );
				Corner( b.x, b.y );
				Corner( b.x, a.y );
				triangles.AddRange( [k, k + 2, k + 1, k, k + 3, k + 2] );
			}
			var mesh = new Mesh( material ) { Bounds = new BBox( bounds.Mins * PacketReader.UnitsPerMetre - Vector3.Up,
				bounds.Maxs * PacketReader.UnitsPerMetre + Vector3.Up ) };
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.CreateIndexBuffer( triangles.Count, triangles );
			node.Object = new SceneObject( world, Model.Builder.AddMesh( mesh ).Create(), Transform.Zero ) { ColorTint = new Color( 0.013f, 0.20f, 0.30f ) };
			node.Object.Flags.CastShadows = false;
		}
		if ( node.Object.IsValid() ) node.Object.RenderingEnabled = visible;
	}

	internal void Show( Shoreline region ) => Change( region, -1 );
	internal void Hide( Shoreline region ) => Change( region, 1 );

	void Change( Shoreline region, int delta )
	{
		int index = indices[region];
		Change( rootForRegion[index], index, delta );
	}

	void Change( Node node, int index, int delta )
	{
		bool wasFull = node.Hidden == 0;
		node.Hidden += delta;
		int length = node.End - node.Start;
		if ( length > 1 )
		{
			int mid = node.Start + length / 2;
			node.Left ??= new( node.Start, mid );
			node.Right ??= new( mid, node.End );
			var changed = index < mid ? node.Left : node.Right;
			var other = index < mid ? node.Right : node.Left;
			Change( changed, index, delta );
			if ( wasFull ) Draw( other, true );
			if ( node.Hidden == 0 )
			{
				Draw( changed, false );
				Draw( other, false );
			}
		}
		Draw( node, node.Hidden == 0 );
	}

	public void Dispose()
	{
		foreach ( var root in roots ) Release( root );
	}

	static void Release( Node node )
	{
		node.Object?.Delete();
		if ( node.Left is null ) return;
		Release( node.Left );
		Release( node.Right );
	}
}
