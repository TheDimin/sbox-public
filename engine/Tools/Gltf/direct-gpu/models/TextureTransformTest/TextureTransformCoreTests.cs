using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

/// <summary>Tests written before a direct GPU texture-transform implementation. No renderer is allocated.</summary>
[TestClass]
public sealed class TextureTransformCoreTests
{
	public static string CheckoutRoot { get; set; }

	[TestMethod]
	public void PinnedSourceAndAllDependenciesHaveTheAuditedIdentity()
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, false );
		Assert.AreEqual( 7, input.Hashes.Count );
		Assert.AreEqual( 12, input.Document.Root.GetProperty( "nodes" ).GetArrayLength() );
		Assert.AreEqual( 9, input.Document.Root.GetProperty( "meshes" ).GetArrayLength() );
		Assert.AreEqual( 9, input.Document.Root.GetProperty( "materials" ).GetArrayLength() );
		Assert.AreEqual( 5, input.Document.Root.GetProperty( "images" ).GetArrayLength() );
		Assert.IsFalse( input.Document.Root.TryGetProperty( "animations", out _ ) );
		Assert.IsFalse( input.Document.Root.TryGetProperty( "skins", out _ ) );
		Assert.AreEqual( 0, input.Document.GetDefaultScene() );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AllMaterialsMatchIndependentAffineUvArithmetic( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		for ( var material = 0; material < 9; material++ )
		{
			var texture = input.Document.ReadMaterial( material ).BaseColorTexture;
			Assert.AreEqual( material < 3 ? 0 : material < 6 ? 1 : material - 4, texture.Index );
			Assert.AreEqual( 0, texture.TexCoord );
			foreach ( var point in TextureTransformReference.ProbeUvs )
				Vector( TextureTransformReference.SampleUv( material, point.X, point.Y ), texture.TransformUv( point ), 3e-7, $"material {material}, uv {point}" );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TextureRolesRetainClampLinearNoMipAndSrgbSemantics( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		var sampler = input.Document.ReadSampler( 0 );
		Assert.AreEqual( 33071, sampler.WrapS ); Assert.AreEqual( 33071, sampler.WrapT );
		Assert.AreEqual( 9729, sampler.MagFilter ); Assert.AreEqual( 9729, sampler.MinFilter );
		for ( var material = 0; material < 9; material++ )
		{
			var info = input.Document.ReadMaterial( material ).BaseColorTexture;
			Assert.IsTrue( info.IsSrgb );
			var texture = input.Document.ReadTexture( info.Index );
			Assert.AreEqual( info.Index, texture.Image );
			Assert.AreEqual( material < 6 ? (int?)0 : null, texture.Sampler );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RealBufferViewsAccessorsAndUnsignedIntTopologyAreRespected( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		for ( var mesh = 0; mesh < 9; mesh++ )
		{
			var upload = input.Document.MaterializePrimitiveUpload( mesh, 0 );
			Assert.AreEqual( 4, upload.Layout.VertexCount ); Assert.AreEqual( 4, upload.Layout.SourceMode );
			CollectionAssert.AreEquivalent( new[] { "POSITION", "TEXCOORD_0" }, upload.Attributes.Keys.ToArray() );
			var position = upload.Attributes["POSITION"]; var uv = upload.Attributes["TEXCOORD_0"];
			Assert.AreEqual( 5126, position.ComponentType ); Assert.AreEqual( 3, position.Components );
			Assert.AreEqual( 12, position.ByteStride ); Assert.AreEqual( 0, position.ByteOffset ); Assert.AreEqual( 48, position.Storage.Bytes.Length );
			Assert.AreEqual( 5126, uv.ComponentType ); Assert.AreEqual( 2, uv.Components );
			Assert.AreEqual( 8, uv.ByteStride ); Assert.AreEqual( 0, uv.ByteOffset ); Assert.AreEqual( 32, uv.Storage.Bytes.Length );
			for ( var vertex = 0; vertex < 4; vertex++ )
			{
				var p = position.Storage.Bytes.Span.Slice( vertex * position.ByteStride + position.ByteOffset );
				var observed = new NVector3( BinaryPrimitives.ReadSingleLittleEndian( p ), BinaryPrimitives.ReadSingleLittleEndian( p[4..] ), BinaryPrimitives.ReadSingleLittleEndian( p[8..] ) );
				Assert.AreEqual( TextureTransformReference.Positions[vertex], observed );
				var t = uv.Storage.Bytes.Span.Slice( vertex * uv.ByteStride + uv.ByteOffset );
				Assert.AreEqual( TextureTransformReference.SourceUv( mesh, vertex ), new NVector2( BinaryPrimitives.ReadSingleLittleEndian( t ), BinaryPrimitives.ReadSingleLittleEndian( t[4..] ) ) );
			}
			Assert.AreEqual( 5125, upload.Indices.ComponentType ); Assert.AreEqual( 4, upload.Indices.ByteStride ); Assert.AreEqual( 6, upload.Indices.Count );
			Assert.AreEqual( 0, upload.Indices.ByteOffset ); Assert.AreEqual( 24, upload.Indices.Storage.Bytes.Length );
			for ( var index = 0; index < 6; index++ )
				Assert.AreEqual( TextureTransformReference.Indices[index], BinaryPrimitives.ReadUInt32LittleEndian( upload.Indices.Storage.Bytes.Span[(index * 4)..] ) );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MarkerHierarchyAndQuadWorldPositionsMatchIndependentArithmetic( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		var pose = input.Document.EvaluatePose();
		for ( var node = 0; node < 12; node++ )
			for ( var vertex = 0; vertex < 4; vertex++ )
			{
				var expected = TextureTransformReference.WorldPosition( node, vertex );
				var actual = input.Document.EvaluateVertexPosition( node, 0, vertex, pose );
				Assert.IsTrue( NVector3.Distance( expected, actual ) <= 2e-7f, $"node {node}, vertex {vertex}: expected {expected}, observed {actual}" );
			}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void UnsupportedOptionalExtensionUsesOnlyTheDefinedCoreFallback( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled, supports: false );
		CollectionAssert.Contains( input.Document.UnsupportedOptionalExtensions.ToArray(), TextureTransformReference.Extension );
		for ( var material = 0; material < 9; material++ )
		{
			var texture = input.Document.ReadMaterial( material ).BaseColorTexture;
			foreach ( var uv in TextureTransformReference.ProbeUvs ) Assert.AreEqual( uv, texture.TransformUv( uv ) );
		}
	}

	[TestMethod]
	public void RequiredUnsupportedExtensionIsRejectedWithItsName()
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, false );
		var bytes = input.RequiredFixture();
		var error = Assert.ThrowsException<GltfException>( () => { using var document = GltfReader.Read( bytes, input.Resolve ); } );
		StringAssert.Contains( error.Message, TextureTransformReference.Extension );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DifferentTextureRolesAndTexCoordOverridesStayIndependent( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, false );
		var bytes = input.PerRoleFixture();
		if ( compiled ) bytes = GltfCompiler.Compile( bytes, input.Resolve, TextureTransformReference.Selected );
		using var document = GltfReader.Read( bytes, input.Resolve, TextureTransformReference.Selected );
		var material = document.ReadMaterial( 0 );
		var roles = new[] { material.BaseColorTexture, material.MetallicRoughnessTexture, material.NormalTexture, material.OcclusionTexture, material.EmissiveTexture };
		var expectedSets = new[] { 1, 0, 1, 1, 0 };
		var point = new NVector2( .25f, .75f );
		for ( var role = 0; role < roles.Length; role++ )
		{
			Assert.AreEqual( expectedSets[role], roles[role].TexCoord );
			Vector( TextureTransformReference.RoleUv( role, point.X, point.Y ), roles[role].TransformUv( point ), 3e-7, $"role {role}" );
			Assert.AreEqual( role is 0 or 4, roles[role].IsSrgb );
		}
		Assert.AreEqual( .25f, material.NormalTexture.Scale ); Assert.AreEqual( .4f, material.OcclusionTexture.Strength );
		var primitive = document.MaterializePrimitiveUpload( 0, 0 );
		Assert.IsTrue( primitive.Attributes.ContainsKey( "TEXCOORD_0" ) && primitive.Attributes.ContainsKey( "TEXCOORD_1" ) );
	}

	[TestMethod]
	public void NegativeScaleAndUnboundedOffsetsAreValidRatherThanClamped()
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, false );
		using var document = GltfReader.Read( input.NegativeScaleFixture(), input.Resolve, TextureTransformReference.Selected );
		var texture = document.ReadMaterial( 0 ).BaseColorTexture;
		Assert.AreEqual( new NVector2( -2, 3 ), texture.Tiling ); Assert.AreEqual( new NVector2( 5, -7 ), texture.Offset );
		Vector( new[] { 4.5, -4.75 }, texture.TransformUv( new NVector2( .25f, .75f ) ), 0, "negative scale" );
	}

	[TestMethod]
	public void IndependentReferenceRejectsWrongOrderRotationSignAndVerticalFlip()
	{
		var good = TextureTransformReference.SampleUv( 5, .25, .75 );
		var wrongOrder = TextureTransformReference.Transform( .25 - .2, .75 - .1, 0, 0, 1.5, 1.5, .3 );
		var wrongSign = TextureTransformReference.Transform( .25, .75, -.2, -.1, 1.5, 1.5, -.3 );
		var flipped = TextureTransformReference.SampleUv( 5, .25, .25 );
		foreach ( var wrong in new[] { wrongOrder, wrongSign, flipped } )
			Assert.IsTrue( Math.Abs( wrong[0] - good[0] ) + Math.Abs( wrong[1] - good[1] ) > .1, "Reference must detect this incorrect transform." );
	}

	[TestMethod]
	public void CompiledGlbRetainsPortableImageDependenciesAndExactBytes()
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, true );
		Assert.AreEqual( 0x46546c67u, BinaryPrimitives.ReadUInt32LittleEndian( input.Bytes ) );
		Assert.AreEqual( 2u, BinaryPrimitives.ReadUInt32LittleEndian( input.Bytes.AsSpan( 4 ) ) );
		Assert.AreEqual( (uint)input.Bytes.Length, BinaryPrimitives.ReadUInt32LittleEndian( input.Bytes.AsSpan( 8 ) ) );
		CollectionAssert.Contains( input.Document.Root.GetProperty( "extensionsUsed" ).EnumerateArray().Select( value => value.GetString() ).ToArray(), TextureTransformReference.Extension );
		for ( var image = 0; image < 5; image++ )
		{
			Assert.AreEqual( TextureTransformReference.ImageNames[image], input.Document.Root.GetProperty( "images" )[image].GetProperty( "uri" ).GetString(), "Compiler must preserve the independently managed relative texture asset." );
			CollectionAssert.AreEqual( input.Dependencies[TextureTransformReference.ImageNames[image]], input.Document.ReadImage( image ).Bytes );
		}
		foreach ( var buffer in input.Document.Root.GetProperty( "buffers" ).EnumerateArray() ) Assert.IsFalse( buffer.TryGetProperty( "uri", out _ ) );
		using var missingTexture = GltfReader.Read( input.Bytes, _ => null, TextureTransformReference.Selected );
		var error = Assert.ThrowsException<GltfException>( () => missingTexture.ReadImage( 0 ) );
		StringAssert.Contains( error.Message, "UV.png" );
	}

	private static void Vector( double[] expected, NVector2 actual, double tolerance, string context )
	{
		Assert.IsTrue( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && Math.Abs( expected[0] - actual.X ) <= tolerance && Math.Abs( expected[1] - actual.Y ) <= tolerance,
			$"{context}: expected ({expected[0]:R},{expected[1]:R}), actual {actual}, tolerance {tolerance:R}" );
	}
}
