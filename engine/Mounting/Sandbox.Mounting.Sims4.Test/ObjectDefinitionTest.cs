using Sims4Reader;
using Sims4Reader.Resources;

namespace Sims4MountTest;

[TestClass]
public class ObjectDefinitionTest
{
	[TestMethod]
	public void ObjectDefinition_Parses()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.ObjectDefinition ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Fail( "No OBJD resources found in package." );

		var objd = package.GetResource<ObjectDefinitionResource>( entry );
		Assert.IsNotNull( objd, "Expected OBJD to parse successfully." );
		Assert.IsTrue( objd.Version > 0, "Expected OBJD version > 0." );
	}

	[TestMethod]
	public void ObjectDefinition_MultipleParseable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.ObjectDefinition ).Take( 20 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No OBJD resources found in package." );

		int successCount = 0;
		foreach ( var entry in entries )
		{
			var objd = package.GetResource<ObjectDefinitionResource>( entry );
			if ( objd != null && objd.Version > 0 )
				successCount++;
		}

		Assert.IsTrue( successCount > 0, "At least one OBJD should be parseable." );
		Assert.AreEqual( entries.Count, successCount,
			"All OBJD entries should parse the header." );
	}

	[TestMethod]
	public void ObjectDefinition_HasModelReferences()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.ObjectDefinition ).Take( 50 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No OBJD resources found in package." );

		bool anyHasModels = false;
		foreach ( var entry in entries )
		{
			var objd = package.GetResource<ObjectDefinitionResource>( entry );
			if ( objd.IsFullyParsed && objd.Models.Length > 0 )
			{
				anyHasModels = true;
				break;
			}
		}

		if ( !entries.Any( e => package.GetResource<ObjectDefinitionResource>( e ).IsFullyParsed ) )
			Assert.Fail( "No OBJD entries were fully parsed." );

		Assert.IsTrue( anyHasModels, "Expected at least one OBJD to have Model references." );
	}

	[TestMethod]
	public void ObjectDefinition_ModelReferences_AreModlType()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.ObjectDefinition ).Take( 50 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No OBJD resources found in package." );

		bool foundModlRef = false;
		foreach ( var entry in entries )
		{
			var objd = package.GetResource<ObjectDefinitionResource>( entry );
			if ( !objd.IsFullyParsed || objd.Models.Length == 0 )
				continue;

			foreach ( var modelKey in objd.Models )
			{
				// Skip null/placeholder TGI entries (type=0, instance=0)
				if ( modelKey.Instance == 0 && (uint)modelKey.Type == 0 )
					continue;

				// Non-null Model references should be MODL type (0x01661233)
				Assert.AreEqual( ResourceType.Model, modelKey.Type,
					$"OBJD {entry.Key} Model reference has wrong type: 0x{(uint)modelKey.Type:X8}" );
				foundModlRef = true;
			}
		}

		if ( !foundModlRef )
			Assert.Fail( "No OBJD entries with non-null Model references found." );
	}

	[TestMethod]
	public void ObjectDefinition_HasPropertyTable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.ObjectDefinition ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No OBJD resources found in package." );

		bool anyHasProperties = false;
		foreach ( var entry in entries )
		{
			var objd = package.GetResource<ObjectDefinitionResource>( entry );
			if ( objd.IsFullyParsed && objd.PresentProperties.Count > 0 )
			{
				anyHasProperties = true;
				break;
			}
		}

		if ( !entries.Any( e => package.GetResource<ObjectDefinitionResource>( e ).IsFullyParsed ) )
			Assert.Fail( "No OBJD entries were fully parsed." );

		Assert.IsTrue( anyHasProperties,
			"Expected at least one fully-parsed OBJD to have properties in its table." );
	}
}
