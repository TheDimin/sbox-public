using Editor;

namespace EditorTests;

[TestClass]
public class CloudAssetReferenceIndexTests
{
	private sealed record TestAsset( string Path, string[] References );

	[TestMethod]
	public void WarmQueriesDoNotReadAssetsAgain()
	{
		var reads = 0;
		var assets = new[]
		{
			new TestAsset( "c:/project/a.scene", ["facepunch.props"] ),
			new TestAsset( "c:/project/b.scene", ["facepunch.citizen"] )
		};
		var index = new CloudAssetReferenceIndex<TestAsset>();

		index.Build( assets, x => x.Path, x => { reads++; return x.References; } );
		index.Snapshot( _ => true );
		index.Build( assets, x => x.Path, x => { reads++; return x.References; } );
		var second = index.Snapshot( _ => true );

		Assert.AreEqual( 2, reads );
		Assert.AreEqual( 2, second.Count );
	}

	[TestMethod]
	public void UpdatingOneAssetReplacesOnlyItsReferences()
	{
		var first = new TestAsset( "c:/project/a.scene", ["facepunch.old", "facepunch.old"] );
		var second = new TestAsset( "c:/project/b.scene", ["facepunch.stable"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		index.Build( [first, second], x => x.Path, x => x.References );

		var updated = new TestAsset( first.Path, ["facepunch.new"] );
		index.Update( updated, updated.Path, updated.References );
		var result = index.Snapshot( _ => true );

		Assert.IsFalse( result.ContainsKey( "facepunch.old" ) );
		Assert.AreEqual( updated, result["facepunch.new"].Single() );
		Assert.AreEqual( second, result["facepunch.stable"].Single() );
	}

	[TestMethod]
	public void ReconcileReadsOnlyAddedAssetsAndRemovesDeletedAssets()
	{
		var reads = 0;
		var first = new TestAsset( "c:/project/a.scene", ["facepunch.old"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		index.Build( [first], x => x.Path, x => { reads++; return x.References; } );

		var added = new TestAsset( "c:/project/b.scene", ["facepunch.new"] );
		index.Reconcile( [added], x => x.Path, x => { reads++; return x.References; } );
		var result = index.Snapshot( _ => true );

		Assert.AreEqual( 2, reads );
		Assert.IsFalse( result.ContainsKey( "facepunch.old" ) );
		Assert.AreEqual( added, result["facepunch.new"].Single() );
	}

	[TestMethod]
	public void SnapshotFiltersAssetsAndReturnsDefensiveCollections()
	{
		var project = new TestAsset( "c:/project/a.scene", ["facepunch.shared"] );
		var external = new TestAsset( "d:/external/b.scene", ["facepunch.shared"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		index.Build( [project, external], x => x.Path, x => x.References );

		var result = index.Snapshot( x => x.Path.StartsWith( "c:/project/" ) );
		result["facepunch.shared"].Clear();
		var again = index.Snapshot( x => x.Path.StartsWith( "c:/project/" ) );

		Assert.AreEqual( project, again["facepunch.shared"].Single() );
	}

	[TestMethod]
	public void ResetForcesTheNextBuildToReadAgain()
	{
		var reads = 0;
		var asset = new TestAsset( "c:/project/a.scene", ["facepunch.props"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		index.Build( [asset], x => x.Path, x => { reads++; return x.References; } );

		index.Reset();
		index.Build( [asset], x => x.Path, x => { reads++; return x.References; } );

		Assert.AreEqual( 2, reads );
	}

	[TestMethod]
	public void SavedJsonReferencesAreAvailableWithoutReadingAnAsset()
	{
		var references = CloudAsset.ReadGameResourceReferences( null,
			"""{"__references":["facepunch.props","facepunch.citizen"]}""" );

		CollectionAssert.AreEqual( new[] { "facepunch.props", "facepunch.citizen" }, references );
	}

	[TestMethod]
	public void MalformedSavedJsonClearsReferencesSafely()
	{
		var references = CloudAsset.ReadGameResourceReferences( null, "{not-json" );

		Assert.AreEqual( 0, references.Length );
	}
}
