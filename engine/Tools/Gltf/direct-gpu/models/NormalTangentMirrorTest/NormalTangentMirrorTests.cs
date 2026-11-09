using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using NVector2 = System.Numerics.Vector2;

namespace Sandbox.Gltf.DirectGpu.NormalTangentMirrorValidation;
using static NormalTangentMirrorReference;

/// <summary>CPU/source/staging tests. Public methods on an internal helper support reflection runners.</summary>
internal static class NormalTangentMirrorTests
{
	public const int ExpectedCases = 24;
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new NormalTangentMirrorReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "all_raw_attributes_and_indices",form,() =>
			{
				using var d = form.Read(); Require( d.GetDefaultScene() == 0 && d.Root.GetProperty( "nodes" ).GetArrayLength() == 1 && d.Root.GetProperty( "meshes" ).GetArrayLength() == 1,"Actual asset shape changed." );
				for ( var a = 0; a < 5; a++ )
				{
					var expected = reference.Oracle.GetProperty( "accessors" )[a]; var actual = d.ReadFloatAccessor( a ); var values = expected.GetProperty( "values" ).EnumerateArray().SelectMany( x => x.EnumerateArray().Select( v => v.GetDouble() ) ).ToArray(); Require( actual.Length == values.Length,"Accessor count changed." );
					for ( var i = 0; i < actual.Length; i++ ) Close( values[i],actual[i],0,$"raw accessor {a}/{i}" );
				}
			} );
			Run( "authored_both_handedness_signs_preserved_in_upload",form,() =>
			{
				using var d = form.Read(); var upload = d.MaterializePrimitiveUpload( 0,0 ); var tangent = upload.Attributes["TANGENT"];
				Require( tangent.Components == 4 && tangent.ComponentType == 5126 && tangent.ByteStride == 16 && !tangent.Normalized && tangent.Count == 2770 && tangent.Binding.AccessorIndex == 3,"Tangent source declaration changed." );
				var actual = d.ReadFloatAccessor( 3 ); Require( Enumerable.Range( 0,2770 ).Count( i => actual[i*4+3] == -1 ) == 80 && Enumerable.Range( 0,2770 ).Count( i => actual[i*4+3] == 1 ) == 2690,"Both authored handedness populations must survive." );
			} );
			Run( "normal_role_linear_scale_uv_and_double_sided",form,() =>
			{
				using var d = form.Read(); var material = d.ReadMaterial( 0 ); var normal = material.NormalTexture;
				Require( material.DoubleSided && normal.Index == 1 && normal.Scale == 1 && normal.TexCoord == 0 && !normal.IsSrgb && normal.Offset == NVector2.Zero && normal.Tiling == NVector2.One && normal.Rotation == 0,"Normal role/defaults or double-sided changed." );
				var plan = global::Sandbox.GltfMaterialUvPlan.Create( material,d.MaterializePrimitiveUpload( 0,0 ),0 ); var binding = plan.Bindings[global::Sandbox.GltfMaterialTextureRole.Normal];
				Require( !binding.IsSrgb && binding.Source.Binding.AccessorIndex == 4,"Normal role did not retain actual linear UV0 binding." );
			} );
			Run( "staging_retains_authored_frame_and_runtime_fields",form,() =>
			{
				using var d = form.Read(); var plan = GltfGpuPrimitivePlan.Create( d,0,0 ); Require( !plan.GenerateFlatNormals && plan.Indices.Length == 15720,"Authored frame was replaced by a generated flat frame." );
				foreach ( var semantic in new[] { "POSITION","NORMAL","TANGENT","TEXCOORD" } ) Require( plan.Fields.Count( f => f.Semantic == semantic && f.SemanticIndex == 0 ) == 1,"Missing runtime field: " + semantic );
			} );
			Run( "portable_compiled_geometry_and_three_images",form,() =>
			{
				if ( !form.Compiled ) return;
				Require( BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 8 ) ) == form.Bytes.Length,"Compiled form must remain valid GLB." );
				using var d = GltfReader.Read( form.Bytes,_ => throw new InvalidOperationException( "Compiled form required external I/O." ) ); _ = d.MaterializePrimitiveUpload( 0,0 ); for ( var i = 0; i < 3; i++ ) Require( d.ReadImage( i ).Bytes.Length > 0,"Missing embedded image." );
			} );
		}
		var source = reference.Forms.First( f => f.Name == "glTF/source" );
		foreach ( var compiled in new[] { false,true } )
		{
			var root = JsonNode.Parse( source.Bytes ); root["extensionsUsed"] = new JsonArray( "KHR_texture_transform" );
			var n = root["materials"][0]["normalTexture"]; n["scale"] = .25; n["extensions"] = JsonNode.Parse( "{\"KHR_texture_transform\":{\"offset\":[0.125,-0.25],\"scale\":[2,0.5],\"rotation\":1.5707963267948966,\"texCoord\":1}}" );
			root["meshes"][0]["primitives"][0]["attributes"]["TEXCOORD_1"] = 4;
			var bytes = System.Text.Encoding.UTF8.GetBytes( root.ToJsonString() ); if ( compiled ) bytes = GltfCompiler.Compile( bytes,source.Resolve );
			var fixture = new NormalTangentMirrorReference.Form( "derived_normal_scale_transform/" + (compiled ? "compiled_glb_c" : "source"),compiled,bytes,source.Resolve );
			Run( "normal_scale_and_uv1_transform_literal",fixture,() =>
			{
				using var d = fixture.Read(); var material = d.ReadMaterial( 0 ); var normal = material.NormalTexture; Require( normal.Scale == .25f && normal.TexCoord == 1 && !normal.IsSrgb,"Normal scale/UV override changed." );
				var plan = global::Sandbox.GltfMaterialUvPlan.Create( material,d.MaterializePrimitiveUpload( 0,0 ),0 ); var binding = plan.Bindings[global::Sandbox.GltfMaterialTextureRole.Normal]; var uv = binding.Transform.Apply( new NVector2( .25f,.75f ) );
				Close( -.25,uv.X,1e-6,"UV transform U" ); Close( .25,uv.Y,1e-6,"UV transform V" ); Require( binding.TexCoord == 1,"Extension UV override ignored." );
			} );
			Run( "normal_transform_does_not_replace_supplied_tangents",fixture,() => { using var d = fixture.Read(); var values = d.ReadFloatAccessor( 3 ); for ( var i = 0; i < 2770; i++ ) for ( var c = 0; c < 4; c++ ) Close( reference.Values( 3,i )[c],values[i*4+c],0,"Normal transform rewrote supplied tangent." ); } );
		}
		Require( results.Count == ExpectedCases,"Case inventory changed." ); return results.ToArray();
		void Run( string name, NormalTangentMirrorReference.Form form, Action test ) { try { test(); results.Add( new( name,form.Name,true,null ) ); } catch ( Exception e ) { results.Add( new( name,form.Name,false,e.ToString() ) ); } }
	}
}
