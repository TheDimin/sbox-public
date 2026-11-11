using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfSkinningPaletteTests
{
	[TestMethod]
	public void PaletteUsesSkinJointOrderPreservesShearAndIgnoresMeshNode()
	{
		using var document = Fixture();
		var helper = new GltfSkinningPalette( document.ReadSkin( 0 ) );
		var matrices = helper.Evaluate( document.EvaluatePose() );
		Assert.AreEqual( 2, matrices.Length );
		// Joint 2: inverse-bind x -1, rotate 45 degrees, translate x +3,
		// then parent-scale x by 2. The combined affine basis contains shear.
		var first = NVector.Transform( GltfGeometry.ConvertPosition( new NVector( 2, 2, 0 ) ), matrices[0] );
		AssertVector( new NVector( 0, 6 - MathF.Sqrt( 2 ), 3 / MathF.Sqrt( 2 ) ) / 0.0254f, first );
		// Joint 1: inverse-bind doubles x, rotate +90 degrees around z, then y +3.
		var second = NVector.Transform( GltfGeometry.ConvertPosition( NVector.UnitX ), matrices[1] );
		AssertVector( new NVector( 0, 0, 5 ) / 0.0254f, second );
		// The two mesh nodes have different, nonidentity transforms; one shared palette
		// must match the independently evaluated skinned point for both.
		AssertVector( GltfGeometry.ConvertPosition( document.EvaluateVertexPosition( 0, 0, 0 ) ),
			NVector.Transform( GltfGeometry.ConvertPosition( NVector.UnitX ), matrices[0] ) );
		AssertVector( GltfGeometry.ConvertPosition( document.EvaluateVertexPosition( 3, 0, 0 ) ),
			NVector.Transform( GltfGeometry.ConvertPosition( NVector.UnitX ), matrices[0] ) );
	}

	[TestMethod]
	public void AnimatedInstancesAreIndependentAndObjectPlacementIsAppliedOnce()
	{
		using var document = Fixture();
		var helper = new GltfSkinningPalette( document.ReadSkin( 0 ) );
		var rest = helper.Evaluate( document.EvaluatePose() );
		var placed = helper.Evaluate( document.EvaluatePose( 0, 1f ), NMatrix.CreateTranslation( 10, 20, 30 ) );
		var point = GltfGeometry.ConvertPosition( NVector.UnitX );
		AssertVector( new NVector( 0, 0, 5 ) / 0.0254f, NVector.Transform( point, rest[1] ) );
		// Joint 1 translation interpolates to y=5 at one second; inverse-bind x2 + rotation adds y=2.
		AssertVector( new NVector( 0, 0, 7 ) / 0.0254f + new NVector( 10, 20, 30 ), NVector.Transform( point, placed[1] ) );
		AssertVector( new NVector( 0, 0, 5 ) / 0.0254f, NVector.Transform( point, rest[1] ) );
	}

	[TestMethod]
	public void CompiledAndSourcePalettesMatchAtRestAndAnimatedTimes()
	{
		using var source = Fixture();
		var bytes = SourceBytes();
		using var compiled = GltfReader.Read( GltfCompiler.Compile( bytes ) );
		var original = new GltfSkinningPalette( source.ReadSkin( 0 ) );
		var output = new GltfSkinningPalette( compiled.ReadSkin( 0 ) );
		foreach ( var time in new[] { 0f, 0.5f, 1f, 2f } )
		{
			var a = original.Evaluate( source.EvaluatePose( 0, time ) );
			var b = output.Evaluate( compiled.EvaluatePose( 0, time ) );
			for ( var i = 0; i < a.Length; i++ )
				foreach ( var point in new[] { NVector.Zero, NVector.UnitX, NVector.UnitY, NVector.UnitZ } )
					AssertVector( NVector.Transform( point, a[i] ), NVector.Transform( point, b[i] ) );
		}
	}

	[TestMethod]
	public void SnapshotsAreImmutableAndPoseResultsDoNotAlias()
	{
		using var document = Fixture();
		var skin = document.ReadSkin( 0 );
		var helper = new GltfSkinningPalette( skin );
		var baseline = helper.Evaluate( document.EvaluatePose() );
		var point = GltfGeometry.ConvertPosition( new NVector( 2, 2, 0 ) );
		AssertVector( new NVector( 0, 6 - MathF.Sqrt( 2 ), 3 / MathF.Sqrt( 2 ) ) / 0.0254f, NVector.Transform( point, baseline[0] ) );
		skin.Joints[0] = 1;
		skin.InverseBindMatrices[0] = NMatrix.Identity;
		var first = helper.Evaluate( document.EvaluatePose() );
		Assert.AreEqual( baseline[0], first[0] );
		Assert.AreEqual( baseline[1], first[1] );
		first[0] = default;
		Assert.AreEqual( baseline[0], helper.Evaluate( document.EvaluatePose() )[0] );
	}

	[TestMethod]
	public void WriteReusesDestinationAndLeavesTrailingValuesUnchanged()
	{
		using var document = Fixture();
		var helper = new GltfSkinningPalette( document.ReadSkin( 0 ) );
		var pose = document.EvaluatePose();
		var sentinel = NMatrix.CreateTranslation( 123, 456, 789 );
		var output = new[] { default( NMatrix ), default( NMatrix ), sentinel };
		helper.Write( pose, output );
		Assert.AreEqual( helper.Evaluate( pose )[0], output[0] );
		Assert.AreEqual( helper.Evaluate( pose )[1], output[1] );
		Assert.AreEqual( sentinel, output[2] );
		Assert.AreNotEqual( default( NMatrix ), output[0] );
		Assert.ThrowsException<ArgumentException>( () => helper.Write( pose, new NMatrix[1] ) );
		Assert.ThrowsException<ArgumentException>( () => helper.Write( pose, Array.Empty<NMatrix>() ) );
	}

	[TestMethod]
	public void WriteValidationDoesNotPartiallyChangeDestination()
	{
		using var document = Fixture();
		var helper = new GltfSkinningPalette( new GltfSkin { Joints = [1, 99], InverseBindMatrices = [NMatrix.Identity, NMatrix.Identity] } );
		var sentinel = NMatrix.CreateTranslation( 123, 456, 789 );
		var output = new[] { sentinel, sentinel };
		Assert.ThrowsException<GltfException>( () => helper.Write( document.EvaluatePose(), output ) );
		Assert.AreEqual( sentinel, output[0] );
		Assert.AreEqual( sentinel, output[1] );
		var valid = new GltfSkinningPalette( document.ReadSkin( 0 ) );
		var badPlacement = NMatrix.Identity;
		badPlacement.M22 = float.NaN;
		Assert.ThrowsException<GltfException>( () => valid.Write( document.EvaluatePose(), output, badPlacement ) );
		Assert.AreEqual( sentinel, output[0] );
		Assert.AreEqual( sentinel, output[1] );
	}

	[TestMethod]
	public void NoncommutingObjectPlacementIsAppliedAfterSkinningExactlyOnce()
	{
		using var document = Fixture();
		var helper = new GltfSkinningPalette( document.ReadSkin( 0 ) );
		var placement = NMatrix.CreateScale( 2, 3, 4 ) * NMatrix.CreateRotationZ( MathF.PI / 2 ) * NMatrix.CreateTranslation( 10, 20, 30 );
		var output = new NMatrix[2];
		helper.Write( document.EvaluatePose(), output, placement );
		var y = (6 - MathF.Sqrt( 2 )) / 0.0254f;
		var z = (3 / MathF.Sqrt( 2 )) / 0.0254f;
		var actual = NVector.Transform( GltfGeometry.ConvertPosition( new NVector( 2, 2, 0 ) ), output[0] );
		var expected = new NVector( 10 - 3 * y, 20, 30 + 4 * z );
		Assert.IsTrue( NVector.Distance( expected, actual ) < 0.0005f, $"Expected {expected}, got {actual}." );
	}

	[TestMethod]
	public void RejectsInvalidCountsIndicesAndNonAffineOrNonfiniteMatrices()
	{
		Assert.ThrowsException<GltfException>( () => new GltfSkinningPalette( new GltfSkin { Joints = [0, 1], InverseBindMatrices = [NMatrix.Identity] } ) );
		Assert.ThrowsException<GltfException>( () => new GltfSkinningPalette( new GltfSkin { Joints = [-1], InverseBindMatrices = [NMatrix.Identity] } ) );
		Assert.ThrowsException<GltfException>( () => new GltfSkinningPalette( new GltfSkin { Joints = [1, 1], InverseBindMatrices = [NMatrix.Identity, NMatrix.Identity] } ) );
		var invalid = NMatrix.Identity; invalid.M14 = 1;
		Assert.ThrowsException<GltfException>( () => new GltfSkinningPalette( new GltfSkin { Joints = [0], InverseBindMatrices = [invalid] } ) );
		var helper = new GltfSkinningPalette( new GltfSkin { Joints = [99], InverseBindMatrices = [NMatrix.Identity] } );
		using var document = Fixture();
		Assert.ThrowsException<GltfException>( () => helper.Evaluate( document.EvaluatePose() ) );
		var nonfinite = NMatrix.Identity; nonfinite.M11 = float.NaN;
		Assert.ThrowsException<GltfException>( () => helper.Evaluate( _ => nonfinite ) );
	}

	private static void AssertVector( NVector expected, NVector actual ) => Assert.IsTrue( NVector.Distance( expected, actual ) < 0.0001f, $"Expected {expected}, got {actual}." );
	private static GltfDocument Fixture() => GltfReader.Read( SourceBytes() );

	private static byte[] SourceBytes()
	{
		var bytes = new List<byte>(); var views = new List<object>(); var accessors = new List<object>();
		int Floats( float[] values, string type, int count, bool bounds = false )
		{
			var offset = bytes.Count;
			foreach ( var value in values ) { var b = new byte[4]; BinaryPrimitives.WriteSingleLittleEndian( b, value ); bytes.AddRange( b ); }
			var view = views.Count; views.Add( new { buffer = 0, byteOffset = offset, byteLength = bytes.Count - offset } );
			var item = new Dictionary<string, object> { ["bufferView"] = view, ["componentType"] = 5126, ["count"] = count, ["type"] = type };
			if ( bounds ) { item["min"] = type == "SCALAR" ? new[] { 0f } : new[] { 1f, 0f, 0f }; item["max"] = type == "SCALAR" ? new[] { 2f } : new[] { 1f, 0f, 0f }; }
			accessors.Add( item ); return accessors.Count - 1;
		}
		var ibm = Floats( [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 1, 2, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1], "MAT4", 2 );
		var position = Floats( [1, 0, 0], "VEC3", 1, true );
		var offset = bytes.Count; bytes.AddRange( new byte[4] ); var jointView = views.Count;
		views.Add( new { buffer = 0, byteOffset = offset, byteLength = 4 } ); var joints = accessors.Count;
		accessors.Add( new { bufferView = jointView, componentType = 5121, count = 1, type = "VEC4" } );
		var weights = Floats( [1, 0, 0, 0], "VEC4", 1 );
		var time = Floats( [0, 2], "SCALAR", 2, true );
		var translation = Floats( [0, 3, 0, 0, 7, 0], "VEC3", 2 );
		var q = MathF.Sqrt( 0.5f );
		var half45Sin = MathF.Sin( MathF.PI / 8 );
		var half45Cos = MathF.Cos( MathF.PI / 8 );
		return JsonSerializer.SerializeToUtf8Bytes( new
		{
			asset = new { version = "2.0" },
			scene = 0,
			scenes = new[] { new { nodes = new[] { 0, 3, 5 } } },
			nodes = new object[] { new { mesh = 0, skin = 0, translation = new[] { 100f,0f,0f } }, new { translation = new[] { 0f,3f,0f }, rotation = new[] { 0f,0f,q,q } },
				new { rotation = new[] { 0f,0f,half45Sin,half45Cos }, translation = new[] { 3f,0f,0f } }, new { mesh = 0, skin = 0, translation = new[] { -100f,0f,0f } },
				new { scale = new[] { 2f,1f,1f }, children = new[] { 2 } }, new { children = new[] { 1,4 } } },
			skins = new[] { new { joints = new[] { 2, 1 }, skeleton = 5, inverseBindMatrices = ibm } },
			meshes = new[] { new { primitives = new[] { new { mode = 0, attributes = new Dictionary<string, int> { ["POSITION"] = position, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights } } } } },
			buffers = new[] { new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes.ToArray() ), byteLength = bytes.Count } },
			bufferViews = views,
			accessors,
			animations = new[] { new { samplers = new[] { new { input = time, output = translation } }, channels = new[] { new { sampler = 0, target = new { node = 1, path = "translation" } } } } }
		} );
	}
}
