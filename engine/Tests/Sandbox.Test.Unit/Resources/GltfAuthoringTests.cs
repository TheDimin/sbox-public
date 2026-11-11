using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Editor;
using Editor.GltfFacetTypes;

namespace Sandbox.Tests;

[TestClass]
public class GltfAuthoringTests
{
	const string Source = """
	{
	  "asset": { "version": "2.0" },
	  "extensionsUsed": ["KHR_mesh_quantization", "KHR_materials_unlit"],
	  "extensionsRequired": ["KHR_materials_unlit"],
	  "scenes": [ { "nodes": [0] } ],
	  "nodes": [ { "name": "Root", "children": [1] }, { "name": "Child" }, { "name": "Loose" } ],
	  "materials": [
	    { "name": "Paint", "pbrMetallicRoughness": { "metallicFactor": 0.25 }, "futureKey": 7 },
	    { "name": "Flat", "extensions": { "KHR_materials_unlit": {} } }
	  ]
	}
	""";

	static GltfAuthoringDocument Load( JsonObject overrides = null ) => GltfAuthoringDocument.Load( Encoding.UTF8.GetBytes( Source ), overrides );

	static T Facet<T>( GltfElement element ) where T : GltfFacet
	{
		var facet = (T)GltfFacets.Create( typeof( T ), element );
		facet.Read( facet.Resolve( element.Json, false ) ?? new JsonObject() );
		return facet;
	}

	static void Set<T>( GltfAuthoringDocument document, GltfElement element, Action<T> change, string property ) where T : GltfFacet
	{
		var facet = Facet<T>( element );
		change( facet );
		document.Mutate( element, json => facet.Write( facet.Resolve( json, true ), property ) );
	}

	[TestMethod]
	public void UnchangedDocumentHasNoOverridesAndSourceStaysByteIdentical()
	{
		var document = Load();
		Assert.IsNull( document.CreateOverrides() );
		var bytes = Encoding.UTF8.GetBytes( Source );
		Assert.AreSame( bytes, GltfOverrides.ApplyToSource( bytes, null ) );
	}

	[TestMethod]
	public void FacetEditBecomesPatchThatReproducesTheEditAndKeepsUnknownKeys()
	{
		var document = Load();
		var paint = document.GetElements( GltfElementKind.Material )[0];
		Set<MaterialPbrFacet>( document, paint, x => x.RoughnessFactor = 0.5f, nameof( MaterialPbrFacet.RoughnessFactor ) );

		var overrides = document.CreateOverrides();
		Assert.AreEqual( """{"materials":{"0":{"pbrMetallicRoughness":{"roughnessFactor":0.5}}}}""", overrides.ToJsonString() );

		// Reloading the source with the saved overrides gives the same document.
		var reloaded = Load( JsonNode.Parse( overrides.ToJsonString() ).AsObject() );
		var material = reloaded.GetElements( GltfElementKind.Material )[0].Json;
		Assert.AreEqual( 0.5f, material["pbrMetallicRoughness"]["roughnessFactor"].GetValue<float>() );
		Assert.AreEqual( 0.25f, material["pbrMetallicRoughness"]["metallicFactor"].GetValue<float>() );
		Assert.AreEqual( 7, material["futureKey"].GetValue<int>() );
		Assert.AreEqual( overrides.ToJsonString(), reloaded.CreateOverrides().ToJsonString(), "Saving again must give the same overrides." );
	}

	[TestMethod]
	public void AddingAndRemovingExtensionFacetsKeepsExtensionListsInSync()
	{
		var document = Load();
		var materials = document.GetElements( GltfElementKind.Material );
		var clearcoat = (MaterialClearcoatFacet)GltfFacets.Create( typeof( MaterialClearcoatFacet ), materials[0] );
		document.Mutate( materials[0], json => clearcoat.Write( clearcoat.Resolve( json, true ), null ) );
		var unlit = Facet<MaterialUnlitFacet>( materials[1] );
		document.Mutate( materials[1], unlit.RemoveFrom );

		var output = GltfOverrides.ReadJson( GltfOverrides.ApplyToSource( Encoding.UTF8.GetBytes( Source ), document.CreateOverrides() ) );
		var used = output["extensionsUsed"].AsArray().Select( x => x.GetValue<string>() ).ToArray();
		CollectionAssert.AreEqual( new[] { "KHR_mesh_quantization", "KHR_materials_clearcoat" }, used );
		Assert.IsNull( output["extensionsRequired"], "A removed required extension leaves extensionsRequired." );
		Assert.IsNull( output["materials"][1]["extensions"], "Empty parent objects are removed with the facet." );
		Assert.AreEqual( 0f, output["materials"][0]["extensions"]["KHR_materials_clearcoat"]["clearcoatFactor"].GetValue<float>() );
	}

	[TestMethod]
	public void CodecWritesGltfShapes()
	{
		var document = Load();
		var paint = document.GetElements( GltfElementKind.Material )[0];
		Set<MaterialSurfaceFacet>( document, paint, x => x.EmissiveFactor = new Color( 1, 0.5f, 0 ), nameof( MaterialSurfaceFacet.EmissiveFactor ) );
		Set<MaterialSurfaceFacet>( document, paint, x => x.AlphaMode = GltfAlphaMode.Mask, nameof( MaterialSurfaceFacet.AlphaMode ) );

		Assert.AreEqual( "[1,0.5,0]", paint.Json["emissiveFactor"].ToJsonString() );
		Assert.AreEqual( "MASK", paint.Json["alphaMode"].GetValue<string>() );
		Assert.AreEqual( GltfAlphaMode.Mask, Facet<MaterialSurfaceFacet>( paint ).AlphaMode );
		Assert.AreEqual( new Color( 1, 0.5f, 0 ), Facet<MaterialSurfaceFacet>( paint ).EmissiveFactor );
	}

	[TestMethod]
	public void GlbOverrideReplacesOnlyTheJsonChunk()
	{
		byte[] bin = [1, 2, 3, 4, 5, 6, 7, 8];
		var glb = Glb( Encoding.UTF8.GetBytes( Source ), bin );
		var overrides = JsonNode.Parse( """{"nodes":{"1":{"name":"Renamed"}}}""" ).AsObject();

		var output = GltfOverrides.ApplyToSource( glb, overrides );

		Assert.AreEqual( output.Length, (int)BinaryPrimitives.ReadUInt32LittleEndian( output.AsSpan( 8 ) ) );
		var jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian( output.AsSpan( 12 ) );
		Assert.AreEqual( 0, jsonLength % 4 );
		CollectionAssert.AreEqual( glb[(20 + JsonLength( glb ))..], output[(20 + jsonLength)..], "BIN chunk changed." );
		Assert.AreEqual( "Renamed", GltfOverrides.ReadJson( output )["nodes"][1]["name"].GetValue<string>() );
	}

	[TestMethod]
	public void OverrideForMissingElementWarnsAndIsSkipped()
	{
		var warnings = new System.Collections.Generic.List<string>();
		var overrides = JsonNode.Parse( """{"materials":{"9":{"name":"Gone"}}}""" ).AsObject();
		GltfAuthoringDocument.Load( Encoding.UTF8.GetBytes( Source ), overrides, warnings );
		Assert.AreEqual( 1, warnings.Count );
	}

	[TestMethod]
	public void HierarchyShowsSceneTreeAndUnreachableNodes()
	{
		var document = Load();
		var scene = document.GetElements( GltfElementKind.Scene )[0];
		var nodes = document.GetElements( GltfElementKind.Node );
		CollectionAssert.AreEqual( new[] { nodes[0] }, document.GetChildren( scene ).ToArray() );
		Assert.IsTrue( document.IsDescendant( scene, nodes[1] ) );
		CollectionAssert.AreEqual( new[] { nodes[2] }, document.GetUnreachableNodes().ToArray() );
		Assert.AreSame( nodes[1], document.GetChildren( nodes[0] ).Single(), "Element references must be stable." );
	}

	[TestMethod]
	public void EmissiveAboveOneIsSavedAsEmissiveStrength()
	{
		var document = Load();
		var paint = document.GetElements( GltfElementKind.Material )[0];
		Set<MaterialSurfaceFacet>( document, paint, x => x.EmissiveFactor = new Color( 4, 2, 0 ), nameof( MaterialSurfaceFacet.EmissiveFactor ) );

		Assert.AreEqual( "[1,0.5,0]", paint.Json["emissiveFactor"].ToJsonString(), "glTF limits emissiveFactor to 0..1." );
		Assert.AreEqual( 4f, paint.Json["extensions"]["KHR_materials_emissive_strength"]["emissiveStrength"].GetValue<float>() );
		Assert.AreEqual( new Color( 4, 2, 0 ), Facet<MaterialSurfaceFacet>( paint ).EmissiveFactor, "The inspector shows the product." );
		CollectionAssert.Contains( GltfOverrides.ReadJson( document.CreateSourceBytes() )["extensionsUsed"].AsArray().Select( x => x.GetValue<string>() ).ToArray(), "KHR_materials_emissive_strength" );

		Set<MaterialSurfaceFacet>( document, paint, x => x.EmissiveFactor = new Color( 0.5f, 0, 0 ), nameof( MaterialSurfaceFacet.EmissiveFactor ) );
		Assert.AreEqual( "[0.5,0,0]", paint.Json["emissiveFactor"].ToJsonString() );
		Assert.IsNull( paint.Json["extensions"]?["KHR_materials_emissive_strength"], "A color within 0..1 needs no strength." );
	}

	[TestMethod]
	public void SavedSourceHoldsEditsAndKeepsExtensionListsInSync()
	{
		var document = Load();
		var materials = document.GetElements( GltfElementKind.Material );
		Set<MaterialPbrFacet>( document, materials[0], x => x.RoughnessFactor = 0.5f, nameof( MaterialPbrFacet.RoughnessFactor ) );
		document.Mutate( materials[1], Facet<MaterialUnlitFacet>( materials[1] ).RemoveFrom );

		var saved = GltfOverrides.ReadJson( document.CreateSourceBytes() );
		Assert.AreEqual( 0.5f, saved["materials"][0]["pbrMetallicRoughness"]["roughnessFactor"].GetValue<float>() );
		Assert.AreEqual( 7, saved["materials"][0]["futureKey"].GetValue<int>(), "Unknown keys survive a save." );
		Assert.IsNull( saved["extensionsRequired"] );
		CollectionAssert.AreEqual( new[] { "KHR_mesh_quantization" }, saved["extensionsUsed"].AsArray().Select( x => x.GetValue<string>() ).ToArray() );
	}

	[TestMethod]
	public void TextureSlotEditKeepsTextureTransform()
	{
		var document = Load();
		var paint = document.GetElements( GltfElementKind.Material )[0];
		document.Mutate( paint, json => json["pbrMetallicRoughness"]["baseColorTexture"] = JsonNode.Parse( """{"index":0,"extensions":{"KHR_texture_transform":{"scale":[2,2]}}}""" ) );
		Set<MaterialPbrFacet>( document, paint, x => x.BaseColorTexture = x.BaseColorTexture with { Texture = 3, TexCoord = 1 }, nameof( MaterialPbrFacet.BaseColorTexture ) );

		var slot = paint.Json["pbrMetallicRoughness"]["baseColorTexture"];
		Assert.AreEqual( 3, slot["index"].GetValue<int>() );
		Assert.AreEqual( 1, slot["texCoord"].GetValue<int>() );
		Assert.IsNotNull( slot["extensions"]["KHR_texture_transform"], "The transform belongs to the slot and must stay." );

		Set<MaterialPbrFacet>( document, paint, x => x.BaseColorTexture = x.BaseColorTexture with { Texture = -1 }, nameof( MaterialPbrFacet.BaseColorTexture ) );
		Assert.IsNull( paint.Json["pbrMetallicRoughness"]["baseColorTexture"], "An empty slot removes the reference." );
	}

	[TestMethod]
	public void AddedElementsAreSavedAndUndoRemovesThem()
	{
		var document = Load();
		var image = document.AddElement( GltfElementKind.Image, new JsonObject { ["uri"] = "a.png" } );
		Assert.AreEqual( 0, image.Index );
		Assert.AreEqual( "a.png", GltfOverrides.ReadJson( document.CreateSourceBytes() )["images"][0]["uri"].GetValue<string>() );
		document.RemoveLastElement( GltfElementKind.Image );
		Assert.AreEqual( 0, document.Count( GltfElementKind.Image ) );
	}

	[TestMethod]
	public void MaterialSphereSourceIsOneMeshWithThatMaterial()
	{
		var document = Load();
		var sphere = GltfOverrides.ReadJson( document.CreateMaterialSphereSource( 1 ) );
		Assert.AreEqual( 1, sphere["meshes"].AsArray().Count );
		Assert.AreEqual( 1, sphere["meshes"][0]["primitives"][0]["material"].GetValue<int>() );
		Assert.AreEqual( 0, sphere["scene"].GetValue<int>() );
		var position = sphere["meshes"][0]["primitives"][0]["attributes"]["POSITION"].GetValue<int>();
		Assert.AreEqual( 17 * 33, sphere["accessors"][position]["count"].GetValue<int>() );
		Assert.AreEqual( 2, sphere["materials"].AsArray().Count, "Materials stay so the index is valid." );
	}

	[TestMethod]
	public void SamplersAreSharedByTheirSettings()
	{
		var document = Load();
		var clamp = new GltfTextureSampling( GltfMagFilter.Linear, GltfMinFilter.Default, GltfWrap.ClampToEdge, GltfWrap.ClampToEdge );
		var first = document.FindOrAddSampler( clamp );
		Assert.AreEqual( first, document.FindOrAddSampler( clamp ), "The same settings reuse one sampler." );
		Assert.AreNotEqual( first, document.FindOrAddSampler( GltfTextureSampling.Default with { WrapS = GltfWrap.MirroredRepeat } ) );
		Assert.AreEqual( 2, document.Count( GltfElementKind.Sampler ) );
		Assert.IsTrue( GltfTextureSampling.Default.IsDefault );
	}

	[TestMethod]
	public void RestoredSnapshotRemovesAddedElements()
	{
		var document = Load();
		var before = document.SnapshotRoot();
		document.FindOrAddImage( "a.png", "a" );
		Assert.AreEqual( 0, document.FindOrAddImage( "a.png", "a" ), "One image per URI." );
		document.RestoreRoot( before );
		Assert.AreEqual( 0, document.Count( GltfElementKind.Image ) );
		Assert.AreEqual( "Paint", document.GetElements( GltfElementKind.Material )[0].Name );
	}

	const string VariantSource = """
	{
	  "asset": { "version": "2.0" },
	  "extensionsUsed": ["KHR_materials_variants"],
	  "extensions": { "KHR_materials_variants": { "variants": [ { "name": "Red" }, { "name": "Blue" } ] } },
	  "materials": [ { "name": "A" }, { "name": "B" } ],
	  "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "material": 0,
	    "extensions": { "KHR_materials_variants": { "mappings": [ { "material": 1, "variants": [1] } ] } } } ] } ]
	}
	""";

	[TestMethod]
	public void MeshVariantsComeFromPrimitiveMappings()
	{
		var document = GltfAuthoringDocument.Load( Encoding.UTF8.GetBytes( VariantSource ), null );
		var mesh = document.GetElements( GltfElementKind.Mesh )[0];
		var variants = document.GetElements( GltfElementKind.Variant );
		CollectionAssert.AreEqual( new[] { variants[1] }, document.GetMeshVariants( mesh ).ToArray() );
		Assert.AreEqual( 1, document.GetVariantMaterial( mesh, 0, variants[1] ) );
		Assert.AreEqual( -1, document.GetVariantMaterial( mesh, 0, variants[0] ), "An unmapped variant keeps the default material." );
		Assert.AreEqual( "Blue", variants[1].Name );
	}

	const string SkinSource = """
	{
	  "asset": { "version": "2.0" },
	  "buffers": [ { "byteLength": 60, "uri": "data:application/octet-stream;base64,AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAEAAAAAAQAA/wAAAP8AAACAfwAA" } ],
	  "bufferViews": [ { "buffer": 0, "byteOffset": 0, "byteLength": 36 }, { "buffer": 0, "byteOffset": 36, "byteLength": 12 }, { "buffer": 0, "byteOffset": 48, "byteLength": 12 } ],
	  "accessors": [
	    { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0,0,0], "max": [1,1,0] },
	    { "bufferView": 1, "componentType": 5121, "count": 3, "type": "VEC4" },
	    { "bufferView": 2, "componentType": 5121, "normalized": true, "count": 3, "type": "VEC4" }
	  ],
	  "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0, "JOINTS_0": 1, "WEIGHTS_0": 2 } } ] } ],
	  "nodes": [ { "mesh": 0, "skin": 0 }, { "name": "Hip" }, { "name": "Knee" } ],
	  "skins": [ { "joints": [1, 2] } ],
	  "scenes": [ { "nodes": [0, 1, 2] } ]
	}
	""";

	[TestMethod]
	public void JointWeightSourceColorsTheMeshesOfTheSkin()
	{
		var document = GltfAuthoringDocument.Load( Encoding.UTF8.GetBytes( SkinSource ), null );
		var skin = document.GetElements( GltfElementKind.Skin )[0];
		CollectionAssert.AreEqual( new[] { "Hip", "Knee" }, document.GetJointNodes( skin ).Select( x => x.Name ).ToArray() );
		Assert.AreEqual( 1, document.GetSkinnedNodes( skin ).Count() );

		var heat = GltfOverrides.ReadJson( document.CreateJointWeightSource( skin, 1 ) );
		var primitive = heat["meshes"][0]["primitives"][0];
		Assert.IsNull( primitive["attributes"]["JOINTS_0"], "The preview is not skinned." );
		var color = primitive["attributes"]["COLOR_0"].GetValue<int>();
		Assert.AreEqual( 3, heat["accessors"][color]["count"].GetValue<int>() );
		Assert.IsNotNull( heat["materials"][primitive["material"].GetValue<int>()]["extensions"]["KHR_materials_unlit"] );
		Assert.IsNull( heat["skins"] );
		CollectionAssert.Contains( heat["extensionsUsed"].AsArray().Select( x => x.GetValue<string>() ).ToArray(), "KHR_materials_unlit" );
	}

	static int JsonLength( byte[] glb ) => (int)BinaryPrimitives.ReadUInt32LittleEndian( glb.AsSpan( 12 ) );

	static byte[] Glb( byte[] json, byte[] bin )
	{
		var jsonPadded = (json.Length + 3) & ~3;
		var output = new byte[12 + 8 + jsonPadded + 8 + bin.Length];
		BinaryPrimitives.WriteUInt32LittleEndian( output, 0x46546C67 );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 8 ), (uint)output.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 12 ), (uint)jsonPadded );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 16 ), 0x4E4F534A );
		json.CopyTo( output, 20 );
		output.AsSpan( 20 + json.Length, jsonPadded - json.Length ).Fill( (byte)' ' );
		var binStart = 20 + jsonPadded;
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( binStart ), (uint)bin.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( binStart + 4 ), 0x004E4942 );
		bin.CopyTo( output, binStart + 8 );
		return output;
	}

	/// <summary>One node that moves from x=0 at t=0 to x=2 at t=1 (LINEAR), with a STEP rotation.</summary>
	static string AnimationSource()
	{
		float[] data = [0, 1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 1, 0, 0.7071068f, 0, 0.7071068f];
		var bytes = new byte[data.Length * 4];
		Buffer.BlockCopy( data, 0, bytes, 0, bytes.Length );
		return $$"""
		{
		  "asset": { "version": "2.0" },
		  "scenes": [ { "nodes": [0] } ],
		  "nodes": [ { "name": "Box" } ],
		  "buffers": [ { "byteLength": {{bytes.Length}}, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String( bytes )}}" } ],
		  "bufferViews": [ { "buffer": 0, "byteOffset": 0, "byteLength": 8 }, { "buffer": 0, "byteOffset": 8, "byteLength": 24 }, { "buffer": 0, "byteOffset": 32, "byteLength": 32 } ],
		  "accessors": [
		    { "bufferView": 0, "componentType": 5126, "count": 2, "type": "SCALAR", "min": [0], "max": [1] },
		    { "bufferView": 1, "componentType": 5126, "count": 2, "type": "VEC3" },
		    { "bufferView": 2, "componentType": 5126, "count": 2, "type": "VEC4" }
		  ],
		  "animations": [ {
		    "channels": [ { "sampler": 0, "target": { "node": 0, "path": "translation" } }, { "sampler": 1, "target": { "node": 0, "path": "rotation" } } ],
		    "samplers": [ { "input": 0, "output": 1 }, { "input": 0, "output": 2, "interpolation": "STEP" } ]
		  } ]
		}
		""";
	}

	[TestMethod]
	public void AnimationSamplesInterpolateLikeTheRuntime()
	{
		var document = GltfAuthoringDocument.Load( Encoding.UTF8.GetBytes( AnimationSource() ), null );
		var animation = document.GetElements( GltfElementKind.Animation )[0];
		var (duration, channels) = document.SampleAnimation( animation, 4 );

		Assert.AreEqual( 1f, duration );
		var translation = channels.Single( x => x.Path == "translation" ).Values;
		Assert.AreEqual( 5, translation.Length, "Samples from 0 to 1 s at 4 per second, both ends included." );
		Assert.AreEqual( 1f, translation[2][0], 1e-5f, "LINEAR is halfway at 0.5 s." );
		var rotation = channels.Single( x => x.Path == "rotation" ).Values;
		Assert.AreEqual( 1f, rotation[3][3], 1e-5f, "STEP keeps the first key until the last one." );
		Assert.AreEqual( 0.7071068f, rotation[4][1], 1e-5f );
	}

	[TestMethod]
	public void WrittenAnimationSamplesReadBackAndReplaceTheirBuffer()
	{
		var document = GltfAuthoringDocument.Load( Encoding.UTF8.GetBytes( AnimationSource() ), null );
		var animation = document.GetElements( GltfElementKind.Animation )[0];
		var box = document.GetElements( GltfElementKind.Node )[0];
		static float[][] Moves( float end ) => [[0, 0, 0], [end / 2, 0, 0], [end, 0, 0]];

		document.WriteAnimationSamples( animation, 2, [new GltfAnimationChannelSamples( box, "translation", Moves( 4 ) )] );
		var buffersAfterFirst = document.SnapshotRoot()["buffers"].AsArray().Count;
		document.WriteAnimationSamples( animation, 2, [new GltfAnimationChannelSamples( box, "translation", Moves( 6 ) )] );

		Assert.AreEqual( buffersAfterFirst, document.SnapshotRoot()["buffers"].AsArray().Count, "A second write replaces the editor buffer." );
		var saved = GltfAuthoringDocument.Load( document.CreateSourceBytes(), null );
		var (duration, channels) = saved.SampleAnimation( saved.GetElements( GltfElementKind.Animation )[0], 2 );
		Assert.AreEqual( 1f, duration );
		Assert.AreEqual( 6f, channels.Single( x => x.Path == "translation" ).Values[2][0], 1e-5f );
		Assert.AreEqual( 1f, channels.Single( x => x.Path == "rotation" ).Values[1][3], 1e-5f, "Channels that were not written stay." );
	}
}
