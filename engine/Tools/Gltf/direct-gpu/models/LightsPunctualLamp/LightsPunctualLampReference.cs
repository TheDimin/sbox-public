using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.LightsPunctualLampValidation;

/// <summary>Pinned JSON/GLB oracle authored by Python, independent of the producer's light evaluator.</summary>
public sealed class LightsPunctualLampReference
{
	public const string Extension = "KHR_lights_punctual";
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly IReadOnlySet<string> Selected = new HashSet<string>( StringComparer.Ordinal ) { Extension };
	public JsonElement Oracle { get; }
	public IReadOnlyList<Form> Forms { get; }
	public sealed record Form( string Name, string Variant, bool Compiled, byte[] Bytes, Func<string, byte[]> Resolve )
	{
		public GltfDocument Read( bool selected = true ) => GltfReader.Read( Bytes, Resolve, selected ? Selected : null );
	}

	public LightsPunctualLampReference( string checkoutRoot )
	{
		var root = Path.GetFullPath( checkoutRoot );
		using var json = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( root, "research/gltf-validation/direct-gpu/models/LightsPunctualLamp/reference.json" ) ) );
		Oracle = json.RootElement.Clone();
		Require( Oracle.GetProperty( "revision" ).GetString() == CorpusRevision, "Independent corpus pin changed." );
		var files = new Dictionary<string, byte[]>( StringComparer.Ordinal );
		foreach ( var file in Oracle.GetProperty( "files" ).EnumerateObject() )
		{
			var path = Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/LightsPunctualLamp", file.Name );
			var info = new FileInfo( path );
			Require( info.Exists && info.Length == file.Value.GetProperty( "length" ).GetInt32() && info.Length < 8 * 1024 * 1024, "Pinned input size changed: " + path );
			var bytes = File.ReadAllBytes( path );
			Require( Hash( bytes ) == file.Value.GetProperty( "sha256" ).GetString(), "Pinned input hash changed: " + path );
			files.Add( file.Name, bytes );
		}
		byte[] Resolve( string uri ) => !uri.Contains( '/' ) && !uri.Contains( '\\' ) && files.TryGetValue( "glTF/" + uri, out var bytes )
			? bytes : throw new InvalidOperationException( "Unexpected relative dependency: " + uri );
		var forms = new List<Form>();
		foreach ( var variant in new[] { "glTF", "glTF-Binary" } )
		{
			var source = files[variant + "/LightsPunctualLamp." + (variant == "glTF" ? "gltf" : "glb")];
			forms.Add( new( variant + "/source", variant, false, source, Resolve ) );
			forms.Add( new( variant + "/compiled_glb_c", variant, true, GltfCompiler.Compile( source, Resolve, Selected ), Resolve ) );
		}
		Forms = forms.AsReadOnly();
	}

	public JsonElement Variant( Form form ) => Oracle.GetProperty( "variants" ).GetProperty( form.Variant );
	public static double[] Numbers( JsonElement value ) => value.EnumerateArray().Select( x => x.GetDouble() ).ToArray();
	public static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	public static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
	public static void Close( double expected, double actual, double tolerance, string label ) => Require( double.IsFinite( actual ) && Math.Abs( expected - actual ) <= tolerance,
		$"{label}: expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}." );
}
