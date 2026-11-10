using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfNodeLodTests
{
	private const string Lod = "\"extensions\":{\"MSFT_lod\":{\"ids\":[1,2]}}";

	[TestMethod]
	public void ReadsChainAndCoverageFromHighestToLowest()
	{
		using var document = Open( $"[{{{Lod},\"extras\":{{\"MSFT_screencoverage\":[0.5,0.2,0.01]}}}},{{}},{{}}]" );
		var chain = document.ReadNodeLods().Single();
		Assert.AreEqual( 0, chain.Node );
		CollectionAssert.AreEqual( new[] { 1, 2 }, chain.Levels.ToArray() );
		CollectionAssert.AreEqual( new[] { 0.5f, 0.2f, 0.01f }, chain.ScreenCoverage.ToArray() );
		Assert.IsNull( chain.Warning );
	}

	[TestMethod]
	public void CoverageWithWrongCountOrOrderIsDroppedWithWarning()
	{
		using var count = Open( $"[{{{Lod},\"extras\":{{\"MSFT_screencoverage\":[0.5,0.2]}}}},{{}},{{}}]" );
		Assert.AreEqual( 0, count.ReadNodeLods().Single().ScreenCoverage.Count );
		Assert.IsNotNull( count.ReadNodeLods().Single().Warning );
		using var order = Open( $"[{{{Lod},\"extras\":{{\"MSFT_screencoverage\":[0.1,0.2,0.3]}}}},{{}},{{}}]" );
		Assert.IsNotNull( order.ReadNodeLods().Single().Warning );
	}

	[TestMethod]
	public void UnsupportedExtensionFallsBackToBaseNodeOnly()
	{
		using var document = Open( $"[{{{Lod}}},{{}},{{}}]", supports: false );
		Assert.AreEqual( 0, document.ReadNodeLods().Count );
	}

	[TestMethod]
	public void InvalidChainsAreRejected()
	{
		foreach ( var nodes in new[]
		{
			"[{\"extensions\":{\"MSFT_lod\":{\"ids\":[5]}}},{}]",                                  // out of range
			"[{\"extensions\":{\"MSFT_lod\":{\"ids\":[0]}}},{}]",                                  // self
			"[{\"extensions\":{\"MSFT_lod\":{\"ids\":[1,1]}}},{}]",                                // duplicate
			"[{\"extensions\":{\"MSFT_lod\":{\"ids\":[]}}},{}]",                                   // empty
			"[{\"extensions\":{\"MSFT_lod\":{\"ids\":[1]}}},{\"extensions\":{\"MSFT_lod\":{\"ids\":[0]}}}]", // cycle
			"[{\"extensions\":{\"MSFT_lod\":{\"ids\":[2]}}},{\"extensions\":{\"MSFT_lod\":{\"ids\":[2]}}},{}]", // shared level
		} )
		{
			using var document = Open( nodes );
			Assert.ThrowsException<GltfException>( () => document.ReadNodeLods(), nodes );
		}
	}

	[TestMethod]
	public void NestedChainIsIgnoredWithWarning()
	{
		using var document = Open( "[{\"extensions\":{\"MSFT_lod\":{\"ids\":[1]}}},{\"extensions\":{\"MSFT_lod\":{\"ids\":[2]}}},{}]" );
		var chain = document.ReadNodeLods().Single();
		Assert.AreEqual( 0, chain.Node );
		CollectionAssert.AreEqual( new[] { 1 }, chain.Levels.ToArray() );
		StringAssert.Contains( chain.Warning, "ignored" );
	}

	[TestMethod]
	public void ExtensionMustBeDeclaredUsed()
	{
		var json = $"{{\"asset\":{{\"version\":\"2.0\"}},\"nodes\":[{{{Lod}}},{{}},{{}}]}}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "MSFT_lod" } );
		StringAssert.Contains( Assert.ThrowsException<GltfException>( () => document.ReadNodeLods() ).Message, "extensionsUsed" );
	}

	[TestMethod]
	public void ReadsMaterialChainsAndAllowsSharedLowerMaterials()
	{
		var json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"MSFT_lod\"],\"materials\":[" +
			"{\"extensions\":{\"MSFT_lod\":{\"ids\":[2]}}},{\"extensions\":{\"MSFT_lod\":{\"ids\":[2]}}},{}]}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "MSFT_lod" } );
		var chains = document.ReadMaterialLods();
		Assert.AreEqual( 2, chains.Count );
		CollectionAssert.AreEqual( new[] { 2 }, chains[0].Levels.ToArray() );
	}

	private static GltfDocument Open( string nodes, bool supports = true )
		=> GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"MSFT_lod\"],\"nodes\":" + nodes + "}" ),
			supportedExtensions: supports ? new HashSet<string>( StringComparer.Ordinal ) { "MSFT_lod" } : null );
}
