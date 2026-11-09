using Sandbox.SceneRenderer.Gpu;
using System;
using System.Collections.Generic;

namespace Sandbox.Gltf.DirectGpu.LightsPunctualLampValidation;

/// <summary>
/// Bounded coordinator-only native packing observations. These diagnose existing
/// light APIs before mapping physical units; they do not establish rendered lux.
/// The caller supplies an owned private world. Every temporary light is deleted.
/// </summary>
public static class LightsPunctualLampNativeLightAudit
{
	public sealed record Observation( string Case, string NativeKind, uint PackedType, double[] Color,
		float LinearFalloff, float QuadraticFalloff, float FalloffBias, float Radius, float RadiusSquared,
		double[] Forward, double[] Position, double[] ConeCosines, string EngineMvid, string RendererMvid );
	public const int ExpectedMeasurements = 6;

	public static Observation[] Run( SceneWorld world )
	{
		ArgumentNullException.ThrowIfNull( world );
		var rows = new List<Observation>();
		ScenePointLight point = null;
		SceneSpotLight spot = null;
		SceneDirectionalLight directional = null;
		try
		{
			point = new ScenePointLight( world, new global::Vector3( 11, 23, 37 ), 1000, global::Color.White ) { ShadowsEnabled = false };
			rows.Add( Pack( "point_native_q1_color1_radius1000", point ) );
			// A candidate expressed explicitly, never installed as glTF production mapping.
			// q_native=.0254^2 would make q*d_inches^2 equal d_meters^2 if the
			// hidden distance function were an unbiased reciprocal quadratic.
			point.QuadraticAttenuation = 10000f * .0254f * .0254f;
			point.LightColor = new global::Color( 15, 9.478124678134919f, 3.5864963382482533f );
			rows.Add( Pack( "point_candidate_meter_quadratic_literal_lamp_rgb", point ) );
			point.Radius = 2000;
			rows.Add( Pack( "point_same_candidate_double_finite_radius", point ) );
			spot = new SceneSpotLight( world, new global::Vector3( 11, 23, 37 ), global::Color.White )
			{
				Rotation = Rotation.LookAt( new global::Vector3( -1, 0, 0 ) ), Radius = 1000,
				QuadraticAttenuation = 10000f * .0254f * .0254f, ConeInner = 30, ConeOuter = 60, FallOff = 1, ShadowsEnabled = false
			};
			rows.Add( Pack( "spot_engine_half_degrees_30_60_emission_minus_x", spot ) );
			spot.ConeInner = 59.99f;
			rows.Add( Pack( "spot_narrow_cone_preserve_or_measure_native_clamp", spot ) );
			directional = new SceneDirectionalLight( world, Rotation.LookAt( new global::Vector3( -1, 0, 0 ) ), global::Color.White ) { ShadowsEnabled = false };
			rows.Add( Pack( "directional_constructor_color1_emission_minus_x", directional ) );
		}
		finally
		{
			try { directional?.Delete(); }
			finally
			{
				try { spot?.Delete(); }
				finally { point?.Delete(); }
			}
		}
		if ( rows.Count != ExpectedMeasurements ) throw new InvalidOperationException( "Native light audit inventory changed." );
		return rows.ToArray();
	}

	private static Observation Pack( string name, SceneLight light )
	{
		if ( !RenderContext.PackSceneLight( light, out var p, out _, out _ ) )
			throw new InvalidOperationException( "Native PackSceneLight did not return light data for " + name );
		return new( name, light.GetType().FullName, p.Type, [p.Color.x, p.Color.y, p.Color.z],
			p.LinearFalloff, p.QuadraticFalloff, p.FalloffBias, p.Radius, p.RadiusSquared,
			[p.Row0.x, p.Row1.x, p.Row2.x], [p.Row0.w, p.Row1.w, p.Row2.w],
			[p.SpotLightInnerOuterConeCosines.x, p.SpotLightInnerOuterConeCosines.y, p.SpotLightInnerOuterConeCosines.z, p.SpotLightInnerOuterConeCosines.w],
			typeof( SceneLight ).Assembly.ManifestModule.ModuleVersionId.ToString(), typeof( RenderContext ).Assembly.ManifestModule.ModuleVersionId.ToString() );
	}
}
