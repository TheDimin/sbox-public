using System;
using System.Linq;
using Sandbox.Clutter;

[TestClass]
public class ClutterAnchorNeighborsTests
{
	private static Vector3[] Placements() => Enumerable.Range( 0, 100 ).Select( i => new Vector3( (i % 10) * 4f, (i / 10) * 4f, 0f ) ).ToArray();

	[TestMethod]
	public void AnchorsAreRetainedAndNeighborCountIsBounded()
	{
		var roots = Placements();
		var result = ClutterAnchorNeighbors.Build( roots, Enumerable.Repeat( true, roots.Length ).ToArray(), 20f, 0.5f );
		Assert.AreEqual( roots.Length * 8, result.Length );
		Assert.IsTrue( result.Any( id => id != uint.MaxValue ) );
		for ( int id = 0; id < roots.Length; ++id )
		{
			var neighbors = result.Skip( id * 8 ).Take( 8 ).Where( n => n != uint.MaxValue ).ToArray();
			Assert.AreEqual( neighbors.Length, neighbors.Distinct().Count() );
			foreach ( var anchor in neighbors )
			{
				Assert.IsTrue( anchor < roots.Length && anchor != id );
				Assert.IsTrue( result.Skip( (int)anchor * 8 ).Take( 8 ).All( n => n == uint.MaxValue ), "Retained anchors cannot depend on removal chains." );
			}
		}
	}

	[TestMethod]
	public void ReorderingSlotsKeepsTheSameWorldNeighbors()
	{
		var roots = Placements();
		var reversed = roots.Reverse().ToArray();
		var active = Enumerable.Repeat( true, roots.Length ).ToArray();
		var first = ClutterAnchorNeighbors.Build( roots, active, 20f, 0.5f );
		var second = ClutterAnchorNeighbors.Build( reversed, active, 20f, 0.5f );
		for ( int id = 0; id < roots.Length; ++id )
		{
			var a = first.Skip( id * 8 ).Take( 8 ).Where( n => n != uint.MaxValue ).Select( n => roots[n] ).ToArray();
			var b = second.Skip( (roots.Length - 1 - id) * 8 ).Take( 8 ).Where( n => n != uint.MaxValue ).Select( n => reversed[n] ).ToArray();
			CollectionAssert.AreEqual( a, b );
		}
	}

	[TestMethod]
	public void RemovingAndReusingASlotDoesNotKeepOldReferences()
	{
		var roots = Placements();
		var active = Enumerable.Repeat( true, roots.Length ).ToArray();
		var first = ClutterAnchorNeighbors.Build( roots, active, 20f, 0.5f );
		uint removed = first.First( id => id != uint.MaxValue );
		active[removed] = false;
		var removedResult = ClutterAnchorNeighbors.Build( roots, active, 20f, 0.5f );
		Assert.IsFalse( removedResult.Contains( removed ) );
		roots[removed] = new Vector3( 100000f, 100000f, 100000f );
		active[removed] = true;
		var reusedResult = ClutterAnchorNeighbors.Build( roots, active, 20f, 0.5f );
		Assert.IsFalse( reusedResult.Contains( removed ) );
	}

	[TestMethod]
	public void EmptyFractionsAndUnsupportedScalesRetainAll()
	{
		var roots = Placements();
		var active = Enumerable.Repeat( true, roots.Length ).ToArray();
		foreach ( var fraction in new[] { 0f, 1f } )
			Assert.IsTrue( ClutterAnchorNeighbors.Build( roots, active, 20f, fraction ).All( n => n == uint.MaxValue ) );
		foreach ( var height in new[] { 0f, float.NaN, float.MaxValue, float.Epsilon } )
			Assert.IsTrue( ClutterAnchorNeighbors.Build( roots, active, height, 0.5f ).All( n => n == uint.MaxValue ) );
	}

	[TestMethod]
	public void InvalidPositionsStayAndDoNotMutateTheActiveMask()
	{
		var roots = new[] { new Vector3( float.NaN, 0, 0 ), new Vector3( float.MaxValue, 0, 0 ), Vector3.Zero };
		var active = new[] { true, true, true };
		Assert.IsTrue( ClutterAnchorNeighbors.Build( roots, active, 20f, 0.5f ).All( n => n == uint.MaxValue ) );
		CollectionAssert.AreEqual( new[] { true, true, true }, active );
	}
	[TestMethod]
	public void CrowdedCellsKeepAllPlacementsRegardlessOfSlotOrder()
	{
		var roots = Enumerable.Range( 0, 2048 ).Select( i => new Vector3( i * 0.001f, 1f, 0f ) ).ToArray();
		var active = Enumerable.Repeat( true, roots.Length ).ToArray();
		var first = ClutterAnchorNeighbors.Build( roots, active, 20f, 0.5f );
		var reversed = ClutterAnchorNeighbors.Build( roots.Reverse().ToArray(), active, 20f, 0.5f );
		Assert.IsTrue( first.All( id => id == uint.MaxValue ), "Crowded neighborhoods must keep grass instead of scanning or truncating candidates." );
		CollectionAssert.AreEqual( first, reversed );
		var sparse = Placements();
		Assert.IsTrue( ClutterAnchorNeighbors.Build( sparse, Enumerable.Repeat( true, sparse.Length ).ToArray(), 20f, 0.5f ).Any( id => id != uint.MaxValue ) );
	}
}
