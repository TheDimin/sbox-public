using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.InterpolationTestValidation;

[TestClass]
public sealed class InterpolationConformanceTests
{
	public static string CheckoutRoot { get; set; } = FindCheckout();
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkoutRoot ) { CheckoutRoot = Path.GetFullPath( checkoutRoot ); Measurements.Clear(); }
	private const double FloatEpsilon = 1.1920928955078125e-7;
	// A diagnostic ceiling for these float arithmetic operations, not approval
	// of a resampling approximation or of rendered-image tolerances.
	private const double ArithmeticCeiling = 8 * FloatEpsilon;

	[DataTestMethod]
	[DataRow( 0, "glTF" )] [DataRow( 1, "glTF" )] [DataRow( 2, "glTF" )]
	[DataRow( 3, "glTF" )] [DataRow( 4, "glTF" )] [DataRow( 5, "glTF" )]
	[DataRow( 6, "glTF" )] [DataRow( 7, "glTF" )] [DataRow( 8, "glTF" )]
	[DataRow( 0, "glTF-Binary" )] [DataRow( 1, "glTF-Binary" )] [DataRow( 2, "glTF-Binary" )]
	[DataRow( 3, "glTF-Binary" )] [DataRow( 4, "glTF-Binary" )] [DataRow( 5, "glTF-Binary" )]
	[DataRow( 6, "glTF-Binary" )] [DataRow( 7, "glTF-Binary" )] [DataRow( 8, "glTF-Binary" )]
	[DataRow( 0, "glTF->glb_c" )] [DataRow( 1, "glTF->glb_c" )] [DataRow( 2, "glTF->glb_c" )]
	[DataRow( 3, "glTF->glb_c" )] [DataRow( 4, "glTF->glb_c" )] [DataRow( 5, "glTF->glb_c" )]
	[DataRow( 6, "glTF->glb_c" )] [DataRow( 7, "glTF->glb_c" )] [DataRow( 8, "glTF->glb_c" )]
	[DataRow( 0, "glTF-Binary->glb_c" )] [DataRow( 1, "glTF-Binary->glb_c" )] [DataRow( 2, "glTF-Binary->glb_c" )]
	[DataRow( 3, "glTF-Binary->glb_c" )] [DataRow( 4, "glTF-Binary->glb_c" )] [DataRow( 5, "glTF-Binary->glb_c" )]
	[DataRow( 6, "glTF-Binary->glb_c" )] [DataRow( 7, "glTF-Binary->glb_c" )] [DataRow( 8, "glTF-Binary->glb_c" )]
	public void EveryClipMatchesIndependentSourceAtKeysSidesAndInteriors( int clip, string form )
	{
		using var input = new Inputs( CheckoutRoot, form );
		var reference = input.Reference;
		var expectedChannel = reference.Channel( clip );
		var animation = input.Document.Root.GetProperty( "animations" )[clip];
		var sampler = animation.GetProperty( "samplers" )[animation.GetProperty( "channels" )[0].GetProperty( "sampler" ).GetInt32()];
		var times = input.Document.ReadFloatAccessor( sampler.GetProperty( "input" ).GetInt32() );
		var values = input.Document.ReadFloatAccessor( sampler.GetProperty( "output" ).GetInt32() );
		var sampleCount = 0; var comparisons = 0; double maximumScaledError = 0;
		foreach ( var time in reference.SampleTimes( clip ) )
		{
			var label = $"{form}, clip {clip}, time {time:R}";
			var expected = InterpolationReference.Sample( expectedChannel.Times, expectedChannel.Values, expectedChannel.Mode, time, expectedChannel.Path == "rotation" );
			var actual = GltfAnimationSampler.Sample( times, values, expected.Length, expectedChannel.Mode, time, expectedChannel.Path == "rotation" );
			Check( expected, actual.Select( value => (double)value ).ToArray(), label + " channel", ref comparisons, ref maximumScaledError );
			var pose = input.Document.EvaluatePose( clip, time );
			for ( var node = 0; node < reference.NodeCount; node++ )
			{
				Check( reference.World( node, clip, time ), Matrix( pose.GetWorldTransform( node ) ), label + $" node {node}", ref comparisons, ref maximumScaledError );
				Assert.AreEqual( 0, pose.GetMorphWeights( node ).Length, label + " un-authored morph weights" );
			}
			foreach ( var vertex in new[] { 0, 7, 15, 23 } )
			{
				var position = input.Document.EvaluateVertexPosition( expectedChannel.Node, 0, vertex, pose );
				Check( reference.Position( expectedChannel.Node, 0, vertex, clip, time ), [position.X, position.Y, position.Z], label + $" vertex {vertex}", ref comparisons, ref maximumScaledError );
			}
			sampleCount++;
		}
		Assert.AreEqual( 37, sampleCount, "Key/one-sided/interior inventory drifted." );
		Measurements.Add( new { test = nameof( EveryClipMatchesIndependentSourceAtKeysSidesAndInteriors ), clip, form, samples = sampleCount, comparisons, maximum_scaled_error = maximumScaledError, arithmetic_ceiling = ArithmeticCeiling, scope = "loaded managed sampler/pose; GPU and wall-clock untested" } );
	}

	[TestMethod]
	public void PinnedSampleIncludesAllNineDistinctCurvesAndInterleavedGeometry()
	{
		using var input = new Inputs( CheckoutRoot, "glTF" );
		var reference = input.Reference;
		Assert.AreEqual( 9, reference.ClipCount );
		var combinations = new HashSet<string>( StringComparer.Ordinal );
		for ( var clip = 0; clip < 9; clip++ )
		{
			var channel = reference.Channel( clip ); combinations.Add( channel.Path + ":" + channel.Mode );
			CollectionAssert.AreEqual( new double[] { 0, .5, 1, 1.5, 2 }, channel.Times );
		}
		Assert.AreEqual( 9, combinations.Count );
		var views = reference.Root.GetProperty( "bufferViews" );
		Assert.AreEqual( 24, views[0].GetProperty( "byteStride" ).GetInt32() );
		Assert.AreEqual( 32, views[1].GetProperty( "byteStride" ).GetInt32() );
		Assert.AreEqual( 36, reference.Root.GetProperty( "accessors" )[6].GetProperty( "byteOffset" ).GetInt32() );
		// These exact singular scales are valid authored animation. Positions
		// collapse; an inverse-transpose normal is mathematically undefined.
		foreach ( var clip in new[] { 0, 1, 2 } )
		{
			var channel = reference.Channel( clip );
			foreach ( var time in new[] { .5, 1.5 } ) CollectionAssert.AreEqual( new double[] { 0, 0, 0 }, InterpolationReference.Sample( channel.Times, channel.Values, channel.Mode, time, false ) );
		}
	}

	[TestMethod]
	public void StepOneFloatBeforeKeyDiffersFromExactAndAfterKey()
	{
		float[] times = [0, .5f, 2]; float[] values = [11, -7, 3];
		Assert.AreEqual( 11f, GltfAnimationSampler.Sample( times, values, 1, "STEP", MathF.BitDecrement( .5f ) )[0] );
		Assert.AreEqual( -7f, GltfAnimationSampler.Sample( times, values, 1, "STEP", .5f )[0] );
		Assert.AreEqual( -7f, GltfAnimationSampler.Sample( times, values, 1, "STEP", MathF.BitIncrement( .5f ) )[0] );
	}

	[TestMethod]
	public void CubicTangentsUseSecondsAndQuaternionNormalization()
	{
		// Analytic value at t=.25: .84375*1 + .140625*4*.5 +
		// .15625*3 - .046875*4*(-.25) = 1.640625 exactly.
		Assert.AreEqual( 1.640625f, GltfAnimationSampler.Sample( [1, 5], [0, 1, .5f, -.25f, 3, 0], 1, "CUBICSPLINE", 2 )[0] );
		float[] quaternions = [0,0,0,0, 0,0,0,1, 0,0,1,0, 0,0,0,0, 0,0,1,0, 0,0,0,0];
		var actual = GltfAnimationSampler.Sample( [0, 2], quaternions, 4, "CUBICSPLINE", 1, true );
		var comparisons = 0; double error = 0;
		Check( [0, 0, 3 / Math.Sqrt( 13 ), 2 / Math.Sqrt( 13 )], actual.Select( value => (double)value ).ToArray(), "duration-scaled normalized cubic quaternion", ref comparisons, ref error );
	}

	[TestMethod]
	public void LinearUsesShortQuaternionArcButCubicPreservesAuthoredSigns()
	{
		var halfRoot = MathF.Sqrt( .5f );
		var linear = GltfAnimationSampler.Sample( [0, 2], [0, 0, 0, 1, 0, 0, -halfRoot, -halfRoot], 4, "LINEAR", 1, true );
		var comparisons = 0; double error = 0;
		Check( [0, 0, Math.Sin( Math.PI / 8 ), Math.Cos( Math.PI / 8 )], linear.Select( value => (double)value ).ToArray(), "linear short arc", ref comparisons, ref error );
		float[] cubic = [0,0,0,0, 0,0,0,1, 0,0,0,0, 0,0,0,0, 0,0,-halfRoot,-halfRoot, 0,0,0,0];
		var actual = GltfAnimationSampler.Sample( [0, 2], cubic, 4, "CUBICSPLINE", 1, true );
		Check( [0, 0, -Math.Cos( Math.PI / 8 ), Math.Sin( Math.PI / 8 )], actual.Select( value => (double)value ).ToArray(), "cubic authored signs", ref comparisons, ref error );
	}

	private static void Check( double[] expected, double[] actual, string label, ref int comparisons, ref double maximum )
	{
		Assert.AreEqual( expected.Length, actual.Length, label );
		for ( var component = 0; component < expected.Length; component++ )
		{
			Assert.IsTrue( double.IsFinite( actual[component] ), label + $" nonfinite component {component}" );
			var scaled = Math.Abs( actual[component] - expected[component] ) / Math.Max( 1, Math.Abs( expected[component] ) );
			maximum = Math.Max( maximum, scaled ); comparisons++;
			Assert.IsTrue( scaled <= ArithmeticCeiling, $"{label} component {component}: expected {expected[component]:R}, actual {actual[component]:R}, scaled error {scaled:R}, ceiling {ArithmeticCeiling:R}" );
		}
	}

	public static double[] Matrix( Matrix4x4 value ) => [value.M11,value.M12,value.M13,value.M14, value.M21,value.M22,value.M23,value.M24, value.M31,value.M32,value.M33,value.M34, value.M41,value.M42,value.M43,value.M44];
	private static string FindCheckout()
	{
		var directory = new DirectoryInfo( Path.GetDirectoryName( typeof( GltfReader ).Assembly.Location )! );
		while ( directory is not null && !File.Exists( Path.Combine( directory.FullName, "engine/Sandbox-Engine.slnx" ) ) ) directory = directory.Parent;
		return directory?.FullName ?? throw new InvalidOperationException( "Cannot locate engine checkout." );
	}

	public sealed class Inputs : IDisposable
	{
		public InterpolationReference Reference { get; }
		public GltfDocument Document { get; }
		public Dictionary<string, string> Hashes { get; } = new( StringComparer.Ordinal );
		public Inputs( string checkout, string form )
		{
			Assert.IsTrue( form is "glTF" or "glTF-Binary" or "glTF->glb_c" or "glTF-Binary->glb_c", "Unknown validation form." );
			var corpus = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets" );
			using var manifest = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( checkout, "research/gltf-validation/corpus-case-manifest.json" ) ) );
			Assert.AreEqual( InterpolationReference.CorpusCommit, manifest.RootElement.GetProperty( "corpus" ).GetProperty( "commit" ).GetString() );
			byte[] Read( string relative )
			{
				var path = Path.GetFullPath( Path.Combine( corpus, relative ) );
				Assert.IsTrue( path.StartsWith( Path.GetFullPath( corpus ) + Path.DirectorySeparatorChar, StringComparison.Ordinal ), "Dependency escapes corpus." );
				var info = new FileInfo( path ); Assert.IsTrue( info.Length > 0 && info.Length <= 2 * 1024 * 1024, "Missing or oversized model input." );
				var bytes = File.ReadAllBytes( path ); var hash = Convert.ToHexStringLower( SHA256.HashData( bytes ) );
				Assert.AreEqual( manifest.RootElement.GetProperty( "files" ).GetProperty( relative ).GetProperty( "sha256" ).GetString(), hash, relative );
				Hashes[relative] = hash; return bytes;
			}
			const string prefix = "Models/InterpolationTest/";
			var text = Read( prefix + "glTF/InterpolationTest.gltf" );
			byte[] TextResolve( string uri ) => Read( prefix + "glTF/" + uri );
			Reference = new InterpolationReference( text, TextResolve );
			var binary = form.StartsWith( "glTF-Binary", StringComparison.Ordinal );
			var raw = binary ? Read( prefix + "glTF-Binary/InterpolationTest.glb" ) : text;
			byte[] Resolve( string uri ) => Read( prefix + (binary ? "glTF-Binary/" : "glTF/") + uri );
			if ( form.EndsWith( "->glb_c", StringComparison.Ordinal ) ) raw = GltfCompiler.Compile( raw, Resolve );
			Document = GltfReader.Read( raw, Resolve );
		}
		public void Dispose() { Document.Dispose(); Reference.Dispose(); }
	}
}
