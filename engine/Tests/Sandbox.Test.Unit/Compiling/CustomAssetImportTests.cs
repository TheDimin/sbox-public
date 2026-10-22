using System;
using System.Collections.Generic;

namespace CompilingTests;

[TestClass]
public class CustomAssetImportTests
{
	[TestMethod]
	public void ImportsEachResourceOnceAndKeepsCompiledOnlyResources()
	{
		string[] files =
		[
			"data/source.custom", "data/source.custom_c", "data/source.custom.meta",
			"data/compiled.custom_c", "data/compiled.custom",
			"cloud/only.custom_c", "CLOUD/ONLY.CUSTOM_C",
			"images/texture.png", "notes/readme.txt"
		];

		var paths = Editor.AssetType.GetCustomTypeFiles( files,
			extension => extension.Equals( ".custom", StringComparison.OrdinalIgnoreCase ) ||
				extension.Equals( ".custom_c", StringComparison.OrdinalIgnoreCase ) ).ToArray();

		CollectionAssert.AreEqual( new[] { "data/source.custom", "data/compiled.custom_c", "cloud/only.custom_c" }, paths );
	}

	[TestMethod]
	public void ANewImportPassSeesNewlyRegisteredTypes()
	{
		var types = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		string[] files = ["data/new.custom", "data/new.custom_c"];

		Assert.AreEqual( 0, Editor.AssetType.GetCustomTypeFiles( files, types.Contains ).Count() );
		types.Add( ".custom" );
		types.Add( ".custom_c" );
		Assert.AreEqual( 1, Editor.AssetType.GetCustomTypeFiles( files, types.Contains ).Count() );
	}
}
