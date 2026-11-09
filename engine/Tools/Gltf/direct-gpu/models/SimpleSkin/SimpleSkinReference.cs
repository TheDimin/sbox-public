using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.Validation;

using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// Independent oracle for the pinned SimpleSkin asset, not a general glTF loader.
/// Literal geometry, weights, joint pivot and rounded authored quaternion keys
/// were transcribed from the pinned sample's raw little-endian buffers.
/// </summary>
public static class SimpleSkinReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public const double Duration = 5.5;
	public static readonly uint[] TriangleIndices = { 0, 1, 3, 0, 3, 2, 2, 3, 5, 2, 5, 4, 4, 5, 7, 4, 7, 6, 6, 7, 9, 6, 9, 8 };
	private static readonly (float Z, float W)[] Keys =
	{
		(0, 1), (.383f, .924f), (.707f, .707f), (.707f, .707f), (.383f, .924f), (0, 1),
		(0, 1), (-.383f, .924f), (-.707f, .707f), (-.707f, .707f), (-.383f, .924f), (0, 1)
	};
	public static readonly float[] SampleTimes = BuildSampleTimes();

	public sealed class Form
	{
		private readonly byte[] source;
		private readonly Func<string, byte[]> resolve;
		internal Form( string name, byte[] source, Func<string, byte[]> resolve ) => (Name, this.source, this.resolve) = (name, source, resolve);
		public string Name { get; }
		public GltfDocument Read() => GltfReader.Read( source, resolve );
	}

	/// <summary>Two pinned source variants and each variant's losslessly compiled valid GLB.</summary>
	public static Form[] OpenForms( string checkoutRoot )
	{
		var root = Path.Combine( Path.GetFullPath( checkoutRoot ), "Temp/gltf-reference/glTF-Sample-Assets/Models/SimpleSkin" );
		var blobs = new Dictionary<string, byte[]>( StringComparer.Ordinal );
		foreach ( var entry in new[]
		{
			("SimpleSkin_geometry.bin", "35f0ca6bc07976c0aadc163d4737758989cfcc9870a99800004c4478fe698b63"),
			("SimpleSkin_skinningData.bin", "413bc8a8c0da673df767874e6f05f0b00c9261756a08586b9d555bd5535414b5"),
			("SimpleSkin_inverseBindMatrices.bin", "5844ab221cd2ad367420a248f6387b46e5f8498604427730a37ee96b4d4ec599"),
			("SimpleSkin_animation.bin", "ff21b1bc3d0abbe53f7665060ec6c22d873b6e85a7669171a2b6b8ae01c39347")
		} ) blobs.Add( entry.Item1, ReadPinned( Path.Combine( root, "glTF", entry.Item1 ), entry.Item2 ) );
		byte[] Resolve( string uri ) => blobs.TryGetValue( uri, out var bytes ) ? bytes : throw new InvalidOperationException( "Unexpected SimpleSkin dependency: " + uri );
		var results = new List<Form>();
		foreach ( var variant in new[]
		{
			("glTF", "8d92e9888340eb98e82a65a1f8b037d9a1d9f09e5fd0b6ba6d40fd93700b3239"),
			("glTF-Embedded", "7d0c3f48d0510d101269cb4fdd3ee035eaada0e04809a0899e0b4cfe8c38e68f")
		} )
		{
			var bytes = ReadPinned( Path.Combine( root, variant.Item1, "SimpleSkin.gltf" ), variant.Item2 );
			results.Add( new Form( variant.Item1 + "/source", bytes, Resolve ) );
			var compiled = GltfCompiler.Compile( bytes, Resolve );
			if ( BinaryPrimitives.ReadUInt32LittleEndian( compiled ) != 0x46546c67 || BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( 4 ) ) != 2 || BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( 8 ) ) != compiled.Length )
				throw new InvalidOperationException( "Compiler did not emit a valid GLB header." );
			results.Add( new Form( variant.Item1 + "/compiled_glb", compiled, Resolve ) );
		}
		return results.ToArray();
	}

	private static byte[] ReadPinned( string path, string expectedHash )
	{
		using var stream = File.OpenRead( path );
		if ( stream.Length is < 1 or > 8192 ) throw new InvalidOperationException( "Unexpected sample input length: " + path );
		var bytes = new byte[(int)stream.Length];
		stream.ReadExactly( bytes );
		if ( stream.ReadByte() != -1 || Convert.ToHexStringLower( SHA256.HashData( bytes ) ) != expectedHash )
			throw new InvalidOperationException( "Pinned sample input changed: " + path );
		return bytes;
	}

	public static Vector3 BindPosition( int vertex )
	{
		if ( (uint)vertex >= 10 ) throw new ArgumentOutOfRangeException( nameof( vertex ) );
		return new Vector3( (vertex & 1) == 0 ? -.5f : .5f, (vertex / 2) * .5f, 0 );
	}

	/// <summary>
	/// Same-axis shortest-arc quaternion SLERP is linear angular interpolation.
	/// atan2 reads the actual rounded .383/.924 authored values rather than
	/// pretending the sample contains an exact 45-degree key.
	/// </summary>
	public static double Angle( double time )
	{
		if ( !double.IsFinite( time ) ) throw new ArgumentOutOfRangeException( nameof( time ) );
		time = Math.Clamp( time, 0, Duration );
		var first = Math.Min( (int)Math.Floor( time * 2 ), Keys.Length - 1 );
		var second = Math.Min( first + 1, Keys.Length - 1 );
		var a = 2 * Math.Atan2( Keys[first].Z, Keys[first].W );
		var b = 2 * Math.Atan2( Keys[second].Z, Keys[second].W );
		return a + (b - a) * (time * 2 - first);
	}

	public static Vector3 Position( int vertex, double time )
	{
		var p = BindPosition( vertex );
		var weight = p.Y * .5;
		var angle = Angle( time );
		var sine = Math.Sin( angle ); var cosine = Math.Cos( angle );
		var x = p.X * cosine - (p.Y - 1) * sine;
		var y = p.X * sine + (p.Y - 1) * cosine + 1;
		return new Vector3( (float)(p.X * (1 - weight) + x * weight), (float)(p.Y * (1 - weight) + y * weight), 0 );
	}

	/// <summary>Literal expected axis/unit map, deliberately independent of GltfGeometry.</summary>
	public static Vector3 ToEngine( Vector3 source ) => new( source.Z / .0254f, source.X / .0254f, source.Y / .0254f );

	public static Vector3 ReadFloat3( GltfUploadStream stream, int vertex )
	{
		var offset = checked(stream.ByteOffset + vertex * stream.ByteStride);
		var data = stream.Storage.Bytes.Span;
		return new Vector3( BinaryPrimitives.ReadSingleLittleEndian( data[offset..] ), BinaryPrimitives.ReadSingleLittleEndian( data[(offset + 4)..] ), BinaryPrimitives.ReadSingleLittleEndian( data[(offset + 8)..] ) );
	}

	public static Vector4 ReadFloat4( GltfUploadStream stream, int vertex )
	{
		var offset = checked(stream.ByteOffset + vertex * stream.ByteStride);
		var data = stream.Storage.Bytes.Span;
		return new Vector4( BinaryPrimitives.ReadSingleLittleEndian( data[offset..] ), BinaryPrimitives.ReadSingleLittleEndian( data[(offset + 4)..] ), BinaryPrimitives.ReadSingleLittleEndian( data[(offset + 8)..] ), BinaryPrimitives.ReadSingleLittleEndian( data[(offset + 12)..] ) );
	}

	public static ushort[] ReadU16x4( GltfUploadStream stream, int vertex )
	{
		var offset = checked(stream.ByteOffset + vertex * stream.ByteStride);
		var data = stream.Storage.Bytes.Span;
		return new[] { BinaryPrimitives.ReadUInt16LittleEndian( data[offset..] ), BinaryPrimitives.ReadUInt16LittleEndian( data[(offset + 2)..] ), BinaryPrimitives.ReadUInt16LittleEndian( data[(offset + 4)..] ), BinaryPrimitives.ReadUInt16LittleEndian( data[(offset + 6)..] ) };
	}

	private static float[] BuildSampleTimes()
	{
		var times = new SortedSet<float> { -.25f, 5.75f };
		for ( var i = 0; i <= 44; i++ ) times.Add( i * .125f );
		for ( var i = 1; i < 11; i++ ) { times.Add( i * .5f - .00001f ); times.Add( i * .5f + .00001f ); }
		return times.ToArray();
	}
}
