using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Editor;

namespace Sandbox.Tests;

[TestClass]
public class GltfLodEditingTests
{
	static JsonObject Node( string json = "{}" ) => JsonNode.Parse( json ).AsObject();

	[TestMethod]
	public void WriteThenReadKeepsIdsAndThresholds()
	{
		var node = Node( """{"mesh":0,"extras":{"keep":1}}""" );
		var levels = new GltfLodLevels { BaseCoverage = 0.5f, Levels = [new( 1, 0.2f ), new( 2, 0.01f )] };
		GltfLodEditing.Write( node, levels );
		var read = GltfLodEditing.Read( node );
		CollectionAssert.AreEqual( new[] { 1, 2 }, read.Levels.Select( x => x.Node ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0.2f, 0.01f }, read.Levels.Select( x => x.Coverage ).ToArray() );
		Assert.AreEqual( 0.5f, read.BaseCoverage );
		Assert.AreEqual( 1, node["extras"]["keep"].GetValue<int>() );
	}

	[TestMethod]
	public void EmptyLevelsRemoveExtensionAndThresholdsButKeepOtherExtras()
	{
		var node = Node( """{"extensions":{"MSFT_lod":{"ids":[1]}},"extras":{"MSFT_screencoverage":[0.5,0],"keep":1}}""" );
		GltfLodEditing.Write( node, new GltfLodLevels() );
		Assert.IsNull( node["extensions"] );
		Assert.IsNull( node["extras"]["MSFT_screencoverage"] );
		Assert.AreEqual( 1, node["extras"]["keep"].GetValue<int>() );
		var only = Node( """{"extras":{"MSFT_screencoverage":[0.5,0]}}""" );
		GltfLodEditing.Write( only, new GltfLodLevels() );
		Assert.IsNull( only["extras"] );
	}

	[TestMethod]
	public void MissingOrMismatchedThresholdsFallBackToDefaults()
	{
		var read = GltfLodEditing.Read( Node( """{"extensions":{"MSFT_lod":{"ids":[1,2]}},"extras":{"MSFT_screencoverage":[0.9]}}""" ) );
		Assert.AreEqual( 0.5f, read.BaseCoverage );
		CollectionAssert.AreEqual( new[] { 0.25f, 0f }, read.Levels.Select( x => x.Coverage ).ToArray() );
	}

	[TestMethod]
	public void AddGivesOldLastLevelACutoffAndNewLastNeverCulls()
	{
		var one = GltfLodEditing.Add( new GltfLodLevels(), 1 );
		var two = GltfLodEditing.Add( one, 2 );
		CollectionAssert.AreEqual( new[] { 0.25f, 0f }, two.Levels.Select( x => x.Coverage ).ToArray() );
		var three = GltfLodEditing.Add( two, 3 );
		Assert.AreEqual( 0f, three.Levels[^1].Coverage );
		Assert.IsTrue( three.Levels[0].Coverage >= three.Levels[1].Coverage );
		Assert.IsTrue( three.Levels[1].Coverage > 0 );
	}

	[TestMethod]
	public void RemoveAndMoveKeepThresholdsInPlaceAndOrdered()
	{
		var levels = new GltfLodLevels { BaseCoverage = 0.5f, Levels = [new( 1, 0.2f ), new( 2, 0.05f ), new( 3, 0f )] };
		var moved = GltfLodEditing.Move( levels, 0, 1 );
		CollectionAssert.AreEqual( new[] { 2, 1, 3 }, moved.Levels.Select( x => x.Node ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0.2f, 0.05f, 0f }, moved.Levels.Select( x => x.Coverage ).ToArray() );
		var removed = GltfLodEditing.RemoveAt( levels, 2 );
		CollectionAssert.AreEqual( new[] { 1, 2 }, removed.Levels.Select( x => x.Node ).ToArray() );
		Assert.AreEqual( 0f, removed.Levels[^1].Coverage );
		Assert.AreEqual( levels, GltfLodEditing.Move( levels, 0, -1 ) );
	}

	[TestMethod]
	public void SetCoverageStaysBetweenNeighbours()
	{
		var levels = new GltfLodLevels { BaseCoverage = 0.5f, Levels = [new( 1, 0.2f ), new( 2, 0f )] };
		Assert.AreEqual( 0.2f, GltfLodEditing.SetCoverage( levels, -1, 0.1f ).BaseCoverage );
		Assert.AreEqual( 0.5f, GltfLodEditing.SetCoverage( levels, 0, 0.9f ).Levels[0].Coverage );
		Assert.AreEqual( 0.2f, GltfLodEditing.SetCoverage( levels, 1, 0.9f ).Levels[1].Coverage );
		Assert.AreEqual( 0f, GltfLodEditing.SetCoverage( levels, 1, -1f ).Levels[1].Coverage );
	}

	[TestMethod]
	public void NewLevelNodeCopiesMeshSkinAndTransform()
	{
		var copy = GltfLodEditing.CreateLevelNode( Node( """{"name":"Rock","mesh":3,"skin":1,"translation":[1,2,3],"children":[5],"camera":0}""" ), "Rock", 2 );
		Assert.AreEqual( "Rock_LOD2", copy["name"].GetValue<string>() );
		Assert.AreEqual( 3, copy["mesh"].GetValue<int>() );
		Assert.AreEqual( 3, copy["translation"].AsArray().Count );
		Assert.IsNull( copy["children"] );
		Assert.IsNull( copy["camera"] );
	}

	[TestMethod]
	public void EligibleNodesExcludeSelfChainStartersAndTakenLevels()
	{
		var nodes = new List<JsonObject>
		{
			Node(), Node(), Node(), Node( """{"extensions":{"MSFT_lod":{"ids":[4]}}}""" ), Node(), Node()
		};
		CollectionAssert.AreEqual( new[] { 1, 2, 5 }, GltfLodEditing.EligibleNodes( nodes, 0 ).ToArray() );
		// For the chain owner itself, its own levels are not offered unless asked.
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 5 }, GltfLodEditing.EligibleNodes( nodes, 3 ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 4, 5 }, GltfLodEditing.EligibleNodes( nodes, 3, includeCurrent: true ).ToArray() );
	}
}
