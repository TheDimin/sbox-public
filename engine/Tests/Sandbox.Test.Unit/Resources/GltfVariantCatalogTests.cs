using System;
using Sandbox;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfVariantCatalogTests
{
	[TestMethod]
	public void IndexSelectionIsExactAndInvalidSelectionIsTransactional()
	{
		var catalog = new GltfVariantCatalog( ["red", "blue", "red"] );
		var selection = catalog.Select( 2 );
		Assert.AreEqual( 2, selection.Index );
		Assert.AreEqual( "red", selection.Name );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => catalog.Select( -1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => catalog.Select( 3 ) );
		Assert.AreEqual( 2, selection.Index );
	}

	[TestMethod]
	public void NamesAreAnImmutableSnapshot()
	{
		string[] source = ["red", "blue"];
		var catalog = new GltfVariantCatalog( source ); source[0] = "changed";
		Assert.AreEqual( "red", catalog.Names[0] );
		Assert.ThrowsException<NotSupportedException>( () => ((System.Collections.Generic.IList<string>)catalog.Names)[0] = "changed" );
	}

	[TestMethod]
	public void ReloadResolvesAUniqueNameRatherThanReusingReorderedIndex()
	{
		var before = new GltfVariantCatalog( ["red", "blue"] );
		var after = new GltfVariantCatalog( ["blue", "red"] );
		var selected = before.Select( 0 );
		var transferred = after.Transfer( selected );
		Assert.AreEqual( 1, transferred.Index );
		Assert.AreEqual( "red", transferred.Name );
	}

	[TestMethod]
	public void ReloadRejectsMissingOrAmbiguousIdentityWithoutChangingOldSelection()
	{
		var duplicate = new GltfVariantCatalog( ["red", "red"] );
		var selection = duplicate.Select( 1 );
		Assert.ThrowsException<InvalidOperationException>( () => new GltfVariantCatalog( ["red"] ).Transfer( selection ) );
		var unique = new GltfVariantCatalog( ["red", "blue"] ).Select( 0 );
		Assert.ThrowsException<InvalidOperationException>( () => new GltfVariantCatalog( ["blue"] ).Transfer( unique ) );
		Assert.ThrowsException<InvalidOperationException>( () => new GltfVariantCatalog( ["red", "red"] ).Transfer( unique ) );
		Assert.AreEqual( 1, selection.Index ); Assert.AreEqual( "red", selection.Name );
		Assert.AreEqual( 0, unique.Index );
	}

	[TestMethod]
	public void ResetTransfersAcrossAnyGenerationAndNamesAreOrdinal()
	{
		var catalog = new GltfVariantCatalog( ["red", "RED"] );
		Assert.IsNull( catalog.Select( null ) );
		Assert.IsNull( new GltfVariantCatalog( [] ).Transfer( null ) );
		Assert.AreEqual( 0, new GltfVariantCatalog( ["red"] ).Transfer( catalog.Select( 0 ) ).Index );
	}
}
