using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfNodeVisibilityTests
{
	private const string Extension = "KHR_node_visibility";

	[TestMethod]
	public void VisibilityIsInheritedAndDefaultsToTrueRegardlessOfNodeOrder()
	{
		using var document = Open( """
			[{"extensions":{"KHR_node_visibility":{"visible":true}}},
			 {"children":[0],"extensions":{"KHR_node_visibility":{"visible":false}}},
			 {},{"children":[2]}]
			""" );
		CollectionAssert.AreEqual( new[] { false, false, true, true }, document.EvaluateNodeVisibility() );
	}

	[TestMethod]
	public void OverridesAreLocalAndStillRespectHiddenAncestors()
	{
		using var document = Open( """
			[{"children":[1],"extensions":{"KHR_node_visibility":{"visible":false}}},
			 {"extensions":{"KHR_node_visibility":{"visible":true}}}]
			""" );
		CollectionAssert.AreEqual( new[] { false, false }, document.EvaluateNodeVisibility( new Dictionary<int, bool> { [1] = true } ) );
		CollectionAssert.AreEqual( new[] { true, true }, document.EvaluateNodeVisibility( new Dictionary<int, bool> { [0] = true } ) );
		CollectionAssert.AreEqual( new[] { true, false }, document.EvaluateNodeVisibility( new Dictionary<int, bool> { [0] = true, [1] = false } ) );
		CollectionAssert.AreEqual( new[] { false, false }, document.EvaluateNodeVisibility() );
	}

	[TestMethod]
	public void UnsupportedOptionalExtensionFallsBackToVisible()
	{
		using var document = Open( """[{"extensions":{"KHR_node_visibility":{"visible":false}}}]""", supports: false );
		CollectionAssert.AreEqual( new[] { true }, document.EvaluateNodeVisibility() );
		CollectionAssert.Contains( document.UnsupportedOptionalExtensions.ToArray(), Extension );
	}

	[TestMethod]
	public void InvalidValuesAndOverrideIndicesAreRejected()
	{
		using var malformed = Open( """[{"extensions":{"KHR_node_visibility":{"visible":0}}}]""" );
		Assert.ThrowsException<GltfException>( () => malformed.EvaluateNodeVisibility() );
		using var valid = Open( """[{}]""" );
		Assert.ThrowsException<GltfException>( () => valid.EvaluateNodeVisibility( new Dictionary<int, bool> { [1] = false } ) );
	}

	[TestMethod]
	public void VisibilityExtensionMustBeDeclaredUsed()
	{
		const string json = """{"asset":{"version":"2.0"},"nodes":[{"extensions":{"KHR_node_visibility":{"visible":false}}}]}""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { Extension } );
		StringAssert.Contains( Assert.ThrowsException<GltfException>( () => document.EvaluateNodeVisibility() ).Message, "extensionsUsed" );
	}

	[TestMethod]
	public void HiddenAnimatedJointStillDeformsVisibleMeshAndKeepsSourceData()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, [1, 0, 0], true );
		var joints = fixture.Bytes( "VEC4", 1, [0, 0, 0, 0] );
		var weights = fixture.Floats( "VEC4", 1, [1, 0, 0, 0] );
		var times = fixture.Floats( "SCALAR", 2, [0, 2], true );
		var translations = fixture.Floats( "VEC3", 2, [0, 0, 0, 4, 0, 0] );
		using var original = fixture.Read( $$$"""
			"extensionsUsed":["KHR_node_visibility"],
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}},"JOINTS_0":{{{joints}}},"WEIGHTS_0":{{{weights}}} }}]}],
			"nodes":[{"mesh":0,"skin":0},{"extensions":{"KHR_node_visibility":{"visible":false}},"extras":{"marker":"preserved"}}],
			"skins":[{"joints":[1]}],
			"animations":[{"samplers":[{"input":{{{times}}},"output":{{{translations}}} }],"channels":[{"sampler":0,"target":{"node":1,"path":"translation"}}]}]
			""" );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( original.Root.GetRawText() ), supportedExtensions: new HashSet<string>( StringComparer.Ordinal ) { Extension } );
		CollectionAssert.AreEqual( new[] { true, false }, document.EvaluateNodeVisibility() );
		Assert.AreEqual( new Vector3( 3, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0, document.EvaluatePose( 0, 1 ) ) );
		Assert.AreEqual( "preserved", document.Root.GetProperty( "nodes" )[1].GetProperty( "extras" ).GetProperty( "marker" ).GetString() );
		Assert.AreEqual( 1, document.Root.GetProperty( "animations" )[0].GetProperty( "channels" )[0].GetProperty( "target" ).GetProperty( "node" ).GetInt32() );
	}

	private static GltfDocument Open( string nodes, bool supports = true )
		=> GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_node_visibility\"],\"nodes\":" + nodes + "}" ),
			supportedExtensions: supports ? new HashSet<string>( StringComparer.Ordinal ) { Extension } : null );
}
