using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ResourceTests;

[TestClass]
public sealed class GltfLodAssetTests
{
	// Node 0 is in the scene. Nodes 1 and 2 are its lower levels and are not. Node 3 is a plain node.
	private const string Source = """
		{"asset":{"version":"2.0"},"extensionsUsed":["MSFT_lod"],
		 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
		 "nodes":[{"mesh":0,"extensions":{"MSFT_lod":{"ids":[1,2]}},"extras":{"MSFT_screencoverage":[0.5,0.2,0.01]}},{"mesh":0},{"mesh":0},{"mesh":0}],
		 "scenes":[{"nodes":[0,3]}],"scene":0}
		""";

	private static GltfDocument Read( string source = Source ) =>
		GltfReader.Read( Encoding.UTF8.GetBytes( source ), supportedExtensions: new HashSet<string> { "MSFT_lod" } );

	[TestMethod]
	public void LevelsOutsideTheSceneJoinTheirChainAndKeepThresholds()
	{
		using var document = Read();
		using var asset = GltfGpuAsset.Prepare( document, GltfGpuAsset.DefaultComputeShaderPath, _ => null );
		Assert.AreEqual( 1, asset.LodGroups.Count );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, asset.LodGroups[0].Nodes.ToArray() );
		CollectionAssert.AreEqual( new[] { 0.5f, 0.2f, 0.01f }, asset.LodGroups[0].Thresholds );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3 }, asset.Nodes.Select( x => x.Index ).Order().ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 2 }, new[] { asset.LodLevelOfNode[3], asset.LodLevelOfNode[0], asset.LodLevelOfNode[1], asset.LodLevelOfNode[2] } );
		Assert.AreEqual( -1, asset.LodGroupOfNode[3] );
	}

	[TestMethod]
	public void LevelNodeThatIsAlsoASceneChildDrawsOnlyOnce()
	{
		using var document = Read( Source.Replace( "\"nodes\":[0,3]", "\"nodes\":[0,1,3]" ) );
		using var asset = GltfGpuAsset.Prepare( document, GltfGpuAsset.DefaultComputeShaderPath, _ => null );
		Assert.AreEqual( 1, asset.Nodes.Count( x => x.Index == 1 ) );
		Assert.AreEqual( 1, asset.LodLevelOfNode[1] );
	}

	[TestMethod]
	public void WithoutExtensionSupportOnlyTheBaseNodeAndSceneNodesDraw()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ) );
		using var asset = GltfGpuAsset.Prepare( document, GltfGpuAsset.DefaultComputeShaderPath, _ => null );
		Assert.AreEqual( 0, asset.LodGroups.Count );
		CollectionAssert.AreEqual( new[] { 0, 3 }, asset.Nodes.Select( x => x.Index ).Order().ToArray() );
	}
}
