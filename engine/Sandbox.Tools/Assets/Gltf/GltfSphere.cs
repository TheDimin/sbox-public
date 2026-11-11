using System;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>A UV sphere as glTF data, so a material can be previewed through the same loader as the model.</summary>
static class GltfSphere
{
	const int Rings = 16;
	const int Segments = 32;
	const float Radius = 0.5f;

	/// <summary>Add the sphere geometry to <paramref name="root"/> as a new buffer and return a mesh that uses it.</summary>
	public static JsonObject Append( JsonObject root, int materialIndex )
	{
		var vertexCount = (Rings + 1) * (Segments + 1);
		var positions = new float[vertexCount * 3];
		var normals = new float[vertexCount * 3];
		var uvs = new float[vertexCount * 2];
		var v = 0;
		for ( var ring = 0; ring <= Rings; ring++ )
		{
			var phi = MathF.PI * ring / Rings;
			for ( var segment = 0; segment <= Segments; segment++, v++ )
			{
				var theta = MathF.Tau * segment / Segments;
				var x = MathF.Sin( phi ) * MathF.Cos( theta );
				var y = MathF.Cos( phi );
				var z = MathF.Sin( phi ) * MathF.Sin( theta );
				normals[v * 3] = x; normals[v * 3 + 1] = y; normals[v * 3 + 2] = z;
				positions[v * 3] = x * Radius; positions[v * 3 + 1] = y * Radius; positions[v * 3 + 2] = z * Radius;
				uvs[v * 2] = (float)segment / Segments; uvs[v * 2 + 1] = (float)ring / Rings;
			}
		}

		var indices = new ushort[Rings * Segments * 6];
		var n = 0;
		for ( var ring = 0; ring < Rings; ring++ )
			for ( var segment = 0; segment < Segments; segment++ )
			{
				var a = (ushort)(ring * (Segments + 1) + segment);
				var b = (ushort)(a + Segments + 1);
				// Counter-clockwise when seen from outside, as glTF requires.
				indices[n++] = a; indices[n++] = (ushort)(a + 1); indices[n++] = b;
				indices[n++] = (ushort)(a + 1); indices[n++] = (ushort)(b + 1); indices[n++] = b;
			}

		var data = new byte[positions.Length * 4 + normals.Length * 4 + uvs.Length * 4 + indices.Length * 2];
		var offset = 0;
		int Copy( Array source, int bytes ) { Buffer.BlockCopy( source, 0, data, offset, bytes ); var start = offset; offset += bytes; return start; }
		var positionOffset = Copy( positions, positions.Length * 4 );
		var normalOffset = Copy( normals, normals.Length * 4 );
		var uvOffset = Copy( uvs, uvs.Length * 4 );
		var indexOffset = Copy( indices, indices.Length * 2 );

		var buffers = Collection( root, "buffers" );
		buffers.Add( new JsonObject { ["byteLength"] = data.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( data ) } );
		var buffer = buffers.Count - 1;

		var views = Collection( root, "bufferViews" );
		int View( int start, int length, int? target )
		{
			var view = new JsonObject { ["buffer"] = buffer, ["byteOffset"] = start, ["byteLength"] = length };
			if ( target is { } t ) view["target"] = t;
			views.Add( view );
			return views.Count - 1;
		}

		var accessors = Collection( root, "accessors" );
		int Accessor( int view, int componentType, int count, string type, JsonObject extra = null )
		{
			var accessor = new JsonObject { ["bufferView"] = view, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
			if ( extra is not null ) foreach ( var (key, value) in extra ) accessor[key] = value?.DeepClone();
			accessors.Add( accessor );
			return accessors.Count - 1;
		}

		const int Float = 5126, UnsignedShort = 5123, ArrayBuffer = 34962, ElementArrayBuffer = 34963;
		var position = Accessor( View( positionOffset, positions.Length * 4, ArrayBuffer ), Float, vertexCount, "VEC3",
			new JsonObject { ["min"] = new JsonArray( -Radius, -Radius, -Radius ), ["max"] = new JsonArray( Radius, Radius, Radius ) } );
		var normal = Accessor( View( normalOffset, normals.Length * 4, ArrayBuffer ), Float, vertexCount, "VEC3" );
		var uv = Accessor( View( uvOffset, uvs.Length * 4, ArrayBuffer ), Float, vertexCount, "VEC2" );
		var index = Accessor( View( indexOffset, indices.Length * 2, ElementArrayBuffer ), UnsignedShort, indices.Length, "SCALAR" );

		var primitive = new JsonObject
		{
			["attributes"] = new JsonObject { ["POSITION"] = position, ["NORMAL"] = normal, ["TEXCOORD_0"] = uv, ["TEXCOORD_1"] = uv },
			["indices"] = index,
		};
		if ( materialIndex >= 0 ) primitive["material"] = materialIndex;
		return new JsonObject { ["name"] = "Material Preview Sphere", ["primitives"] = new JsonArray( primitive ) };
	}

	static JsonArray Collection( JsonObject root, string key )
	{
		if ( root[key] is not JsonArray array ) root[key] = array = new JsonArray();
		return array;
	}
}
