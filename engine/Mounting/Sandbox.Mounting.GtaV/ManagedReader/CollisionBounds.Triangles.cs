using System;
using System.IO;
using System.Runtime.InteropServices;
using NumericsVector3 = System.Numerics.Vector3;

namespace GtaV.ManagedReader;

internal sealed partial class CollisionBounds
{
	const int Segments = 8;
	const int Rings = Segments / 2;
	static readonly (float Sin, float Cos)[] RadialDirections = BuildRadialDirections();
	static readonly NumericsVector3[] SphereDirections = BuildSphereDirections();

	static (float Sin, float Cos)[] BuildRadialDirections()
	{
		var directions = new (float Sin, float Cos)[Segments + 1];
		for ( int i = 0; i < directions.Length; i++ )
		{
			float angle = (2f * MathF.PI) * i / Segments;
			directions[i] = (MathF.Sin( angle ), MathF.Cos( angle ));
		}
		return directions;
	}

	static NumericsVector3[] BuildSphereDirections()
	{
		var directions = new NumericsVector3[(Rings + 1) * (Segments + 1)];
		for ( int i = 0; i <= Rings; i++ )
		{
			float angle = MathF.PI * i / Rings;
			float sin = MathF.Sin( angle ), cos = MathF.Cos( angle );
			for ( int j = 0; j <= Segments; j++ )
				directions[i * (Segments + 1) + j] = new( sin * RadialDirections[j].Cos, sin * RadialDirections[j].Sin, cos );
		}
		return directions;
	}

	ref struct TriangleWriter
	{
		readonly BinaryWriter output;
		readonly Action<NumericsVector3, NumericsVector3, NumericsVector3> triangle;
		readonly Affine transform;
		readonly ReadOnlySpan<Affine> hullPath;
		internal int Count { get; private set; }

		internal TriangleWriter( BinaryWriter output, Affine transform, ReadOnlySpan<Affine> hullPath,
			Action<NumericsVector3, NumericsVector3, NumericsVector3> triangle )
		{
			this.output = output;
			this.transform = transform;
			this.hullPath = hullPath;
			this.triangle = triangle;
		}

		internal void Triangle( NumericsVector3 a, NumericsVector3 b, NumericsVector3 c )
		{
			a = Point( a );
			b = Point( b );
			c = Point( c );
			if ( triangle is not null ) triangle( a, b, c );
			else
			{
				Span<NumericsVector3> points = stackalloc NumericsVector3[3] { a, b, c };
				output.Write( MemoryMarshal.AsBytes( points ) );
			}
			Count++;
		}

		NumericsVector3 Point( NumericsVector3 point )
		{
			point = transform.Point( point );
			// Hull point clouds apply the transforms from the leaf to the root.
			for ( int i = hullPath.Length - 1; i >= 0; i-- ) point = hullPath[i].Point( point );
			return point;
		}

		void Quad( NumericsVector3 a, NumericsVector3 b, NumericsVector3 c, NumericsVector3 d )
		{
			Triangle( a, b, c );
			Triangle( a, c, d );
		}

		internal void Box( NumericsVector3 origin, NumericsVector3 x, NumericsVector3 y, NumericsVector3 z )
		{
			var p1 = origin + x;
			var p2 = origin + y;
			var p3 = origin + x + y;
			var p4 = origin + z;
			var p5 = origin + x + z;
			var p6 = origin + y + z;
			var p7 = origin + x + y + z;
			Quad( origin, p2, p3, p1 );
			Quad( p4, p5, p7, p6 );
			Quad( origin, p1, p5, p4 );
			Quad( p2, p6, p7, p3 );
			Quad( origin, p4, p6, p2 );
			Quad( p1, p3, p7, p5 );
		}

		internal void Sphere( NumericsVector3 center, float radius )
		{
			Span<NumericsVector3> points = stackalloc NumericsVector3[(Rings + 1) * (Segments + 1)];
			for ( int i = 0; i < points.Length; i++ ) points[i] = center + SphereDirections[i] * radius;
			for ( int i = 0; i < Rings; i++ )
			{
				int top = i * (Segments + 1), bottom = top + Segments + 1;
				for ( int j = 0; j < Segments; j++ )
					Quad( points[top + j], points[bottom + j], points[bottom + j + 1], points[top + j + 1] );
			}
		}

		internal void Cylinder( NumericsVector3 a, NumericsVector3 b, float radius, bool caps )
		{
			var n = b - a;
			float length = Length( n );
			if ( length < 1e-6f ) return;
			n *= 1f / length;
			var u = NumericsVector3.Cross( MathF.Abs( n.Z ) < 0.9f ? NumericsVector3.UnitZ : NumericsVector3.UnitX, n );
			u *= 1f / MathF.Max( Length( u ), 1e-12f );
			var v = NumericsVector3.Cross( n, u );
			var before = (u * RadialDirections[0].Cos + v * RadialDirections[0].Sin) * radius;
			for ( int j = 1; j <= Segments; j++ )
			{
				var directions = RadialDirections[j];
				var after = (u * directions.Cos + v * directions.Sin) * radius;
				var a0 = a + before;
				var a1 = a + after;
				var b0 = b + before;
				var b1 = b + after;
				Quad( a0, a1, b1, b0 );
				if ( caps )
				{
					Triangle( a, a1, a0 );
					Triangle( b, b0, b1 );
				}
				before = after;
			}
		}

		static float Length( NumericsVector3 v ) => MathF.Sqrt( v.X * v.X + v.Y * v.Y + v.Z * v.Z );
	}
}
