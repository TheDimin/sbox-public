using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

/// <summary>Forward reader/compiler assertions for COLOR_n's core-legal component/dimension contract.</summary>
public static class BoxVertexColorsColorLayoutTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public const int ExpectedCases = 16;
	public static Result[] RunAll( string checkoutRoot )
	{
		var path = Path.Combine( checkoutRoot, "research/gltf-validation/direct-gpu/models/BoxVertexColors/fixtures/float3.gltf" ); var original = File.ReadAllText( path ); var results = new List<Result>();
		var mutations = new (string Name, Action<JsonObject, JsonObject> Change)[]
		{
			("signed_byte_normalized", (_, accessor) => { accessor["componentType"] = 5120; accessor["normalized"] = true; }),
			("signed_short_normalized", (_, accessor) => { accessor["componentType"] = 5122; accessor["normalized"] = true; }),
			("unsigned_byte_without_normalization", (_, accessor) => accessor["componentType"] = 5121),
			("unsigned_short_without_normalization", (_, accessor) => accessor["componentType"] = 5123),
			("unsigned_int", (_, accessor) => accessor["componentType"] = 5125),
			("vec2_color", (_, accessor) => accessor["type"] = "VEC2"),
			("color_set_gap", (primitive, _) => primitive["attributes"].AsObject().Remove( "COLOR_0" )),
			("color_set_leading_zero", (primitive, _) => { var attributes = primitive["attributes"].AsObject(); var value = attributes["COLOR_0"].DeepClone(); attributes.Remove( "COLOR_0" ); attributes["COLOR_00"] = value; })
		};
		foreach ( var mutation in mutations ) foreach ( var compiled in new[] { false, true } )
		{
			try
			{
				var root = JsonNode.Parse( original ).AsObject(); var primitive = root["meshes"][0]["primitives"][0].AsObject(); var accessor = root["accessors"][primitive["attributes"]["COLOR_0"].GetValue<int>()].AsObject(); mutation.Change( primitive, accessor );
				var bytes = Encoding.UTF8.GetBytes( root.ToJsonString() ); var rejected = false;
				try
				{
					if ( compiled ) bytes = GltfCompiler.Compile( bytes );
					using var document = GltfReader.Read( bytes ); _ = document.MaterializePrimitiveUpload( 0, 0 );
				}
				catch ( GltfException ) { rejected = true; }
				BoxVertexColorsReference.Require( rejected, "Illegal COLOR_n role layout was silently accepted: " + mutation.Name ); results.Add( new( mutation.Name, compiled ? "derived_invalid_compiler_or_compiled_import_rejection" : "derived_invalid_source_import_rejection", true, null ) );
			}
			catch ( Exception error ) { results.Add( new( mutation.Name, compiled ? "derived_invalid_compiled" : "derived_invalid_source", false, error.ToString() ) ); }
		}
		BoxVertexColorsReference.Require( results.Count == ExpectedCases, "Frozen invalid layout inventory changed." ); return results.ToArray();
	}
}
