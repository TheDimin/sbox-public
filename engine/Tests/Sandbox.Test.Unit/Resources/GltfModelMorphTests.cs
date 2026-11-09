using Sandbox.Gltf;
using System;
using System.Buffers.Binary;
using System.Text;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Tests;

[TestClass]
public class GltfModelMorphTests
{
	[TestMethod]
	public void NativeTargetsRetainPositionNormalAndDuplicatedCornerMapping()
	{
		using var document = GltfReader.Read( Source() );
		var prepared = GltfModelMorphs.Prepare( document, 0, 0, 0, [2, 0, 2, 1] );
		Assert.AreEqual( 1, prepared.Targets.Count );
		var target = prepared.Targets[0];
		for ( var vertex = 0; vertex < 4; vertex++ )
		{
			var source = new[] { 2, 0, 2, 1 }[vertex];
			var positive = target.Positive.Span[vertex];
			var negative = target.Negative.Span[vertex];
			Assert.AreEqual( vertex, positive.VertexIndex );
			Assert.AreEqual( (source + 1) / .0254f, positive.PositionDelta.y, .0001f );
			Assert.AreEqual( (source + 1) / 10f, positive.NormalDelta.z, .00001f );
			Assert.AreEqual( -positive.PositionDelta, negative.PositionDelta );
			Assert.AreEqual( -positive.NormalDelta, negative.NormalDelta );
		}
	}

	[TestMethod]
	public void TargetIdentitySeparatesMeshNodeInstancesButIsStableAcrossReimports()
	{
		using var document = GltfReader.Read( Source() );
		var first = GltfModelMorphs.Prepare( document, 0, 0, 0, [0, 1, 2] ).Targets[0];
		var repeated = GltfModelMorphs.Prepare( document, 0, 0, 0, [0, 1, 2] ).Targets[0];
		var otherNode = GltfModelMorphs.Prepare( document, 1, 0, 0, [0, 1, 2] ).Targets[0];
		Assert.AreEqual( first.PositiveName, repeated.PositiveName );
		Assert.AreNotEqual( first.PositiveName, otherNode.PositiveName );
		Assert.AreNotEqual( first.NegativeName, first.PositiveName );
	}

	[TestMethod]
	public void StaticMorphsUseMeshDefaultsAndIndependentNodeOverrides()
	{
		var json = Encoding.UTF8.GetString( Source() ).Replace( "\"weights\":[0]", "\"weights\":[0.25]", StringComparison.Ordinal )
			.Replace( "\"nodes\":[{\"mesh\":0},{\"mesh\":0}]", "\"nodes\":[{\"mesh\":0},{\"mesh\":0,\"weights\":[-2]}]", StringComparison.Ordinal );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		Assert.IsTrue( GltfModelRig.RequiresRig( document ) );
		var rig = GltfModelRig.Prepare( document );
		Assert.IsTrue( rig.HasMorphs );
		Assert.AreEqual( 2, rig.Primitives.Count );
		var pose = document.EvaluatePose();
		Assert.AreEqual( .25f, pose.GetMorphWeights( 0 )[0] );
		Assert.AreEqual( -2f, pose.GetMorphWeights( 1 )[0] );
		var first = rig.Primitives[0]; var second = rig.Primitives[1];
		Assert.AreNotEqual( first.Morphs.Targets[0].PositiveName, second.Morphs.Targets[0].PositiveName );
		var prepared = GltfModelPrimitiveBuilder.Prepare( first.Data, upload: document.MaterializePrimitiveUpload( 0, 0 ), skinning: first.Skinning, morphs: first.Morphs );
		Assert.AreEqual( 3, prepared.VertexCount );
		var bounds = second.Morphs.EvaluateBounds( pose );
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			var expected = (Vector3)GltfGeometry.ConvertPosition( document.EvaluateVertexPosition( 1, 0, vertex, pose ) );
			Assert.IsTrue( bounds.Contains( expected ), "Signed and greater-than-one weights must remain inside conservative morph bounds." );
		}
	}

	[TestMethod]
	public void LocalNormalDeltasKeepTheirLinearScaleBeforeNegativeNonuniformNodeTransform()
	{
		using var document = GltfReader.Read( Source() );
		var data = document.CreateDrawData( 0, 0 );
		var targets = GltfModelMorphs.Prepare( document, 0, 0, 0, [0, 1, 2] );
		var matrix = NMatrix.CreateScale( -2, 3, .5f ) * NMatrix.CreateRotationZ( .4f ) * NMatrix.CreateTranslation( 5, 7, 11 );
		Assert.IsTrue( NMatrix.Invert( matrix, out var inverse ) );
		var normalMatrix = NMatrix.Transpose( inverse );
		var expected = GltfGeometry.ConvertDirection( NVector.Normalize( NVector.TransformNormal( new NVector( 0, .2f, 1 ), normalMatrix ) ) );
		var converted = GltfGeometry.ConvertTransform( matrix );
		Assert.IsTrue( NMatrix.Invert( converted, out var convertedInverse ) );
		var convertedNormal = NMatrix.Transpose( convertedInverse );
		var delta = (NVector)targets.Targets[0].Positive.Span[0].NormalDelta;
		var actual = NVector.Normalize( NVector.TransformNormal( data.Normals.Span[0] + delta * 2, convertedNormal ) );
		Assert.IsTrue( NVector.Distance( expected, actual ) < .00001f );
		var wrong = NVector.Normalize( NVector.Normalize( NVector.TransformNormal( data.Normals.Span[0], convertedNormal ) ) + NVector.TransformNormal( delta, convertedNormal ) * 2 );
		Assert.IsTrue( NVector.Distance( expected, wrong ) > .01f, "Fixture must distinguish separately normalized baked bases from source-local morphing." );
		var positionDelta = (NVector)targets.Targets[0].Positive.Span[0].PositionDelta;
		var expectedDelta = GltfGeometry.ConvertPosition( NVector.TransformNormal( new NVector( 1, 0, 0 ), matrix ) );
		Assert.IsTrue( NVector.Distance( expectedDelta, NVector.TransformNormal( positionDelta, converted ) ) < .0001f, "Position deltas are vectors; translation must not be applied." );
	}

	[TestMethod]
	[DataRow( -2f, 0f, 2f )]
	[DataRow( -.5f, 0f, .5f )]
	[DataRow( 0f, 0f, 0f )]
	[DataRow( .75f, .75f, 0f )]
	[DataRow( 2f, 2f, 0f )]
	public void SignedWeightsPreserveTheirFullRange( float weight, float positive, float negative )
	{
		var split = GltfModelMorphs.SplitWeight( weight );
		Assert.AreEqual( positive, split.Positive );
		Assert.AreEqual( negative, split.Negative );
		Assert.AreEqual( weight, split.Positive - split.Negative );
	}

	[TestMethod]
	public void InvalidWeightsAndUnmappedOrUnsupportedDeltasAreNeverSilentlyDropped()
	{
		Assert.ThrowsException<GltfException>( () => GltfModelMorphs.SplitWeight( float.NaN ) );
		Assert.ThrowsException<GltfException>( () => GltfModelMorphs.SplitWeight( float.PositiveInfinity ) );
		using var document = GltfReader.Read( Source() );
		Assert.ThrowsException<GltfException>( () => GltfModelMorphs.Prepare( document, 0, 0, 0, [0, 1, 3] ) );
		using var tangents = GltfReader.Read( Source( tangent: true ) );
		var error = Assert.ThrowsException<GltfException>( () => GltfModelMorphs.Prepare( tangents, 0, 0, 0, [0, 1, 2] ) );
		StringAssert.Contains( error.Message, "tangent morph" );
	}

	private static byte[] Source( bool tangent = false )
	{
		var bytes = new byte[tangent ? 228 : 180];
		void Float( int at, float value ) => BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at, 4 ), value );
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			Float( vertex * 12, vertex == 1 ? 1 : 0 ); Float( vertex * 12 + 4, vertex == 2 ? 1 : 0 );
			Float( 36 + vertex * 12 + 8, 1 );
			Float( 72 + vertex * 12, vertex + 1 ); Float( 108 + vertex * 12 + 4, (vertex + 1) / 10f );
			Float( 144 + vertex * 12 + 8, .25f );
			if ( tangent ) { Float( 180 + vertex * 16, 1 ); Float( 180 + vertex * 16 + 12, 1 ); }
		}
		var target = tangent ? ",\"TANGENT\":4" : "";
		var json = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0,1]}],\"nodes\":[{\"mesh\":0},{\"mesh\":0}],\"buffers\":[{\"byteLength\":180,\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( bytes ) + "\"}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":72,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":108,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":144,\"byteLength\":36}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},{\"bufferView\":1,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},{\"bufferView\":2,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[1,0,0],\"max\":[3,0,0]},{\"bufferView\":3,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"},{\"bufferView\":4,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}],\"meshes\":[{\"weights\":[0],\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1},\"targets\":[{\"POSITION\":2,\"NORMAL\":3" + target + "}]}]}]}" );
		if ( !tangent ) return json;
		var root = System.Text.Json.Nodes.JsonNode.Parse( json );
		root["buffers"][0]["byteLength"] = bytes.Length;
		root["bufferViews"].AsArray().Add( new System.Text.Json.Nodes.JsonObject { ["buffer"] = 0, ["byteOffset"] = 180, ["byteLength"] = 48 } );
		root["accessors"].AsArray().Add( new System.Text.Json.Nodes.JsonObject { ["bufferView"] = 5, ["componentType"] = 5126, ["count"] = 3, ["type"] = "VEC4" } );
		root["meshes"][0]["primitives"][0]["attributes"]["TANGENT"] = 5;
		return Encoding.UTF8.GetBytes( root.ToJsonString() );
	}
}
