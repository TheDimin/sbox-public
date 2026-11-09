using Sandbox.Gltf;
using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Tests;

/// <summary>
/// Analytic coordinate-path acceptance. Expected asymmetric positions are literal
/// source-space calculations, not outputs from another conversion/evaluation helper.
/// Native front-face behavior and matched camera images have separate GPU gates.
/// </summary>
[TestClass]
public sealed class GltfCoordinatePathTests
{
	private const float Inches = 1f / .0254f;
	private static void Vector( NVector expected, NVector actual, float tolerance = .001f ) => Assert.IsTrue( NVector.Distance( expected, actual ) < tolerance, $"Expected {expected}, got {actual}." );

	[TestMethod]
	public void AsymmetricLocalGeometryUsesTheExactProperCyclicBasisOnce()
	{
		using var document = GltfReader.Read( Source() );
		var data = document.CreateDrawData( 0, 0 );
		Vector( new NVector( 3, 1, 2 ) * Inches, data.Positions.Span[0] );
		Vector( new NVector( 3, 2, 2 ) * Inches, data.Positions.Span[1] );
		Vector( new NVector( 3, 1, 4 ) * Inches, data.Positions.Span[2] );
		Vector( NVector.UnitX, data.Normals.Span[0] );
		Vector( NVector.UnitY, new( data.Tangents.Span[0].X, data.Tangents.Span[0].Y, data.Tangents.Span[0].Z ) );
		Assert.AreEqual( -1f, data.Tangents.Span[0].W, "A proper basis must preserve authored tangent handedness." );
		Vector( NVector.UnitY, GltfGeometry.ConvertDirection( NVector.UnitX ) );
		Vector( NVector.UnitZ, GltfGeometry.ConvertDirection( NVector.UnitY ) );
		Vector( NVector.UnitX, GltfGeometry.ConvertDirection( NVector.UnitZ ) );
	}

	[TestMethod]
	public void TransformConjugationScalesTranslationButNotLinearAxesTwice()
	{
		var converted = GltfGeometry.ConvertTransform( Parent( false ) );
		Vector( new NVector( 23, -1, 9 ) * Inches, NVector.Transform( new NVector( 3, 1, 2 ) * Inches, converted ) );
		Assert.AreEqual( 11 * Inches, converted.M41, .001f );
		Assert.AreEqual( 5 * Inches, converted.M42, .001f );
		Assert.AreEqual( 7 * Inches, converted.M43, .001f );
		Assert.AreEqual( 24f, converted.GetDeterminant(), .0001f, "Unit conversion cancels around the linear transform." );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void StaticHierarchyPreservesAsymmetryNormalsTangentsAndDeclaredWindingRule( bool negative )
	{
		using var document = GltfReader.Read( Source( negative: negative ) );
		var primitive = GltfStaticSceneAssembler.Assemble( document ).Primitives[0].Data;
		var expected = negative ? new[] { new NVector( 24.2f, 5, 3 ), new NVector( 24.2f, 5, 1 ), new NVector( 24.2f, -1, 3 ) }
			: new[] { new NVector( 24.2f, 5, 11 ), new NVector( 24.2f, 5, 13 ), new NVector( 24.2f, -1, 11 ) };
		for ( var i = 0; i < expected.Length; i++ ) Vector( expected[i] * Inches, primitive.Positions.Span[i] );
		Vector( NVector.UnitX, primitive.Normals.Span[0] );
		Vector( negative ? -NVector.UnitZ : NVector.UnitZ, new( primitive.Tangents.Span[0].X, primitive.Tangents.Span[0].Y, primitive.Tangents.Span[0].Z ) );
		Assert.AreEqual( negative ? 1f : -1f, primitive.Tangents.Span[0].W );
		CollectionAssert.AreEqual( negative ? new uint[] { 0, 2, 1 } : new uint[] { 0, 1, 2 }, primitive.Indices.ToArray() );
	}

	[TestMethod]
	public void SkinInverseBindsHierarchyAndAnimationShareOneBasisAndIgnoreMeshNodeTransform()
	{
		using var document = GltfReader.Read( Source( skin: true, animation: true ) );
		var palettes = new GltfSkinningPalette( document.ReadSkin( 0 ) );
		foreach ( var sample in new[] { (Time: 0f, Expected: new NVector( 24.2f, 9.5f, 10.5f )), (Time: .5f, Expected: new NVector( 22.8f, 5, 11.5f )) } )
		{
			var pose = document.EvaluatePose( 0, sample.Time );
			var matrices = palettes.Evaluate( pose );
			var local = new NVector( 3, 1, 2 ) * Inches;
			var evaluated = NVector.Transform( local, matrices[0] ) * .25f + NVector.Transform( local, matrices[1] ) * .75f;
			Vector( sample.Expected * Inches, evaluated );
			// The ordinary CPU vertex path must agree with independent literal results too.
			var sourcePosition = document.EvaluateVertexPosition( 1, 0, 0, pose );
			Vector( new NVector( sample.Expected.Y, sample.Expected.Z, sample.Expected.X ), sourcePosition );
		}
	}

	[TestMethod]
	public void MorphPositionAndNormalDeltasAreLocalVectorsBeforeHierarchyAndSkinTransforms()
	{
		using var document = GltfReader.Read( Source( morph: true ) );
		var pose = document.EvaluatePose();
		Vector( new NVector( 5.3f, 11.4f, 25.8f ), document.EvaluateVertexPosition( 1, 0, 0, pose ) );
		var data = document.CreateDrawData( 0, 0 );
		var morph = GltfModelMorphs.Prepare( document, 1, 0, 0, [0, 1, 2] );
		var delta = morph.Targets[0].Positive.Span[0];
		Vector( new NVector( 1, .5f, -.25f ) * Inches, delta.PositionDelta );
		Vector( new NVector( -.2f, .25f, .5f ), delta.NormalDelta );
		var world = GltfGeometry.ConvertTransform( document.GetWorldTransform( 1 ) );
		Assert.IsTrue( NMatrix.Invert( world, out var inverse ) );
		var normal = NVector.Normalize( NVector.TransformNormal( data.Normals.Span[0] + (NVector)delta.NormalDelta * .4f, NMatrix.Transpose( inverse ) ) );
		Vector( NVector.Normalize( new NVector( .23f, -1f / 15f, .05f ) ), normal, .00001f );
		using var skinned = GltfReader.Read( Source( skin: true, morph: true ) );
		Vector( new NVector( 9.6875f, 10.9f, 25.8f ), skinned.EvaluateVertexPosition( 1, 0, 0, skinned.EvaluatePose() ) );
	}

	[TestMethod]
	public void ExistingCaptureCameraIsTheNegativeSourceXHemisphere()
	{
		Vector( new NVector( 1, -1, .65f ), GltfGeometry.ConvertDirection( new NVector( -1, .65f, 1 ) ) );
		Vector( new NVector( 1, 1, .65f ), GltfGeometry.ConvertDirection( new NVector( 1, .65f, 1 ) ) );
	}

	private static NMatrix Parent( bool negative ) => new( 0, negative ? -2 : 2, 0, 0, -3, 0, 0, 0, 0, 0, 4, 0, 5, 7, 11, 1 );

	private static byte[] Source( bool negative = false, bool skin = false, bool morph = false, bool animation = false )
	{
		using var bytes = new MemoryStream();
		using var writer = new BinaryWriter( bytes, Encoding.UTF8, leaveOpen: true );
		var views = new JsonArray(); var accessors = new JsonArray();
		int Floats( float[] values, string type, int count, bool position = false )
		{
			var offset = (int)bytes.Position;
			foreach ( var value in values ) writer.Write( value );
			views.Add( new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = values.Length * 4 } );
			var accessor = new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5126, ["count"] = count, ["type"] = type };
			if ( position ) { accessor["min"] = new JsonArray( 1, 2, 3 ); accessor["max"] = new JsonArray( 2, 4, 3 ); }
			accessors.Add( accessor ); return accessors.Count - 1;
		}
		var positionIndex = Floats( [1, 2, 3, 2, 2, 3, 1, 4, 3], "VEC3", 3, true );
		var normalIndex = Floats( [0, 0, 1, 0, 0, 1, 0, 0, 1], "VEC3", 3 );
		var tangentIndex = Floats( [1, 0, 0, -1, 1, 0, 0, -1, 1, 0, 0, -1], "VEC4", 3 );
		var attributes = new JsonObject { ["POSITION"] = positionIndex, ["NORMAL"] = normalIndex, ["TANGENT"] = tangentIndex };
		var primitive = new JsonObject { ["attributes"] = attributes };
		var mesh = new JsonObject { ["primitives"] = new JsonArray( primitive ) };
		if ( morph )
		{
			var p = Floats( [.5f, -.25f, 1, .5f, -.25f, 1, .5f, -.25f, 1], "VEC3", 3 );
			accessors[p]["min"] = new JsonArray( .5f, -.25f, 1 ); accessors[p]["max"] = new JsonArray( .5f, -.25f, 1 );
			var n = Floats( [.25f, .5f, -.2f, .25f, .5f, -.2f, .25f, .5f, -.2f], "VEC3", 3 );
			primitive["targets"] = new JsonArray( new JsonObject { ["POSITION"] = p, ["NORMAL"] = n } );
			mesh["weights"] = new JsonArray( .4f );
		}
		var parent = new JsonObject { ["matrix"] = JsonSerializerArray( Parent( negative ) ), ["children"] = new JsonArray( skin ? 2 : 1 ) };
		var meshNode = new JsonObject { ["mesh"] = 0, ["translation"] = skin ? new JsonArray( 99, 88, 77 ) : new JsonArray( 1, -2, .3f ) };
		var nodes = new JsonArray( parent, meshNode );
		var root = new JsonObject { ["asset"] = new JsonObject { ["version"] = "2.0" }, ["scene"] = 0, ["scenes"] = new JsonArray( new JsonObject { ["nodes"] = skin ? new JsonArray( 0, 1 ) : new JsonArray( 0 ) } ), ["nodes"] = nodes, ["meshes"] = new JsonArray( mesh ) };
		if ( skin )
		{
			var offset = (int)bytes.Position; writer.Write( new byte[] { 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0 } );
			views.Add( new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = 12 } );
			accessors.Add( new JsonObject { ["bufferView"] = views.Count - 1, ["componentType"] = 5121, ["count"] = 3, ["type"] = "VEC4" } ); attributes["JOINTS_0"] = accessors.Count - 1;
			attributes["WEIGHTS_0"] = Floats( [.25f, .75f, 0, 0, .25f, .75f, 0, 0, .25f, .75f, 0, 0], "VEC4", 3 );
			var first = NMatrix.CreateTranslation( -1, 0, 0 ); var second = NMatrix.CreateScale( 1, .5f, 1 ) * NMatrix.CreateTranslation( 0, -2, 0 );
			var inverseBinds = Floats( [.. Values( first ), .. Values( second )], "MAT4", 2 );
			root["skins"] = new JsonArray( new JsonObject { ["joints"] = new JsonArray( 2, 3 ), ["inverseBindMatrices"] = inverseBinds } ); meshNode["skin"] = 0;
			nodes.Add( new JsonObject { ["translation"] = new JsonArray( 1, -2, .3f ), ["children"] = new JsonArray( 3 ) } ); nodes.Add( new JsonObject { ["translation"] = new JsonArray( 0, 1, 0 ) } );
		}
		if ( animation )
		{
			var times = Floats( [0, 1], "SCALAR", 2 ); var translations = Floats( [1, -2, .3f, 2, 1, -.4f], "VEC3", 2 );
			accessors[times]["min"] = new JsonArray( 0 ); accessors[times]["max"] = new JsonArray( 1 );
			root["animations"] = new JsonArray( new JsonObject { ["samplers"] = new JsonArray( new JsonObject { ["input"] = times, ["output"] = translations } ), ["channels"] = new JsonArray( new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = skin ? 2 : 1, ["path"] = "translation" } } ) } );
		}
		root["bufferViews"] = views; root["accessors"] = accessors;
		root["buffers"] = new JsonArray( new JsonObject { ["byteLength"] = bytes.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes.ToArray() ) } );
		return Encoding.UTF8.GetBytes( root.ToJsonString() );
	}

	private static float[] Values( NMatrix m ) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
	private static JsonArray JsonSerializerArray( NMatrix matrix )
	{
		var result = new JsonArray(); foreach ( var value in Values( matrix ) ) result.Add( value ); return result;
	}
}
