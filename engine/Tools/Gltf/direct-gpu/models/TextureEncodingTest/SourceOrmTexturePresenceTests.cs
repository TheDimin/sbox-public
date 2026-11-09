using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Actual candidate uniform/cache assertions; independent image acceptance remains in the unchanged emission gate.</summary>
[TestClass]
public sealed class SourceOrmTexturePresenceTests
{
	private const string GammaPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAABGdBTUEAAE4g3rEiDgAAAAxJREFUCNdjYOj4DwACEwGItIREUAAAAABJRU5ErkJggg==";
	private static readonly string[] Roles = ["BaseColor", "MetallicRoughness", "Occlusion", "Emissive"];
	private static string manifestPath, manifestHash, sourceHash;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string path, string hash, string source ) { manifestPath = path; manifestHash = hash; sourceHash = source; Measurements.Clear(); }

	[TestMethod]
	public async Task FourRolesHaveExactPresenceFlagsOnActualSourceAndPortableMaterialLeases()
	{
		Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && Graphics.IsAvailable && !Graphics.IsActive && ThreadSafe.IsMainThread );
		Measurements.Clear(); using var session = await SurfaceOrmCandidateGate.AcquireReviewedTemplate( manifestPath, manifestHash, sourceHash );
		var cases = 0;
		// All absent, all present and each role independently present catches a
		// swapped role/flag as well as a flag accidentally inherited from a peer.
		foreach ( var mask in new[] { 0, 15, 1, 2, 4, 8 } ) foreach ( var portable in new[] { false, true } )
		{
			var source = Fixture( mask ); using var document = GltfReader.Read( portable ? GltfCompiler.Compile( source ) : source );
			var material = document.ReadMaterial( 0 ); var prepared = GltfStockMaterialAdapter.PrepareSource( document, material );
			var uv = GltfMaterialUvPlan.Create( material, document.MaterializePrimitiveUpload( 0, 0 ) );
			using var first = GltfStockMaterialAdapter.AcquireSource( prepared, session.Value, uv );
			using var second = GltfStockMaterialAdapter.AcquireSource( prepared, session.Value, uv );
			Assert.AreSame( first.Value, second.Value, "Repeated exact flags, factors, textures and transforms should share the native material." );
			var observed = new List<object>();
			for ( var index = 0; index < Roles.Length; index++ )
			{
				var name = "g_flGltfHas" + Roles[index] + "Texture"; var expected = (mask & (1 << index)) == 0 ? 0f : 1f;
				Assert.IsTrue( session.Value.HasParameter( name ), "The reviewed template must actually declare " + name );
				var actual = first.Value.GetVector4( name ).x; observed.Add( new { role = Roles[index], parameter = name, expected, actual } );
				Assert.AreEqual( expected, actual, "Presence flags are exact deterministic0/1 material scalars: " + name );
			}
			var retained = second.Value; first.Dispose(); Assert.IsTrue( retained.IsValid && ReferenceEquals( retained, second.Value ), "A live peer must survive one lease release." );
			Measurements.Add( new { mask, portable, observed, resource_roles = prepared.Resources.Keys.Select( key => key.ToString() ).Order().ToArray(),
				scope = "Actual template presence scalar/cache/peer lifetime only; absent-role pixels are separately compared by unchanged SourceOrmEmissionGpuTests." } ); cases++;
		}
		Assert.AreEqual( 12, cases );
	}
	private static byte[] Fixture( int mask )
	{
		var root = JsonNode.Parse( """
			{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"type":"VEC3","count":3,"min":[0,0,0],"max":[0,0,0]},{"componentType":5126,"type":"VEC2","count":3}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1},"material":0}]}],"images":[{"uri":"data:image/png;base64,"}],"textures":[{"source":0}],"materials":[{"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.3,0.4,0.5],"metallicFactor":0.1,"roughnessFactor":0.9},"emissiveFactor":[0.25,0.5,0.75]}]}
			""" )!.AsObject();
		root["images"]![0]!["uri"] = "data:image/png;base64," + GammaPng; var material = root["materials"]![0]!;
		if ( (mask & 1) != 0 ) material["pbrMetallicRoughness"]!["baseColorTexture"] = JsonNode.Parse( """{"index":0}""" );
		if ( (mask & 2) != 0 ) material["pbrMetallicRoughness"]!["metallicRoughnessTexture"] = JsonNode.Parse( """{"index":0}""" );
		if ( (mask & 4) != 0 ) material["occlusionTexture"] = JsonNode.Parse( """{"index":0}""" );
		if ( (mask & 8) != 0 ) material["emissiveTexture"] = JsonNode.Parse( """{"index":0}""" );
		return Encoding.UTF8.GetBytes( root.ToJsonString() );
	}
}
