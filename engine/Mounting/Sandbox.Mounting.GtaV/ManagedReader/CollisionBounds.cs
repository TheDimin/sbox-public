using System;
using System.IO;
using static GtaV.ManagedReader.MetaResource;
using NumericsVector3 = System.Numerics.Vector3;

namespace GtaV.ManagedReader;

/// <summary>Emits mount collision packets from resource bounds.</summary>
internal sealed partial class CollisionBounds( RscResource resource )
{
	const int MaxDepth = 64;
	const int BoundSize = 0x70;
	const int ChildMatrixSize = 64;
	const int PolygonSize = 16;

	enum Shape : byte { Sphere = 0, Geometry = 4, GeometryBvh = 8, Composite = 10 }

	readonly record struct Affine( NumericsVector3 X, NumericsVector3 Y, NumericsVector3 Z, NumericsVector3 Translation )
	{
		internal static Affine Identity => new( NumericsVector3.UnitX, NumericsVector3.UnitY, NumericsVector3.UnitZ, NumericsVector3.Zero );
		internal static Affine Read( ReadOnlySpan<byte> data ) =>
			new( Vector( data, 0 ), Vector( data, 16 ), Vector( data, 32 ), Vector( data, 48 ) );

		internal NumericsVector3 Axes( NumericsVector3 point ) => X * point.X + Y * point.Y + Z * point.Z;
		internal NumericsVector3 Point( NumericsVector3 point ) => Axes( point ) + Translation;
		internal Affine Compose( in Affine next ) => new( Axes( next.X ), Axes( next.Y ), Axes( next.Z ), Point( next.Translation ) );
	}

	readonly ref struct ChildList
	{
		internal ReadOnlySpan<byte> Pointers { get; }
		internal ReadOnlySpan<byte> Matrices { get; }
		internal int Count => Pointers.Length / sizeof(ulong);

		internal ChildList( RscResource resource, ulong pointer )
		{
			var row = resource.At( pointer + BoundSize, 0x32 );
			int count = Short( row, 0x30 );
			Pointers = resource.Array( Pointer( row, 0 ), count, sizeof(ulong) );
			if ( count != 0 && Pointers.IsEmpty ) throw new InvalidDataException( "Composite collision bound has no child list." );
			ulong matrices = Pointer( row, 8 );
			if ( matrices == 0 ) matrices = Pointer( row, 16 );
			Matrices = resource.Array( matrices, count, ChildMatrixSize );
		}

		internal ulong PointerAt( int index ) => Pointer( Pointers, index * sizeof(ulong) );
		internal Affine TransformAt( int index ) => Matrices.IsEmpty ? Affine.Identity : Affine.Read( Matrices[(index * ChildMatrixSize)..] );
	}

	internal void Write( BinaryWriter output, ulong root, bool hulls = false )
	{
		var stream = output.BaseStream;
		long start = stream.Position;
		output.Write( 0 );
		if ( root == 0 ) return;

		Span<ulong> path = stackalloc ulong[MaxDepth];
		Span<Affine> transforms = stackalloc Affine[MaxDepth];
		int count = WriteNode( output, root, Affine.Identity, hulls, path, transforms, 0 );
		WriteCount( output, start, hulls ? -count : count );
	}

	internal void ReadTriangles( ulong root, Action<NumericsVector3, NumericsVector3, NumericsVector3> triangle, Action endLeaf )
	{
		if ( root == 0 ) return;
		Span<ulong> path = stackalloc ulong[MaxDepth];
		WriteNode( null, root, Affine.Identity, false, path, [], 0, triangle, endLeaf );
	}

	internal void WriteSelectedHulls( BinaryWriter output, ulong root, ReadOnlySpan<bool> selected, bool requireMatchingCounts = false )
	{
		long start = output.BaseStream.Position;
		output.Write( 0 );
		if ( root == 0 || (Shape)resource.At( root, BoundSize )[0x10] != Shape.Composite ) return;

		var children = new ChildList( resource, root );
		if ( requireMatchingCounts && children.Count != selected.Length ) return;
		Span<ulong> path = stackalloc ulong[MaxDepth];
		Span<Affine> transforms = stackalloc Affine[MaxDepth];
		path[0] = root;
		int count = 0;
		for ( int i = 0; i < Math.Min( children.Count, selected.Length ); i++ )
		{
			if ( !selected[i] ) continue;
			ulong child = children.PointerAt( i );
			if ( child == 0 ) continue;
			transforms[0] = children.TransformAt( i );
			count = checked(count + WriteNode( output, child, Affine.Identity, true, path, transforms, 1 ));
		}
		WriteCount( output, start, -count );
	}

	int WriteNode( BinaryWriter output, ulong pointer, in Affine transform, bool hulls, Span<ulong> path, Span<Affine> transforms, int depth,
		Action<NumericsVector3, NumericsVector3, NumericsVector3> triangle = null, Action endLeaf = null )
	{
		Visit( path, depth, pointer );
		var row = resource.At( pointer, BoundSize );
		var shape = (Shape)row[0x10];
		if ( shape == Shape.Composite )
		{
			var children = new ChildList( resource, pointer );
			int count = 0;
			for ( int i = 0; i < children.Count; i++ )
			{
				ulong child = children.PointerAt( i );
				if ( child == 0 ) continue;
				var placement = children.TransformAt( i );
				if ( hulls ) transforms[depth] = placement;
				var next = hulls ? transform : transform.Compose( placement );
				count = checked(count + WriteNode( output, child, next, hulls, path, transforms, depth + 1, triangle, endLeaf ));
			}
			return count;
		}

		long start = hulls ? output.BaseStream.Position : 0;
		if ( hulls ) output.Write( 0 );
		var triangles = new TriangleWriter( output, transform, hulls ? transforms[..depth] : [], triangle );
		switch ( shape )
		{
			case Shape.Geometry:
			case Shape.GeometryBvh:
				WriteGeometry( pointer, ref triangles );
				break;
			case Shape.Sphere:
				triangles.Sphere( Vector( row, 0x50 ), Float( row, 0x14 ) );
				break;
			default:
				var min = Vector( row, 0x30 );
				var size = Vector( row, 0x20 ) - min;
				triangles.Box( min, new( size.X, 0, 0 ), new( 0, size.Y, 0 ), new( 0, 0, size.Z ) );
				break;
		}

		if ( !hulls )
		{
			endLeaf?.Invoke();
			return triangles.Count;
		}
		if ( triangles.Count < 2 )
		{
			output.BaseStream.SetLength( start );
			output.BaseStream.Position = start;
			return 0;
		}
		WriteCount( output, start, checked(triangles.Count * 3) );
		return 1;
	}

	void WriteGeometry( ulong pointer, scoped ref TriangleWriter sink )
	{
		var geometry = resource.At( pointer, 0x122 );
		int count = checked((int)Word( geometry, 0xD4 ));
		var polygons = resource.Array( Pointer( geometry, 0x88 ), count, PolygonSize );
		if ( count != 0 && polygons.IsEmpty ) throw new InvalidDataException( "Collision geometry has no polygon list." );
		var vertices = resource.Array( Pointer( geometry, 0xB0 ), checked((int)Word( geometry, 0xD0 )), 6 );
		var quantum = Vector( geometry, 0x90 );
		var center = Vector( geometry, 0xA0 );

		for ( int at = 0; at < polygons.Length; at += PolygonSize )
		{
			var polygon = polygons.Slice( at, PolygonSize );
			int type = polygon[0] & 7;
			switch ( type )
			{
				case 0:
					sink.Triangle( Vertex( vertices, Short( polygon, 4 ) & 0x7FFF, quantum, center ),
						Vertex( vertices, Short( polygon, 6 ) & 0x7FFF, quantum, center ),
						Vertex( vertices, Short( polygon, 8 ) & 0x7FFF, quantum, center ) );
					break;
				case 1:
					sink.Sphere( Vertex( vertices, Short( polygon, 2 ), quantum, center ), Float( polygon, 4 ) );
					break;
				case 2:
				case 4:
					var a = Vertex( vertices, Short( polygon, 2 ), quantum, center );
					var b = Vertex( vertices, Short( polygon, 8 ), quantum, center );
					float radius = Float( polygon, 4 );
					sink.Cylinder( a, b, radius, caps: type == 4 );
					if ( type == 2 )
					{
						sink.Sphere( a, radius );
						sink.Sphere( b, radius );
					}
					break;
				case 3:
					// The polygon stores four corners on the box's diagonals.
					var p1 = Vertex( vertices, Short( polygon, 4 ), quantum, center );
					var p2 = Vertex( vertices, Short( polygon, 6 ), quantum, center );
					var p3 = Vertex( vertices, Short( polygon, 8 ), quantum, center );
					var p4 = Vertex( vertices, Short( polygon, 10 ), quantum, center );
					var edge = ((p3 + p4) - (p1 + p2)) * 0.5f;
					sink.Box( p1, edge, (p3 - edge) - p1, (p4 - edge) - p1 );
					break;
			}
		}
	}

	static NumericsVector3 Vertex( ReadOnlySpan<byte> vertices, int index, NumericsVector3 quantum, NumericsVector3 center )
	{
		if ( index >= vertices.Length / 6 ) return center;
		int offset = index * 6;
		var position = new NumericsVector3( unchecked((short)Short( vertices, offset )),
			unchecked((short)Short( vertices, offset + 2 )), unchecked((short)Short( vertices, offset + 4 )) );
		return position * quantum + center;
	}

	static void Visit( Span<ulong> path, int depth, ulong pointer )
	{
		if ( depth >= path.Length || path[..depth].Contains( pointer ) )
			throw new InvalidDataException( "Collision bounds have an invalid composite hierarchy." );
		path[depth] = pointer;
	}

	static void WriteCount( BinaryWriter output, long start, int count )
	{
		long end = output.BaseStream.Position;
		output.BaseStream.Position = start;
		output.Write( count );
		output.BaseStream.Position = end;
	}
}
