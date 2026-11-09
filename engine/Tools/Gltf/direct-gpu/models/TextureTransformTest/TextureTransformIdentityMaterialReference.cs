using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

/// <summary>
/// A model-local source derivative used only to acquire identity-mapping reference
/// materials. Literal reference vertices already carry independently transformed
/// coordinates; their materials must not apply the source affine transform again.
/// Original source files and their material declarations are never modified.
/// </summary>
public static class TextureTransformIdentityMaterialReference
{
	public static GltfDocument CreateDocument( TextureTransformReference.Inputs source, bool compiled )
	{
		ArgumentNullException.ThrowIfNull( source );
		var root = JsonNode.Parse( source.Dependencies["TextureTransformTest.gltf"] )
			?? throw new InvalidOperationException( "Pinned source JSON is absent." );
		foreach ( var material in root["materials"].AsArray() )
		{
			var texture = material["pbrMetallicRoughness"]["baseColorTexture"];
			if ( texture["extensions"] is not JsonObject extensions ) continue;
			extensions.Remove( TextureTransformReference.Extension );
			if ( extensions.Count == 0 ) texture.AsObject().Remove( "extensions" );
		}
		if ( root["extensionsUsed"] is JsonArray used )
		{
			var retained = used.Where( value => value.GetValue<string>() != TextureTransformReference.Extension )
				.Select( value => value.DeepClone() ).ToArray();
			if ( retained.Length == 0 ) root.AsObject().Remove( "extensionsUsed" );
			else root["extensionsUsed"] = new JsonArray( retained );
		}
		var bytes = Encoding.UTF8.GetBytes( root.ToJsonString() );
		if ( compiled ) bytes = GltfCompiler.Compile( bytes, source.Resolve );
		return GltfReader.Read( bytes, source.Resolve );
	}
}
