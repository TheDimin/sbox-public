using System;
using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NVector2 = System.Numerics.Vector2;

/// <summary>Actual model consumers of the production CPU UV plan, not copied producer code or shader proof.</summary>
[TestClass]
public sealed class TextureTransformUvPlanModelTests
{
	public static string CheckoutRoot { get; set; }

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void EveryModelMaterialUsesItsExactPrimitiveSourceBinding( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		for ( var mesh = 0; mesh < 9; mesh++ )
		{
			var upload = input.Document.MaterializePrimitiveUpload( mesh, 0 );
			var plan = GltfMaterialUvPlan.CreateForVariant( input.Document, mesh, 0, upload, null );
			Assert.AreEqual( mesh, plan.MaterialIndex ); Assert.AreEqual( 1, plan.Bindings.Count );
			var binding = plan.Bindings[GltfMaterialTextureRole.BaseColor];
			Assert.AreSame( upload.Attributes["TEXCOORD_0"], binding.Source ); Assert.AreEqual( 0, binding.TexCoord );
			Assert.AreEqual( 5126, binding.Source.ComponentType ); Assert.AreEqual( 2, binding.Source.Components );
			Assert.AreEqual( 8, binding.Source.ByteStride ); Assert.AreEqual( 0, binding.Source.ByteOffset );
			Assert.AreEqual( mesh < 3 ? 0 : mesh < 6 ? 1 : mesh - 4, binding.TextureIndex ); Assert.IsTrue( binding.IsSrgb );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void PreparedModelAffineRowsMatchIndependentSampleAndPreserveUvBytes( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		for ( var mesh = 0; mesh < 9; mesh++ )
		{
			var upload = input.Document.MaterializePrimitiveUpload( mesh, 0 );
			var stream = upload.Attributes["TEXCOORD_0"]; var original = stream.Storage.Bytes.ToArray();
			var binding = GltfMaterialUvPlan.Create( input.Document.ReadMaterial( mesh ), upload, mesh ).Bindings[GltfMaterialTextureRole.BaseColor];
			for ( var vertex = 0; vertex < 4; vertex++ )
			{
				var source = Read( stream, vertex ); var expected = TextureTransformReference.SampleUv( mesh, source.X, source.Y );
				Vector( expected, binding.Transform.Apply( source ), $"mesh {mesh}, source vertex {vertex}" );
			}
			foreach ( var point in TextureTransformReference.ProbeUvs )
				Vector( TextureTransformReference.SampleUv( mesh, point.X, point.Y ), binding.Transform.Apply( point ), $"mesh {mesh}, off-grid probe {point}" );
			CollectionAssert.AreEqual( original, stream.Storage.Bytes.ToArray(), "Preparing transform rows must not bake or alter the shared source UV bytes." );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MultiRoleDerivativePreparesIndependentChannelsAndUnlitSubset( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, false );
		var bytes = input.PerRoleFixture(); if ( compiled ) bytes = GltfCompiler.Compile( bytes, input.Resolve, TextureTransformReference.Selected );
		using var document = GltfReader.Read( bytes, input.Resolve, TextureTransformReference.Selected );
		var upload = document.MaterializePrimitiveUpload( 0, 0 ); var material = document.ReadMaterial( 0 );
		var plan = GltfMaterialUvPlan.Create( material, upload, 0 );
		var roles = new[] { GltfMaterialTextureRole.BaseColor, GltfMaterialTextureRole.MetallicRoughness, GltfMaterialTextureRole.Normal, GltfMaterialTextureRole.Occlusion, GltfMaterialTextureRole.Emissive };
		var expectedSets = new[] { 1, 0, 1, 1, 0 };
		for ( var index = 0; index < roles.Length; index++ )
		{
			var binding = plan.Bindings[roles[index]]; var source = upload.Attributes["TEXCOORD_" + expectedSets[index]];
			Assert.AreEqual( expectedSets[index], binding.TexCoord ); Assert.AreSame( source, binding.Source );
			for ( var vertex = 0; vertex < 4; vertex++ )
			{
				var uv = Read( source, vertex );
				Vector( TextureTransformReference.RoleUv( index, uv.X, uv.Y ), binding.Transform.Apply( uv ), $"role {roles[index]}, vertex {vertex}" );
			}
		}
		var unlit = GltfMaterialUvPlan.CreateForRoles( material, upload, new[] { GltfMaterialTextureRole.BaseColor }, 0 );
		Assert.AreEqual( 1, unlit.Bindings.Count ); Assert.AreSame( plan.Bindings[GltfMaterialTextureRole.BaseColor].Source, unlit.Bindings[GltfMaterialTextureRole.BaseColor].Source );
	}

	private static NVector2 Read( GltfUploadStream source, int vertex )
	{
		Assert.AreEqual( 5126, source.ComponentType ); Assert.AreEqual( 2, source.Components );
		var bytes = source.Storage.Bytes.Span[checked(source.ByteOffset + vertex * source.ByteStride)..];
		return new NVector2( BinaryPrimitives.ReadSingleLittleEndian( bytes ), BinaryPrimitives.ReadSingleLittleEndian( bytes[4..] ) );
	}
	private static void Vector( double[] expected, NVector2 actual, string context ) =>
		Assert.IsTrue( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && Math.Abs( expected[0] - actual.X ) <= 3e-7 && Math.Abs( expected[1] - actual.Y ) <= 3e-7,
			$"{context}: expected ({expected[0]:R},{expected[1]:R}), actual {actual}." );
}
