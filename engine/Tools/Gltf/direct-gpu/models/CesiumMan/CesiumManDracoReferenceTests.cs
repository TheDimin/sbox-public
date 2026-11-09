using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.CesiumMan;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Variant-local Draco bytes from a separate JS/WASM integration of the same upstream codec family.</summary>
[TestClass]
public sealed class CesiumManDracoReferenceTests
{
	private static string checkout;
	private static string referencePath;
	private static readonly IReadOnlySet<string> Extensions = new HashSet<string>( StringComparer.Ordinal ) { "KHR_draco_mesh_compression" };
	public static void Configure( string checkoutRoot, string oraclePath ) => (checkout, referencePath) = (checkoutRoot, oraclePath);

	[TestMethod]
	public void DecoderReferenceRecordsPinnedOfficialIntegrationAndFiles()
	{
		using var reference = Reference();
		var decoder = reference.RootElement.GetProperty( "decoder" );
		Assert.AreEqual( "1.5.7", decoder.GetProperty( "version" ).GetString() );
		Assert.AreEqual( "8786740086a9f4d83f44aa83badfbea4dce7a1b5", decoder.GetProperty( "commit" ).GetString() );
		Assert.AreEqual( "Apache-2.0", decoder.GetProperty( "license" ).GetString() );
		Assert.AreEqual( "8bb2952d2ba7d67e1414f8df819410cb0434a666be53f671fff75f68843d76f6", decoder.GetProperty( "js_sha256" ).GetString() );
		Assert.AreEqual( "712db3449ae2041d6e8a224c395bda6cedb49e51322fae38b7db9beb8b381889", decoder.GetProperty( "wasm_sha256" ).GetString() );
		StringAssert.Contains( decoder.GetProperty( "independence" ).GetString(), "shared codec ancestry" );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DecodedAttributeFormatsAndEveryRawComponentMatchTheSeparateIntegration( bool compiled )
	{
		using var reference = Reference();
		using var document = Document( compiled );
		var decoded = document.ReadDracoPrimitiveData( 0, 0 );
		Assert.AreEqual( reference.RootElement.GetProperty( "vertex_count" ).GetInt32(), decoded.VertexCount );
		foreach ( var expected in reference.RootElement.GetProperty( "attributes" ).EnumerateObject() )
		{
			var stream = decoded.Attributes[expected.Name];
			Assert.AreEqual( expected.Value.GetProperty( "component_type" ).GetInt32(), stream.ComponentType, expected.Name );
			Assert.AreEqual( expected.Value.GetProperty( "components" ).GetInt32(), stream.Components, expected.Name );
			Assert.AreEqual( expected.Value.GetProperty( "normalized" ).GetBoolean(), stream.Normalized, expected.Name );
			Assert.AreEqual( decoded.VertexCount, stream.Count );
			Assert.AreEqual( expected.Value.GetProperty( "raw_sha256" ).GetString(), Hash( stream.Bytes ), expected.Name );
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void EveryTriangleIndexMatchesTheDecodedVariantRatherThanCoreOrdering( bool compiled )
	{
		using var reference = Reference();
		using var document = Document( compiled );
		var decoded = document.ReadDracoPrimitiveData( 0, 0 );
		var expected = reference.RootElement.GetProperty( "indices" );
		Assert.AreEqual( expected.GetArrayLength(), decoded.Indices.Count );
		Assert.AreEqual( 5123, decoded.Indices.ComponentType );
		for ( var index = 0; index < decoded.Indices.Count; index++ )
			Assert.AreEqual( expected[index].GetUInt32(), (uint)BinaryPrimitives.ReadUInt16LittleEndian( decoded.Indices.Bytes.AsSpan( index * 2, 2 ) ), $"Index {index}" );
	}

	[TestMethod]
	public void IndependentlyDecodedWeightsAndJointIdsSatisfyCoreSkinConstraints()
	{
		using var reference = Reference();
		var attributes = reference.RootElement.GetProperty( "attributes" );
		var weights = attributes.GetProperty( "WEIGHTS_0" ).GetProperty( "values" );
		var joints = attributes.GetProperty( "JOINTS_0" ).GetProperty( "values" );
		Assert.AreEqual( weights.GetArrayLength(), joints.GetArrayLength() );
		for ( var vertex = 0; vertex < reference.RootElement.GetProperty( "vertex_count" ).GetInt32(); vertex++ )
		{
			var nonzero = new HashSet<int>();
			for ( var component = 0; component < 4; component++ )
			{
				var index = vertex * 4 + component; var weight = weights[index].GetSingle(); var joint = joints[index].GetInt32();
				Assert.IsTrue( float.IsFinite( weight ) && weight >= 0, $"Decoded source weight is invalid at vertex {vertex}, component {component}: {weight}. Investigate upstream asset; do not silently clamp." );
				Assert.IsTrue( joint is >= 0 and < 19, $"Decoded source joint index is invalid at vertex {vertex}: {joint}." );
				if ( weight != 0 ) Assert.IsTrue( nonzero.Add( joint ), $"Decoded vertex {vertex} has duplicate nonzero influence joint {joint}." );
			}
		}
	}

	[TestMethod]
	public void SourceHashAndDecodedWeightSummaryHaveNotChanged()
	{
		using var reference = Reference();
		Assert.AreEqual( reference.RootElement.GetProperty( "source" ).GetProperty( "sha256" ).GetString(), Hash( File.ReadAllBytes( SourcePath() ) ) );
		var weights = reference.RootElement.GetProperty( "attributes" ).GetProperty( "WEIGHTS_0" ).GetProperty( "values" );
		var negative = weights.EnumerateArray().Count( value => value.GetSingle() < 0 );
		Assert.AreEqual( reference.RootElement.GetProperty( "weight_summary" ).GetProperty( "negative_components" ).GetInt32(), negative );
		Assert.AreEqual( reference.RootElement.GetProperty( "vertex_count" ).GetInt32() * 4, weights.GetArrayLength() );
	}

	private static JsonDocument Reference()
	{
		if ( string.IsNullOrWhiteSpace( checkout ) || string.IsNullOrWhiteSpace( referencePath ) ) throw new InvalidOperationException( "Configure the coordinator's checkout/reference paths first." );
		var document = JsonDocument.Parse( File.ReadAllBytes( referencePath ) );
		Assert.AreEqual( 1, document.RootElement.GetProperty( "version" ).GetInt32() );
		Assert.AreEqual( "90d7ede14c7e280af263824604b427a1ca02cb66", document.RootElement.GetProperty( "pin" ).GetString() );
		return document;
	}
	private static string SourcePath() => Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/CesiumMan/glTF-Draco/CesiumMan.gltf" );
	private static GltfDocument Document( bool compiled )
	{
		var path = SourcePath();
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( path ), Uri.UnescapeDataString( uri ) ) );
		var bytes = File.ReadAllBytes( path );
		if ( compiled ) bytes = GltfCompiler.Compile( bytes, Resolve, Extensions );
		return GltfReader.Read( bytes, Resolve, Extensions );
	}
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
