using System;
using System.Buffers;
using System.Collections.Generic;
using Sandbox;
using NumericsVector3 = System.Numerics.Vector3;

namespace GtaV;

internal sealed partial class PhysicsStreamSystem
{
	[ConVar( "gtav_direct_collision", Help = "Decode GTA world collision directly into indexed physics meshes." )]
	internal static bool DirectCollision { get; set; } = true;

	[ConVar( "gtav_authored_collision", Help = "Use authored YBN bounds instead of grid cells for direct world collision." )]
	internal static bool AuthoredCollision { get; set; }

	[ConVar( "gtav_retain_native_collision", Help = "Retain built GTA world collision in the bounded idle cache." )]
	internal static bool RetainNativeCollision { get; set; } = true;

	static Chunk[] DecodeChunks( MountArchive archive, uint file, bool direct, bool authored )
	{
		if ( !direct ) return ReadChunks( archive, file );

		using var stage = new StreamMeasurements.Stage( authored ? "collision.decode_authored" : "collision.decode_direct" );
		using var builder = new WorldChunkBuilder( authored );
		archive.ReadCollisionTriangles( file, builder.Add, authored ? builder.EndLeaf : null );
		return builder.Finish();
	}

	/// <summary>Builds grid chunks or preserves authored leaf boundaries in traversal order.</summary>
	sealed class WorldChunkBuilder( bool authored ) : IDisposable
	{
		// The authored mode permits larger meshes, but caps the worst native build input.
		const int AuthoredMaxTriangles = 2048;
		readonly int pointsPerChunk = (authored ? AuthoredMaxTriangles : MaxTriangles) * 3;

		sealed class Cell
		{
			internal Vector3[] Points;
			internal int Count;
			internal readonly List<Chunk> Chunks = new();
		}

		readonly Dictionary<(int X, int Y), Cell> cells = new();
		readonly Cell leaf = new();
		readonly Dictionary<Vector3, uint> vertexIndices = new();
		readonly List<Vector3> indexedVertices = new();

		internal void Add( NumericsVector3 a, NumericsVector3 b, NumericsVector3 c )
		{
			var first = new Vector3( a.X, a.Y, a.Z );
			var second = new Vector3( b.X, b.Y, b.Z );
			var third = new Vector3( c.X, c.Y, c.Z );
			var cell = leaf;
			if ( !authored )
			{
				var centre = (first + second + third) / 3f;
				var key = ((int)MathF.Floor( centre.x / CellSize ), (int)MathF.Floor( centre.y / CellSize ));
				if ( !cells.TryGetValue( key, out cell ) ) cells.Add( key, cell = new() );
			}
			if ( cell.Points is null || cell.Count + 3 > cell.Points.Length )
			{
				var buffer = ArrayPool<Vector3>.Shared.Rent( Math.Min( pointsPerChunk, Math.Max( 48, cell.Count * 2 ) ) );
				if ( cell.Points is { } previous )
				{
					previous.AsSpan( 0, cell.Count ).CopyTo( buffer );
					ArrayPool<Vector3>.Shared.Return( previous );
				}
				cell.Points = buffer;
			}

			cell.Points[cell.Count++] = first;
			cell.Points[cell.Count++] = second;
			cell.Points[cell.Count++] = third;
			if ( cell.Count == pointsPerChunk ) Flush( cell );
		}

		internal void EndLeaf()
		{
			if ( leaf.Count != 0 ) Flush( leaf );
		}

		void Flush( Cell cell )
		{
			var points = cell.Points.AsSpan( 0, cell.Count );
			var bounds = new BBox( points[0], points[0] );
			for ( int i = 1; i < points.Length; i++ ) bounds = bounds.AddPoint( points[i] );
			var origin = bounds.Center;
			var indices = new uint[points.Length];
			for ( int i = 0; i < points.Length; i++ )
			{
				var vertex = (points[i] - origin) * PacketReader.UnitsPerMetre;
				if ( !vertexIndices.TryGetValue( vertex, out uint index ) )
				{
					index = (uint)indexedVertices.Count;
					vertexIndices.Add( vertex, index );
					indexedVertices.Add( vertex );
				}
				indices[i] = index;
			}

			// Mesh storage is owned by the builder. Only the scratch points return to the pool.
			var vertices = indexedVertices.ToArray();
			if ( vertices.Length < 3 )
			{
				int count = vertices.Length;
				Array.Resize( ref vertices, 3 );
				vertices.AsSpan( count ).Fill( vertices[0] );
			}
			var body = new PhysicsBodyBuilder();
			body.Meshes.Add( new() { Vertices = vertices, Indices = indices, Materials = [] } );
			cell.Chunks.Add( new( bounds, body, points.Length ) );
			ArrayPool<Vector3>.Shared.Return( cell.Points );
			cell.Points = null;
			cell.Count = 0;
			vertexIndices.Clear();
			indexedVertices.Clear();
		}

		internal Chunk[] Finish()
		{
			if ( authored ) return leaf.Chunks.ToArray();
			var chunks = new List<Chunk>();
			foreach ( var cell in cells.Values )
			{
				if ( cell.Count != 0 ) Flush( cell );
				chunks.AddRange( cell.Chunks );
			}
			return chunks.ToArray();
		}

		public void Dispose()
		{
			if ( leaf.Points is { } leafPoints ) ArrayPool<Vector3>.Shared.Return( leafPoints );
			foreach ( var cell in cells.Values )
				if ( cell.Points is { } points ) ArrayPool<Vector3>.Shared.Return( points );
		}
	}
}
