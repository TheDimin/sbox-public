using Editor;

namespace EditorTests;

[TestClass]
public class ResourceReferenceTypeTests
{
	[TestMethod]
	public void ReferenceLookupPreservesExtensionRulesAndFirstRegisteredType()
	{
		var first = new AssetType { FileExtension = "prefab" };
		var duplicate = new AssetType { FileExtension = "PREFAB" };
		var lookup = ResourceCompileContextImp.CreateReferenceTypes( [first, duplicate] );
		Assert.AreSame( first, ResourceCompileContextImp.FindReferenceType( ".Prefab", lookup ) );
		Assert.AreSame( first, ResourceCompileContextImp.FindReferenceType( ".prefab_c", lookup ) );
		Assert.IsNull( ResourceCompileContextImp.FindReferenceType( ".prefab_C", lookup ) );
		Assert.IsNull( ResourceCompileContextImp.FindReferenceType( ".01", lookup ) );
	}

	[TestMethod]
	public void ANewScanRecognizesTypesRegisteredAfterAnEarlierMiss()
	{
		var earlier = ResourceCompileContextImp.CreateReferenceTypes( [] );
		Assert.IsNull( ResourceCompileContextImp.FindReferenceType( ".custom", earlier ) );
		var added = new AssetType { FileExtension = "custom" };
		var later = ResourceCompileContextImp.CreateReferenceTypes( [added] );
		Assert.AreSame( added, ResourceCompileContextImp.FindReferenceType( ".custom", later ) );
		Assert.IsNull( ResourceCompileContextImp.FindReferenceType( ".custom", earlier ) );
	}
}
