using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Sandbox.Gltf.DirectGpu.PrimitiveModeNormalsValidation;

using Matrix4x4 = System.Numerics.Matrix4x4;
using Vector3 = System.Numerics.Vector3;

/// <summary>Model-specific raw-byte oracle. No producer accessor, pose, topology or basis helper supplies expected values.</summary>
public sealed class PrimitiveModeNormalsReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public const int PrimitiveCount = 25;
	public readonly IReadOnlyDictionary<string, byte[]> Blobs;
	public sealed record Form( string Name, byte[] Bytes, Func<string, byte[]> Resolve )
	{
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve );
	}
	public readonly Form[] Forms;
	public PrimitiveModeNormalsReference( string checkoutRoot )
	{
		var root = Path.Combine( Path.GetFullPath( checkoutRoot ), "Temp/gltf-reference/glTF-Sample-Assets/Models/PrimitiveModeNormalsTest/glTF" );
		var blobs = new Dictionary<string, byte[]>( StringComparer.Ordinal );
		foreach ( var (name, length, hash) in new[]
		{
			("PrimitiveModeNormalsTest.gltf", 17010, "432c9445f45c31d71b729c20804f91c04a9c60da67cab76c8fb61c2c70c83631"),
			("Points.bin", 786432, "e1c8835ce934b6b4a9d028130babf442dfe1ec2d39c7e795bcbd4bde363eb363"),
			("Lines.bin", 786432, "98febe022a429d8be2c7f628b89a4a1338354ef186c7b10b2f58f53296f1f8f1"),
			("Triangles.bin", 4380, "99addf8a578a4088a9fdcd67a360c57be9f064355b21f53ad7bf30fffb6fc607"),
			("Colors.bin", 262144, "20fd01539648e65f70bb9c1a31e1334ce0e4fb951436d818ebdf250ec3e74481"),
			("Plane.bin", 92, "68eb0aaba58264604648e828ba7536b3c52333659445200f56d90b23b05a2f36"),
			("Labels.png", 32528, "6deb4b18af0594c4696b2afa2601ccba90b9a05d64322faf9ae608a6372edf15")
		} )
		{
			var bytes = File.ReadAllBytes( Path.Combine( root, name ) );
			Require( bytes.Length == length && Convert.ToHexStringLower( SHA256.HashData( bytes ) ) == hash, "Pinned input changed: " + name );
			blobs.Add( name, bytes );
		}
		Blobs = blobs;
		byte[] Resolve( string uri ) => blobs.TryGetValue( uri, out var bytes ) ? bytes : throw new InvalidDataException( "Unexpected dependency: " + uri );
		var source = blobs["PrimitiveModeNormalsTest.gltf"]; var compiled = GltfCompiler.Compile( source, Resolve );
		Require( BinaryPrimitives.ReadUInt32LittleEndian( compiled ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( 8 ) ) == compiled.Length, "Derived packaging must be a valid GLB container." );
		// The compiler preserves external images; Labels.png remains an explicit package dependency.
		Forms = new[] { new Form( "glTF/source", source, Resolve ), new Form( "glTF/derived_compiled_glb", compiled, Resolve ) };
	}
	public static int SourceMode( int mesh ) => mesh == 24 ? 4 : new[] { 0, 3, 4 }[mesh % 3];
	public static int Node( int mesh ) => mesh == 24 ? 26 : mesh;
	public static int DrawMode( int mesh ) => SourceMode( mesh ) == 3 ? 1 : SourceMode( mesh );
	public static bool HasNormal( int mesh ) => mesh is >= 12 and < 24;
	public static bool HasColor( int mesh ) => mesh < 24 && mesh % 6 >= 3;
	public static bool Flat( int mesh ) => !HasNormal( mesh ) && SourceMode( mesh ) == 4;
	public static int Count( int mesh ) => mesh == 24 ? 4 : mesh % 3 == 2 ? 205 : 65536;
	public static int? Material( int mesh ) => mesh == 24 ? 1 : mesh % 12 >= 6 ? 0 : null;
	public static int PositionAccessor( int mesh ) => mesh == 24 ? 7 : mesh % 3;
	public static int NormalAccessor( int mesh ) => mesh % 3 == 2 ? 3 : mesh % 3;
	public static int ColorAccessor( int mesh ) => mesh % 3 == 2 ? 5 : 6;
	public byte[] PositionBytes( int mesh ) => Blobs[mesh == 24 ? "Plane.bin" : mesh % 3 == 0 ? "Points.bin" : mesh % 3 == 1 ? "Lines.bin" : "Triangles.bin"];
	public Vector3 LocalPosition( int mesh, int vertex ) => Float3( PositionBytes( mesh ), vertex * 12 );
	public Vector3 SourceWorldPosition( int mesh, int vertex )
	{
		var p = LocalPosition( mesh, vertex );
		if ( mesh == 24 ) p = p * new Vector3( 20, 14, 1 ) + new Vector3( -10, -7, -1.5f );
		else p += new Vector3( (mesh % 3 - 1) * 2.5f + (mesh < 12 ? -4 : 4) + 2.5f, 3 - 3 * (mesh % 12 / 3), .5f );
		return p;
	}
	public Vector3 EnginePosition( int mesh, int vertex )
	{
		var p = SourceWorldPosition( mesh, vertex );
		return new Vector3( p.Z / .0254f, p.X / .0254f, p.Y / .0254f );
	}
	public Vector3 AuthoredEngineNormal( int mesh, int vertex )
	{
		Require( HasNormal( mesh ), "No authored normal exists for this primitive." );
		var n = LocalPosition( mesh, vertex ); return Vector3.Normalize( new Vector3( n.Z, n.X, n.Y ) );
	}
	public uint[] RawIndices( int mesh )
	{
		if ( mesh < 24 && mesh % 3 != 2 ) return Enumerable.Range( 0, Count( mesh ) ).Select( x => (uint)x ).ToArray();
		var bytes = Blobs[mesh == 24 ? "Plane.bin" : "Triangles.bin"]; var offset = mesh == 24 ? 80 : 2460; var count = mesh == 24 ? 6 : 960;
		return Enumerable.Range( 0, count ).Select( x => (uint)BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( offset + x * 2, 2 ) ) ).ToArray();
	}
	public uint[] ExpandedIndices( int mesh )
	{
		var indices = RawIndices( mesh );
		if ( SourceMode( mesh ) != 3 ) return indices;
		var pairs = new uint[(indices.Length - 1) * 2];
		for ( var i = 0; i < indices.Length - 1; i++ ) { pairs[i * 2] = indices[i]; pairs[i * 2 + 1] = indices[i + 1]; }
		return pairs;
	}
	public Vector3 FlatEngineNormal( int mesh, uint[] triangleSources, int corner )
	{
		var start = corner / 3 * 3; var a = EnginePosition( mesh, (int)triangleSources[start] );
		var b = EnginePosition( mesh, (int)triangleSources[start + 1] ); var c = EnginePosition( mesh, (int)triangleSources[start + 2] );
		return Vector3.Normalize( Vector3.Cross( b - a, c - a ) );
	}
	public float Color( int mesh, int vertex, int component ) => HasColor( mesh ) ? Blobs["Colors.bin"][vertex * 4 + component] / 255f : 1;
	public float Uv( int mesh, int vertex, int component ) => mesh == 24 ? BinaryPrimitives.ReadSingleLittleEndian( Blobs["Plane.bin"].AsSpan( 48 + vertex * 8 + component * 4, 4 ) ) : 0;
	public static Vector3 TransformNormal( Vector3 normal, Matrix4x4 placement )
	{
		var a = new Vector3( placement.M11, placement.M12, placement.M13 ); var b = new Vector3( placement.M21, placement.M22, placement.M23 ); var c = new Vector3( placement.M31, placement.M32, placement.M33 );
		var determinant = Vector3.Dot( a, Vector3.Cross( b, c ) ); Require( determinant != 0 && float.IsFinite( determinant ), "Normal oracle needs nonsingular finite placement." );
		return Vector3.Normalize( (normal.X * Vector3.Cross( b, c ) + normal.Y * Vector3.Cross( c, a ) + normal.Z * Vector3.Cross( a, b )) / determinant );
	}
	public static Vector3 Float3( ReadOnlySpan<byte> bytes, int offset ) => new( BinaryPrimitives.ReadSingleLittleEndian( bytes[offset..] ), BinaryPrimitives.ReadSingleLittleEndian( bytes[(offset + 4)..] ), BinaryPrimitives.ReadSingleLittleEndian( bytes[(offset + 8)..] ) );
	public static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
