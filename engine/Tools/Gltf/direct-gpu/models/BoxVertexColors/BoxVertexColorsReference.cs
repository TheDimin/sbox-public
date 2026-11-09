using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

/// <summary>Frozen independent stdlib-decoded cube and explicitly authored color fixtures.</summary>
public sealed class BoxVertexColorsReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public NVector3[] Positions { get; }
	public NVector3[] Normals { get; }
	public uint[] Indices { get; }
	public Form[] Forms { get; }
	public sealed record Form( string Name, bool Compiled, bool Derived, byte[] Bytes, Func<string, byte[]> Resolve,
		int ColorComponentType, int ColorComponents, int ColorStride, NVector4[] RawColors, NVector4? Color1 )
	{
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve );
		public NVector4 DrawColor( int vertex ) => NVector4.Clamp( RawColors[vertex], NVector4.Zero, NVector4.One );
	}

	public BoxVertexColorsReference( string checkoutRoot )
	{
		var owned = Path.Combine( Path.GetFullPath( checkoutRoot ), "research/gltf-validation/direct-gpu/models/BoxVertexColors" );
		var corpus = Path.Combine( Path.GetFullPath( checkoutRoot ), "Temp/gltf-reference/glTF-Sample-Assets/Models/BoxVertexColors" );
		using var reference = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( owned, "reference.json" ) ) ); var data = reference.RootElement;
		Require( data.GetProperty( "corpus_revision" ).GetString() == CorpusRevision && data.GetProperty( "license" ).GetString() == "CC0-1.0", "Pinned oracle provenance changed." );
		Positions = Vectors3( data.GetProperty( "positions" ) ); Normals = Vectors3( data.GetProperty( "normals" ) );
		Indices = data.GetProperty( "indices" ).EnumerateArray().Select( value => value.GetUInt32() ).ToArray();
		Require( Positions.Length == 24 && Normals.Length == 24 && Indices.Length == 36, "Pinned cube inventory changed." );
		var sourceFiles = new Dictionary<string, byte[]>( StringComparer.Ordinal );
		foreach ( var item in data.GetProperty( "source_hashes" ).EnumerateObject() )
		{
			var bytes = File.ReadAllBytes( Path.Combine( corpus, item.Name ) );
			Require( Hash( bytes ) == item.Value.GetString(), "Pinned source hash differs: " + item.Name ); sourceFiles.Add( item.Name, bytes );
		}
		Require( File.ReadAllText( Path.Combine( corpus, "metadata.json" ) ).Contains( "CC0-1.0", StringComparison.Ordinal ), "Retain the source license metadata." );
		var forms = new List<Form>();
		foreach ( var source in new[] { "glTF/BoxVertexColors.gltf", "glTF-Binary/BoxVertexColors.glb", "glTF-Embedded/BoxVertexColors.gltf" } )
		{
			byte[] Resolve( string uri ) => uri == "buffer.bin" ? sourceFiles["glTF/buffer.bin"] : throw new InvalidOperationException( "Unexpected dependency: " + uri );
			var colors = Positions.Select( value => new NVector4( value, 1 ) ).ToArray(); var bytes = sourceFiles[source];
			var variant = source[..source.IndexOf( '/' )];
			forms.Add( new( variant + "/source", false, false, bytes, Resolve, 5126, 3, 12, colors, null ) );
			forms.Add( new( variant + "/compiled_glb", true, false, GltfCompiler.Compile( bytes, Resolve ), Resolve, 5126, 3, 12, colors, null ) );
		}
		using var fixtures = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( owned, "fixtures.json" ) ) );
		foreach ( var item in fixtures.RootElement.EnumerateArray() )
		{
			var name = item.GetProperty( "name" ).GetString(); var path = item.GetProperty( "path" ).GetString();
			var bytes = File.ReadAllBytes( Path.Combine( owned, "fixtures", path ) ); Require( Hash( bytes ) == item.GetProperty( "sha256" ).GetString(), "Frozen fixture changed: " + path );
			byte[] Resolve( string uri ) => throw new InvalidOperationException( "Self-contained fixture requested " + uri );
			var colors = Vectors4( item.GetProperty( "expected_colors" ) ); var color1 = Vector4( item.GetProperty( "expected_color1" ) );
			foreach ( var compiled in new[] { false, true } ) forms.Add( new( "derived/" + name + (compiled ? "/compiled_glb" : "/source"), compiled, true,
				compiled ? GltfCompiler.Compile( bytes, Resolve ) : bytes, Resolve, item.GetProperty( "component_type" ).GetInt32(), item.GetProperty( "components" ).GetInt32(), item.GetProperty( "stride" ).GetInt32(), colors, color1 ) );
		}
		Forms = forms.ToArray(); Require( Forms.Length == 20, "Frozen source/compiled/fixture form inventory changed." );
	}

	public NVector3 EnginePosition( int sourceVertex ) => EngineDirection( Positions[sourceVertex] ) / .0254f;
	public NVector3 EngineNormal( int sourceVertex ) => EngineDirection( Normals[sourceVertex] );
	public static NVector3 EngineDirection( NVector3 value ) => new( value.Z, value.X, value.Y );
	private static NVector3[] Vectors3( JsonElement rows ) => rows.EnumerateArray().Select( row => new NVector3( row[0].GetSingle(), row[1].GetSingle(), row[2].GetSingle() ) ).ToArray();
	private static NVector4[] Vectors4( JsonElement rows ) => rows.EnumerateArray().Select( Vector4 ).ToArray();
	private static NVector4 Vector4( JsonElement row ) => new( row[0].GetSingle(), row[1].GetSingle(), row[2].GetSingle(), row[3].GetSingle() );
	private static string Hash( byte[] value ) => Convert.ToHexStringLower( SHA256.HashData( value ) );
	internal static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
