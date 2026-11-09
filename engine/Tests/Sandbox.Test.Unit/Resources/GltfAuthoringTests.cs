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
}
