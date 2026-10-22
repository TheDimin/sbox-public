using Editor;

namespace EditorTests;

[TestClass]
public class CloudAssetReferenceIndexTests
{
	private sealed record TestAsset( string Path, string[] References );

	[TestMethod]
	public void UpdatesReadOnlyAfterTheInitialIndexIsBuilt()
	{
		var asset = new TestAsset( "c:/project/source.scene", ["facepunch.old"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		var reads = 0;
		string[] ReadChanged()
		{
			reads++;
			return ["facepunch.changed"];
		}

		index.Update( asset, asset.Path, ReadChanged );
		Assert.AreEqual( 0, reads );
		index.Build( [asset], x => x.Path, x => x.References );
		index.Update( asset, asset.Path, ReadChanged );
		Assert.AreEqual( 0, reads );
		var result = index.Snapshot( _ => true );
		Assert.AreEqual( 1, reads );
		Assert.IsFalse( result.ContainsKey( "facepunch.old" ) );
		Assert.AreEqual( asset, result["facepunch.changed"].Single() );
	}

	[TestMethod]
	public void TheSameAssetAtMultiplePathsIsReturnedOncePerPackage()
	{
		var asset = new TestAsset( "c:/project/original.scene", ["facepunch.props", "facepunch.props"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		index.Build( [asset], x => x.Path, x => x.References );
		index.Update( asset, "c:/project/moved.scene", asset.References );

		var result = index.Snapshot( _ => true );
		CollectionAssert.AreEqual( new[] { asset }, result["facepunch.props"].ToArray() );
	}

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
	public void ReconcileDoesNotReadAssetsThatWereRemovedBeforeUse()
	{
		var reads = 0;
		var first = new TestAsset( "c:/project/a.scene", ["facepunch.old"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		index.Build( [first], x => x.Path, x => { reads++; return x.References; } );

		var added = new TestAsset( "c:/project/b.scene", ["facepunch.new"] );
		index.Reconcile( [added], x => x.Path, x => { reads++; return x.References; } );
		Assert.AreEqual( 0, reads );
		var result = index.Snapshot( _ => true );

		Assert.AreEqual( 1, reads );
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
		index.Snapshot( _ => true );

		index.Reset();
		index.Build( [asset], x => x.Path, x => { reads++; return x.References; } );
		index.Snapshot( _ => true );

		Assert.AreEqual( 2, reads );
	}

	[TestMethod]
	public void ProjectQueriesLeaveOtherSourcesUnreadUntilAGlobalQuery()
	{
		var project = new TestAsset( "c:/project/a.scene", ["facepunch.project"] );
		var library = new TestAsset( "c:/library/b.scene", ["facepunch.library"] );
		var external = new TestAsset( "d:/external/c.scene", ["facepunch.external"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		var reads = 0;
		index.Build( [project, library, external], x => x.Path, x => { reads++; return x.References; } );
		Assert.AreEqual( 0, reads );

		var scoped = index.Snapshot( x => x != external );
		Assert.AreEqual( 2, reads );
		Assert.AreEqual( 2, scoped.Count );
		Assert.IsFalse( scoped.ContainsKey( "facepunch.external" ) );

		var global = index.Snapshot( _ => true );
		Assert.AreEqual( 3, reads );
		Assert.AreEqual( external, global["facepunch.external"].Single() );
		index.Snapshot( _ => true );
		Assert.AreEqual( 3, reads );
	}

	[TestMethod]
	public void LibraryScopeChangesReadOnlyNewlyIncludedSources()
	{
		var project = new TestAsset( "c:/project/a.scene", ["facepunch.project"] );
		var library = new TestAsset( "c:/library/b.scene", ["facepunch.library"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		var reads = 0;
		index.Build( [project, library], x => x.Path, x => { reads++; return x.References; } );
		index.Snapshot( x => x == project );
		Assert.AreEqual( 1, reads );

		var added = index.Snapshot( _ => true );
		Assert.AreEqual( 2, reads );
		Assert.AreEqual( 2, added.Count );
		var removed = index.Snapshot( x => x == library );
		Assert.AreEqual( 2, reads );
		Assert.AreEqual( 1, removed.Count );
		Assert.AreEqual( library, removed["facepunch.library"].Single() );
	}

	[TestMethod]
	public void SourceSaveReplacesUnreadReferencesWithoutCallingTheSourceReader()
	{
		var asset = new TestAsset( "c:/project/a.scene", ["facepunch.old"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		index.Build( [asset], x => x.Path, _ => throw new System.InvalidOperationException( "Saved references must replace the disk reader." ) );
		var saved = new[] { "facepunch.saved", "facepunch.saved" };
		index.Update( asset, asset.Path, saved );
		saved[0] = "facepunch.modified";

		var result = index.Snapshot( _ => true );
		Assert.AreEqual( 1, result.Count );
		Assert.AreEqual( asset, result["facepunch.saved"].Single() );
	}

	[TestMethod]
	public void MetadataChangesOutsideTheCurrentScopeAreReadWhenNeeded()
	{
		var project = new TestAsset( "c:/project/a.scene", ["facepunch.project"] );
		var external = new TestAsset( "d:/external/b.vmdl", ["facepunch.old"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		index.Build( [project, external], x => x.Path, x => x.References );
		index.Snapshot( x => x == project );
		var reads = 0;
		string[] metadata = ["facepunch.changed"];
		index.Update( external, external.Path, () => { reads++; return metadata; } );
		index.Snapshot( x => x == project );
		Assert.AreEqual( 0, reads );

		metadata = ["facepunch.latest"];
		var result = index.Snapshot( _ => true );
		Assert.AreEqual( 1, reads );
		Assert.IsFalse( result.ContainsKey( "facepunch.old" ) );
		Assert.IsFalse( result.ContainsKey( "facepunch.changed" ) );
		Assert.AreEqual( external, result["facepunch.latest"].Single() );
	}

	[TestMethod]
	public void ReconcileUsesTheCurrentAssetInstanceAtAnExistingPath()
	{
		var old = new TestAsset( "c:/project/a.scene", ["facepunch.old"] );
		var replacement = new TestAsset( old.Path, ["facepunch.new"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		var reads = 0;
		index.Build( [old], x => x.Path, x => { reads++; return x.References; } );
		index.Snapshot( _ => true );
		index.Reconcile( [replacement], x => x.Path, x => { reads++; return x.References; } );
		Assert.AreEqual( 1, reads );

		var result = index.Snapshot( _ => true );
		Assert.AreEqual( 2, reads );
		Assert.IsFalse( result.ContainsKey( "facepunch.old" ) );
		Assert.AreSame( replacement, result["facepunch.new"].Single() );
	}

	[TestMethod]
	public void AReaderCanAddSourcesAndInvalidateItsOwnEntry()
	{
		var first = new TestAsset( "c:/project/a.scene", ["facepunch.old"] );
		var added = new TestAsset( "c:/project/b.scene", ["facepunch.added"] );
		var index = new CloudAssetReferenceIndex<TestAsset>();
		var laterReads = 0;
		index.Build( [first], x => x.Path, x =>
		{
			index.Update( added, added.Path, added.References );
			index.Update( first, first.Path, () => { laterReads++; return ["facepunch.new"]; } );
			return x.References;
		} );

		var initial = index.Snapshot( _ => true );
		Assert.AreEqual( first, initial["facepunch.old"].Single() );
		Assert.AreEqual( 0, laterReads );
		var changed = index.Snapshot( _ => true );
		Assert.AreEqual( 1, laterReads );
		Assert.IsFalse( changed.ContainsKey( "facepunch.old" ) );
		Assert.AreEqual( first, changed["facepunch.new"].Single() );
		Assert.AreEqual( added, changed["facepunch.added"].Single() );
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
