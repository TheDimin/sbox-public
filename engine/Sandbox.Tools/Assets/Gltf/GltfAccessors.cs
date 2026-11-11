using System;
using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>Reads glTF accessor data on the CPU, for editor previews such as joint weights.</summary>
static class GltfAccessors
{
	/// <summary>
	/// The values of an accessor as floats, <paramref name="components"/> per element. Normalized integers
	/// become 0..1 (or -1..1). Returns null when the data is unavailable (missing buffer, sparse-only, compressed).
	/// </summary>
	public static float[] Read( JsonObject root, Func<int, byte[]> readBufferView, int accessorIndex, out int components )
	{
		components = 0;
		if ( GltfOverrides.Collection( root, "accessors" )?[accessorIndex] is not JsonObject accessor ) return null;
		if ( accessor["bufferView"] is not JsonValue viewValue || !viewValue.TryGetValue<int>( out var viewIndex ) ) return null;
		components = (accessor["type"]?.GetValue<string>()) switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 0 };
		if ( components == 0 ) return null;
		var componentType = accessor["componentType"]?.GetValue<int>() ?? 0;
		var size = componentType switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => 0 };
		if ( size == 0 ) return null;
		var count = accessor["count"]?.GetValue<int>() ?? 0;
		var normalized = accessor["normalized"]?.GetValue<bool>() ?? false;
		var offset = accessor["byteOffset"]?.GetValue<int>() ?? 0;
		var view = GltfOverrides.Collection( root, "bufferViews" )?[viewIndex] as JsonObject;
		var stride = view?["byteStride"]?.GetValue<int>() ?? 0;
		if ( stride == 0 ) stride = size * components;
		var data = readBufferView( viewIndex );
		if ( data is null ) return null;

		var values = new float[count * components];
		for ( var i = 0; i < count; i++ )
			for ( var c = 0; c < components; c++ )
			{
				var at = offset + i * stride + c * size;
				if ( at + size > data.Length ) return null;
				var span = data.AsSpan( at, size );
				values[i * components + c] = componentType switch
				{
					5126 => BinaryPrimitives.ReadSingleLittleEndian( span ),
					5121 => normalized ? span[0] / 255f : span[0],
					5120 => normalized ? MathF.Max( (sbyte)span[0] / 127f, -1 ) : (sbyte)span[0],
					5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian( span ) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian( span ),
					5122 => normalized ? MathF.Max( BinaryPrimitives.ReadInt16LittleEndian( span ) / 32767f, -1 ) : BinaryPrimitives.ReadInt16LittleEndian( span ),
					_ => BinaryPrimitives.ReadUInt32LittleEndian( span ),
				};
			}
		return values;
	}
}
