using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

[TestClass]
public class RcolContainerTest
{
	[TestMethod]
	public void RcolContainer_GeomEntry_ContainsGeometryChunk()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.Geometry ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Inconclusive( "No GEOM resources found in package." );

		var rcol = package.GetResource<RcolContainer>( entry );
		Assert.IsTrue( rcol.ChunkEntries.Count > 0, "Expected RCOL to have chunk entries." );

		var geomChunk = rcol.GetChunk<GeometryRcolChunk>();
		Assert.IsNotNull( geomChunk, "Expected RCOL to contain a GeometryRcolChunk." );
	}

	[TestMethod]
	public void RcolContainer_MatdEntry_ContainsMaterialDefinitionChunk()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.MaterialDefinition ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Inconclusive( "No MATD resources found in package." );

		var rcol = package.GetResource<RcolContainer>( entry );
		Assert.IsTrue( rcol.ChunkEntries.Count > 0, "Expected RCOL to have chunk entries." );

		var matd = rcol.GetChunk<MaterialDefinition>();
		Assert.IsNotNull( matd, "Expected RCOL to contain a MaterialDefinition chunk." );
	}

	[TestMethod]
	public void MaterialDefinition_HasShaderEntries()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.MaterialDefinition ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Inconclusive( "No MATD resources found in package." );

		var rcol = package.GetResource<RcolContainer>( entry );
		var matd = rcol.GetChunk<MaterialDefinition>();
		Assert.IsNotNull( matd );
		Assert.IsTrue( matd.ShaderEntries.Count > 0,
			"Expected MaterialDefinition to have at least one shader entry." );
	}

	[TestMethod]
	public void MaterialDefinition_ShaderEntries_ContainTextureRefs()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var matdEntries = package.FindAll( ResourceType.MaterialDefinition ).Take( 10 ).ToList();
		if ( matdEntries.Count == 0 )
			Assert.Inconclusive( "No MATD resources found in package." );

		// At least one MATD should contain texture reference entries
		bool anyHasTextureRef = false;
		foreach ( var entry in matdEntries )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var matd = rcol.GetChunk<MaterialDefinition>();
			if ( matd == null ) continue;

			foreach ( var shaderEntry in matd.ShaderEntries )
			{
				if ( shaderEntry is ShaderTextureRef or ShaderTextureKey or ShaderImageMapKey )
				{
					anyHasTextureRef = true;
					break;
				}
			}
			if ( anyHasTextureRef ) break;
		}

		Assert.IsTrue( anyHasTextureRef,
			"Expected at least one MATD to have a texture reference shader entry." );
	}

	[TestMethod]
	public void RcolContainer_MultipleMatdEntries_AllParseable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.MaterialDefinition ).Take( 5 ).ToList();
		if ( entries.Count == 0 )
			Assert.Inconclusive( "No MATD resources found in package." );

		int successCount = 0;
		foreach ( var entry in entries )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var matd = rcol.GetChunk<MaterialDefinition>();
			if ( matd != null )
				successCount++;
		}

		Assert.IsTrue( successCount > 0, "At least one MATD should be parseable via RCOL." );
	}
}
