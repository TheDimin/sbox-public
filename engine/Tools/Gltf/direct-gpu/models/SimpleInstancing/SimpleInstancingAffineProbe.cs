using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using static SimpleInstancingReference;

/// <summary>Explicit synthetic EXT fixture: authored tangents, valid TRS hierarchy, six scale regimes. No producer decoder is duplicated.</summary>
internal static class SimpleInstancingAffineProbe
{
	public const int ExpectedMeasurements = 6;
	public const int FixtureInstances = 6;
	private static readonly float[][] Scales = { new[] { 1f, 1.25f, 2f }, new[] { -1f, 1.25f, 2f }, new[] { 1e-30f, 1e-30f, 1e-30f }, new[] { -1e-30f, 2e-20f, 3e-10f }, new[] { 0f, 1.25f, 2f }, new[] { 0f, 0f, 0f } };
	private static readonly Matrix4x4 NodeWorld = new( .6f * 2, .8f * 3, 0, 0, -.8f * 2, .6f * 3, 0, 0, 0, 0, 4, 0, 3, -5, 7, 1 );
	public sealed record Measurement( string Form, string Placement, int Positions, int DefinedNormalTangentFrames, int SingularFramesWithFiniteOutput, double PositionErrorInches, double NormalError, double TangentError, double HandednessError );
	public static async Task<IReadOnlyList<Measurement>> Run( string checkoutRoot, SceneWorld world, Material computeMaterial, Func<Task> waitForActualFrames, Func<int?, Material> materialResolver = null )
	{
		var reference = new SimpleInstancingReference( checkoutRoot ); var rows = new List<Measurement>();
		foreach ( var form in Forms( reference ) )
		{
			using var d = form.Read(); using var asset = GltfGpuAsset.Create( d, computeMaterial, materialResolver ); using var instance = asset.CreateInstance( world );
			foreach ( var (name, placement) in new[] { ("identity", Matrix4x4.Identity), ("affine_shear", AffinePlacement), ("mirrored_affine_shear", MirroredPlacement) } )
			{
				instance.Placement = placement; await waitForActualFrames(); Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0, "No completed current affine fixture GPU submission." );
				rows.Add( Check( reference, form.Name, name, instance.Readback(), placement ) );
			}
		}
		Require( rows.Count == ExpectedMeasurements, "Frozen affine GPU measurement inventory changed." ); return rows.AsReadOnly();
	}
	private static Measurement Check( SimpleInstancingReference reference, string form, string placementName, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, Matrix4x4 placement )
	{
		Require( snapshots.Count == FixtureInstances, "All six authored instances, including tiny/zero diagnostic rows, must retain identity." );
		var idProperty = typeof( GltfGpuPrimitiveSnapshot ).GetProperty( "InstanceIndex" ); Require( idProperty?.PropertyType == typeof( int? ), "Pending nullable instance identity; affine GPU acceptance incomplete." );
		var ids = snapshots.Select( x => (int?)idProperty.GetValue( x ) ).ToArray(); Require( ids.All( x => x.HasValue ) && ids.Select( x => x.Value ).Order().SequenceEqual( Enumerable.Range( 0, FixtureInstances ) ), "Duplicate/missing source instance identity." );
		var positions = 0; var frames = 0; var singular = 0; var pe = 0d; var ne = 0d; var te = 0d; var we = 0d;
		foreach ( var row in snapshots )
		{
			var id = ((int?)idProperty.GetValue( row )).Value; Require( row.Node == 0 && row.Mesh == 0 && row.Primitive == 0 && row.VertexCount == VertexCount && row.SourceVertices.SequenceEqual( Enumerable.Range( 0, VertexCount ).Select( x => (uint)x ) ), "Fixture source identity/correspondence changed." );
			Require( row.Bytes.Length == row.VertexStride * VertexCount && row.IndexDataOrigin == "gpu-readback" && row.Indices.Length == IndexCount, "Expected completed vertex bytes and actual bound GPU indices." );
			var p = Field( "POSITION", 3 ); var n = Field( "NORMAL", 3 ); var t = Field( "TANGENT", 4 ); var sign = 0;
			for ( var v = 0; v < VertexCount; v++ )
			{
				var normal = reference.Value( 2, v ); var expected = SimpleInstancingAffineReference.Transform( reference.Value( 1, v ), normal, Tangent( normal ), 1, Local( id ), NodeWorld, placement );
				var actualP = Read( p, v ); var actualN = Read( n, v ); var actualT = Read( t, v ); pe = Math.Max( pe, Error( expected.P, actualP ) ); positions++;
				if ( expected.NormalDefined ) { ne = Math.Max( ne, Error( expected.N, actualN ) ); te = Math.Max( te, Error( expected.T, actualT.Take( 3 ).ToArray() ) ); we = Math.Max( we, Math.Abs( expected.W - actualT[3] ) ); frames++; sign = expected.DeterminantSign; }
				else singular++; // No unique inverse-transpose direction at exact singularity; finite output is an engine robustness check only.
			}
			if ( sign != 0 )
			{
				var indices = reference.Indices; if ( sign < 0 ) for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]); Require( row.Indices.SequenceEqual( indices ), "Tiny/reflected instance lost actual bound winding sign." );
			}
			GltfGpuField Field( string name, int count ) { var f = row.Fields.Single( x => x.Semantic == name && x.SemanticIndex == 0 ); Require( f.ComponentType == 5126 && f.Components == count && f.Offset >= 0 && f.Offset + count * 4 <= row.VertexStride, "Invalid runtime frame field." ); return f; }
			double[] Read( GltfGpuField field, int vertex ) => Enumerable.Range( 0, field.Components ).Select( c => { var x = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + c * 4 ) ); Require( float.IsFinite( x ), "Nonfinite affine/singular output." ); return (double)x; } ).ToArray();
		}
		Require( pe <= .0005 && ne <= 3e-6 && te <= 3e-6 && we <= 1e-6, $"Affine GPU frame errors exceed candidate bounds: position {pe:R}in, normal {ne:R}, tangent {te:R}, W {we:R}." );
		Require( positions == 144 && frames == 96 && singular == 48, "Tiny invertible transforms were mistaken for singular normals or zero instances were dropped." );
		return new( form, placementName, positions, frames, singular, pe, ne, te, we );
	}
	private static Form[] Forms( SimpleInstancingReference reference )
	{
		var source = reference.Forms.Single( x => x.Name == "glTF/source" ); var root = JsonNode.Parse( source.Bytes ); var data = new byte[624];
		for ( var i = 0; i < FixtureInstances; i++ )
		{
			Write( i * 12, new[] { 2.5f, 5f, 7.5f } ); Write( 72 + i * 16, new[] { .5f, .5f, .5f, .5f } ); Write( 168 + i * 12, Scales[i] );
		}
		for ( var v = 0; v < VertexCount; v++ ) { var t = Tangent( reference.Value( 2, v ) ); Write( 240 + v * 16, new[] { (float)t[0], (float)t[1], (float)t[2], 1f } ); }
		root["buffers"].AsArray().Add( new JsonObject { ["uri"] = "affine-fixture.bin", ["byteLength"] = data.Length } );
		foreach ( var (offset, length) in new[] { (0, 72), (72, 96), (168, 72), (240, 384) } ) root["bufferViews"].AsArray().Add( new JsonObject { ["buffer"] = 1, ["byteOffset"] = offset, ["byteLength"] = length } );
		for ( var i = 3; i <= 5; i++ ) root["accessors"][i] = new JsonObject { ["bufferView"] = i + 3, ["byteOffset"] = 0, ["componentType"] = 5126, ["count"] = FixtureInstances, ["type"] = i == 4 ? "VEC4" : "VEC3" };
		root["accessors"].AsArray().Add( new JsonObject { ["bufferView"] = 9, ["byteOffset"] = 0, ["componentType"] = 5126, ["count"] = VertexCount, ["type"] = "VEC4" } ); root["meshes"][0]["primitives"][0]["attributes"]["TANGENT"] = 6;
		root["nodes"][0]["matrix"] = new JsonArray( .6, .8, 0, 0, -.8, .6, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 ); root["nodes"].AsArray().Add( new JsonObject { ["children"] = new JsonArray( 0 ), ["scale"] = new JsonArray( 2, 3, 4 ), ["translation"] = new JsonArray( 3, -5, 7 ) } ); root["scenes"][0]["nodes"] = new JsonArray( 1 ); root["extensionsRequired"] = new JsonArray( Extension );
		byte[] Resolve( string uri ) => uri == "affine-fixture.bin" ? data : source.Resolve( uri ); var bytes = Encoding.UTF8.GetBytes( root.ToJsonString() );
		return new[] { new Form( "synthetic_affine/source", "synthetic", false, bytes, Resolve ), new Form( "synthetic_affine/compiled_glb_c", "synthetic", true, GltfCompiler.Compile( bytes, Resolve, Selected ), Resolve ) };
		void Write( int offset, float[] values ) { for ( var c = 0; c < values.Length; c++ ) BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( offset + c * 4 ), values[c] ); }
	}
	private static Matrix4x4 Local( int instance ) { var s = Scales[instance]; return new( 0, s[0], 0, 0, 0, 0, s[1], 0, s[2], 0, 0, 0, 2.5f, 5, 7.5f, 1 ); }
	private static double[] Tangent( double[] normal ) => normal[2] != 0 ? new double[] { 1, 0, 0 } : new double[] { 0, 0, 1 };
	private static double Error( double[] expected, double[] actual ) => expected.Zip( actual, (a, b) => Math.Abs( a - b ) ).Max();
}
