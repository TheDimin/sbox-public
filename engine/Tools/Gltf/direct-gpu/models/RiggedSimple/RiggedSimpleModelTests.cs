using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.Tests;

using Vector3 = global::System.Numerics.Vector3;
using Quaternion = global::System.Numerics.Quaternion;
using Matrix4x4 = global::System.Numerics.Matrix4x4;
using Assert = global::Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>
/// Model-specific checks compiled by the editor validation coordinator against the
/// installed producer. The reference reads source bytes independently. These tests
/// establish CPU/data expectations; they do not establish GPU rendering parity.
/// </summary>
[TestClass]
public sealed class RiggedSimpleModelTests
{
	public static string CheckoutRoot { get; set; }
	private static string SourceRoot => Path.Combine( CheckoutRoot ?? throw new InvalidOperationException( "Set CheckoutRoot before running model checks." ),
		"Temp/gltf-reference/glTF-Sample-Assets/Models/RiggedSimple" );
	private static string Original => Path.Combine( SourceRoot, "glTF/RiggedSimple.gltf" );

	[TestMethod]
	public void IndependentOracleHasTheAuditedSourceFeatures()
	{
		using var reference = RiggedSimpleReference.Read( Original );
		Assert.AreEqual( 5, reference.NodeCount );
		Assert.AreEqual( 160, reference.VertexCount );
		Assert.AreEqual( 564, reference.Indices.Length );
		Assert.AreEqual( 2, reference.Joints.Length );
		Assert.AreEqual( 1, reference.InfluenceSetCount );
		Assert.AreEqual( 50, reference.KeyTimes.Length );
		Assert.AreEqual( 0.04166661947965622f, reference.KeyTimes[0] );
		Assert.AreEqual( 2.083333015441895f, reference.KeyTimes[^1] );
		Assert.AreEqual( 0, reference.MeshIndex );
		Assert.AreEqual( 2, reference.MeshNode );
		CollectionAssert.AreEqual( new[] { 3, 4 }, reference.Joints );
	}

	[TestMethod]
	public void IndependentOracleDetectsAnimatedMotionAndIncorrectMatrixOrder()
	{
		using var reference = RiggedSimpleReference.Read( Original );
		var rest = reference.Evaluate( null );
		var motion = reference.Evaluate( reference.KeyTimes[reference.KeyTimes.Length / 2] );
		var maximumMotion = rest.Positions.Zip( motion.Positions, Vector3.Distance ).Max();
		Assert.IsTrue( maximumMotion > 0.1f, "Fixture must expose visible deformation, not only metadata changes." );
		var wrongOrder = reference.Evaluate( reference.KeyTimes[reference.KeyTimes.Length / 2], reversePaletteOrder: true );
		Assert.IsTrue( motion.Positions.Zip( wrongOrder.Positions, Vector3.Distance ).Max() > 0.1f,
			"Reference sensitivity must reject jointWorld * inverseBind instead of inverseBind * jointWorld." );
		var doubleMeshTransform = motion.Positions.Select( point => Vector3.Transform( point, motion.Worlds[reference.MeshNode] ) ).ToArray();
		Assert.IsTrue( motion.Positions.Zip( doubleMeshTransform, Vector3.Distance ).Max() > 0.1f,
			"Reference sensitivity must reject applying the skinned mesh-node world twice." );
	}

	[DataTestMethod]
	[DataRow( "glTF/RiggedSimple.gltf", false )]
	[DataRow( "glTF/RiggedSimple.gltf", true )]
	[DataRow( "glTF-Embedded/RiggedSimple.gltf", false )]
	[DataRow( "glTF-Embedded/RiggedSimple.gltf", true )]
	[DataRow( "glTF-Binary/RiggedSimple.glb", false )]
	[DataRow( "glTF-Binary/RiggedSimple.glb", true )]
	public void SourceAndCompiledPosesMatchIndependentHierarchyAndSkin( string relativePath, bool compiled )
	{
		using var reference = RiggedSimpleReference.Read( Original );
		var path = Path.Combine( SourceRoot, relativePath );
		var bytes = File.ReadAllBytes( path );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( path ), Uri.UnescapeDataString( uri ) ) );
		if ( compiled ) bytes = GltfCompiler.Compile( bytes, Resolve );
		using var actual = GltfReader.Read( bytes, Resolve );
		var samples = new List<float?> { null, 0f, reference.KeyTimes[0] - 0.01f, reference.KeyTimes[^1] + 0.01f };
		foreach ( var key in reference.KeyTimes ) samples.Add( key );
		for ( var key = 1; key < reference.KeyTimes.Length; key++ ) samples.Add( (reference.KeyTimes[key - 1] + reference.KeyTimes[key]) * 0.5f );
		var selectedVertices = new[] { 0, 1, 7, 23, 47, 63, 79, 80, 103, 127, 145, 159 };
		foreach ( var time in samples )
		{
			var expected = reference.Evaluate( time );
			var pose = actual.EvaluatePose( time.HasValue ? 0 : null, time.GetValueOrDefault() );
			for ( var node = 0; node < reference.NodeCount; node++ )
				AssertMatrix( expected.Worlds[node], pose.GetWorldTransform( node ), $"{relativePath} compiled={compiled} node={node} time={time}" );
			foreach ( var vertex in selectedVertices )
				AssertVector( expected.Positions[vertex], actual.EvaluateVertexPosition( reference.MeshNode, 0, vertex, pose ),
					$"{relativePath} compiled={compiled} vertex={vertex} time={time}" );
		}
	}

	[TestMethod]
	public void UploadUsesActualSeparateStreamFormatsAndSharedViewOffsets()
	{
		var path = Original;
		using var actual = GltfReader.Read( File.ReadAllBytes( path ), uri => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( path ), uri ) ) );
		var upload = actual.MaterializePrimitiveUpload( 0, 0 );
		var position = upload.Attributes["POSITION"];
		var normal = upload.Attributes["NORMAL"];
		var joints = upload.Attributes["JOINTS_0"];
		var weights = upload.Attributes["WEIGHTS_0"];
		Assert.AreSame( position.Storage, normal.Storage, "One source view should have one owned storage." );
		Assert.AreEqual( 1920, position.ByteOffset ); Assert.AreEqual( 0, normal.ByteOffset );
		Assert.AreEqual( 12, position.ByteStride ); Assert.AreEqual( 12, normal.ByteStride );
		Assert.AreEqual( 5123, joints.ComponentType ); Assert.AreEqual( 8, joints.ByteStride ); Assert.IsFalse( joints.Normalized );
		Assert.AreEqual( 5126, weights.ComponentType ); Assert.AreEqual( 16, weights.ByteStride ); Assert.IsFalse( weights.Normalized );
		Assert.AreEqual( 5123, upload.Indices.ComponentType ); Assert.AreEqual( 2, upload.Indices.ByteStride );
		Assert.AreEqual( 160, position.Count ); Assert.AreEqual( 564, upload.Indices.Count );
	}

	[TestMethod]
	public void IndependentOracleRetainsDistinctInstancesAndAffinePlacement()
	{
		using var reference = RiggedSimpleReference.Read( Original );
		var a = reference.Evaluate( reference.KeyTimes[0] );
		var before = (Vector3[])a.Positions.Clone();
		var b = reference.Evaluate( reference.KeyTimes[25] );
		Assert.IsTrue( a.Positions.Zip( b.Positions, Vector3.Distance ).Max() > 0.1f );
		CollectionAssert.AreEqual( before, a.Positions, "Evaluating B must not mutate A." );
		var placement = new Matrix4x4( 2, 0.2f, 0, 0, 0, 0.5f, 0.3f, 0, 0, 0, -1, 0, 3, -4, 5, 1 );
		var placed = reference.ToEngine( b, placement );
		for ( var vertex = 0; vertex < b.Positions.Length; vertex++ )
		{
			var p = b.Positions[vertex];
			AssertVector( Vector3.Transform( new Vector3( p.Z, p.X, p.Y ) / 0.0254f, placement ), placed.Positions[vertex], "basis then full affine placement" );
			Assert.IsTrue( MathF.Abs( placed.Normals[vertex].Length() - 1 ) < 0.00001f );
		}
	}


	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RepackedInterleavedStreamsKeepTheSameAnimatedGeometry( bool compiled )
	{
		using var reference = RiggedSimpleReference.Read( Original );
		var fixture = RiggedSimpleLayoutFixture.Create( Original );
		var bytes = compiled ? GltfCompiler.Compile( fixture ) : fixture;
		using var actual = GltfReader.Read( bytes );
		var upload = actual.MaterializePrimitiveUpload( 0, 0 );
		var positions = upload.Attributes["POSITION"]; var normals = upload.Attributes["NORMAL"];
		var joints = upload.Attributes["JOINTS_0"]; var weights = upload.Attributes["WEIGHTS_0"];
		Assert.AreSame( positions.Storage, normals.Storage ); Assert.AreSame( positions.Storage, joints.Storage ); Assert.AreSame( positions.Storage, weights.Storage );
		Assert.AreEqual( 64, positions.ByteStride ); Assert.AreEqual( 64, normals.ByteStride );
		Assert.AreEqual( 64, joints.ByteStride ); Assert.AreEqual( 64, weights.ByteStride );
		Assert.AreEqual( 0, positions.ByteOffset ); Assert.AreEqual( 16, normals.ByteOffset );
		Assert.AreEqual( 48, joints.ByteOffset ); Assert.AreEqual( 28, weights.ByteOffset );
		foreach ( var time in new[] { reference.KeyTimes[0], reference.KeyTimes[25], (reference.KeyTimes[31] + reference.KeyTimes[32]) * 0.5f, reference.KeyTimes[^1] } )
		{
			var expected = reference.Evaluate( time ); var pose = actual.EvaluatePose( 0, time );
			for ( var vertex = 0; vertex < reference.VertexCount; vertex++ )
				AssertVector( expected.Positions[vertex], actual.EvaluateVertexPosition( reference.MeshNode, 0, vertex, pose ),
					$"repacked compiled={compiled} vertex={vertex} time={time}" );
		}
	}

	private static void AssertVector( Vector3 expected, Vector3 actual, string context )
	{
		AssertFloat( expected.X, actual.X, context + " x" ); AssertFloat( expected.Y, actual.Y, context + " y" ); AssertFloat( expected.Z, actual.Z, context + " z" );
	}

	private static void AssertMatrix( Matrix4x4 expected, Matrix4x4 actual, string context )
	{
		var e = new[] { expected.M11, expected.M12, expected.M13, expected.M14, expected.M21, expected.M22, expected.M23, expected.M24,
			expected.M31, expected.M32, expected.M33, expected.M34, expected.M41, expected.M42, expected.M43, expected.M44 };
		var a = new[] { actual.M11, actual.M12, actual.M13, actual.M14, actual.M21, actual.M22, actual.M23, actual.M24,
			actual.M31, actual.M32, actual.M33, actual.M34, actual.M41, actual.M42, actual.M43, actual.M44 };
		for ( var i = 0; i < e.Length; i++ ) AssertFloat( e[i], a[i], context + " element=" + i );
	}

	private static void AssertFloat( float expected, float actual, string context )
	{
		// Numerical float-evaluator comparison only; no resampling approximation or
		// visual/GPU tolerance is approved by this CPU check.
		var tolerance = 32 * 1.1920929e-7f * MathF.Max( 1, MathF.Abs( expected ) );
		Assert.IsTrue( float.IsFinite( actual ) && MathF.Abs( expected - actual ) <= tolerance,
			$"{context}: expected {expected:R}, actual {actual:R}, bound {tolerance:R}" );
	}
}
