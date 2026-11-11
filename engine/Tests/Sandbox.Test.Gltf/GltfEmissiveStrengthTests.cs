using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfEmissiveStrengthTests
{
	private const string Extension = "KHR_materials_emissive_strength";
	private static GltfDocument Read( string json, params string[] supported ) =>
		GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string>( supported, StringComparer.Ordinal ) );

	[TestMethod]
	public void StrengthDefaultsToOneAndPreservesValuesAboveOne()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_emissive_strength"],"materials":[
			 {},{"extensions":{"KHR_materials_emissive_strength":{}}},
			 {"emissiveFactor":[0.2,0.4,0.6],"extensions":{"KHR_materials_emissive_strength":{"emissiveStrength":5}}},
			 {"extensions":{"KHR_materials_emissive_strength":{"emissiveStrength":0}}}]}
			""", Extension );
		Assert.AreEqual( 1f, document.ReadMaterialEmissiveStrength( 0 ) );
		Assert.AreEqual( 1f, document.ReadMaterialEmissiveStrength( 1 ) );
		Assert.AreEqual( 5f, document.ReadMaterialEmissiveStrength( 2 ) );
		Assert.AreEqual( 0f, document.ReadMaterialEmissiveStrength( 3 ) );
		Assert.AreEqual( new System.Numerics.Vector3( .2f, .4f, .6f ), document.ReadMaterial( 2 ).EmissiveFactor );
	}

	[TestMethod]
	public void UnsupportedOptionalUsesCoreEmissionAndRequiredStillRejects()
	{
		const string optional = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_emissive_strength"],
			 "materials":[{"extensions":{"KHR_materials_emissive_strength":{"emissiveStrength":5}}}]}
			""";
		using var document = Read( optional );
		Assert.AreEqual( 1f, document.ReadMaterialEmissiveStrength( 0 ) );
		CollectionAssert.Contains( document.UnsupportedOptionalExtensions.ToArray(), Extension );
		var required = optional.Replace( "\"materials\":", "\"extensionsRequired\":[\"KHR_materials_emissive_strength\"],\"materials\":" );
		var error = Assert.ThrowsException<GltfException>( () => Read( required ) );
		StringAssert.Contains( error.Message, "Unsupported required glTF extension" );
	}

	[DataTestMethod]
	[DataRow( "-0.01" )]
	[DataRow( "null" )]
	[DataRow( "\"5\"" )]
	[DataRow( "1e100" )]
	public void InvalidOrFloatUnrepresentableStrengthIsRejected( string value )
	{
		using var document = Read( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_materials_emissive_strength\"],\"materials\":[{\"extensions\":{\"KHR_materials_emissive_strength\":{\"emissiveStrength\":" + value + "}}}]}", Extension );
		Assert.ThrowsException<GltfException>( () => document.ReadMaterialEmissiveStrength( 0 ) );
	}

	[TestMethod]
	public void SupportedStrengthRequiresObjectDeclarationAndCannotCoexistWithUnlit()
	{
		using var badObject = Read( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_emissive_strength"],"materials":[{"extensions":{"KHR_materials_emissive_strength":null}}]}""", Extension );
		Assert.ThrowsException<GltfException>( () => badObject.ReadMaterialEmissiveStrength( 0 ) );
		using var undeclared = Read( """{"asset":{"version":"2.0"},"materials":[{"extensions":{"KHR_materials_emissive_strength":{}}}]}""", Extension );
		Assert.ThrowsException<GltfException>( () => undeclared.ReadMaterialEmissiveStrength( 0 ) );
		using var excluded = Read( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_emissive_strength","KHR_materials_unlit"],"materials":[{"extensions":{"KHR_materials_emissive_strength":{},"KHR_materials_unlit":{}}}]}""", Extension );
		Assert.ThrowsException<GltfException>( () => excluded.ReadMaterialEmissiveStrength( 0 ) );
	}
}
