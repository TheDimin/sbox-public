using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

/// <summary>
/// Independent literal expectations for one pinned model. This is neither a
/// replacement glTF producer nor the implementation's GPU input data.
/// </summary>
public static class TextureTransformReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public const string Extension = "KHR_texture_transform";
	public static readonly IReadOnlySet<string> Selected = new HashSet<string>( StringComparer.Ordinal ) { Extension };
	public static readonly string[] ImageNames = { "UV.png", "Arrow.png", "Correct.png", "NotSupported.png", "Error.png" };
	public static readonly NVector3[] Positions = { new( -.5f, .5f, 0 ), new( .5f, .5f, 0 ), new( .5f, -.5f, 0 ), new( -.5f, -.5f, 0 ) };
	public static readonly uint[] Indices = { 0, 2, 1, 2, 0, 3 };
	public static readonly NVector2[] ProbeUvs = { new( 0, 0 ), new( 1, 0 ), new( 1, 1 ), new( 0, 1 ), new( .25f, .75f ), new( -.125f, 1.25f ) };
	public static readonly int[] NodeMeshes = { 0, 1, 2, 3, 6, 7, 8, 4, 6, 7, 5, 6 };
	private static readonly Dictionary<string, string> PinnedHashes = new( StringComparer.Ordinal )
	{
		["TextureTransformTest.gltf"] = "c22c8c6c96c0ea4bcbb9b47ea245a093c5ef59acc5fd425effa4c00da4cdf164",
		["TextureTransformTest.bin"] = "d0cdd23f2fa0996a0db99d4932fb9912a51a34e27c8bf832d072dc856ee9a7af",
		["UV.png"] = "ac37ff52fe06a4c8c35ad4e1e7e8da039dfa8afc9b629d40a44ca8b8cfe9d03d",
		["Arrow.png"] = "5df5b251fed0ac306cf859a30b59a4953d152483a4bed2b84e5bba1c667a1e64",
		["Correct.png"] = "3a14b12635ebbcee3ea427fbdb4d20da73e4740ec7a51683d700a2d6b4b7861a",
		["NotSupported.png"] = "1fbdeca7e5c105d677a39578e4bab82b89c8aa9f406ee681ede05635ea0f8c74",
		["Error.png"] = "8d1032ef535a5c379bf78f9f565d3180ee3fc2d1cfef6892dee6e567728b619c"
	};

	public sealed class Inputs : IDisposable
	{
		public byte[] Bytes { get; }
		public GltfDocument Document { get; }
		public IReadOnlyDictionary<string, byte[]> Dependencies { get; }
		public IReadOnlyDictionary<string, string> Hashes { get; }
		private readonly byte[] original;

		public Inputs( string checkoutRoot, bool compiled, bool supports = true )
		{
			if ( string.IsNullOrWhiteSpace( checkoutRoot ) ) throw new ArgumentException( "Set the engine checkout path explicitly.", nameof( checkoutRoot ) );
			var root = Path.GetFullPath( checkoutRoot );
			using var manifest = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( root, "research/gltf-validation/corpus-case-manifest.json" ) ) );
			if ( manifest.RootElement.GetProperty( "corpus" ).GetProperty( "commit" ).GetString() != CorpusRevision ) throw new InvalidOperationException( "Corpus manifest revision changed." );
			var directory = Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/TextureTransformTest/glTF" );
			var dependencies = new Dictionary<string, byte[]>( StringComparer.Ordinal );
			var hashes = new Dictionary<string, string>( StringComparer.Ordinal );
			foreach ( var pair in PinnedHashes )
			{
				var path = Path.Combine( directory, pair.Key );
				var info = new FileInfo( path );
				if ( !info.Exists || info.Length is < 1 or > 32768 ) throw new InvalidOperationException( "Missing or oversized pinned model input: " + path );
				var bytes = File.ReadAllBytes( path ); var hash = Convert.ToHexStringLower( SHA256.HashData( bytes ) );
				if ( hash != pair.Value ) throw new InvalidOperationException( "Pinned input changed: " + path );
				dependencies.Add( pair.Key, bytes ); hashes.Add( pair.Key, hash );
			}
			Dependencies = dependencies; Hashes = hashes;
			original = dependencies["TextureTransformTest.gltf"];
			Bytes = compiled ? GltfCompiler.Compile( original, Resolve, Selected ) : original;
			Document = GltfReader.Read( Bytes, Resolve, supports ? Selected : null );
		}

		public byte[] Resolve( string uri ) => Dependencies.TryGetValue( uri, out var bytes ) ? bytes : throw new InvalidOperationException( "Unexpected model dependency: " + uri );
		public void Dispose() => Document.Dispose();
		private JsonNode Source() => JsonNode.Parse( original ) ?? throw new InvalidOperationException( "Missing source JSON." );
		private static byte[] Encode( JsonNode value ) => Encoding.UTF8.GetBytes( value.ToJsonString() );

		public byte[] RequiredFixture()
		{
			var value = Source(); value["extensionsRequired"] = new JsonArray( Extension ); return Encode( value );
		}

		public byte[] NegativeScaleFixture()
		{
			var value = Source();
			value["materials"][0]["pbrMetallicRoughness"]["baseColorTexture"]["extensions"][Extension] =
				JsonNode.Parse( "{\"offset\":[5,-7],\"scale\":[-2,3]}" );
			return Encode( value );
		}

		public byte[] PerRoleFixture()
		{
			var value = Source(); var material = value["materials"][0]; var pbr = material["pbrMetallicRoughness"];
			pbr["baseColorTexture"] = JsonNode.Parse( "{\"index\":0,\"texCoord\":0,\"extensions\":{\"KHR_texture_transform\":{\"texCoord\":1,\"offset\":[0.5,0]}}}" );
			pbr["metallicRoughnessTexture"] = JsonNode.Parse( "{\"index\":1,\"extensions\":{\"KHR_texture_transform\":{\"offset\":[0,0.25],\"rotation\":1.5707963267948966,\"scale\":[2,3]}}}" );
			material["normalTexture"] = JsonNode.Parse( "{\"index\":0,\"scale\":0.25,\"extensions\":{\"KHR_texture_transform\":{\"texCoord\":1,\"offset\":[-0.2,0.1],\"rotation\":-0.3,\"scale\":[0.5,2]}}}" );
			material["occlusionTexture"] = JsonNode.Parse( "{\"index\":0,\"texCoord\":1,\"strength\":0.4}" );
			material["emissiveTexture"] = JsonNode.Parse( "{\"index\":1,\"extensions\":{\"KHR_texture_transform\":{\"offset\":[0,1],\"scale\":[1,-1]}}}" );
			material["emissiveFactor"] = new JsonArray( 1, 1, 1 );
			value["meshes"][0]["primitives"][0]["attributes"]["TEXCOORD_1"] = 1;
			return Encode( value );
		}
	}

	public static NVector2 SourceUv( int mesh, int vertex )
	{
		var uv = vertex switch { 0 => new NVector2( 0, 0 ), 1 => new NVector2( 1, 0 ), 2 => new NVector2( 1, 1 ), 3 => new NVector2( 0, 1 ), _ => throw new ArgumentOutOfRangeException( nameof( vertex ) ) };
		return mesh < 3 ? uv * .5f : uv;
	}

	public static double[] SampleUv( int material, double u, double v ) => material switch
	{
		0 => new[] { u + .5, v }, 1 => new[] { u, v + .5 }, 2 => new[] { u + .5, v + .5 },
		3 => Transform( u, v, 0, 0, 1, 1, Math.PI / 8 ), 4 => new[] { u * 1.5, v * 1.5 },
		5 => Transform( u, v, -.2, -.1, 1.5, 1.5, .3 ),
		6 or 7 or 8 => new[] { u, v }, _ => throw new ArgumentOutOfRangeException( nameof( material ) )
	};

	public static double[] RoleUv( int role, double u, double v ) => role switch
	{
		0 => new[] { u + .5, v }, 1 => Transform( u, v, 0, .25, 2, 3, Math.PI / 2 ),
		2 => Transform( u, v, -.2, .1, .5, 2, -.3 ), 3 => new[] { u, v }, 4 => new[] { u, 1 - v },
		_ => throw new ArgumentOutOfRangeException( nameof( role ) )
	};

	/// <summary>Double precision Khronos Sample Renderer affine arithmetic, independent of TransformUv.</summary>
	public static double[] Transform( double u, double v, double offsetU, double offsetV, double scaleU, double scaleV, double angle )
	{
		var x = u * scaleU; var y = v * scaleV;
		return new[] { offsetU + Math.Cos( angle ) * x + Math.Sin( angle ) * y, offsetV - Math.Sin( angle ) * x + Math.Cos( angle ) * y };
	}

	public static NVector3 WorldPosition( int node, int vertex )
	{
		var p = Positions[vertex];
		var (x, y, z, scale) = node switch
		{
			0 => (-1.1, .55, 0d, 1d), 1 => (0d, .55, 0d, 1d), 2 => (1.1, .55, 0d, 1d),
			3 => (-1.1, -.55, 0d, 1d),
			4 => (-1.1 - .07904822439840126, -.55 - .5162674857624154, .01, .15),
			5 => (-1.1 + .2778174593052023, -.55 - .2778174593052023, .01, .15),
			6 => (-1.1 + .5162674857624154, -.55 + .07904822439840126, .01, .15),
			7 => (0d, -.55, 0d, 1d),
			8 => (.018544972870134853, -.55 - .018544972870134853, .01, .1),
			9 => (.2778174593052023, -.55 - .2778174593052023, .01, .15),
			10 => (1.1, -.55, 0d, 1d), 11 => (1.1 - .07, -.55 - .25, .01, .1),
			_ => throw new ArgumentOutOfRangeException( nameof( node ) )
		};
		return new NVector3( (float)(p.X * scale + x), (float)(p.Y * scale + y), (float)(p.Z * scale + z) );
	}

	public static NVector3 EnginePosition( int node, int vertex )
	{
		var p = WorldPosition( node, vertex ); return new NVector3( p.Z, p.X, p.Y ) / .0254f;
	}
}
