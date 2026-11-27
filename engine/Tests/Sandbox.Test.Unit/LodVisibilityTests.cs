using GtaV;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MountingTests;

[TestClass]
public class LodVisibilityTests
{
	sealed class Representation
	{
		public bool Visible = true;
	}

	[TestMethod]
	public void PartialSiblingGroupKeepsParentAndHidesChildren()
	{
		var group = new LodVisibility<Representation>( ( obj, enabled ) => obj.Visible = enabled );
		var parent = new Representation();
		var first = new Representation();
		var last = new Representation();
		var p = new LodEntityId( 1, 502 );
		var a = new LodEntityId( 2, 132 );
		var b = new LodEntityId( 3, 132 );
		group.Add( a, first, 0, p );
		group.Add( p, parent, 2, null );
		Assert.IsTrue( parent.Visible && !first.Visible );
		group.Add( b, last, 0, p );
		Assert.IsTrue( !parent.Visible && first.Visible && last.Visible );
		group.Remove( b );
		Assert.IsTrue( parent.Visible && !first.Visible && !last.Visible );
	}

	[TestMethod]
	public void TimingIdentifiesTheReplacementWhenASelectedPlacementIsRemoved()
	{
		var group = new LodVisibility<int>( ( _, _ ) => { } );
		string exit = null;
		group.SelectionEnded = ( id, reason ) => exit = reason == "removed" ? group.ReplacementAfterRemoval( id ) : reason;
		var parent = new LodEntityId( 1, 0 );
		var first = new LodEntityId( 2, 0 );
		var second = new LodEntityId( 2, 1 );
		group.Add( parent, 0, 2, null );
		group.Add( first, 1, 0, parent );
		group.Remove( parent );
		Assert.AreEqual( "finer", exit );
		group.Add( parent, 0, 2, null );
		Assert.AreEqual( "coarser", exit );
		group.Add( second, 2, 0, parent );
		Assert.AreEqual( "finer", exit );
		group.Remove( first );
		Assert.AreEqual( "coarser", exit );
		group.Remove( second );
		group.Remove( parent );
		Assert.AreEqual( "removed", exit );
	}

	[TestMethod]
	public void GrandchildrenCannotDrawThroughCoveringAncestor()
	{
		var group = new LodVisibility<Representation>( ( obj, enabled ) => obj.Visible = enabled );
		var root = new Representation();
		var middle = new Representation();
		var leaf = new Representation();
		var sibling = new Representation();
		var r = new LodEntityId( 1, 0 );
		var m = new LodEntityId( 2, 0 );
		var l = new LodEntityId( 3, 0 );
		var s = new LodEntityId( 2, 1 );
		group.Add( r, root, 2, null );
		group.Add( m, middle, 1, r );
		group.Add( l, leaf, 0, m );
		Assert.IsTrue( root.Visible && !middle.Visible && !leaf.Visible );
		group.Add( s, sibling, 0, r );
		Assert.IsTrue( !root.Visible && !middle.Visible && leaf.Visible && sibling.Visible );
		group.Remove( m );
		Assert.IsTrue( root.Visible && !leaf.Visible && !sibling.Visible );
		group.Add( m, middle, 1, r );
		Assert.IsTrue( !root.Visible && !middle.Visible && leaf.Visible && sibling.Visible );
	}
}
