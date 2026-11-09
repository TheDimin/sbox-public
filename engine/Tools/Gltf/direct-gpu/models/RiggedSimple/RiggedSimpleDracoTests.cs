using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.Tests;

using Assert = global::Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Vector3 = global::System.Numerics.Vector3;

/// <summary>
/// Draco-variant order and poses from the separately integrated official WASM
/// decoder. It shares codec ancestry with production Draco; no core point
/// correspondence is assumed, and no engine/GPU claim comes from authoring it.
/// </summary>
[TestClass]
public sealed class RiggedSimpleDracoTests
{
	private static string checkout;
	private static string oraclePath;
	private static readonly IReadOnlySet<string> Extensions = new HashSet<string>( StringComparer.Ordinal ) { "KHR_draco_mesh_compression" };
	public static void Configure( string checkoutRoot, string decodedOraclePath ) => (checkout, oraclePath) = (checkoutRoot, decodedOraclePath);
	private static string Source => Path.Combine( checkout ?? throw new InvalidOperationException( "Configure the model checks first." ),
		"Temp/gltf-reference/glTF-Sample-Assets/Models/RiggedSimple/glTF-Draco/RiggedSimple.gltf" );

	[TestMethod]
	public void OfficialDecoderAndSourceIdentityArePinnedAndCodecAncestryIsExplicit()
	{
		using var oracle = Oracle(); var root = oracle.RootElement;
		Assert.AreEqual( "90d7ede14c7e280af263824604b427a1ca02cb66", root.GetProperty( "pin" ).GetString() );
		Assert.AreEqual( "Models/RiggedSimple/glTF-Draco/RiggedSimple.gltf", root.GetProperty( "source" ).GetProperty( "relative" ).GetString() );
		Assert.AreEqual( Hash( File.ReadAllBytes( Source ) ), root.GetProperty( "source" ).GetProperty( "sha256" ).GetString() );
		var decoder = root.GetProperty( "decoder" );
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
	public void EveryDecodedRawAttributeMatchesTheVariantLocalSeparateIntegration( bool compiled )
	{
		using var oracle = Oracle(); using var actual = Document( compiled );
		var expected = oracle.RootElement; var primitive = actual.ReadDracoPrimitiveData( 0, 0 );
		Assert.AreEqual( expected.GetProperty( "vertex_count" ).GetInt32(), primitive.VertexCount );
		foreach ( var attribute in expected.GetProperty( "attributes" ).EnumerateObject() )
		{
			var stream = primitive.Attributes[attribute.Name]; var source = attribute.Value;
			Assert.AreEqual( source.GetProperty( "component_type" ).GetInt32(), stream.ComponentType, attribute.Name );
			Assert.AreEqual( source.GetProperty( "components" ).GetInt32(), stream.Components, attribute.Name );
			Assert.AreEqual( source.GetProperty( "normalized" ).GetBoolean(), stream.Normalized, attribute.Name );
			Assert.AreEqual( source.GetProperty( "count" ).GetInt32(), stream.Count, attribute.Name );
			Assert.AreEqual( source.GetProperty( "raw_sha256" ).GetString(), Hash( stream.Bytes ), attribute.Name );
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void EveryDecodedTriangleIndexMatchesItsOwnWasmPointOrder( bool compiled )
	{
		using var oracle = Oracle(); using var actual = Document( compiled );
		var expected = oracle.RootElement.GetProperty( "indices" ); var stream = actual.ReadDracoPrimitiveData( 0, 0 ).Indices;
		Assert.AreEqual( 5123, stream.ComponentType ); Assert.AreEqual( expected.GetArrayLength(), stream.Count );
		for ( var index = 0; index < stream.Count; index++ )
			Assert.AreEqual( expected[index].GetUInt32(), (uint)BinaryPrimitives.ReadUInt16LittleEndian( stream.Bytes.AsSpan( index * stream.ByteStride, 2 ) ), "triangle component " + index );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void EveryVariantLocalVertexMatchesIndependentSkinAtKeysAndBetweenKeys( bool compiled )
	{
		using var reference = RiggedSimpleReference.ReadDraco( Source, oraclePath );
		using var actual = Document( compiled );
		var times = new float?[] { null, 0, reference.KeyTimes[0], reference.KeyTimes[12],
			(reference.KeyTimes[24] + reference.KeyTimes[25]) * 0.5f, reference.KeyTimes[25], reference.KeyTimes[^1], reference.KeyTimes[^1] + 0.25f };
		foreach ( var time in times )
		{
			var expected = reference.Evaluate( time ); var pose = actual.EvaluatePose( time.HasValue ? 0 : null, time.GetValueOrDefault() );
			for ( var vertex = 0; vertex < reference.VertexCount; vertex++ )
			{
				var value = actual.EvaluateVertexPosition( reference.MeshNode, 0, vertex, pose );
				var scale = MathF.Max( 1, expected.Positions[vertex].Length() );
				Assert.IsTrue( Vector3.Distance( value, expected.Positions[vertex] ) <= 32 * 1.1920929e-7f * scale,
					$"compiled={compiled} time={time} vertex={vertex} expected={expected.Positions[vertex]} actual={value}" );
			}
		}
	}

	[TestMethod]
	public void DecodedVariantRetainsAllAuthoredWeightsWithoutClampingOrRenormalization()
	{
		using var oracle = Oracle(); var root = oracle.RootElement;
		var attributes = root.GetProperty( "attributes" );
		var weights = attributes.GetProperty( "WEIGHTS_0" ).GetProperty( "values" );
		var joints = attributes.GetProperty( "JOINTS_0" ).GetProperty( "values" );
		Assert.AreEqual( root.GetProperty( "vertex_count" ).GetInt32() * 4, weights.GetArrayLength() );
		Assert.AreEqual( weights.GetArrayLength(), joints.GetArrayLength() );
		for ( var vertex = 0; vertex < root.GetProperty( "vertex_count" ).GetInt32(); vertex++ )
		{
			var used = new HashSet<int>();
			for ( var component = 0; component < 4; component++ )
			{
				var at = vertex * 4 + component; var weight = weights[at].GetSingle(); var joint = joints[at].GetInt32();
				Assert.IsTrue( float.IsFinite( weight ) && weight >= 0, $"Actual decoded weight {weight} is invalid; report source/integration evidence, do not clamp it." );
				Assert.IsTrue( joint is >= 0 and < 2, "Decoded joint escapes this variant's actual skin." );
				if ( weight != 0 ) Assert.IsTrue( used.Add( joint ), "Decoded source has duplicate nonzero joint influence." );
			}
		}
	}

	private static JsonDocument Oracle()
	{
		if ( string.IsNullOrWhiteSpace( oraclePath ) ) throw new InvalidOperationException( "The coordinator must configure the independently authored decode file." );
		return JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
	}
	private static GltfDocument Document( bool compiled )
	{
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( Source ), Uri.UnescapeDataString( uri ) ) );
		var bytes = File.ReadAllBytes( Source );
		if ( compiled ) bytes = GltfCompiler.Compile( bytes, Resolve, Extensions );
		return GltfReader.Read( bytes, Resolve, Extensions );
	}
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
