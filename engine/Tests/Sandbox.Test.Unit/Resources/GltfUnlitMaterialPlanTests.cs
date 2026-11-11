using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Nodes;
using Sandbox.Gltf;

namespace Sandbox.Tests;

using NVector4 = System.Numerics.Vector4;
using Plan = GltfUnlitMaterialPlan;

/// <summary>Pure managed planning tests. No material, image decoding, GPU or native model is created.</summary>
[TestClass]
public class GltfUnlitMaterialPlanTests
{
	[TestMethod]
	public void ExactLinearFactorAndOpaqueOutputDoNotAcquireSrgbOrPremultipliedSemantics()
	{
		using var fixture = new Fixture( """
			{"pbrMetallicRoughness":{"baseColorFactor":[1,0.217637640824031,0,0.2]},"extensions":{"KHR_materials_unlit":{}}}
			""" );
		var plan = Plan.Create( fixture.Document.ReadMaterial( 0 ), 7 );
		Assert.AreEqual( new NVector4( 1, .217637640824031f, 0, .2f ), plan.LinearBaseColorFactor );
		Assert.AreEqual( new NVector4( 1, .217637640824031f, 0, 1 ), plan.LinearFactorForShader );
		Assert.AreEqual( 7, plan.MaterialIndex ); Assert.IsTrue( plan.CullBackFaces );
		Assert.AreEqual( Plan.CoverageMode.Opaque, plan.AlphaMode ); Assert.IsNull( plan.EffectiveAlphaCutoff );
		var bound = plan.Bind( fixture.Document, fixture.Upload );
		Assert.AreEqual( Plan.Features.LinearFactor, bound.RequiredFeatures ); Assert.AreEqual( 0, bound.Uv.Bindings.Count ); Assert.AreEqual( 0, bound.Textures.Bindings.Count );
		Assert.AreEqual( 0, fixture.ResourceReads );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( .2f )]
	[DataRow( 1f )]
	public void OpaqueIgnoresProductAlpha( float productAlpha )
	{
		var plan = Plan.Create( Material( "OPAQUE", .5f ) ); var coverage = plan.EvaluateCoverage( productAlpha );
		Assert.IsFalse( coverage.Discard ); Assert.AreEqual( 1f, coverage.OutputAlpha );
	}

	[TestMethod]
	[DataRow( .49999997f, .5f, true, 0f )]
	[DataRow( .5f, .5f, false, 1f )]
	[DataRow( .50000006f, .5f, false, 1f )]
	[DataRow( 0f, 0f, false, 1f )]
	[DataRow( 1f, 2f, true, 0f )]
	public void MaskUsesGreaterThanOrEqualCutoffAndPreservesLegalCutoffsAboveOne( float productAlpha, float cutoff, bool discarded, float outputAlpha )
	{
		var plan = Plan.Create( Material( "MASK", cutoff ) ); var coverage = plan.EvaluateCoverage( productAlpha );
		Assert.AreEqual( cutoff, plan.EffectiveAlphaCutoff ); Assert.AreEqual( discarded, coverage.Discard ); Assert.AreEqual( outputAlpha, coverage.OutputAlpha );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( .25f )]
	[DataRow( 1f )]
	public void BlendPreservesStraightAlphaAndIgnoresCutoff( float productAlpha )
	{
		var plan = Plan.Create( Material( "BLEND", 2f ) ); var coverage = plan.EvaluateCoverage( productAlpha );
		Assert.IsFalse( coverage.Discard ); Assert.AreEqual( productAlpha, coverage.OutputAlpha ); Assert.IsNull( plan.EffectiveAlphaCutoff );
		Assert.AreEqual( new NVector4( .8f, .4f, .2f, .3f ), plan.LinearFactorForShader, "RGB must not be premultiplied by source alpha." );
	}

	[TestMethod]
	public void UnlitIgnoresLitRolesWithoutRequiringTheirUvsOrReadingTheirImages()
	{
		using var fixture = new Fixture( """
			{"pbrMetallicRoughness":{"baseColorFactor":[0,0.217637640824031,1,1],"metallicFactor":1,"roughnessFactor":0.1,"metallicRoughnessTexture":{"index":0,"texCoord":31}},
			 "normalTexture":{"index":0,"texCoord":29,"scale":2},"occlusionTexture":{"index":0,"texCoord":27,"strength":0.2},
			 "emissiveTexture":{"index":0,"texCoord":25},"emissiveFactor":[1,0.5,0.25],"extensions":{"KHR_materials_unlit":{}}}
			""" );
		var plan = Plan.Create( fixture.Document.ReadMaterial( 0 ) ); var bound = plan.Bind( fixture.Document, fixture.Upload );
		Assert.AreEqual( new NVector4( 0, .217637640824031f, 1, 1 ), plan.LinearBaseColorFactor );
		Assert.AreEqual( 0, bound.Uv.Bindings.Count ); Assert.AreEqual( 0, bound.Textures.Bindings.Count );
		Assert.AreEqual( Plan.Features.LinearFactor, bound.RequiredFeatures ); Assert.IsFalse( bound.HasAuthoredNormal ); Assert.AreEqual( 0, fixture.ResourceReads );
	}

	[TestMethod]
	public void BaseColorUsesCanonicalUvAndEncodingPlansWithoutReadingPngData()
	{
		using var fixture = new Fixture( """
			{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":0,"extensions":{"KHR_texture_transform":{"texCoord":1,"offset":[0.2,-0.4],"scale":[-2,3],"rotation":1.5707963267948966}}}},"extensions":{"KHR_materials_unlit":{}}}
			""", uvSets: 2 );
		var bound = Plan.Create( fixture.Document.ReadMaterial( 0 ), 3 ).Bind( fixture.Document, fixture.Upload );
		Assert.AreEqual( 1, bound.Uv.Bindings.Count ); Assert.AreEqual( 1, bound.Textures.Bindings.Count );
		var uv = bound.Uv.Bindings[GltfMaterialTextureRole.BaseColor]; var texture = bound.Textures.Bindings[GltfMaterialTextureRole.BaseColor];
		Assert.AreEqual( 1, uv.TextureInfo.TexCoord ); Assert.AreSame( fixture.Upload.Attributes["TEXCOORD_1"], uv.Source );
		Assert.AreEqual( 0, texture.TextureIndex ); Assert.AreEqual( 0, texture.ImageIndex ); Assert.AreEqual( 0, texture.SamplerIndex );
		Assert.IsTrue( texture.IsSrgb && texture.HasLinearAlpha && texture.StraightAlpha && texture.IgnoreImageColorMetadata && texture.TopLeftOrigin );
		Assert.AreEqual( 9986, texture.Sampler.MinFilter ); Assert.AreEqual( 9729, texture.Sampler.MagFilter );
		Assert.AreEqual( 33071, texture.Sampler.WrapS ); Assert.AreEqual( 33648, texture.Sampler.WrapT );
		Assert.AreEqual( 0, fixture.ResourceReads ); Assert.IsTrue( bound.HasResolvedBaseColorImage );
		Assert.AreEqual( Plan.Features.LinearFactor | Plan.Features.BaseColorTexture, bound.RequiredFeatures );
	}

	[TestMethod]
	public void MissingConsumedUvFailsButAnUnresolvedImageRemainsAnExplicitRealizationBlocker()
	{
		const string material = """{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":1}},"extensions":{"KHR_materials_unlit":{}}}""";
		using ( var missingUv = new Fixture( material ) )
			Assert.ThrowsException<GltfException>( () => Plan.Create( missingUv.Document.ReadMaterial( 0 ) ).Bind( missingUv.Document, missingUv.Upload ) );
		using var noImage = new Fixture( material, uvSets: 2, textureHasImage: false );
		var bound = Plan.Create( noImage.Document.ReadMaterial( 0 ) ).Bind( noImage.Document, noImage.Upload );
		Assert.IsFalse( bound.HasResolvedBaseColorImage );
		StringAssert.Contains( Assert.ThrowsException<GltfException>( () => bound.RequireRealizable( Plan.Features.All ) ).Message, "image" );
		Assert.AreEqual( 0, noImage.ResourceReads );
	}

	[TestMethod]
	[DataRow( "OPAQUE", false )]
	[DataRow( "MASK", true )]
	[DataRow( "BLEND", true )]
	public void TextureRgbAndLinearTextureAlphaAreSeparateRealizationRequirements( string mode, bool consumesAlpha )
	{
		using var fixture = new Fixture( $"{{\"alphaMode\":\"{mode}\",\"pbrMetallicRoughness\":{{\"baseColorTexture\":{{\"index\":0}}}},\"extensions\":{{\"KHR_materials_unlit\":{{}}}}}}", uvSets: 1 );
		var bound = Plan.Create( fixture.Document.ReadMaterial( 0 ) ).Bind( fixture.Document, fixture.Upload );
		Assert.IsTrue( bound.RequiredFeatures.HasFlag( Plan.Features.BaseColorTexture ) );
		Assert.AreEqual( consumesAlpha, bound.RequiredFeatures.HasFlag( Plan.Features.TextureAlpha ) );
		Assert.AreEqual( mode == "MASK", bound.RequiredFeatures.HasFlag( Plan.Features.Mask ) );
		Assert.AreEqual( mode == "BLEND", bound.RequiredFeatures.HasFlag( Plan.Features.Blend ) );
		Assert.AreEqual( 0, fixture.ResourceReads );
	}

	[TestMethod]
	[DataRow( 3, "OPAQUE", false )]
	[DataRow( 4, "OPAQUE", false )]
	[DataRow( 3, "MASK", false )]
	[DataRow( 4, "MASK", true )]
	[DataRow( 4, "BLEND", true )]
	public void VertexRgbIsLinearAndOnlyRgbaNonopaqueCoverageConsumesVertexAlpha( int components, string mode, bool usesAlpha )
	{
		using var fixture = new Fixture( $"{{\"alphaMode\":\"{mode}\",\"extensions\":{{\"KHR_materials_unlit\":{{}}}}}}", colorComponents: components );
		var bound = Plan.Create( fixture.Document.ReadMaterial( 0 ) ).Bind( fixture.Document, fixture.Upload );
		Assert.AreEqual( components, bound.VertexColorComponents ); Assert.AreSame( fixture.Upload.Attributes["COLOR_0"], bound.ColorSource );
		Assert.IsTrue( bound.RequiredFeatures.HasFlag( Plan.Features.VertexRgb ) ); Assert.AreEqual( usesAlpha, bound.RequiredFeatures.HasFlag( Plan.Features.VertexAlpha ) );
	}

	[TestMethod]
	public void AuthoredSlotPlanRetainsDifferentPrimitiveContextsWithoutChangingSourceStreams()
	{
		using var first = new Fixture( UnlitJson, colorComponents: 3, normal: true, mode: 0 );
		using var second = new Fixture( UnlitJson, mode: 4 );
		var source = Plan.Create( first.Document.ReadMaterial( 0 ), 4 );
		var a = source.Bind( first.Document, first.Upload ); var b = source.Bind( second.Document, second.Upload );
		Assert.AreSame( source, a.Source ); Assert.AreSame( source, b.Source ); Assert.AreNotSame( a, b );
		Assert.AreEqual( 0, a.SourceMode ); Assert.AreEqual( 4, b.SourceMode ); Assert.IsTrue( a.HasAuthoredNormal ); Assert.IsFalse( b.HasAuthoredNormal );
		Assert.AreEqual( 3, a.VertexColorComponents ); Assert.AreEqual( 0, b.VertexColorComponents );
		Assert.IsTrue( a.RequiredFeatures.HasFlag( Plan.Features.VertexRgb ) ); Assert.IsFalse( b.RequiredFeatures.HasFlag( Plan.Features.VertexRgb ) );
		Assert.AreSame( first.Upload.Attributes["COLOR_0"], a.ColorSource ); Assert.IsNull( b.ColorSource );
		first.Document.Dispose(); Assert.AreEqual( 36, a.ColorSource.Storage.Bytes.Length );
	}

	[TestMethod]
	public void ValidMaskedDoubleSidedDataIsPlannedButOpaqueCandidateCapabilitiesDoNotClaimIt()
	{
		using var fixture = new Fixture( """{"alphaMode":"MASK","alphaCutoff":0.6,"doubleSided":true,"extensions":{"KHR_materials_unlit":{}}}""", colorComponents: 4 );
		var plan = Plan.Create( fixture.Document.ReadMaterial( 0 ) ); var bound = plan.Bind( fixture.Document, fixture.Upload );
		Assert.IsFalse( plan.CullBackFaces );
		var supported = Plan.Features.LinearFactor | Plan.Features.VertexRgb;
		Assert.AreEqual( Plan.Features.Mask | Plan.Features.DoubleSided | Plan.Features.VertexAlpha, bound.GetUnsupportedFeatures( supported ) );
		var error = Assert.ThrowsException<GltfException>( () => bound.RequireRealizable( supported ) ); StringAssert.Contains( error.Message, "Mask" );
		bound.RequireRealizable( Plan.Features.All ); // Descriptor capability arithmetic only, not a rendered support claim.
	}

	[TestMethod]
	public void NonUnlitOrUnsupportedOptionalFallbackCannotBePromotedIntoTheUnlitPath()
	{
		Assert.ThrowsException<ArgumentNullException>( () => Plan.Create( null ) );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_unlit"],"materials":[{"extensions":{"KHR_materials_unlit":{}}}]}""" ) );
		Assert.IsFalse( document.ReadMaterial( 0 ).Unlit ); Assert.ThrowsException<GltfException>( () => Plan.Create( document.ReadMaterial( 0 ) ) );
	}

	[TestMethod]
	[DataRow( -0.1f )]
	[DataRow( 1.1f )]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	public void InvalidLinearFactorsFailBeforeAnyResourcePreparation( float value )
	{
		Assert.ThrowsException<GltfException>( () => Plan.Create( new GltfMaterial { Unlit = true, AlphaMode = "OPAQUE", BaseColorFactor = new NVector4( 1, value, 0, 1 ) } ) );
	}

	[TestMethod]
	[DataRow( -0.1f )]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	public void InvalidMaskCutoffsAreNotSubstitutedByOpaqueDefaults( float cutoff )
	{
		Assert.ThrowsException<GltfException>( () => Plan.Create( Material( "MASK", cutoff ) ) );
	}

	private static GltfMaterial Material( string mode, float cutoff ) => new() { Unlit = true, AlphaMode = mode, AlphaCutoff = cutoff, BaseColorFactor = new( .8f, .4f, .2f, .3f ) };
	private const string UnlitJson = """{"extensions":{"KHR_materials_unlit":{}}}""";
	private sealed class Fixture : IDisposable
	{
		internal GltfDocument Document { get; }
		internal GltfPrimitiveUploadData Upload { get; }
		internal int ResourceReads { get; private set; }
		internal Fixture( string materialJson, int uvSets = 0, int colorComponents = 0, bool normal = false, int mode = 4, bool textureHasImage = true )
		{
			var accessors = new JsonArray(); var attributes = new JsonObject();
			void Add( string semantic, string type )
			{
				attributes[semantic] = accessors.Count;
				accessors.Add( JsonNode.Parse( $"{{\"componentType\":5126,\"count\":3,\"type\":\"{type}\"}}" ) );
			}
			Add( "POSITION", "VEC3" ); accessors[0]!["min"] = new JsonArray( 0, 0, 0 ); accessors[0]!["max"] = new JsonArray( 0, 0, 0 );
			if ( normal ) Add( "NORMAL", "VEC3" );
			for ( var set = 0; set < uvSets; set++ ) Add( "TEXCOORD_" + set, "VEC2" );
			if ( colorComponents > 0 ) Add( "COLOR_0", "VEC" + colorComponents );
			var texture = new JsonObject { ["sampler"] = 0 }; if ( textureHasImage ) texture["source"] = 0;
			var root = new JsonObject
			{
				["asset"] = new JsonObject { ["version"] = "2.0" },
				["extensionsUsed"] = new JsonArray( "KHR_materials_unlit", "KHR_texture_transform" ),
				["accessors"] = accessors,
				["images"] = new JsonArray( new JsonObject { ["uri"] = "must-not-read.png" } ),
				["textures"] = new JsonArray( texture ),
				["samplers"] = JsonNode.Parse( """[{"minFilter":9986,"magFilter":9729,"wrapS":33071,"wrapT":33648}]""" ),
				["materials"] = new JsonArray( JsonNode.Parse( materialJson ) ),
				["meshes"] = new JsonArray( new JsonObject { ["primitives"] = new JsonArray( new JsonObject { ["attributes"] = attributes, ["mode"] = mode, ["material"] = 0 } ) } )
			};
			Document = GltfReader.Read( Encoding.UTF8.GetBytes( root.ToJsonString() ), _ => { ResourceReads++; throw new InvalidOperationException( "Planning tried to read an image." ); }, new HashSet<string> { "KHR_materials_unlit", "KHR_texture_transform" } );
			Upload = Document.MaterializePrimitiveUpload( 0, 0 );
		}
		public void Dispose() => Document.Dispose();
	}
}
