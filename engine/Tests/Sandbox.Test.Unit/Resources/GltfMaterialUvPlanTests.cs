using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

/// <summary>CPU-only material role preparation; no native resources or graphics context are required.</summary>
[TestClass]
public sealed class GltfMaterialUvPlanTests
{
	[TestMethod]
	public void FiveRolesRetainTheirActualNormalizedSourceStreamsAndOffsets()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var plan = GltfMaterialUvPlan.Create( document.ReadMaterial( 0 ), upload, 0 );
		Assert.AreEqual( 5, plan.Bindings.Count ); Assert.AreEqual( 0, plan.MaterialIndex );
		var expected = new[] { (GltfMaterialTextureRole.BaseColor, 1), (GltfMaterialTextureRole.MetallicRoughness, 0),
			(GltfMaterialTextureRole.Normal, 1), (GltfMaterialTextureRole.Occlusion, 0), (GltfMaterialTextureRole.Emissive, 1) };
		foreach ( var (role, set) in expected )
		{
			var binding = plan.Bindings[role]; var stream = upload.Attributes["TEXCOORD_" + set];
			Assert.AreEqual( role, binding.Role ); Assert.AreEqual( set, binding.TexCoord );
			Assert.AreSame( stream, binding.Source ); Assert.IsTrue( binding.Source.Normalized );
			Assert.AreEqual( set == 0 ? 5123 : 5121, binding.Source.ComponentType ); Assert.AreEqual( 2, binding.Source.Components );
			Assert.AreEqual( set == 0 ? 4 : 12, binding.Source.ByteOffset ); Assert.AreEqual( 16, binding.Source.ByteStride );
			Assert.AreEqual( 8, binding.Source.Binding.ViewByteOffset ); Assert.AreEqual( 48, binding.Source.Storage.Bytes.Length );
			Assert.AreEqual( role is GltfMaterialTextureRole.BaseColor or GltfMaterialTextureRole.Emissive, binding.IsSrgb );
		}
		Assert.AreSame( plan.Bindings[GltfMaterialTextureRole.BaseColor].Source.Storage, plan.Bindings[GltfMaterialTextureRole.MetallicRoughness].Source.Storage );
	}

	[TestMethod]
	public void AffineRowsApplyScaleOriginRotationThenOffsetWithoutBakingSource()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var source = upload.Attributes["TEXCOORD_1"].Storage.Bytes.ToArray();
		var plan = GltfMaterialUvPlan.Create( document.ReadMaterial( 0 ), upload );
		var transform = plan.Bindings[GltfMaterialTextureRole.BaseColor].Transform;
		Assert.AreEqual( new NVector2( .25f, -.5f ), transform.Offset ); Assert.AreEqual( new NVector2( 2, 3 ), transform.Tiling );
		Assert.AreEqual( MathF.PI / 2, transform.Rotation );
		Vector( new NVector2( 1.75f, -1f ), transform.Apply( new NVector2( .25f, .5f ) ), 1e-6f );
		Vector( new NVector2( .25f, -.5f ), transform.Apply( NVector2.Zero ), 0 );
		Assert.IsTrue( MathF.Abs( transform.RowU.X ) < 1e-6f && MathF.Abs( transform.RowU.Y - 3 ) < 1e-6f && transform.RowU.Z == .25f );
		Assert.IsTrue( MathF.Abs( transform.RowV.X + 2 ) < 1e-6f && MathF.Abs( transform.RowV.Y ) < 1e-6f && transform.RowV.Z == -.5f );
		CollectionAssert.AreEqual( source, upload.Attributes["TEXCOORD_1"].Storage.Bytes.ToArray() );
	}

	[TestMethod]
	public void NegativeScalesAndLargeOffsetsRemainSourceProperties()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var plan = GltfMaterialUvPlan.Create( new GltfMaterial { BaseColorTexture = Info( 1, new NVector2( -2, 3 ), new NVector2( 5, -7 ) ) }, upload );
		var transform = plan.Bindings[GltfMaterialTextureRole.BaseColor].Transform;
		Assert.AreEqual( new NVector3( -2, 0, 5 ), transform.RowU ); Assert.AreEqual( new NVector3( 0, 3, -7 ), transform.RowV );
		Vector( new NVector2( 4.5f, -4.75f ), transform.Apply( new NVector2( .25f, .75f ) ), 0 );
	}

	[TestMethod]
	public void MaterialWithoutTextureBindingsDoesNotRequireOrInventUvs()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
			""" ) );
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		Assert.AreEqual( 0, GltfMaterialUvPlan.Create( null, upload ).Bindings.Count );
		Assert.AreEqual( 0, GltfMaterialUvPlan.Create( new GltfMaterial(), upload ).Bindings.Count );
	}

	[TestMethod]
	public void UnlitRoleSelectionDoesNotValidateIgnoredTextureCoordinatesOrResolveImages()
	{
		var resolves = 0;
		using var document = Read( resolve: _ => { resolves++; throw new InvalidOperationException( "No image resolution is allowed." ); } );
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var material = new GltfMaterial { Unlit = true, BaseColorTexture = Info( 0 ), NormalTexture = Info( 9 ), MetallicRoughnessTexture = Info( 8 ), EmissiveTexture = Info( 7 ) };
		var plan = GltfMaterialUvPlan.CreateForRoles( material, upload, new[] { GltfMaterialTextureRole.BaseColor }, 3 );
		Assert.AreEqual( 1, plan.Bindings.Count ); Assert.IsTrue( plan.Bindings.ContainsKey( GltfMaterialTextureRole.BaseColor ) ); Assert.AreEqual( 0, resolves );
		var failure = Assert.ThrowsException<GltfException>( () => GltfMaterialUvPlan.Create( material, upload, 3 ) );
		StringAssert.Contains( failure.Message, "MetallicRoughness" ); StringAssert.Contains( failure.Message, "TEXCOORD_8" );
	}

	[TestMethod]
	public void MissingConsumedSetNamesTheMaterialRoleAndChannel()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var before = upload.Attributes["TEXCOORD_0"].Storage.Bytes.ToArray();
		var error = Assert.ThrowsException<GltfException>( () => GltfMaterialUvPlan.Create( new GltfMaterial { BaseColorTexture = Info( 2 ) }, upload, 7 ) );
		StringAssert.Contains( error.Message, "7" ); StringAssert.Contains( error.Message, "BaseColor" ); StringAssert.Contains( error.Message, "TEXCOORD_2" );
		CollectionAssert.AreEqual( before, upload.Attributes["TEXCOORD_0"].Storage.Bytes.ToArray() );
	}

	[TestMethod]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	public void NonfiniteUvPropertiesAreRejectedBeforePublishingAPlan( float value )
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		foreach ( var texture in new[] { Info( 0, offset: new NVector2( value, 0 ) ), Info( 0, tiling: new NVector2( 1, value ) ), Info( 0, rotation: value ) } )
			Assert.ThrowsException<GltfException>( () => GltfMaterialUvPlan.Create( new GltfMaterial { BaseColorTexture = texture }, upload ) );
	}

	[TestMethod]
	public void AbsentConsumedRoleStaysAbsentAndDoesNotValidateOtherRoles()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var plan = GltfMaterialUvPlan.CreateForRoles( new GltfMaterial { BaseColorTexture = Info( 99 ) }, upload, new[] { GltfMaterialTextureRole.Normal } );
		Assert.AreEqual( 0, plan.Bindings.Count );
	}

	[TestMethod]
	public void InvalidRoleSelectionAndMaterialIdentityFailWithoutMutatingSource()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 ); var material = document.ReadMaterial( 0 );
		Assert.ThrowsException<ArgumentException>( () => GltfMaterialUvPlan.CreateForRoles( material, upload, new[] { GltfMaterialTextureRole.BaseColor, GltfMaterialTextureRole.BaseColor } ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfMaterialUvPlan.CreateForRoles( material, upload, new[] { (GltfMaterialTextureRole)99 } ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfMaterialUvPlan.Create( material, upload, -1 ) );
		Assert.ThrowsException<ArgumentNullException>( () => GltfMaterialUvPlan.CreateForRoles( material, upload, null ) );
		Assert.AreEqual( 12, upload.Attributes["TEXCOORD_1"].ByteOffset );
	}

	[TestMethod]
	public void VariantPreparationReturnsNewBindingsAndSharesOnlyImmutableSource()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var original = GltfMaterialUvPlan.CreateForVariant( document, 0, 0, upload, null );
		var selected = GltfMaterialUvPlan.CreateForVariant( document, 0, 0, upload, 0 );
		Assert.AreEqual( 0, original.MaterialIndex ); Assert.AreEqual( 1, selected.MaterialIndex );
		Assert.AreEqual( 1, original.Bindings[GltfMaterialTextureRole.BaseColor].TexCoord ); Assert.AreEqual( 0, selected.Bindings[GltfMaterialTextureRole.BaseColor].TexCoord );
		Assert.AreSame( original.Bindings[GltfMaterialTextureRole.BaseColor].Source.Storage, selected.Bindings[GltfMaterialTextureRole.BaseColor].Source.Storage );
		Assert.AreEqual( 5, original.Bindings.Count ); Assert.AreEqual( 1, selected.Bindings.Count );
		Assert.AreEqual( new NVector2( .25f, -.5f ), original.Bindings[GltfMaterialTextureRole.BaseColor].Transform.Offset );
		Assert.AreEqual( NVector2.Zero, selected.Bindings[GltfMaterialTextureRole.BaseColor].Transform.Offset );
	}

	[TestMethod]
	public void FailedVariantPreparationCannotReplaceThePreviousUsableBindings()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var current = GltfMaterialUvPlan.CreateForVariant( document, 0, 0, upload, 0 ); var previous = current;
		var bytes = upload.Attributes["TEXCOORD_0"].Storage.Bytes.ToArray();
		Assert.ThrowsException<GltfException>( () => current = GltfMaterialUvPlan.CreateForVariant( document, 0, 0, upload, 1 ) );
		Assert.AreSame( previous, current ); Assert.AreEqual( 1, current.MaterialIndex );
		CollectionAssert.AreEqual( bytes, upload.Attributes["TEXCOORD_0"].Storage.Bytes.ToArray() );
	}

	[TestMethod]
	public void UnmappedAndUnsupportedOptionalVariantsKeepTheirCoreFallback()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		Assert.AreEqual( 0, GltfMaterialUvPlan.CreateForVariant( document, 0, 0, upload, 2 ).MaterialIndex );
		using var unsupported = GltfReader.Read( Encoding.UTF8.GetBytes( Fixture() ), supportedExtensions: new HashSet<string>( StringComparer.Ordinal ) { "KHR_texture_transform" } );
		Assert.AreEqual( 0, GltfMaterialUvPlan.CreateForVariant( unsupported, 0, 0, unsupported.MaterializePrimitiveUpload( 0, 0 ), 0 ).MaterialIndex );
		CollectionAssert.Contains( unsupported.UnsupportedOptionalExtensions.ToArray(), "KHR_materials_variants" );
	}

	[TestMethod]
	public void BindingsAndOriginalRawStorageRemainUsableAfterDocumentDisposal()
	{
		GltfMaterialUvPlan plan;
		using ( var document = Read() ) plan = GltfMaterialUvPlan.Create( document.ReadMaterial( 0 ), document.MaterializePrimitiveUpload( 0, 0 ) );
		var binding = plan.Bindings[GltfMaterialTextureRole.BaseColor];
		Assert.AreEqual( (byte)255, binding.Source.Storage.Bytes.Span[binding.Source.ByteOffset] ); Assert.AreEqual( 16, binding.Source.ByteStride );
		Assert.IsTrue( binding.IsSrgb ); Vector( new NVector2( .25f, -.5f ), binding.Transform.Apply( NVector2.Zero ), 0 );
	}

	[TestMethod]
	public void CallerRoleListAndReturnedDictionaryCannotChangeThePreparedSelection()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var roles = new List<GltfMaterialTextureRole> { GltfMaterialTextureRole.BaseColor };
		var plan = GltfMaterialUvPlan.CreateForRoles( document.ReadMaterial( 0 ), upload, roles );
		roles[0] = GltfMaterialTextureRole.Normal;
		Assert.AreEqual( 1, plan.Bindings.Count ); Assert.IsTrue( plan.Bindings.ContainsKey( GltfMaterialTextureRole.BaseColor ) );
		var dictionary = (IDictionary<GltfMaterialTextureRole, GltfMaterialUvBinding>)plan.Bindings;
		Assert.ThrowsException<NotSupportedException>( () => dictionary.Clear() );
	}

	[TestMethod]
	public void QuantizedSignedUvDescriptorsAreNotConvertedToAssumedFloatStreams()
	{
		var json = JsonNode.Parse( Fixture() );
		json["extensionsUsed"].AsArray().Add( "KHR_mesh_quantization" ); json["extensionsRequired"] = new JsonArray( "KHR_mesh_quantization" );
		json["accessors"][1]["componentType"] = 5122; json["accessors"][1]["normalized"] = false;
		json["accessors"][2]["componentType"] = 5120;
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json.ToJsonString() ), supportedExtensions: new HashSet<string>( StringComparer.Ordinal ) { "KHR_mesh_quantization", "KHR_texture_transform", "KHR_materials_variants" } );
		var upload = document.MaterializePrimitiveUpload( 0, 0 ); var plan = GltfMaterialUvPlan.Create( document.ReadMaterial( 0 ), upload );
		var first = plan.Bindings[GltfMaterialTextureRole.BaseColor].Source; var second = plan.Bindings[GltfMaterialTextureRole.MetallicRoughness].Source;
		Assert.AreEqual( 5120, first.ComponentType ); Assert.IsTrue( first.Normalized ); Assert.AreEqual( 12, first.ByteOffset );
		Assert.AreEqual( 5122, second.ComponentType ); Assert.IsFalse( second.Normalized ); Assert.AreEqual( 4, second.ByteOffset );
		Assert.AreSame( upload.Attributes["TEXCOORD_1"], first ); Assert.IsFalse( first.IsMaterialized );
	}

	[TestMethod]
	public void SparseImplicitUvBaseRetainsItsMaterializedSourceDescriptor()
	{
		var data = new byte[8]; data[0] = 1; BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 4 ), 65535 ); BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), 32768 );
		var json = $$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{Convert.ToBase64String( data )}}}}}}","byteLength":8}],
			"bufferViews":[{"buffer":0,"byteLength":1},{"buffer":0,"byteOffset":4,"byteLength":4}],
			"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			{"componentType":5123,"normalized":true,"count":3,"type":"VEC2","sparse":{"count":1,"indices":{"bufferView":0,"componentType":5121},"values":{"bufferView":1}}}],
			"textures":[{}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}}}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1}}]}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) ); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var source = GltfMaterialUvPlan.Create( document.ReadMaterial( 0 ), upload ).Bindings[GltfMaterialTextureRole.BaseColor].Source;
		Assert.AreSame( upload.Attributes["TEXCOORD_0"], source ); Assert.IsTrue( source.IsMaterialized );
		Assert.AreEqual( 5123, source.ComponentType ); Assert.IsTrue( source.Normalized ); Assert.AreEqual( 4, source.ByteStride ); Assert.AreEqual( 0, source.ByteOffset );
		Assert.AreEqual( 12, source.Storage.Bytes.Length ); Assert.AreEqual( (ushort)65535, BinaryPrimitives.ReadUInt16LittleEndian( source.Storage.Bytes.Span[4..] ) );
	}

	private static GltfTextureInfo Info( int set, NVector2? tiling = null, NVector2? offset = null, float rotation = 0 ) =>
		new() { Index = 0, TexCoord = set, IsSrgb = true, Tiling = tiling ?? NVector2.One, Offset = offset ?? NVector2.Zero, Rotation = rotation };
	private static GltfDocument Read( Func<string, byte[]> resolve = null ) => GltfReader.Read( Encoding.UTF8.GetBytes( Fixture() ), resolve,
		supportedExtensions: new HashSet<string>( StringComparer.Ordinal ) { "KHR_texture_transform", "KHR_materials_variants" } );
	private static void Vector( NVector2 expected, NVector2 actual, float tolerance ) => Assert.IsTrue( NVector2.Distance( expected, actual ) <= tolerance,
		$"Expected {expected}, observed {actual}, tolerance {tolerance:R}." );

	private static string Fixture()
	{
		var data = Enumerable.Repeat( (byte)0xA7, 64 ).ToArray();
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 12 + vertex * 16 ), (ushort)(vertex == 0 ? 0 : vertex == 1 ? 32768 : 65535) );
			BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 14 + vertex * 16 ), (ushort)(vertex == 0 ? 65535 : vertex == 1 ? 32768 : 0) );
			data[20 + vertex * 16] = (byte)(255 - vertex * 127); data[21 + vertex * 16] = (byte)(vertex * 127);
		}
		return $$$$$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_transform","KHR_materials_variants"],
			"extensions":{"KHR_materials_variants":{"variants":[{"name":"UV0"},{"name":"MissingUV"},{"name":"Unmapped"}]}},
			"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{Convert.ToBase64String( data )}}}}}}","byteLength":64}],
			"bufferViews":[{"buffer":0,"byteOffset":8,"byteLength":48,"byteStride":16}],
			"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			{"bufferView":0,"byteOffset":4,"componentType":5123,"normalized":true,"count":3,"type":"VEC2"},
			{"bufferView":0,"byteOffset":12,"componentType":5121,"normalized":true,"count":3,"type":"VEC2"}],
			"images":[{"uri":"never-load.png"}],"textures":[{"source":0},{"source":0},{"source":0},{"source":0},{"source":0}],
			"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"extensions":{"KHR_texture_transform":{"texCoord":1,"offset":[0.25,-0.5],"scale":[2,3],"rotation":1.5707963267948966}}},"metallicRoughnessTexture":{"index":1}},
			"normalTexture":{"index":2,"texCoord":1,"scale":0.25},"occlusionTexture":{"index":3,"strength":0.4},"emissiveTexture":{"index":4,"texCoord":1}},
			{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":0}}},
			{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":2}}}],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1,"TEXCOORD_1":2},"material":0,
			"extensions":{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]},{"material":2,"variants":[1]}]}}}]}]}
			""";
	}
}
