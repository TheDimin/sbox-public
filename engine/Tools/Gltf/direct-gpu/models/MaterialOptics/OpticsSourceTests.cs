using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.MaterialOpticsValidation;

/// <summary>
/// Pinned source and independent numerical fixtures, not renderer or decoder tests.
/// GPU tests must compare their actual output against these values separately.
/// </summary>
public static class OpticsSourceTests
{
	public sealed record Result( string Name, bool Passed, string Error );
	/// <summary>
	/// Checks diagnostic values read from the implementation under test. Inputs
	/// are: transmission factor .5 with RGBA (.2,.8,.6,1), thickness factor
	/// 2.27 with RGBA (.8,.25,.6,1), IOR 2.42, attenuation green .64 at
	/// one and two attenuation distances, and dispersion 5 at IOR 2.42.
	/// The caller must supply actual evaluated values; image acceptance is separate.
	/// </summary>
	public static void CheckDiagnosticValues( double transmission, double thickness, double f0, double attenuationOne, double attenuationTwo, double redIor, double blueIor )
	{
		Near( transmission, .1 ); Near( thickness, .5675 ); Near( f0, .17239492493416775 );
		Near( attenuationOne, .64 ); Near( attenuationTwo, .4096 ); Near( redIor, 2.2425 ); Near( blueIor, 2.5975 );
	}
	public static Result[] RunAll( string checkoutRoot )
	{
		var rows = new List<Result>();
		void Run( string name, Action action )
		{
			try { action(); rows.Add( new( name, true, null ) ); }
			catch ( Exception e ) { rows.Add( new( name, false, e.ToString() ) ); }
		}
		JsonDocument Source( string name, string hash )
		{
			var path = Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models", name, "glTF", name + ".gltf" );
			var bytes = File.ReadAllBytes( path );
			Require( Convert.ToHexStringLower( SHA256.HashData( bytes ) ) == hash, name + " source identity changed" );
			return JsonDocument.Parse( bytes );
		}
		Run( "compare_dispersion_source", () =>
		{
			using var d = Source( "CompareDispersion", "265ad2d90f81776f9977e84c1a0c0ea901ce63f2726f3548b54dde82bbd84671" );
			var a = Ext( d, 1 ); var b = Ext( d, 2 );
			foreach ( var e in new[] { a, b } )
			{
				Near( N( e, "KHR_materials_ior", "ior" ), 2.42 );
				Near( N( e, "KHR_materials_transmission", "transmissionFactor" ), 1 );
				Require( e.GetProperty( "KHR_materials_transmission" ).GetProperty( "transmissionTexture" ).GetProperty( "index" ).GetInt32() == 0, "transmission texture" );
				Near( N( e, "KHR_materials_volume", "thicknessFactor" ), .5 );
			}
			Require( !a.TryGetProperty( "KHR_materials_dispersion", out _ ), "zero dispersion must be implicit" );
			Near( N( b, "KHR_materials_dispersion", "dispersion" ), 5 );
		} );
		Run( "dragon_attenuation_source", () =>
		{
			using var d = Source( "DragonAttenuation", "bd97e0271b94edaf6eba1b9ea338bd0c990e18c1a9085a1c72aaadd8aba4b66b" );
			var v = Ext( d, 1 ).GetProperty( "KHR_materials_volume" );
			Near( v.GetProperty( "thicknessFactor" ).GetDouble(), 2.27 );
			Near( v.GetProperty( "attenuationDistance" ).GetDouble(), .155 );
			var c = v.GetProperty( "attenuationColor" ); Near( c[0].GetDouble(), .921 ); Near( c[1].GetDouble(), .64 ); Near( c[2].GetDouble(), .064 );
			Require( v.GetProperty( "thicknessTexture" ).GetProperty( "index" ).GetInt32() == 1, "thickness texture" );
		} );
		Run( "dragon_dispersion_source", () =>
		{
			using var d = Source( "DragonDispersion", "13d583166b382eab4f3b83a721172f8a0a992f037dbe9e82070274eb54fb7a94" );
			var e = Ext( d, 0 ); Near( N( e, "KHR_materials_ior", "ior" ), 1.75 ); Near( N( e, "KHR_materials_dispersion", "dispersion" ), 2.04 );
			Near( N( e, "KHR_materials_volume", "thicknessFactor" ), 2.27 ); Near( N( e, "KHR_materials_volume", "attenuationDistance" ), .155 );
		} );
		Run( "dispersion_grid_source", () =>
		{
			using var d = Source( "DispersionTest", "92aa352b110a971808279647ce5608fd684d588dee3106ab1317ef01cef5b638" );
			Near( N( Ext( d, 7 ), "KHR_materials_ior", "ior" ), 1.33 ); Near( N( Ext( d, 11 ), "KHR_materials_dispersion", "dispersion" ), 5 );
			Require( !Ext( d, 12 ).TryGetProperty( "KHR_materials_ior", out _ ), "default IOR should be implicit" );
		} );
		Run( "ior_grid_source", () =>
		{
			using var d = Source( "IORTestGrid", "3b02fa47725d55f7ca1f056d259174b522cde8571979b1c01b8631c9e266c8c1" );
			Near( N( Ext( d, 1 ), "KHR_materials_ior", "ior" ), 1 ); Near( N( Ext( d, 5 ), "KHR_materials_ior", "ior" ), 2.42 );
			Near( N( Ext( d, 1 ), "KHR_materials_transmission", "transmissionFactor" ), 0 ); Near( N( Ext( d, 12 ), "KHR_materials_transmission", "transmissionFactor" ), 1 );
		} );
		Run( "pawn_source", () =>
		{
			using var d = Source( "ABeautifulGame", "2e1b63ae34173c6e3251f749b1b8b97a32e92517aab2cfd4bfffd9158f4dd278" );
			foreach ( var i in new[] { 5, 7 } ) { var e = Ext( d, i ); Near( N( e, "KHR_materials_transmission", "transmissionFactor" ), 1 ); Near( N( e, "KHR_materials_volume", "thicknessFactor" ), .22 ); }
		} );
		Run( "independent_numeric_fixtures", () =>
		{
			// Synthetic channels differ intentionally: R×factor != G×factor.
			Near( .2 * .5, .1 ); Near( 2.27 * .25, .5675 );
			Near( Math.Pow( (2.42 - 1) / (2.42 + 1), 2 ), .17239492493416775 );
			Near( Math.Pow( (1.5 - 1) / (1.5 + 1), 2 ), .04 );
			// At one attenuation distance transmittance equals attenuationColor; at two it is squared.
			Near( Math.Pow( .64, .155 / .155 ), .64 ); Near( Math.Pow( .64, .310 / .155 ), .4096 );
			var spread = (2.42 - 1) * .025 * 5; Near( 2.42 - spread, 2.2425 ); Near( 2.42 + spread, 2.5975 );
			Near( (1.0 - 1) * .025 * 5, 0 );
		} );
		return rows.ToArray();
	}
	private static JsonElement Ext( JsonDocument d, int i ) => d.RootElement.GetProperty( "materials" )[i].GetProperty( "extensions" );
	private static double N( JsonElement e, string extension, string property ) => e.GetProperty( extension ).GetProperty( property ).GetDouble();
	private static void Near( double actual, double expected ) => Require( Math.Abs( actual - expected ) < 1e-6, $"Expected {expected}, got {actual}." );
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
