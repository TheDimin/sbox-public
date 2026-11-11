using System;
using System.Collections.Generic;
using System.Linq;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfStaticSceneAssemblyTests
{
	[TestMethod]
	public void DefaultSceneIncludesEveryPrimitiveAndRepeatedNodeButNotOtherScenes()
	{
		using var document = Fixture( "[ {\"nodes\":[0,2,4] }, {\"nodes\":[3]} ]", "[ {\"translation\":[1,0,0],\"children\":[1]}, {\"mesh\":0,\"translation\":[0,2,0]}, {\"mesh\":1,\"translation\":[0,0,3]}, {\"mesh\":0}, {\"mesh\":0} ]" );
		var result = GltfStaticSceneAssembler.Assemble( document );
		Assert.AreEqual( 6, result.Primitives.Count );
		CollectionAssert.AreEqual( new[] { 1, 1, 2, 2, 4, 4 }, result.Primitives.Select( x => x.Node ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 1, 0, 1, 0, 1 }, result.Primitives.Select( x => x.Primitive ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 0, 1, 1, 0, 0 }, result.Primitives.Select( x => x.Mesh ).ToArray() );
		CollectionAssert.AreEqual( new int?[] { 0, 1, 0, 1, 0, 1 }, result.Primitives.Select( x => x.Data.Material ).ToArray() );
		AssertVector( new NVector3( 0, 1, 2 ) / 0.0254f, result.Primitives[0].Data.Positions.Span[0] );
		AssertVector( new NVector3( 3, 0, 0 ) / 0.0254f, result.Primitives[2].Data.Positions.Span[0] );
		Assert.AreEqual( new NVector2( 0.25f, 0.75f ), result.Primitives[0].Data.TexCoords[2].Span[0] );
		AssertVector( NVector3.Zero, result.BoundsMin );
		AssertVector( new NVector3( 3, 2, 3 ) / 0.0254f, result.BoundsMax );
	}

	[TestMethod]
	public void MirroredNonuniformScaleTransformsNormalsTangentsAndWinding()
	{
		using var document = Fixture( "[{\"nodes\":[0]}]", "[{\"mesh\":0,\"scale\":[-2,3,4]}]" );
		var primitive = GltfStaticSceneAssembler.Assemble( document ).Primitives[0].Data;
		CollectionAssert.AreEqual( new uint[] { 0, 2, 1 }, primitive.Indices.ToArray() );
		var normal = NVector3.Normalize( new NVector3( 0, -0.5f, 1f / 3f ) );
		AssertVector( normal, primitive.Normals.Span[0] );
		var tangent = primitive.Tangents.Span[0];
		AssertVector( NVector3.Normalize( new NVector3( 0, -2, -3 ) ), new NVector3( tangent.X, tangent.Y, tangent.Z ) );
		Assert.AreEqual( -1f, tangent.W );
		Assert.AreEqual( 0f, NVector3.Dot( normal, new NVector3( tangent.X, tangent.Y, tangent.Z ) ), 0.00001f );
	}

	[TestMethod]
	public void MissingDefaultSceneFailsClearly()
	{
		using var missing = Fixture( "[{\"nodes\":[0]}]", "[{\"mesh\":0}]", includeDefault: false );
		Assert.ThrowsException<GltfException>( () => GltfStaticSceneAssembler.Assemble( missing ) );
	}

	[TestMethod]
	public void AllZeroScaleStillProducesFiniteTranslatedPositionsAndBounds()
	{
		// glTF permits singular transforms. The normal direction of collapsed
		// geometry need not be defined; positions and bounds remain well defined.
		using var collapsed = Fixture( "[{\"nodes\":[0]}]", "[{\"mesh\":0,\"translation\":[1,2,3],\"scale\":[0,0,0]}]" );
		var allZero = GltfStaticSceneAssembler.Assemble( collapsed );
		var origin = new NVector3( 3, 1, 2 ) / 0.0254f;
		Assert.IsTrue( allZero.HasBounds );
		Assert.AreEqual( 2, allZero.Primitives.Count );
		foreach ( var primitive in allZero.Primitives )
			foreach ( var position in primitive.Data.Positions.Span )
			{
				Assert.IsTrue( float.IsFinite( position.X ) && float.IsFinite( position.Y ) && float.IsFinite( position.Z ) );
				AssertVector( origin, position );
			}
		AssertVector( origin, allZero.BoundsMin );
		AssertVector( origin, allZero.BoundsMax );
		AssertFiniteFrames( allZero );
	}

	[TestMethod]
	public void RankTwoScaleStillProducesFiniteTransformedPositionsAndBounds()
	{
		using var flattened = Fixture( "[{\"nodes\":[0]}]", "[{\"mesh\":0,\"translation\":[1,2,3],\"scale\":[0,2,3]}]" );
		var rankTwo = GltfStaticSceneAssembler.Assemble( flattened );
		var origin = new NVector3( 3, 1, 2 ) / 0.0254f;
		var expected = new[] { origin, origin, new NVector3( 3, 1, 4 ) / 0.0254f };
		Assert.IsTrue( rankTwo.HasBounds );
		Assert.AreEqual( 2, rankTwo.Primitives.Count );
		foreach ( var primitive in rankTwo.Primitives )
		{
			Assert.AreEqual( expected.Length, primitive.Data.Positions.Length );
			for ( var i = 0; i < expected.Length; i++ )
			{
				var position = primitive.Data.Positions.Span[i];
				Assert.IsTrue( float.IsFinite( position.X ) && float.IsFinite( position.Y ) && float.IsFinite( position.Z ) );
				AssertVector( expected[i], position );
			}
		}
		AssertVector( origin, rankTwo.BoundsMin );
		AssertVector( expected[2], rankTwo.BoundsMax );
		AssertFiniteFrames( rankTwo );
	}

	[TestMethod]
	public void RankTwoMirroredTransformPreservesSurvivingSurfaceFrame()
	{
		using var document = Fixture( "[{\"nodes\":[0]}]", "[{\"mesh\":0,\"scale\":[-2,3,0],\"rotation\":[0,0.70710677,0,0.70710677]}]",
			normalValues: [0, 0, 1, 0, 0, 1, 0, 0, 1], tangentValues: [1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1] );
		var scene = GltfStaticSceneAssembler.Assemble( document );
		var primitive = scene.Primitives[0].Data;
		var positions = primitive.Positions.Span;
		var normal = NVector3.Normalize( NVector3.Cross( positions[1] - positions[0], positions[2] - positions[0] ) );
		var tangent = NVector3.Normalize( positions[1] - positions[0] );

		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, primitive.Indices.ToArray() );
		for ( var i = 0; i < positions.Length; i++ )
		{
			AssertVector( normal, primitive.Normals.Span[i] );
			var frame = primitive.Tangents.Span[i];
			AssertVector( tangent, new NVector3( frame.X, frame.Y, frame.Z ) );
			Assert.AreEqual( 1f, frame.W );
		}
		AssertFiniteFrames( scene );
	}

	[TestMethod]
	public void SelectedSkinOrAnimationFailsButUnselectedAnimationDoesNot()
	{
		using var skin = Fixture( "[{\"nodes\":[0,1]}]", "[{\"mesh\":0,\"skin\":0},{\"name\":\"joint\"}]", skin: true );
		Assert.ThrowsException<GltfException>( () => GltfStaticSceneAssembler.Assemble( skin ) );
		using var animated = Fixture( "[{\"nodes\":[0]}]", "[{\"mesh\":0},{\"mesh\":0}]", animationTarget: 0 );
		Assert.ThrowsException<GltfException>( () => GltfStaticSceneAssembler.Assemble( animated ) );
		using var other = Fixture( "[{\"nodes\":[0]}]", "[{\"mesh\":0},{\"mesh\":0}]", animationTarget: 1 );
		Assert.AreEqual( 2, GltfStaticSceneAssembler.Assemble( other ).Primitives.Count );
	}

	[TestMethod]
	public void MorphAndInstancingAreNotSilentlyDropped()
	{
		using var morph = Fixture( "[{\"nodes\":[0]}]", "[{\"mesh\":0}]", morph: true );
		Assert.ThrowsException<GltfException>( () => GltfStaticSceneAssembler.Assemble( morph ) );
		using var instanced = Fixture( "[{\"nodes\":[0]}]", "[{\"mesh\":0,\"extensions\":{\"EXT_mesh_gpu_instancing\":{\"attributes\":{\"TRANSLATION\":8}}}}]", instancing: true );
		Assert.ThrowsException<GltfException>( () => GltfStaticSceneAssembler.Assemble( instanced ) );
	}

	private static void AssertVector( NVector3 expected, NVector3 actual ) => Assert.IsTrue( NVector3.Distance( expected, actual ) < 0.0001f, $"Expected {expected}, got {actual}." );

	private static void AssertFiniteFrames( GltfStaticScene scene )
	{
		foreach ( var primitive in scene.Primitives )
		{
			foreach ( var normal in primitive.Data.Normals.Span )
				Assert.AreEqual( 1f, normal.Length(), 0.00001f );
			for ( var i = 0; i < primitive.Data.Tangents.Length; i++ )
			{
				var frame = primitive.Data.Tangents.Span[i];
				var tangent = new NVector3( frame.X, frame.Y, frame.Z );
				Assert.AreEqual( 1f, tangent.Length(), 0.00001f );
				Assert.AreEqual( 1f, frame.W );
				if ( primitive.Data.Normals.Length != 0 )
					Assert.AreEqual( 0f, NVector3.Dot( primitive.Data.Normals.Span[i], tangent ), 0.00001f );
			}
		}
	}

	private static GltfDocument Fixture( string scenes, string nodes, bool includeDefault = true, int? animationTarget = null, bool morph = false, bool skin = false, bool instancing = false, float[] normalValues = null, float[] tangentValues = null )
	{
		var unit = MathF.Sqrt( 0.5f );
		float[] position = [0, 0, 0, 1, 0, 0, 0, 1, 0];
		float[] normal = normalValues ?? [unit, unit, 0, unit, unit, 0, unit, unit, 0];
		float[] tangent = tangentValues ?? [unit, -unit, 0, 1, unit, -unit, 0, 1, unit, -unit, 0, 1];
		float[] uv = [0.25f, 0.75f, 1, 0, 0, 1];
		float[] weights = [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0];
		float[] times = [0, 1];
		float[] translations = [0, 0, 0, 1, 0, 0];
		float[] instanceTranslation = [0, 0, 0];
		var data = position.Concat( normal ).Concat( tangent ).Concat( uv ).SelectMany( BitConverter.GetBytes )
			.Concat( new byte[12] ).Concat( weights.Concat( times ).Concat( translations ).Concat( instanceTranslation ).SelectMany( BitConverter.GetBytes ) ).ToArray();
		var animations = animationTarget.HasValue ? $",\"animations\":[{{\"samplers\":[{{\"input\":6,\"output\":7}}],\"channels\":[{{\"sampler\":0,\"target\":{{\"node\":{animationTarget},\"path\":\"translation\"}}}}]}}]" : "";
		var json = """
			{"asset":{"version":"2.0"},EXT_DECL SCENE_DECL"scenes":SCENES_ARRAY,"nodes":NODES_ARRAY,
			"buffers":[{"byteLength":DATA_LENGTH,"uri":"data:application/octet-stream;base64,DATA_BASE64"}],
			"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":48},{"buffer":0,"byteOffset":120,"byteLength":24},{"buffer":0,"byteOffset":144,"byteLength":12},{"buffer":0,"byteOffset":156,"byteLength":48},{"buffer":0,"byteOffset":204,"byteLength":8},{"buffer":0,"byteOffset":212,"byteLength":24},{"buffer":0,"byteOffset":236,"byteLength":12}],
			"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]}, {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"}, {"bufferView":2,"componentType":5126,"count":3,"type":"VEC4"}, {"bufferView":3,"componentType":5126,"count":3,"type":"VEC2"}, {"bufferView":4,"componentType":5121,"count":3,"type":"VEC4"},{"bufferView":5,"componentType":5126,"count":3,"type":"VEC4"},{"bufferView":6,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[1]},{"bufferView":7,"componentType":5126,"count":2,"type":"VEC3"},{"bufferView":8,"componentType":5126,"count":1,"type":"VEC3"}],
			"materials":[{},{}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TANGENT":2,"TEXCOORD_0":3,"TEXCOORD_1":3,"TEXCOORD_2":3 SKIN_ATTR},"material":0 MORPH},{"attributes":{"POSITION":0 SKIN_ATTR},"material":1 MORPH}]},{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TANGENT":2,"TEXCOORD_0":3,"TEXCOORD_1":3,"TEXCOORD_2":3},"material":0},{"attributes":{"POSITION":0},"material":1}]}] SKIN_DECL ANIMATIONS}
			""".Replace( "SCENE_DECL", includeDefault ? "\"scene\":0," : "" )
			.Replace( "EXT_DECL", instancing ? "\"extensionsUsed\":[\"EXT_mesh_gpu_instancing\"],\"extensionsRequired\":[\"EXT_mesh_gpu_instancing\"]," : "" )
			.Replace( "SCENES_ARRAY", scenes ).Replace( "NODES_ARRAY", nodes )
			.Replace( "DATA_LENGTH", data.Length.ToString( System.Globalization.CultureInfo.InvariantCulture ) )
			.Replace( "DATA_BASE64", Convert.ToBase64String( data ) ).Replace( "ANIMATIONS", animations )
			.Replace( "MORPH", morph ? ",\"targets\":[{\"POSITION\":0}]" : "" )
			.Replace( "SKIN_ATTR", skin ? ",\"JOINTS_0\":4,\"WEIGHTS_0\":5" : "" )
			.Replace( "SKIN_DECL", skin ? ",\"skins\":[{\"joints\":[1],\"skeleton\":1}]" : "" );
		return GltfReader.Read( System.Text.Encoding.UTF8.GetBytes( json ), supportedExtensions: instancing ? new HashSet<string> { "EXT_mesh_gpu_instancing" } : null );
	}
}
