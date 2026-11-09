using System;
using System.Buffers.Binary;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureCoordinateTestValidation;

using NMatrix = System.Numerics.Matrix4x4;

/// <summary>Synthetic sensitivity tests only. Their byte arrays are not GPU evidence.</summary>
[TestClass]
public sealed class TextureCoordinateGpuVerifierTests
{
	public static string CheckoutRoot { get; set; }
	[TestMethod]
	public void RuntimeOffsetsAndAllSourceUvSetsAreObserved()
	{
		var form = TextureCoordinateReference.Open( CheckoutRoot ).Forms().Single( value => value.Derived && !value.Compiled );
		var measurement = TextureCoordinateGpuChecks.MeasureSnapshots( form, Fixture( form ), NMatrix.Identity );
		TextureCoordinateGpuChecks.RequireWithin( measurement, 0, 0, 0 );
		Assert.AreEqual( 5, measurement.Primitives ); Assert.AreEqual( 8, measurement.UvStreams );
	}
	[TestMethod]
	public void WrongSelectedUvCannotPass()
	{
		var form = TextureCoordinateReference.Open( CheckoutRoot ).Forms().Single( value => value.Derived && !value.Compiled ); var rows = Fixture( form );
		var uv = rows[0].Fields.Single( value => value.Semantic == "TEXCOORD" && value.SemanticIndex == 1 );
		BinaryPrimitives.WriteSingleLittleEndian( rows[0].Bytes.AsSpan( uv.Offset ), .123f );
		Assert.ThrowsException<InvalidOperationException>( () => TextureCoordinateGpuChecks.RequireWithin( TextureCoordinateGpuChecks.MeasureSnapshots( form, rows, NMatrix.Identity ), 0, 0, 0 ) );
	}
	[TestMethod]
	public void GlobalUvBakeCannotEraseOriginalSet()
	{
		var form = TextureCoordinateReference.Open( CheckoutRoot ).Forms().Single( value => value.Derived && !value.Compiled ); var rows = Fixture( form );
		var uv0 = rows[0].Fields.Single( value => value.Semantic == "TEXCOORD" && value.SemanticIndex == 0 );
		var uv1 = rows[0].Fields.Single( value => value.Semantic == "TEXCOORD" && value.SemanticIndex == 1 );
		rows[0].Bytes.AsSpan( uv1.Offset, 8 ).CopyTo( rows[0].Bytes.AsSpan( uv0.Offset, 8 ) );
		Assert.ThrowsException<InvalidOperationException>( () => TextureCoordinateGpuChecks.RequireWithin( TextureCoordinateGpuChecks.MeasureSnapshots( form, rows, NMatrix.Identity ), 0, 0, 0 ) );
	}
	[TestMethod]
	public void WrongWindingAndCpuIndexMetadataCannotPass()
	{
		var form = TextureCoordinateReference.Open( CheckoutRoot ).Forms()[0]; var rows = Fixture( form );
		(rows[0].Indices[1], rows[0].Indices[2]) = (rows[0].Indices[2], rows[0].Indices[1]);
		Assert.ThrowsException<InvalidOperationException>( () => TextureCoordinateGpuChecks.RequireWithin( TextureCoordinateGpuChecks.MeasureSnapshots( form, rows, NMatrix.Identity ), 0, 0, 0 ) );
		rows = Fixture( form ); var row = rows[0];
		rows[0] = new GltfGpuPrimitiveSnapshot { Node = row.Node, Mesh = row.Mesh, Primitive = row.Primitive, VertexCount = row.VertexCount, VertexStride = row.VertexStride, Bytes = row.Bytes, Fields = row.Fields, SourceVertices = row.SourceVertices, Indices = row.Indices, IndexDataOrigin = "cpu-submission" };
		Assert.ThrowsException<InvalidOperationException>( () => TextureCoordinateGpuChecks.RequireWithin( TextureCoordinateGpuChecks.MeasureSnapshots( form, rows, NMatrix.Identity ), 0, 0, 0 ) );
	}
	[TestMethod]
	public void NonfiniteAndMissingNodeCannotBecomeEmptyPasses()
	{
		var form = TextureCoordinateReference.Open( CheckoutRoot ).Forms()[0]; var rows = Fixture( form );
		BinaryPrimitives.WriteSingleLittleEndian( rows[0].Bytes.AsSpan( 8 ), float.NaN );
		Assert.ThrowsException<InvalidOperationException>( () => TextureCoordinateGpuChecks.RequireWithin( TextureCoordinateGpuChecks.MeasureSnapshots( form, rows, NMatrix.Identity ), 0, 0, 0 ) );
		Assert.ThrowsException<InvalidOperationException>( () => TextureCoordinateGpuChecks.MeasureSnapshots( form, rows[..4], NMatrix.Identity ) );
	}
	private static GltfGpuPrimitiveSnapshot[] Fixture( TextureCoordinateReference.Form form )
	{
		return form.Expected.Select( expected =>
		{
			const int stride = 88; var data = Enumerable.Repeat( (byte)0xd7, 4 * stride ).ToArray();
			var fields = new[] { new GltfGpuField( "POSITION", 0, 8, 3 ), new GltfGpuField( "NORMAL", 0, 32, 3 ), new GltfGpuField( "TEXCOORD", 0, 56, 2 ), new GltfGpuField( "TEXCOORD", 1, 76, 2 ) };
			for ( var vertex = 0; vertex < 4; vertex++ )
			{
				var p = TextureCoordinateReference.EnginePosition( expected.Streams["POSITION"].Values[vertex] ); var n = TextureCoordinateReference.EngineNormal( expected.Streams["NORMAL"].Values[vertex] );
				Write( vertex * stride + 8, p.X ); Write( vertex * stride + 12, p.Y ); Write( vertex * stride + 16, p.Z );
				Write( vertex * stride + 32, n.X ); Write( vertex * stride + 36, n.Y ); Write( vertex * stride + 40, n.Z );
				foreach ( var index in new[] { 0, 1 } ) if ( expected.Streams.TryGetValue( "TEXCOORD_" + index, out var stream ) )
				{
					var offset = vertex * stride + (index == 0 ? 56 : 76); Write( offset, (float)stream.Values[vertex][0] ); Write( offset + 4, (float)stream.Values[vertex][1] );
				}
			}
			return new GltfGpuPrimitiveSnapshot { Node = expected.Node, Mesh = expected.Mesh, Primitive = 0, VertexCount = 4, VertexStride = stride, Bytes = data, Fields = fields,
				SourceVertices = new uint[] { 0, 1, 2, 3 }, Indices = expected.Indices.Values.Select( value => (uint)value[0] ).ToArray(), IndexDataOrigin = "gpu-readback" };
			void Write( int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( offset ), value );
		} ).ToArray();
	}
}
