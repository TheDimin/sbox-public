using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;

public static class MaterialsVariantsShoeReference
{
	public const string Extension = "KHR_materials_variants";
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly IReadOnlySet<string> Selected = new HashSet<string>( StringComparer.Ordinal ) { Extension };
	public static readonly string[] VariantNames = { "midnight", "beach", "street" };
	public static readonly int[] BaseColorTextures = { 1, 3, 4 };
	public const int VertexCount = 13540;
	public const int IndexCount = 68100;

	public sealed record Form( string Name, byte[] Bytes, Func<string, byte[]> Resolve, JsonElement Oracle )
	{
		public GltfDocument Read( bool supports = true ) => GltfReader.Read( Bytes, Resolve, supports ? Selected : null );
	}

	public sealed class Inputs
	{
		public IReadOnlyList<Form> Forms { get; }
		public byte[] SourceJson { get; }
		public Func<string, byte[]> SourceResolve { get; }
		public JsonElement Oracle { get; }
		public Inputs( string checkoutRoot )
		{
			if ( string.IsNullOrWhiteSpace( checkoutRoot ) ) throw new ArgumentException( "Supply the engine checkout root." );
			var root = Path.GetFullPath( checkoutRoot );
			using var oracle = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( root, "research/gltf-validation/direct-gpu/models/MaterialsVariantsShoe/reference.json" ) ) );
			Oracle = oracle.RootElement.Clone();
			Require( Oracle.GetProperty( "revision" ).GetString() == CorpusRevision, "Oracle corpus revision changed." );
			var model = Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/MaterialsVariantsShoe" );
			var forms = new List<Form>();
			foreach ( var variant in new[] { "glTF", "glTF-Binary" } )
			{
				var expected = Oracle.GetProperty( "forms" ).GetProperty( variant );
				var files = new Dictionary<string, byte[]>( StringComparer.Ordinal );
				foreach ( var file in expected.GetProperty( "files" ).EnumerateObject() )
				{
					var path = Path.Combine( model, file.Name ); var info = new FileInfo( path );
					Require( info.Exists && info.Length == file.Value.GetProperty( "length" ).GetInt32() && info.Length <= 8_000_000, "Pinned input missing or size changed: " + path );
					var bytes = File.ReadAllBytes( path );
					Require( Hash( bytes ) == file.Value.GetProperty( "sha256" ).GetString(), "Pinned input identity changed: " + path );
					files.Add( file.Name, bytes );
				}
				var source = files[expected.GetProperty( "source" ).GetString()];
				byte[] Resolve( string uri ) => files.TryGetValue( variant + "/" + uri, out var bytes ) ? bytes : throw new InvalidOperationException( "Unexpected pinned dependency: " + uri );
				forms.Add( new Form( variant + "/source", source, Resolve, expected ) );
				forms.Add( new Form( variant + "/compiled_glb", GltfCompiler.Compile( source, Resolve, Selected ), Resolve, expected ) );
				if ( variant == "glTF" ) { SourceJson = source; SourceResolve = Resolve; }
			}
			Forms = forms.AsReadOnly();
		}

		public byte[] Fixture( Action<JsonNode> change )
		{
			var root = JsonNode.Parse( SourceJson ); change( root ); return Encoding.UTF8.GetBytes( root.ToJsonString() );
		}
		public GltfDocument ReadFixture( Action<JsonNode> change, bool supports = true ) => GltfReader.Read( Fixture( change ), SourceResolve, supports ? Selected : null );
	}

	public static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	public static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
	public static double[] Numbers( JsonElement value ) => value.EnumerateArray().Select( x => x.GetDouble() ).ToArray();
}
