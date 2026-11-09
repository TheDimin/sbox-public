using Sandbox.Gltf.DirectGpu.Lighting;
using System;
using System.Collections.Generic;
using System.Linq;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.LightsPunctualLampValidation;

using static LightsPunctualLampReference;

/// <summary>Source metadata and CPU plan proof only; no GPU or native photometry claim.</summary>
public static class LightsPunctualLampCoreTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public const int ExpectedCases = 30;

	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new LightsPunctualLampReference( checkoutRoot );
		var rows = new List<Result>();
		Run( "oracle_detects_wrong_basis_and_meter_conversion", "reference", () =>
		{
			foreach ( var variant in reference.Oracle.GetProperty( "variants" ).EnumerateObject() )
				foreach ( var light in variant.Value.GetProperty( "lights" ).EnumerateArray() )
				{
					var meters = Numbers( light.GetProperty( "position_meters" ) );
					var engine = Numbers( light.GetProperty( "engine_position_inches" ) );
					Require( Math.Abs( engine[0] - meters[2] ) > .1 && Math.Abs( engine[2] - meters[2] / .0254 ) > 1,
						"Every source light must detect missing meter conversion and wrong basis." );
				}
		} );
		Run( "oracle_detects_swapped_colors_intensity_and_missing_instances", "reference", () =>
		{
			foreach ( var variant in reference.Oracle.GetProperty( "variants" ).EnumerateObject() )
			{
				var lights = variant.Value.GetProperty( "lights" ).EnumerateArray().ToArray();
				Require( lights.Length == 5 && lights.Select( x => x.GetProperty( "node" ).GetInt32() ).SequenceEqual( new[] { 3, 4, 5, 6, 7 } ), "Literal node/light instance inventory changed." );
				Require( lights.Select( x => x.GetProperty( "intensity" ).GetDouble() ).SequenceEqual( new[] { 15d, 1.5, 80, 80, 180 } ), "Literal candela inventory changed." );
				Require( Numbers( lights[0].GetProperty( "linear_color" ) )[0] == 1 && Numbers( lights[2].GetProperty( "linear_color" ) )[0] < .22,
					"Colored/white light swaps must remain detectable." );
			}
		} );

		foreach ( var form in reference.Forms )
		{
			var expected = reference.Variant( form );
			var lights = expected.GetProperty( "lights" ).EnumerateArray().ToArray();
			Run( "reader_preserves_five_literal_point_definitions", form.Name, () =>
			{
				using var document = form.Read();
				for ( var i = 0; i < lights.Length; i++ )
				{
					var source = document.ReadLight( i );
					var color = Numbers( lights[i].GetProperty( "linear_color" ) );
					Require( source.Type == "point" && source.Range is null, "The sample has unbounded point lights, not a chosen finite radius." );
					Close( lights[i].GetProperty( "intensity" ).GetDouble(), source.Intensity, 0, "candela" );
					Close( color[0], source.Color.X, 1e-7, "linear red" );
					Close( color[1], source.Color.Y, 1e-7, "linear green" );
					Close( color[2], source.Color.Z, 1e-7, "linear blue" );
				}
			} );
			Run( "selected_plan_preserves_node_definition_identity_and_units", form.Name, () =>
			{
				using var document = form.Read();
				var plan = GltfPunctualLightPlan.Create( document );
				Require( !plan.UsesUnsupportedOptionalFallback && plan.Bindings.Count == 5, "Selected light count changed." );
				for ( var i = 0; i < lights.Length; i++ )
				{
					var binding = plan.Bindings[i];
					Require( binding.Node == lights[i].GetProperty( "node" ).GetInt32() && binding.Light == i
						&& binding.Kind == GltfPunctualLightPlan.LightKind.Point && binding.Unit == GltfPunctualLightPlan.IntensityUnit.Candela,
						"Each selected node needs its own correct physical light binding." );
					Require( binding.RangeInches is null && binding.RangeMeters is null && binding.InnerConeRadians is null && binding.OuterConeRadians is null,
						"Point lights have infinite range and no spotlight cone." );
					Close( lights[i].GetProperty( "intensity" ).GetDouble(), binding.Intensity, 0, "plan candela" );
				}
			} );
			Run( "literal_world_position_basis_and_inches_are_applied_once", form.Name, () =>
			{
				using var document = form.Read();
				var states = GltfPunctualLightPlan.Create( document ).Evaluate( document.EvaluatePose(), NMatrix.Identity );
				for ( var i = 0; i < states.Count; i++ ) CheckPosition( Numbers( lights[i].GetProperty( "engine_position_inches" ) ), states[i], .00002 );
			} );
			Run( "affine_placement_includes_shear_and_reflection_without_photometry_scale", form.Name, () =>
			{
				using var document = form.Read();
				var placement = new NMatrix( -2, .25f, 0, 0, .5f, 3, .75f, 0, 0, -.5f, 4, 0, 7, -11, 13, 1 );
				var states = GltfPunctualLightPlan.Create( document ).Evaluate( document.EvaluatePose(), placement );
				for ( var i = 0; i < states.Count; i++ )
				{
					var p = Numbers( lights[i].GetProperty( "engine_position_inches" ) );
					var placed = new[] { -2 * p[0] + .5 * p[1] + 7, .25 * p[0] + 3 * p[1] - .5 * p[2] - 11, .75 * p[1] + 4 * p[2] + 13 };
					CheckPosition( placed, states[i], .00005 );
					Close( lights[i].GetProperty( "intensity" ).GetDouble(), states[i].Binding.Intensity, 0, "placement cannot scale candela" );
					Require( states[i].Binding.RangeMeters is null, "Placement cannot invent an unbounded light cutoff." );
				}
			} );
			Run( "unsupported_optional_light_fallback_is_explicit", form.Name, () =>
			{
				using var document = form.Read( selected: false );
				Require( document.UnsupportedOptionalExtensions.Contains( Extension ), "Optional light fallback must be diagnosed." );
				var plan = GltfPunctualLightPlan.Create( document );
				Require( plan.UsesUnsupportedOptionalFallback && plan.Bindings.Count == 0 && plan.Evaluate( null, NMatrix.Identity ).Count == 0,
					"An unsupported optional light extension uses the no-light fallback." );
			} );
			Run( "packaging_preserves_light_declarations_and_transmission_fallback", form.Name, () =>
			{
				using var document = form.Read();
				Require( document.GetDefaultScene() == 0 && document.Root.GetProperty( "nodes" ).GetArrayLength() == 8
					&& document.Root.GetProperty( "meshes" ).GetArrayLength() == 3 && document.Root.GetProperty( "materials" ).GetArrayLength() == 3,
					"Light support must retain existing geometry/material bindings." );
				Require( document.UnsupportedOptionalExtensions.SequenceEqual( new[] { "KHR_materials_transmission" } ),
					"Optional transmission remains a separately reported fallback, not material fidelity." );
				Require( !document.Root.TryGetProperty( "extensionsRequired", out var required ) || required.GetArrayLength() == 0,
					"Packaging may not make optional sample extensions required." );
			} );
			Run( "independent_states_and_owned_metadata_have_explicit_document_lifetime", form.Name, () =>
			{
				var document = form.Read();
				var plan = GltfPunctualLightPlan.Create( document );
				var cachedPose = document.EvaluatePose();
				var first = plan.Evaluate( cachedPose, NMatrix.Identity );
				var second = plan.Evaluate( null, NMatrix.CreateTranslation( 100, 200, 300 ) );
				for ( var i = 0; i < first.Count; i++ )
				{
					CheckPosition( Numbers( lights[i].GetProperty( "engine_position_inches" ) ), first[i], .00002 );
					Close( 100, second[i].PositionInches.X - first[i].PositionInches.X, .00002, "independent X placement" );
				}
				document.Dispose();
				Require( plan.Bindings.Count == 5 && first.Count == 5, "Owned immutable metadata/state survives borrowed document disposal." );
				foreach ( var pose in new[] { cachedPose, null } )
				{
					try { _ = plan.Evaluate( pose, NMatrix.Identity ); }
					catch ( ObjectDisposedException ) { continue; }
					throw new InvalidOperationException( "A borrowed disposed document cannot evaluate a cached or rest light pose." );
				}
			} );
		}
		Require( rows.Count == ExpectedCases, "Frozen lamp case inventory changed." );
		return rows.ToArray();

		void Run( string name, string form, Action body )
		{
			try { body(); rows.Add( new( name, form, true, null ) ); }
			catch ( Exception error ) { rows.Add( new( name, form, false, error.ToString() ) ); }
		}
	}

	private static void CheckPosition( double[] expected, GltfPunctualLightPlan.State state, double tolerance )
	{
		Close( expected[0], state.PositionInches.X, tolerance, "world X inches" );
		Close( expected[1], state.PositionInches.Y, tolerance, "world Y inches" );
		Close( expected[2], state.PositionInches.Z, tolerance, "world Z inches" );
		Require( state.HasPosition && !state.HasEmissionDirection && state.EmissionDirection == System.Numerics.Vector3.Zero, "Point lights need a position and no arbitrary spotlight direction." );
	}
}
