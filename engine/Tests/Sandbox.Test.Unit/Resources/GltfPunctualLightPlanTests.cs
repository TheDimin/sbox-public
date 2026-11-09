using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu.Lighting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Tests;

/// <summary>Physical light metadata and affine placement without a scene or native light allocation.</summary>
[TestClass]
public class GltfPunctualLightPlanTests
{
	private const string Extension = "KHR_lights_punctual";
	private static readonly IReadOnlySet<string> Selected = new HashSet<string>( StringComparer.Ordinal ) { Extension };

	[TestMethod]
	[DataRow( "point", "Point", "Candela" )]
	[DataRow( "spot", "Spot", "Candela" )]
	[DataRow( "directional", "Directional", "Lux" )]
	public void TypesKeepPhysicalUnitsAndExactStandardDefaults( string type, string kind, string unit )
	{
		using var document = Read( type == "spot" ? "{\"type\":\"spot\",\"spot\":{}}" : "{\"type\":\"" + type + "\"}" );
		var binding = GltfPunctualLightPlan.Create( document ).Bindings.Single();
		Assert.AreEqual( kind, binding.Kind.ToString() );
		Assert.AreEqual( unit, binding.Unit.ToString() );
		Assert.AreEqual( NVector.One, binding.LinearColor );
		Assert.AreEqual( 1f, binding.Intensity );
		Assert.IsNull( binding.RangeMeters );
		Assert.IsNull( binding.RangeInches );
		Assert.AreEqual( type == "spot" ? 0f : (float?)null, binding.InnerConeRadians );
		Assert.AreEqual( type == "spot" ? MathF.PI / 4 : (float?)null, binding.OuterConeRadians );
	}

	[TestMethod]
	public void ZeroIntensityBlackLinearColorAndFiniteMeterRangeNeverReceiveDefaults()
	{
		using var document = Read( "{\"type\":\"point\",\"color\":[0,0,0],\"intensity\":0,\"range\":2.54}" );
		var binding = GltfPunctualLightPlan.Create( document ).Bindings.Single();
		Assert.AreEqual( NVector.Zero, binding.LinearColor );
		Assert.AreEqual( 0f, binding.Intensity );
		Assert.AreEqual( 2.54f, binding.RangeMeters );
		Assert.AreEqual( 100f, binding.RangeInches.Value, .00001f );
	}

	[TestMethod]
	public void ConeAnglesAreHalfAnglesAndConvertOnlyRadiansToDegrees()
	{
		using var document = Read( "{\"type\":\"spot\",\"spot\":{\"innerConeAngle\":0.5235987755982988,\"outerConeAngle\":1.5707963267948966}}" );
		var binding = GltfPunctualLightPlan.Create( document ).Bindings.Single();
		Assert.AreEqual( 30f, binding.InnerConeDegrees.Value, .00001f );
		Assert.AreEqual( 90f, binding.OuterConeDegrees.Value, .00001f );
		Assert.AreEqual( MathF.PI / 2, binding.OuterConeRadians );
	}

	[TestMethod]
	public void OnlySelectedSceneNodesCreateBindingsAndSharedDefinitionsKeepDistinctInstances()
	{
		using var document = Read( "{\"type\":\"point\",\"intensity\":17}",
			"[{\"children\":[1,2]},{\"extensions\":{\"KHR_lights_punctual\":{\"light\":0}}},{\"extensions\":{\"KHR_lights_punctual\":{\"light\":0}}},{\"extensions\":{\"KHR_lights_punctual\":{\"light\":0}}}]",
			"[{\"nodes\":[0]},{\"nodes\":[3]}]" );
		var bindings = GltfPunctualLightPlan.Create( document ).Bindings;
		CollectionAssert.AreEqual( new[] { 1, 2 }, bindings.Select( x => x.Node ).ToArray() );
		Assert.IsTrue( bindings.All( x => x.Light == 0 && x.Intensity == 17 ) );
	}

	[TestMethod]
	public void InheritedShearAndReflectedAffinePlacementTransformTheRayWithoutTrsRoundtrip()
	{
		// Parent nonuniform scale followed by a rotated child produces inherited shear.
		// Local -Z rotated 45 degrees around Y is (-sqrt(.5),0,-sqrt(.5)).
		using var document = Read( "{\"type\":\"spot\",\"intensity\":23,\"range\":7,\"spot\":{}}",
			"[{\"translation\":[10,20,30],\"scale\":[2,3,4],\"children\":[1]},{\"translation\":[1,2,3],\"rotation\":[0,0.3826834323650898,0,0.9238795325112867],\"extensions\":{\"KHR_lights_punctual\":{\"light\":0}}}]" );
		var plan = GltfPunctualLightPlan.Create( document );
		var placement = new NMatrix( -2, .25f, 0, 0, .5f, 3, .75f, 0, 0, -.5f, 4, 0, 7, -11, 13, 1 );
		var state = plan.Evaluate( document.EvaluatePose(), placement ).Single();
		// glTF location (12,26,42) meters -> engine (42,12,26) inches -> placement.
		Close( (-84d + 6d) / .0254 + 7, state.PositionInches.X, .0005 );
		Close( (10.5d + 36d - 13d) / .0254 - 11, state.PositionInches.Y, .0005 );
		Close( (9d + 104d) / .0254 + 13, state.PositionInches.Z, .0005 );
		// Source ray (-2,0,-4) -> engine (-4,-2,0), then row-vector placement.
		var length = Math.Sqrt( 7d * 7 + 7d * 7 + 1.5d * 1.5 );
		Close( 7 / length, state.EmissionDirection.X, .000001 );
		Close( -7 / length, state.EmissionDirection.Y, .000001 );
		Close( -1.5 / length, state.EmissionDirection.Z, .000001 );
		Assert.IsTrue( state.HasEmissionDirection );
		Assert.AreEqual( 23f, state.Binding.Intensity );
		Assert.AreEqual( 7f, state.Binding.RangeMeters );
		Assert.AreEqual( 45f, state.Binding.OuterConeDegrees.Value, .00001f );
	}

	[TestMethod]
	public void PointLightsNeedNoDirectionEvenWithCollapsedNodeScale()
	{
		using var document = Read( "{\"type\":\"point\"}", "[{\"scale\":[0,0,0],\"translation\":[1,2,3],\"extensions\":{\"KHR_lights_punctual\":{\"light\":0}}}]" );
		var state = GltfPunctualLightPlan.Create( document ).Evaluate( null, NMatrix.Identity ).Single();
		Assert.IsFalse( state.HasEmissionDirection );
		Assert.AreEqual( NVector.Zero, state.EmissionDirection );
		Close( 3 / .0254, state.PositionInches.X, .00002 );
	}

	[TestMethod]
	public void CollapsedDirectionalRayIsExplicitFiniteAndRecoversOnLaterPlacement()
	{
		using var document = Read( "{\"type\":\"directional\"}" );
		var plan = GltfPunctualLightPlan.Create( document );
		var pose = document.EvaluatePose();
		var collapsed = plan.Evaluate( pose, NMatrix.CreateScale( 0f ) ).Single();
		Assert.IsFalse( collapsed.HasEmissionDirection );
		Assert.AreEqual( NVector.Zero, collapsed.EmissionDirection );
		var recovered = plan.Evaluate( pose, NMatrix.Identity ).Single();
		Assert.IsTrue( recovered.HasEmissionDirection );
		Assert.AreEqual( new NVector( -1, 0, 0 ), recovered.EmissionDirection );
	}

	[TestMethod]
	public void DirectionalLightsIgnorePositionEvenWhenMeterConversionWouldOverflow()
	{
		using var document = Read( "{\"type\":\"directional\",\"intensity\":7}", "[{\"translation\":[1e38,2e38,3e38],\"extensions\":{\"KHR_lights_punctual\":{\"light\":0}}}]" );
		var state = GltfPunctualLightPlan.Create( document ).Evaluate( null, NMatrix.CreateTranslation( 3e38f, 2e38f, 1e38f ) ).Single();
		Assert.IsFalse( state.HasPosition );
		Assert.AreEqual( NVector.Zero, state.PositionInches );
		Assert.AreEqual( new NVector( -1, 0, 0 ), state.EmissionDirection );
		Assert.AreEqual( 7f, state.Binding.Intensity );
	}

	[TestMethod]
	public void TinyNonzeroRayNormalizesWithoutThresholdOrNaN()
	{
		using var document = Read( "{\"type\":\"directional\"}" );
		var state = GltfPunctualLightPlan.Create( document ).Evaluate( null, NMatrix.CreateScale( 1e-30f ) ).Single();
		Assert.IsTrue( state.HasEmissionDirection );
		Assert.AreEqual( new NVector( -1, 0, 0 ), state.EmissionDirection );
	}

	[TestMethod]
	public void AnimatedParentUsesTheSuppliedPoseAndSeparateEvaluationsStayIndependent()
	{
		var values = new[] { 0f, 1f, 0f, 0f, 0f, 2f, 4f, 6f };
		var bytes = new byte[values.Length * sizeof( float )];
		for ( var i = 0; i < values.Length; i++ ) System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( i * 4, 4 ), values[i] );
		var json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_lights_punctual"],
			 "extensions":{"KHR_lights_punctual":{"lights":[{"type":"point","intensity":12}]}},
			 "scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"children":[1]},{"translation":[1,0,0],"extensions":{"KHR_lights_punctual":{"light":0}}}],
			 "buffers":[{"byteLength":32,"uri":"data:application/octet-stream;base64,BUFFER"}],
			 "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":8},{"buffer":0,"byteOffset":8,"byteLength":24}],
			 "accessors":[{"bufferView":0,"componentType":5126,"type":"SCALAR","count":2,"min":[0],"max":[1]},
			 {"bufferView":1,"componentType":5126,"type":"VEC3","count":2}],
			 "animations":[{"channels":[{"sampler":0,"target":{"node":0,"path":"translation"}}],"samplers":[{"input":0,"output":1}]}]}
			""".Replace( "BUFFER", Convert.ToBase64String( bytes ), StringComparison.Ordinal );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: Selected );
		var plan = GltfPunctualLightPlan.Create( document );
		var early = plan.Evaluate( document.EvaluatePose( 0, .25f ), NMatrix.Identity ).Single();
		var late = plan.Evaluate( document.EvaluatePose( 0, .75f ), NMatrix.Identity ).Single();
		Close( 1.5 / .0254, early.PositionInches.X, .00002 );
		Close( 1.5 / .0254, early.PositionInches.Y, .00002 );
		Close( 1 / .0254, early.PositionInches.Z, .00002 );
		Close( 4.5 / .0254, late.PositionInches.X, .00002 );
		Close( 2.5 / .0254, late.PositionInches.Y, .00002 );
		Close( 3 / .0254, late.PositionInches.Z, .00002 );
		Assert.AreEqual( 12f, early.Binding.Intensity );
		Assert.AreEqual( 12f, late.Binding.Intensity );
	}

	[TestMethod]
	public void FiniteSourceRangeIsRetainedWithoutOverflowInConvertedMetadata()
	{
		using var document = Read( "{\"type\":\"point\",\"range\":1e38}" );
		var binding = GltfPunctualLightPlan.Create( document ).Bindings.Single();
		Assert.AreEqual( 1e38f, binding.RangeMeters );
		Assert.IsTrue( double.IsFinite( binding.RangeInches.Value ) && binding.RangeInches.Value > float.MaxValue );
	}

	[TestMethod]
	public void UndeclaredNodeLightExtensionIsNotSilentlyAccepted()
	{
		const string json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"extensions\":{\"KHR_lights_punctual\":{\"light\":0}}}],\"extensions\":{\"KHR_lights_punctual\":{\"lights\":[{\"type\":\"point\"}]}}}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: Selected );
		Assert.ThrowsException<GltfException>( () => GltfPunctualLightPlan.Create( document ) );
	}

	[TestMethod]
	public void ForeignPoseAndProjectiveOrNonfinitePlacementAreRejected()
	{
		using var document = Read( "{\"type\":\"point\"}" );
		using var other = Read( "{\"type\":\"point\"}" );
		var plan = GltfPunctualLightPlan.Create( document );
		Assert.ThrowsException<ArgumentException>( () => plan.Evaluate( other.EvaluatePose(), NMatrix.Identity ) );
		var projective = NMatrix.Identity; projective.M14 = .01f;
		Assert.ThrowsException<GltfException>( () => plan.Evaluate( null, projective ) );
		var nonfinite = NMatrix.Identity; nonfinite.M41 = float.NaN;
		Assert.ThrowsException<GltfException>( () => plan.Evaluate( null, nonfinite ) );
	}

	[TestMethod]
	public void CachedSameOwnerPoseCannotEvaluateAfterBorrowedDocumentDisposal()
	{
		var document = Read( "{\"type\":\"point\"}" );
		var pose = document.EvaluatePose();
		var plan = GltfPunctualLightPlan.Create( document );
		var snapshot = plan.Evaluate( pose, NMatrix.Identity ).Single();
		document.Dispose();
		Assert.ThrowsException<ObjectDisposedException>( () => plan.Evaluate( pose, NMatrix.Identity ) );
		Assert.AreEqual( NVector.Zero, snapshot.PositionInches );
		Assert.AreEqual( 1f, snapshot.Binding.Intensity );
	}

	[TestMethod]
	public void EmptyOptionalFallbackAlsoRetainsAnExplicitBorrowedDocumentLifetime()
	{
		var document = Read( "{\"type\":\"point\"}", selected: false );
		var plan = GltfPunctualLightPlan.Create( document );
		document.Dispose();
		Assert.ThrowsException<ObjectDisposedException>( () => plan.Evaluate( null, NMatrix.Identity ) );
	}

	[TestMethod]
	public void UnsupportedOptionalLightsUseReportedFallbackAndRequiredLightsReject()
	{
		using var optional = Read( "{\"type\":\"point\"}", selected: false );
		Assert.IsTrue( optional.UnsupportedOptionalExtensions.Contains( Extension ) );
		var plan = GltfPunctualLightPlan.Create( optional );
		Assert.IsTrue( plan.UsesUnsupportedOptionalFallback );
		Assert.AreEqual( 0, plan.Bindings.Count );
		Assert.ThrowsException<GltfException>( () => Read( "{\"type\":\"point\"}", selected: false, required: true ) );
	}

	[TestMethod]
	[DataRow( -1 )]
	[DataRow( 1 )]
	public void InvalidNodeDefinitionReferencesRejectBeforeSceneAllocation( int light )
	{
		using var document = Read( "{\"type\":\"point\"}", "[{\"extensions\":{\"KHR_lights_punctual\":{\"light\":" + light + "}}}]" );
		Assert.ThrowsException<GltfException>( () => GltfPunctualLightPlan.Create( document ) );
	}

	[TestMethod]
	public void MissingDefaultSceneIsAnImportError()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"}}" ), supportedExtensions: Selected );
		Assert.ThrowsException<GltfException>( () => GltfPunctualLightPlan.Create( document ) );
	}

	private static GltfDocument Read( string definition, string nodes = "[{\"extensions\":{\"KHR_lights_punctual\":{\"light\":0}}}]", string scenes = "[{\"nodes\":[0]}]", bool selected = true, bool required = false )
	{
		var json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_lights_punctual\"],"
			+ (required ? "\"extensionsRequired\":[\"KHR_lights_punctual\"]," : "")
			+ "\"extensions\":{\"KHR_lights_punctual\":{\"lights\":[" + definition + "]}},\"scene\":0,\"scenes\":" + scenes + ",\"nodes\":" + nodes + "}";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: selected ? Selected : null );
	}

	private static void Close( double expected, double actual, double tolerance ) => Assert.IsTrue( double.IsFinite( actual ) && Math.Abs( expected - actual ) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}." );
}
