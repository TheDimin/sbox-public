using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfVariantNamesTests
{
	private static GltfDocument Read( string json, bool supported = true ) =>
		GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: supported ? new HashSet<string>( StringComparer.Ordinal ) { "KHR_materials_variants" } : null );

	[TestMethod]
	public void SupportedNamesKeepSourceOrderAndDuplicates()
	{
		using var document = Read( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],"extensions":{"KHR_materials_variants":{"variants":[{"name":"red"},{"name":"blue"},{"name":"red"}]}}}""" );
		var names = document.ReadMaterialVariantNames();
		CollectionAssert.AreEqual( new[] { "red", "blue", "red" }, names.ToArray() );
		Assert.ThrowsException<NotSupportedException>( () => ((IList<string>)names)[0] = "other" );
	}

	[TestMethod]
	public void MissingOrUnsupportedOptionalExtensionExposesNoActiveCatalog()
	{
		using var missing = Read( """{"asset":{"version":"2.0"}}""" );
		Assert.AreEqual( 0, missing.ReadMaterialVariantNames().Count );
		using var unsupported = Read( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],"extensions":{"KHR_materials_variants":{"variants":[{"name":"red"}]}}}""", false );
		Assert.AreEqual( 0, unsupported.ReadMaterialVariantNames().Count );
	}

	[DataTestMethod]
	[DataRow( "null" )]
	[DataRow( "{}" )]
	[DataRow( "{\"variants\":[]}" )]
	[DataRow( "{\"variants\":[{}]}" )]
	[DataRow( "{\"variants\":[{\"name\":null}]}" )]
	public void MalformedSupportedCatalogIsRejected( string extension )
	{
		using var document = Read( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_materials_variants\"],\"extensions\":{\"KHR_materials_variants\":" + extension + "}}" );
		Assert.ThrowsException<GltfException>( () => document.ReadMaterialVariantNames() );
	}
}
