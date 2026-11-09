using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfDeformationTests
{
	[TestMethod]
	public void UnskinnedVertexUsesItsCompleteWorldTransform()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, new[] { 1f, 2f, 3f }, true );
		using var document = fixture.Read( $$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}} }}]}],
			"nodes":[{"translation":[10,0,0],"children":[1]},{"mesh":0,"scale":[2,3,4]}]
			""" );
		AssertVector( new Vector3( 12, 6, 12 ), document.EvaluateVertexPosition( 1, 0, 0 ) );
	}

	[TestMethod]
	public void SkinAppliesInverseBindBeforeJointWorldAndIgnoresMeshNodeTransform()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, new[] { 2f, 0f, 0f }, true );
		var joints = fixture.Bytes( "VEC4", 1, new byte[] { 0, 0, 0, 0 } );
		var weights = fixture.Floats( "VEC4", 1, new[] { 1f, 0f, 0f, 0f } );
		var inverseBind = fixture.Floats( "MAT4", 1, new[] { 1f, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 1 } );
		using var document = fixture.Read( $$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}},"JOINTS_0":{{{joints}}},"WEIGHTS_0":{{{weights}}} }}]}],
			"nodes":[{"mesh":0,"skin":0,"translation":[100,0,0]},
			         {"translation":[0,3,0],"rotation":[0,0,0.7071067811865475,0.7071067811865475]}],
			"skins":[{"joints":[1],"inverseBindMatrices":{{{inverseBind}}} }]
			""" );
		AssertVector( new Vector3( 0, 4, 0 ), document.EvaluateVertexPosition( 0, 0, 0 ) );
	}

	[TestMethod]
	public void SkinAccumulatesInfluencesBeyondTheFirstFour()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, new[] { 1f, 0f, 0f }, true );
		var joints0 = fixture.Bytes( "VEC4", 1, new byte[] { 0, 1, 2, 3 } );
		var weights0 = fixture.Floats( "VEC4", 1, new[] { 0.125f, 0.125f, 0.125f, 0.125f } );
		var joints1 = fixture.Bytes( "VEC4", 1, new byte[] { 4, 0, 0, 0 } );
		var weights1 = fixture.Floats( "VEC4", 1, new[] { 0.5f, 0f, 0f, 0f } );
		using var document = fixture.Read( $$$"""
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}},"JOINTS_0":{{{joints0}}},"WEIGHTS_0":{{{weights0}}},"JOINTS_1":{{{joints1}}},"WEIGHTS_1":{{{weights1}}} }}]}],
			"nodes":[{"mesh":0,"skin":0},{},{"translation":[2,0,0]},{"translation":[4,0,0]},
			         {"translation":[6,0,0]},{"translation":[8,0,0]},{"children":[1,2,3,4,5]}],
			"skins":[{"joints":[1,2,3,4,5]}]
			""" );
		AssertVector( new Vector3( 6.5f, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0 ) );
	}

	[TestMethod]
	public void NodeMorphWeightsOverrideMeshWeightsAndMissingPositionDeltaIsZero()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, new[] { 1f, 2f, 3f }, true );
		var normal = fixture.Floats( "VEC3", 1, new[] { 0f, 0f, 1f } );
		var delta = fixture.Floats( "VEC3", 1, new[] { 4f, 0f, 0f }, true );
		var normalDelta = fixture.Floats( "VEC3", 1, new[] { 0f, 1f, 0f } );
		using var document = fixture.Read( $$$"""
			"meshes":[{"weights":[0.25,1],"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}},"NORMAL":{{{normal}}} },"targets":[{"POSITION":{{{delta}}} },{"NORMAL":{{{normalDelta}}} }]}]}],
			"nodes":[{"mesh":0},{"mesh":0,"weights":[0.5,1]}]
			""" );
		AssertVector( new Vector3( 2, 2, 3 ), document.EvaluateVertexPosition( 0, 0, 0 ) );
		AssertVector( new Vector3( 3, 2, 3 ), document.EvaluateVertexPosition( 1, 0, 0 ) );
	}

	[TestMethod]
	public void MorphPositionIsAppliedBeforeSkinning()
	{
		var fixture = new DeformationFixture();
		var position = fixture.Floats( "VEC3", 1, new[] { 1f, 0f, 0f }, true );
		var delta = fixture.Floats( "VEC3", 1, new[] { 2f, 0f, 0f }, true );
		var joints = fixture.Bytes( "VEC4", 1, new byte[] { 0, 0, 0, 0 } );
		var weights = fixture.Floats( "VEC4", 1, new[] { 1f, 0f, 0f, 0f } );
		using var document = fixture.Read( $$$"""
			"meshes":[{"weights":[0.5],"primitives":[{"mode":0,"attributes":{"POSITION":{{{position}}},"JOINTS_0":{{{joints}}},"WEIGHTS_0":{{{weights}}} },"targets":[{"POSITION":{{{delta}}} }]}]}],
			"nodes":[{"mesh":0,"skin":0},{"translation":[3,0,0],"scale":[2,1,1]}],
			"skins":[{"joints":[1]}]
			""" );
		AssertVector( new Vector3( 7, 0, 0 ), document.EvaluateVertexPosition( 0, 0, 0 ) );
	}

	private static void AssertVector( Vector3 expected, Vector3 actual )
	{
		Assert.AreEqual( expected.X, actual.X, 0.00001f );
		Assert.AreEqual( expected.Y, actual.Y, 0.00001f );
		Assert.AreEqual( expected.Z, actual.Z, 0.00001f );
	}
}

internal sealed class DeformationFixture
{
	private readonly MemoryStream bytes = new();
	private readonly List<string> views = new();
	private readonly List<string> accessors = new();

	public int Floats( string type, int count, float[] values, bool includeBounds = false )
	{
		var data = new byte[values.Length * sizeof( float )];
		for ( var i = 0; i < values.Length; i++ )
			BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( i * sizeof( float ), sizeof( float ) ), values[i] );
		var bounds = "";
		if ( includeBounds )
		{
			var components = values.Length / count;
			var minimum = new float[components]; var maximum = new float[components];
			Array.Fill( minimum, float.PositiveInfinity ); Array.Fill( maximum, float.NegativeInfinity );
			for ( var i = 0; i < values.Length; i++ )
			{
				var component = i % components;
				minimum[component] = MathF.Min( minimum[component], values[i] );
				maximum[component] = MathF.Max( maximum[component], values[i] );
			}
			bounds = $",\"min\":{JsonSerializer.Serialize( minimum )},\"max\":{JsonSerializer.Serialize( maximum )}";
		}
		return Add( type, count, 5126, data, bounds );
	}

	public int Bytes( string type, int count, byte[] values ) => Add( type, count, 5121, values, "" );

	private int Add( string type, int count, int componentType, byte[] data, string extra )
	{
		while ( bytes.Length % 4 != 0 ) bytes.WriteByte( 0 );
		var offset = bytes.Length;
		bytes.Write( data );
		var view = views.Count;
		views.Add( $$$"""{"buffer":0,"byteOffset":{{{offset}}},"byteLength":{{{data.Length}}} }""" );
		var accessor = accessors.Count;
		accessors.Add( $$$"""{"bufferView":{{{view}}},"componentType":{{{componentType}}},"count":{{{count}}},"type":"{{{type}}}"{{{extra}}} }""" );
		return accessor;
	}

	public GltfDocument Read( string fields )
	{
		var jsonFields = "\"bufferViews\":[" + string.Join( ",", views ) + "],\"accessors\":[" + string.Join( ",", accessors ) + "]," + fields;
		return SceneFeatureFixture.ReadBytes( bytes.ToArray(), jsonFields );
	}
}
