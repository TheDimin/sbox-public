using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Tests.Gltf;

/// <summary>Reader and static-assembly evidence; these tests do not create engine components or GPU draws.</summary>
[TestClass]
public sealed class GltfCoreSceneConformanceTests
{
	private const string Lights = "KHR_lights_punctual";
	private const string Instancing = "EXT_mesh_gpu_instancing";
	private const string IdentityMatrix = "[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1]";

	// Core requirements: https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#scenes
	// Local matrices must decompose to TRS; a product of valid ancestor transforms may contain shear.
	[TestMethod]
	public void LibraryWithoutScenesRetainsIndependentNodeCameraAndLightDefinitions()
	{
		using var document = Open( """
			"nodes":[{"camera":0,"translation":[2,3,4]},{"translation":[-1,5,7]}],
			"cameras":[{"type":"perspective","perspective":{"yfov":0.8,"znear":0.2}}],
			"extensionsUsed":["KHR_lights_punctual"],
			"extensions":{"KHR_lights_punctual":{"lights":[{"type":"point","intensity":12}]}}
			""", Lights );

		Assert.IsNull( document.GetDefaultScene() );
		AssertVector( new NVector3( 2, 3, 4 ), NVector3.Transform( NVector3.Zero, document.GetWorldTransform( 0 ) ) );
		AssertVector( new NVector3( -1, 5, 7 ), NVector3.Transform( NVector3.Zero, document.GetWorldTransform( 1 ) ) );
		Assert.AreEqual( 0.8f, document.ReadCamera( 0 ).YFov );
		Assert.AreEqual( 12f, document.ReadLight( 0 ).Intensity );
	}

	[TestMethod]
	public void ExplicitEmptyDefaultSceneDoesNotImportAnotherScenesNodes()
	{
		using var document = PointScene( """
			"scene":1,"scenes":[{"nodes":[0]},{}],"nodes":[{"mesh":0,"translation":[2,3,4]}]
			""" );
		var assembled = GltfStaticSceneAssembler.Assemble( document );

		Assert.AreEqual( 1, document.GetDefaultScene() );
		Assert.AreEqual( 0, assembled.Primitives.Count );
		Assert.IsFalse( assembled.HasBounds );
	}

	[TestMethod]
	public void DifferentScenesMayShareRootNodesWithoutMergingUnselectedRoots()
	{
		using var document = PointScene( """
			"scene":1,"scenes":[{"nodes":[0,1]},{"nodes":[0,2]}],
			"nodes":[{"mesh":0,"translation":[1,0,0]},{"mesh":0,"translation":[0,2,0]},{"mesh":0,"translation":[0,0,3]}]
			""" );
		var assembled = GltfStaticSceneAssembler.Assemble( document );

		Assert.AreEqual( 2, assembled.Primitives.Count );
		Assert.AreEqual( 0, assembled.Primitives[0].Node );
		Assert.AreEqual( 2, assembled.Primitives[1].Node );
		AssertVector( new NVector3( 0, 1, 0 ) / 0.0254f, assembled.Primitives[0].Data.Positions.Span[0] );
		AssertVector( new NVector3( 3, 0, 0 ) / 0.0254f, assembled.Primitives[1].Data.Positions.Span[0] );
	}

	[TestMethod]
	[DataRow( "-1" )]
	[DataRow( "0.5" )]
	[DataRow( "\"0\"" )]
	[DataRow( "null" )]
	[DataRow( "true" )]
	public void DefaultSceneIndexMustBeANonnegativeInteger( string value )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( "\"scene\":" + value + ",\"scenes\":[{}]" );
			_ = document.GetDefaultScene();
		} );
	}

	[TestMethod]
	[DataRow( "\"scene\":0" )]
	[DataRow( "\"scene\":0,\"scenes\":null" )]
	[DataRow( "\"scene\":0,\"scenes\":{}" )]
	[DataRow( "\"scene\":0,\"scenes\":[]" )]
	public void DefaultSceneMustReferenceAnExistingArrayElement( string members )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( members );
			_ = document.GetDefaultScene();
		} );
	}

	[TestMethod]
	[DataRow( "null" )]
	[DataRow( "[]" )]
	[DataRow( "{\"nodes\":null}" )]
	[DataRow( "{\"nodes\":{}}" )]
	[DataRow( "{\"nodes\":[]}" )]
	[DataRow( "{\"nodes\":[-1]}" )]
	[DataRow( "{\"nodes\":[\"0\"]}" )]
	[DataRow( "{\"nodes\":[0.5]}" )]
	[DataRow( "{\"nodes\":[1]}" )]
	[DataRow( "{\"nodes\":[0,0]}" )]
	public void SelectedSceneRequiresUniqueValidRootNodeReferences( string scene )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = PointScene( "\"scene\":0,\"scenes\":[" + scene + "],\"nodes\":[{\"mesh\":0}]" );
			_ = GltfStaticSceneAssembler.Assemble( document );
		} );
	}

	[TestMethod]
	public void SceneRootCannotBeAChildOfAnotherNode()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = PointScene( """
				"scene":0,"scenes":[{"nodes":[1]}],"nodes":[{"translation":[9,0,0],"children":[1]},{"mesh":0}]
				""" );
			_ = GltfStaticSceneAssembler.Assemble( document );
		} );
	}

	[TestMethod]
	[DataRow( "null" )]
	[DataRow( "{}" )]
	[DataRow( "[]" )]
	[DataRow( "[-1]" )]
	[DataRow( "[\"1\"]" )]
	[DataRow( "[0.5]" )]
	[DataRow( "[2]" )]
	[DataRow( "[1,1]" )]
	public void ChildReferencesRequireANonemptyArrayOfUniqueValidIndices( string children )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( "\"nodes\":[{\"children\":" + children + "},{}]" );
			_ = document.GetWorldTransform( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "\"translation\":[1,2]" )]
	[DataRow( "\"translation\":[1,2,3,4]" )]
	[DataRow( "\"translation\":[1,\"2\",3]" )]
	[DataRow( "\"scale\":[1,2]" )]
	[DataRow( "\"scale\":[1,2,3,4]" )]
	[DataRow( "\"scale\":[1,1,1e40]" )]
	[DataRow( "\"rotation\":[0,0,1]" )]
	[DataRow( "\"rotation\":[0,0,0,1,0]" )]
	[DataRow( "\"rotation\":[0,0,0,0]" )]
	public void NodeTrsRequiresFiniteVectorsOfTheSpecifiedDimensions( string members )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( "\"nodes\":[{" + members + "}]" );
			_ = document.GetWorldTransform( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "\"rotation\":[0,0,0,1]" )]
	[DataRow( "\"scale\":[1,1,1]" )]
	public void MatrixCannotBeCombinedWithEvenDefaultRotationOrScale( string trs )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( "\"nodes\":[{\"matrix\":" + IdentityMatrix + "," + trs + "}]" );
			_ = document.GetWorldTransform( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,1]" )]
	[DataRow( "[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1,0]" )]
	[DataRow( "[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,\"0\",1]" )]
	[DataRow( "[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,1e40,1]" )]
	public void NodeMatrixRequiresSixteenFiniteNumericComponents( string matrix )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( "\"nodes\":[{\"matrix\":" + matrix + "}]" );
			_ = document.GetWorldTransform( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "[1,0,0,0, 1,1,0,0, 0,0,1,0, 0,0,0,1]" )]
	[DataRow( "[1,0,0,0.5, 0,1,0,0, 0,0,1,0, 0,0,0,1]" )]
	[DataRow( "[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,2]" )]
	public void LocalNodeMatrixMustBeAffineAndDecomposableToTrs( string matrix )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( "\"nodes\":[{\"matrix\":" + matrix + "}]" );
			_ = document.GetWorldTransform( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "[0,0,0,0, 0,0,0,0, 0,0,0,0, 1,2,3,1]", 1f, 2f, 3f )]
	[DataRow( "[0,0,0,0, 0,2,0,0, 0,0,3,0, 1,2,3,1]", 1f, 8f, 15f )]
	[DataRow( "[-2,0,0,0, 0,0,0,0, 0,0,0,0, 1,2,3,1]", -3f, 2f, 3f )]
	public void LocalMatrixAllowsSingularScaleWithoutLosingTranslation( string matrix, float x, float y, float z )
	{
		using var document = Open( "\"nodes\":[{\"matrix\":" + matrix + "}]" );
		AssertVector( new NVector3( x, y, z ), NVector3.Transform( new NVector3( 2, 3, 4 ), document.GetWorldTransform( 0 ) ) );
	}

	[TestMethod]
	public void EquivalentReflectedMatrixAndTrsHaveTheSameNumericWorldTransform()
	{
		using var document = Open( """
			"nodes":[{"matrix":[-2,0,0,0, 0,3,0,0, 0,0,4,0, 5,6,7,1]},
			 {"translation":[5,6,7],"scale":[-2,3,4]}]
			""" );
		var matrix = document.GetWorldTransform( 0 );
		var trs = document.GetWorldTransform( 1 );
		AssertVector( new NVector3( 3, 0, 19 ), NVector3.Transform( new NVector3( 1, -2, 3 ), matrix ) );
		Assert.AreEqual( matrix, trs );
	}

	[TestMethod]
	public void ComposingValidLocalTrsAndMatrixNodesMayProduceWorldShear()
	{
		using var document = Open( """
			"nodes":[
			 {"translation":[10,20,30],"scale":[2,3,4],"children":[1]},
			 {"rotation":[0,0,0.3826834323650898,0.9238795325112867],"children":[2]},
			 {"matrix":[1,0,0,0, 0,1,0,0, 0,0,1,0, 5,-1,2,1]}
			]
			""" );
		var world = document.GetWorldTransform( 2 );
		var rootTwo = MathF.Sqrt( 2 );

		// Apply the leaf translation, then the 45-degree Z rotation, then parent scale and translation.
		AssertVector( new NVector3( 10 + 5 * rootTwo, 20 + 10.5f * rootTwo, 50 ), NVector3.Transform( new NVector3( 1, 2, 3 ), world ) );
		Assert.AreEqual( 2.5f, NVector3.Dot( NVector3.TransformNormal( NVector3.UnitX, world ), NVector3.TransformNormal( NVector3.UnitY, world ) ), 0.0001f );
	}

	[TestMethod]
	public void MissingChildrenAndSceneRootPropertiesRemainValidEmptyDefinitions()
	{
		using var document = PointScene( "\"scene\":0,\"scenes\":[{}],\"nodes\":[{}]" );
		Assert.AreEqual( NMatrix4x4.Identity, document.GetWorldTransform( 0 ) );
		Assert.AreEqual( 0, GltfStaticSceneAssembler.Assemble( document ).Primitives.Count );
	}

	[TestMethod]
	[DataRow( 0f )]
	[DataRow( .001f )]
	[DataRow( 1f )]
	[DataRow( 100f )]
	[DataRow( 1e20f )]
	[DataRow( 1e-20f )]
	public void RoundedLocalTrsMatricesPreserveTheirOriginalComponentsAtDifferentScales( float scale )
	{
		var rotation = NMatrix4x4.CreateFromAxisAngle( NVector3.Normalize( new NVector3( 1, 2, 3 ) ), .37f );
		var matrix = NMatrix4x4.CreateScale( scale, -3 * scale, 2 * scale ) * rotation * NMatrix4x4.CreateTranslation( 1e25f, -7, 11 );
		using var document = Open( MatrixNode( matrix ) );
		Assert.AreEqual( matrix, document.GetWorldTransform( 0 ), "Validation must retain the authored matrix exactly rather than decompose/recompose or reject float32 rounding." );
	}

	[TestMethod]
	[DataRow( .001f )]
	[DataRow( 100f )]
	[DataRow( 1e20f )]
	[DataRow( 1e-20f )]
	public void GenuineLocalShearIsRejectedIndependentlyOfScaleAndTranslation( float scale )
	{
		var matrix = new NMatrix4x4( scale, 0, 0, 0, scale, scale, 0, 0, 0, 0, scale, 0, 1e25f, 0, 0, 1 );
		Assert.ThrowsException<GltfException>( () => { using var document = Open( MatrixNode( matrix ) ); _ = document.GetWorldTransform( 0 ); } );
	}

	[TestMethod]
	public void PublishedDecimalRoundedCameraMatrixRemainsAcceptedUnchanged()
	{
		var matrix = new NMatrix4x4( -.99975f, -.00679829f, .0213218f, 0, .00167596f, .927325f, .374254f, 0,
			-.0223165f, .374196f, -.927081f, 0, -.0115543f, .194711f, -.478297f, 1 );
		using var document = Open( MatrixNode( matrix ) );
		Assert.AreEqual( matrix, document.GetWorldTransform( 0 ) );
	}

	private static string MatrixNode( NMatrix4x4 m ) => "\"nodes\":[{\"matrix\":" + JsonSerializer.Serialize( new[]
	{
		m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
		m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44
	} ) + "}]";

	[TestMethod]
	[DataRow( "[0,0,0]", 10f, 20f, 30f )]
	[DataRow( "[0,2,3]", 10f, 34f, 57f )]
	[DataRow( "[-2,0,0]", 0f, 20f, 30f )]
	public void SingularAncestorScaleKeepsFiniteDescendantWorldPositions( string scale, float x, float y, float z )
	{
		using var document = Open( "\"nodes\":[{\"translation\":[10,20,30],\"scale\":" + scale + ",\"children\":[1]},{\"translation\":[1,2,3]}]" );
		AssertVector( new NVector3( x, y, z ), NVector3.Transform( new NVector3( 4, 5, 6 ), document.GetWorldTransform( 1 ) ) );
	}

	[TestMethod]
	public void NegatedUnitQuaternionProducesTheSameNodeOrientation()
	{
		using var document = Open( """
			"nodes":[{"rotation":[0.5,-0.5,0.5,0.5]},{"rotation":[-0.5,0.5,-0.5,-0.5]}]
			""" );
		var first = document.GetWorldTransform( 0 );
		var second = document.GetWorldTransform( 1 );
		AssertVector( NVector3.UnitZ, NVector3.TransformNormal( NVector3.UnitX, first ) );
		AssertVector( -NVector3.UnitX, NVector3.TransformNormal( NVector3.UnitY, first ) );
		AssertVector( -NVector3.UnitY, NVector3.TransformNormal( NVector3.UnitZ, first ) );
		Assert.AreEqual( first, second );
	}

	[TestMethod]
	public void SharedCameraDefinitionKeepsProjectionValuesIndependentOfNodeTransforms()
	{
		using var document = Open( """
			"nodes":[{"camera":0,"scale":[2,3,4]},{"camera":0,"translation":[100,200,300]}],
			"cameras":[{"type":"perspective","perspective":{"yfov":0.625,"aspectRatio":2.5,"znear":0.125,"zfar":4096}}]
			""" );
		_ = document.GetWorldTransform( 0 );
		_ = document.GetWorldTransform( 1 );
		var camera = document.ReadCamera( 0 );
		Assert.AreEqual( 0.625f, camera.YFov );
		Assert.AreEqual( 2.5f, camera.AspectRatio );
		Assert.AreEqual( 0.125f, camera.ZNear );
		Assert.AreEqual( 4096f, camera.ZFar );
	}

	[TestMethod]
	[DataRow( "perspective", "" )]
	[DataRow( "perspective", ",\"perspective\":null" )]
	[DataRow( "perspective", ",\"perspective\":[]" )]
	[DataRow( "orthographic", "" )]
	[DataRow( "orthographic", ",\"orthographic\":null" )]
	[DataRow( "orthographic", ",\"orthographic\":[]" )]
	public void CameraTypeRequiresItsCorrespondingProjectionObject( string type, string projection )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( "\"cameras\":[{\"type\":\"" + type + "\"" + projection + "}]" );
			_ = document.ReadCamera( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "{}" )]
	[DataRow( "{\"yfov\":1}" )]
	[DataRow( "{\"znear\":0.1}" )]
	[DataRow( "{\"yfov\":0,\"znear\":0.1}" )]
	[DataRow( "{\"yfov\":-1,\"znear\":0.1}" )]
	[DataRow( "{\"yfov\":1,\"znear\":-0.1}" )]
	[DataRow( "{\"yfov\":1,\"znear\":0.1,\"aspectRatio\":0}" )]
	[DataRow( "{\"yfov\":1,\"znear\":0.1,\"aspectRatio\":-2}" )]
	[DataRow( "{\"yfov\":1,\"znear\":0.1,\"zfar\":0.1}" )]
	[DataRow( "{\"yfov\":1e40,\"znear\":0.1}" )]
	[DataRow( "{\"yfov\":1,\"znear\":0.1,\"aspectRatio\":\"2\"}" )]
	public void PerspectiveProjectionRequiresPositiveFiniteFovAspectAndOrderedClipping( string perspective )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( "\"cameras\":[{\"type\":\"perspective\",\"perspective\":" + perspective + "}]" );
			_ = document.ReadCamera( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "{\"ymag\":1,\"znear\":0,\"zfar\":1}" )]
	[DataRow( "{\"xmag\":1,\"znear\":0,\"zfar\":1}" )]
	[DataRow( "{\"xmag\":1,\"ymag\":1,\"zfar\":1}" )]
	[DataRow( "{\"xmag\":1,\"ymag\":1,\"znear\":0}" )]
	[DataRow( "{\"xmag\":1,\"ymag\":0,\"znear\":0,\"zfar\":1}" )]
	[DataRow( "{\"xmag\":1,\"ymag\":1,\"znear\":-1,\"zfar\":1}" )]
	[DataRow( "{\"xmag\":1,\"ymag\":1,\"znear\":1,\"zfar\":1}" )]
	[DataRow( "{\"xmag\":1,\"ymag\":1,\"znear\":0,\"zfar\":-1}" )]
	[DataRow( "{\"xmag\":1e40,\"ymag\":1,\"znear\":0,\"zfar\":1}" )]
	public void OrthographicProjectionRequiresAllMagnitudesAndOrderedNonnegativeClipping( string orthographic )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Open( "\"cameras\":[{\"type\":\"orthographic\",\"orthographic\":" + orthographic + "}]" );
			_ = document.ReadCamera( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "1.5", "-2.5", 1.5f, -2.5f )]
	[DataRow( "-1.5", "-2.5", -1.5f, -2.5f )]
	public void OrthographicMagnificationSignsArePreservedWithoutTurningShouldIntoMust( string x, string y, float expectedX, float expectedY )
	{
		using var document = Open( "\"cameras\":[{\"type\":\"orthographic\",\"orthographic\":{\"xmag\":" + x + ",\"ymag\":" + y + ",\"znear\":0,\"zfar\":1}}]" );
		var camera = document.ReadCamera( 0 );
		Assert.AreEqual( expectedX, camera.XMagnitude );
		Assert.AreEqual( expectedY, camera.YMagnitude );
		Assert.AreEqual( 0f, camera.ZNear );
		Assert.AreEqual( 1f, camera.ZFar );
	}

	[TestMethod]
	[DataRow( -1 )]
	[DataRow( 1 )]
	public void CameraDefinitionIndexMustBeInRange( int index )
	{
		using var document = Open( "\"cameras\":[{\"type\":\"perspective\",\"perspective\":{\"yfov\":1,\"znear\":1}}]" );
		Assert.ThrowsException<GltfException>( () => document.ReadCamera( index ) );
	}

	// https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Khronos/KHR_lights_punctual
	[TestMethod]
	[DataRow( "directional" )]
	[DataRow( "point" )]
	public void DirectionalAndPointLightsRetainWhiteUnitIntensityAndUnboundedRangeDefaults( string type )
	{
		using var document = LightDocument( "{\"type\":\"" + type + "\"}" );
		var light = document.ReadLight( 0 );
		Assert.AreEqual( type, light.Type );
		AssertVector( NVector3.One, light.Color );
		Assert.AreEqual( 1f, light.Intensity );
		Assert.IsNull( light.Range );
	}

	[TestMethod]
	public void BlackLightAndZeroIntensityAreValidValuesWithoutDefaultSubstitution()
	{
		using var document = LightDocument( "{\"type\":\"point\",\"color\":[0,0,0],\"intensity\":0,\"range\":0.125}" );
		var light = document.ReadLight( 0 );
		AssertVector( NVector3.Zero, light.Color );
		Assert.AreEqual( 0f, light.Intensity );
		Assert.AreEqual( 0.125f, light.Range );
	}

	[TestMethod]
	public void SpotLightAcceptsHalfPiOuterBoundaryAndPreservesExplicitInnerAngle()
	{
		using var document = LightDocument( "{\"type\":\"spot\",\"intensity\":7,\"range\":12,\"spot\":{\"innerConeAngle\":0.25,\"outerConeAngle\":1.5707963267948966}}" );
		var light = document.ReadLight( 0 );
		Assert.AreEqual( 0.25f, light.InnerConeAngle );
		Assert.AreEqual( MathF.PI / 2, light.OuterConeAngle );
		Assert.AreEqual( 7f, light.Intensity );
		Assert.AreEqual( 12f, light.Range );
	}

	[TestMethod]
	public void SharedLightDefinitionsKeepPhotometryIndependentOfNodeScaleAndPlacement()
	{
		using var document = Open( """
			"extensionsUsed":["KHR_lights_punctual"],
			"extensions":{"KHR_lights_punctual":{"lights":[{"type":"spot","color":[0.25,0.5,1],"intensity":40,"range":8,"spot":{"innerConeAngle":0.1,"outerConeAngle":0.3}}]}},
			"nodes":[{"translation":[1,2,3],"scale":[2,3,4],"children":[1]},
			 {"extensions":{"KHR_lights_punctual":{"light":0}},"translation":[2,3,4]},
			 {"extensions":{"KHR_lights_punctual":{"light":0}},"translation":[-3,0,0]}]
			""", Lights );
		AssertVector( new NVector3( 5, 11, 19 ), NVector3.Transform( NVector3.Zero, document.GetWorldTransform( 1 ) ) );
		AssertVector( new NVector3( -3, 0, 0 ), NVector3.Transform( NVector3.Zero, document.GetWorldTransform( 2 ) ) );
		var light = document.ReadLight( 0 );
		AssertVector( new NVector3( 0.25f, 0.5f, 1 ), light.Color );
		Assert.AreEqual( 40f, light.Intensity );
		Assert.AreEqual( 8f, light.Range );
		Assert.AreEqual( 0.1f, light.InnerConeAngle );
		Assert.AreEqual( 0.3f, light.OuterConeAngle );
	}

	[TestMethod]
	[DataRow( "{}" )]
	[DataRow( "{\"type\":\"point\",\"color\":[0,1]}" )]
	[DataRow( "{\"type\":\"point\",\"color\":[0,1,0,1]}" )]
	[DataRow( "{\"type\":\"point\",\"color\":[-0.1,0,0]}" )]
	[DataRow( "{\"type\":\"point\",\"color\":[0,1.1,0]}" )]
	[DataRow( "{\"type\":\"point\",\"color\":[0,\"1\",0]}" )]
	[DataRow( "{\"type\":\"point\",\"intensity\":1e40}" )]
	[DataRow( "{\"type\":\"point\",\"range\":-1}" )]
	[DataRow( "{\"type\":\"point\",\"range\":\"1\"}" )]
	[DataRow( "{\"type\":\"directional\",\"range\":1}" )]
	[DataRow( "{\"type\":\"spot\",\"spot\":null}" )]
	[DataRow( "{\"type\":\"spot\",\"spot\":{\"innerConeAngle\":-0.1}}" )]
	[DataRow( "{\"type\":\"spot\",\"spot\":{\"outerConeAngle\":0}}" )]
	[DataRow( "{\"type\":\"spot\",\"spot\":{\"innerConeAngle\":0.5,\"outerConeAngle\":0.5}}" )]
	public void PunctualLightFieldsEnforceColorPhotometryAndConeDomains( string definition )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = LightDocument( definition );
			_ = document.ReadLight( 0 );
		} );
	}

	[TestMethod]
	[DataRow( -1 )]
	[DataRow( 1 )]
	public void PunctualLightDefinitionIndexMustBeInRange( int index )
	{
		using var document = LightDocument( "{\"type\":\"point\"}" );
		Assert.ThrowsException<GltfException>( () => document.ReadLight( index ) );
	}

	// https://github.com/KhronosGroup/glTF/tree/main/extensions/2.0/Vendor/EXT_mesh_gpu_instancing
	[TestMethod]
	public void ScaleOnlyInstancesPreserveZeroNegativeAndNonuniformScale()
	{
		using var document = ScaleInstances();
		var transforms = document.ReadInstances( 0 );
		Assert.AreEqual( 2, transforms.Length );
		AssertVector( new NVector3( 0, -6, 12 ), NVector3.Transform( new NVector3( 2, 3, 4 ), transforms[0] ) );
		AssertVector( new NVector3( -2, 0, 4 ), NVector3.Transform( new NVector3( 2, 3, 4 ), transforms[1] ) );
	}

	[TestMethod]
	public void InterleavedInstanceAccessorsComposeBeforeNodeAndAncestorTransforms()
	{
		using var document = InstanceDocument( "{\"TRANSLATION\":1,\"ROTATION\":2}", """
			{"bufferView":0,"byteOffset":4,"componentType":5126,"count":2,"type":"VEC3"},
			{"bufferView":0,"byteOffset":16,"componentType":5126,"count":2,"type":"VEC4"}
			""", Floats( 999, 1, 2, 3, 0, 0, 0, 1, 999, -4, 5, 6, 0, 0, 1, 0 ),
			"[{\"buffer\":0,\"byteLength\":64,\"byteStride\":32}]", """
			[{"translation":[10,20,30],"rotation":[0,0,1,0],"children":[1]},
			 {"mesh":0,"translation":[2,3,4],"extensions":{"EXT_mesh_gpu_instancing":{"attributes":{"TRANSLATION":1,"ROTATION":2}}}}]
			""" );
		var transforms = document.ReadInstances( 1 );
		var nodeWorld = document.GetWorldTransform( 1 );
		Assert.AreEqual( 2, transforms.Length );
		AssertVector( new NVector3( 2, 2, 3 ), NVector3.Transform( NVector3.UnitX, transforms[0] ) );
		AssertVector( new NVector3( -5, 5, 6 ), NVector3.Transform( NVector3.UnitX, transforms[1] ) );
		AssertVector( new NVector3( 6, 15, 37 ), NVector3.Transform( NVector3.UnitX, transforms[0] * nodeWorld ) );
		AssertVector( new NVector3( 13, 12, 40 ), NVector3.Transform( NVector3.UnitX, transforms[1] * nodeWorld ) );
	}

	[TestMethod]
	public void SparseInstanceTranslationsUseZeroBaseAndOverrideOnlyTheIndexedInstance()
	{
		var bytes = new byte[16];
		bytes[0] = 1;
		Floats( 7, 8, 9 ).CopyTo( bytes, 4 );
		using var document = InstanceDocument( "{\"TRANSLATION\":1}", """
			{"componentType":5126,"count":2,"type":"VEC3","sparse":{"count":1,"indices":{"bufferView":0,"componentType":5121},"values":{"bufferView":1}}}
			""", bytes, "[{\"buffer\":0,\"byteLength\":1},{\"buffer\":0,\"byteOffset\":4,\"byteLength\":12}]" );
		var transforms = document.ReadInstances( 0 );
		Assert.AreEqual( 2, transforms.Length );
		Assert.AreEqual( NMatrix4x4.Identity, transforms[0] );
		AssertVector( new NVector3( 7, 8, 9 ), NVector3.Transform( NVector3.Zero, transforms[1] ) );
	}

	[TestMethod]
	[DataRow( 0, 32767 )]
	[DataRow( 3, -32768 )]
	public void SignedNormalizedShortInstanceQuaternionsUseLittleEndianXyzwComponents( int component, int value )
	{
		var bytes = new byte[8];
		BinaryPrimitives.WriteInt16LittleEndian( bytes.AsSpan( component * 2, 2 ), (short)value );
		using var document = InstanceDocument( "{\"ROTATION\":1}", "{\"bufferView\":0,\"componentType\":5122,\"normalized\":true,\"count\":1,\"type\":\"VEC4\"}", bytes, "[{\"buffer\":0,\"byteLength\":8}]" );
		var transform = document.ReadInstances( 0 )[0];
		AssertVector( NVector3.UnitX, NVector3.TransformNormal( NVector3.UnitX, transform ) );
		AssertVector( component == 0 ? -NVector3.UnitY : NVector3.UnitY, NVector3.TransformNormal( NVector3.UnitY, transform ) );
		AssertVector( component == 0 ? -NVector3.UnitZ : NVector3.UnitZ, NVector3.TransformNormal( NVector3.UnitZ, transform ) );
	}

	[TestMethod]
	public void MutatingReturnedInstanceTransformsDoesNotChangeAnotherRead()
	{
		using var document = ScaleInstances();
		var first = document.ReadInstances( 0 );
		first[0] = NMatrix4x4.CreateTranslation( 100, 200, 300 );
		var second = document.ReadInstances( 0 );
		Assert.AreNotSame( first, second );
		AssertVector( new NVector3( 0, -6, 12 ), NVector3.Transform( new NVector3( 2, 3, 4 ), second[0] ) );
	}

	[TestMethod]
	[DataRow( "TRANSLATION", "{\"componentType\":5126,\"count\":1,\"type\":\"SCALAR\"}" )]
	[DataRow( "TRANSLATION", "{\"componentType\":5126,\"count\":1,\"type\":\"VEC4\"}" )]
	[DataRow( "TRANSLATION", "{\"componentType\":5123,\"count\":1,\"type\":\"VEC3\"}" )]
	[DataRow( "TRANSLATION", "{\"componentType\":5126,\"normalized\":true,\"count\":1,\"type\":\"VEC3\"}" )]
	[DataRow( "SCALE", "{\"componentType\":5126,\"count\":1,\"type\":\"VEC2\"}" )]
	[DataRow( "SCALE", "{\"componentType\":5122,\"normalized\":true,\"count\":1,\"type\":\"VEC3\"}" )]
	[DataRow( "SCALE", "{\"componentType\":5126,\"normalized\":true,\"count\":1,\"type\":\"VEC3\"}" )]
	[DataRow( "ROTATION", "{\"componentType\":5126,\"count\":1,\"type\":\"VEC3\"}" )]
	[DataRow( "ROTATION", "{\"componentType\":5123,\"normalized\":true,\"count\":1,\"type\":\"VEC4\"}" )]
	[DataRow( "ROTATION", "{\"componentType\":5122,\"count\":1,\"type\":\"VEC4\"}" )]
	[DataRow( "ROTATION", "{\"componentType\":5120,\"count\":1,\"type\":\"VEC4\"}" )]
	[DataRow( "ROTATION", "{\"componentType\":5126,\"normalized\":true,\"count\":1,\"type\":\"VEC4\"}" )]
	public void InstanceTrsSemanticsEnforceTheirSpecifiedAccessorFormats( string semantic, string accessor )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = InstanceDocument( "{\"" + semantic + "\":1}", accessor );
			_ = document.ReadInstances( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "{}" )]
	[DataRow( "null" )]
	[DataRow( "[]" )]
	[DataRow( "{\"TRANSLATION\":-1}" )]
	[DataRow( "{\"TRANSLATION\":99}" )]
	[DataRow( "{\"TRANSLATION\":\"1\"}" )]
	[DataRow( "{\"TRANSLATION\":1.5}" )]
	public void InstanceAttributesRequireANonemptyObjectOfValidAccessorReferences( string attributes )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = InstanceDocument( attributes, "{\"componentType\":5126,\"count\":1,\"type\":\"VEC3\"}" );
			_ = document.ReadInstances( 0 );
		} );
	}

	private static GltfDocument Open( string members, params string[] extensions ) => GltfReader.Read(
		Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"}," + members + "}" ),
		supportedExtensions: new HashSet<string>( extensions, StringComparer.Ordinal ) );

	private static GltfDocument PointScene( string members ) => Open( members + "," + """
		"accessors":[{"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
		"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":0}}]}]
		""" );

	private static GltfDocument LightDocument( string definition ) => Open(
		"\"extensionsUsed\":[\"KHR_lights_punctual\"],\"extensions\":{\"KHR_lights_punctual\":{\"lights\":[" + definition + "]}}", Lights );

	private static GltfDocument ScaleInstances() => InstanceDocument( "{\"SCALE\":1}",
		"{\"bufferView\":0,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}", Floats( 0, -2, 3, -1, 0, 1 ), "[{\"buffer\":0,\"byteLength\":24}]" );

	private static GltfDocument InstanceDocument( string attributes, string accessors, byte[] data = null, string views = null, string nodes = null )
	{
		nodes ??= "[{\"mesh\":0,\"extensions\":{\"EXT_mesh_gpu_instancing\":{\"attributes\":" + attributes + "}}}]";
		var buffer = data is null ? "" : ",\"buffers\":[{\"byteLength\":" + data.Length + ",\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( data ) + "\"}],\"bufferViews\":" + views;
		return Open( """
			"extensionsUsed":["EXT_mesh_gpu_instancing"],"extensionsRequired":["EXT_mesh_gpu_instancing"],
			"meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":0}}]}],
			"accessors":[{"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			""" + accessors + "],\"nodes\":" + nodes + buffer, Instancing );
	}

	private static byte[] Floats( params float[] values )
	{
		var bytes = new byte[values.Length * sizeof( float )];
		for ( var i = 0; i < values.Length; i++ )
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( i * sizeof( float ), sizeof( float ) ), values[i] );
		return bytes;
	}

	private static void AssertVector( NVector3 expected, NVector3 actual )
	{
		Assert.AreEqual( expected.X, actual.X, 0.0001f, "X" );
		Assert.AreEqual( expected.Y, actual.Y, 0.0001f, "Y" );
		Assert.AreEqual( expected.Z, actual.Z, 0.0001f, "Z" );
	}
}
