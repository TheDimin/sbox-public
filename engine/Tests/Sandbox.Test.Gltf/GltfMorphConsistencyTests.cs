using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfMorphConsistencyTests
{
	[TestMethod]
	public void MorphWeightsUseFirstPrimitiveThatDefinesTargets()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"meshes":[{"weights":[0.25,0.75],"primitives":[
			 {"attributes":{"POSITION":0}},{"attributes":{"POSITION":0},"targets":[{},{}]},
			 {"attributes":{"POSITION":0},"targets":[{},{}]}]}],
			 "accessors":[{"componentType":5126,"count":1,"type":"VEC3"}],
			 "nodes":[{"mesh":0},{"mesh":0,"weights":[0.5,0.125]}]}
			""" );
		CollectionAssert.AreEqual( new[] { 0.25f, 0.75f }, document.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { 0.5f, 0.125f }, document.GetMorphWeights( 1 ) );
		Assert.AreEqual( 0, document.ReadMorphTargets( 0, 0 ).Length );
		Assert.AreEqual( 2, document.ReadMorphTargets( 0, 1 ).Length );
	}

	[TestMethod]
	public void MixedPrimitivesDefaultAllMeshWeightsToZero()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"meshes":[{"primitives":[
			 {"attributes":{"POSITION":0}},{"attributes":{"POSITION":0},"targets":[{},{}]}]}],
			 "accessors":[{"componentType":5126,"count":1,"type":"VEC3"}],"nodes":[{"mesh":0}]}
			""" );
		CollectionAssert.AreEqual( new[] { 0f, 0f }, document.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	public void InconsistentDefinedTargetCountsAreRejected()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"meshes":[{"primitives":[
			 {"attributes":{"POSITION":0},"targets":[{}]},
			 {"attributes":{"POSITION":0},"targets":[{},{}]}]}],
			 "accessors":[{"componentType":5126,"count":1,"type":"VEC3"}],"nodes":[{"mesh":0}]}
			""" );
		Assert.ThrowsException<GltfException>( () => document.GetMorphWeights( 0 ) );
	}

	[TestMethod]
	[DataRow( "POSITION", 5120, false )]
	[DataRow( "POSITION", 5122, true )]
	[DataRow( "NORMAL", 5120, true )]
	[DataRow( "TANGENT", 5122, true )]
	public void QuantizationPermitsDefinedMorphFormats( string semantic, int componentType, bool normalized )
	{
		using var document = ReadMorph( semantic, componentType, normalized, true );
		Assert.AreEqual( 1, document.ReadMorphTargets( 0, 0 ).Length );
	}

	[TestMethod]
	[DataRow( "POSITION", 5120, false, false )]
	[DataRow( "NORMAL", 5122, true, false )]
	[DataRow( "POSITION", 5121, true, true )]
	[DataRow( "POSITION", 5123, false, true )]
	[DataRow( "NORMAL", 5120, false, true )]
	[DataRow( "TANGENT", 5122, false, true )]
	[DataRow( "NORMAL", 5125, false, true )]
	public void UnsupportedMorphFormatsAreRejected( string semantic, int componentType, bool normalized, bool quantized )
	{
		using var document = ReadMorph( semantic, componentType, normalized, quantized );
		Assert.ThrowsException<GltfException>( () => document.ReadMorphTargets( 0, 0 ) );
	}

	[TestMethod]
	public void NodeArrayEntriesMustBeObjects()
	{
		using var document = Read( """{"asset":{"version":"2.0"},"nodes":[null]}""" );
		Assert.ThrowsException<GltfException>( () => document.GetWorldTransform( 0 ) );
		Assert.ThrowsException<GltfException>( () => document.GetMorphWeights( 0 ) );
	}

	private static GltfDocument Read( string json, params string[] extensions ) =>
		GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string>( extensions, StringComparer.Ordinal ) );

	private static GltfDocument ReadMorph( string semantic, int componentType, bool normalized, bool quantized )
	{
		var extensions = quantized ? "\"extensionsUsed\":[\"KHR_mesh_quantization\"],\"extensionsRequired\":[\"KHR_mesh_quantization\"]," : "";
		var flag = normalized ? "true" : "false";
		var attributes = semantic == "POSITION" ? "\"POSITION\":0" : $"\"POSITION\":0,\"{semantic}\":0";
		var json = $$$"""
			{"asset":{"version":"2.0"},{{{extensions}}}
			 "accessors":[{"componentType":5126,"count":1,"type":"VEC3"},
			              {"componentType":{{{componentType}}},"normalized":{{{flag}}},"count":1,"type":"VEC3"}],
			 "meshes":[{"primitives":[{"attributes":{ {{{attributes}}} },"targets":[{"{{{semantic}}}":1}]}]}]}
			""";
		return Read( json, "KHR_mesh_quantization" );
	}
}
