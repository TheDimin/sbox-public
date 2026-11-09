using System;
using System.Collections.Generic;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>Builds one aligned arena from distinct immutable upload snapshots.</summary>
internal sealed class GltfGpuInputArenaBuilder
{
	private readonly Dictionary<GltfUploadStorage, int> offsets = new( ReferenceEqualityComparer.Instance );
	private readonly List<(GltfUploadStorage Storage, int Offset)> ordered = new();
	private int length;

	internal int Add( GltfUploadStorage storage )
	{
		ArgumentNullException.ThrowIfNull( storage );
		if ( offsets.TryGetValue( storage, out var existing ) ) return existing;
		var start = ((long)length + 3) & ~3L;
		var end = start + storage.Bytes.Length;
		// Reserve the final word padding as well as the storage itself.
		if ( ((end + 3) & ~3L) > Array.MaxLength ) throw new GltfException( "glTF input arena exceeds the supported array length." );
		length = (int)end;
		var offset = (int)start;
		offsets.Add( storage, offset );
		ordered.Add( (storage, offset) );
		return offset;
	}

	internal byte[] Build()
	{
		var bytes = new byte[(int)(((long)length + 3) & ~3L)];
		foreach ( var (storage, offset) in ordered ) storage.Bytes.Span.CopyTo( bytes.AsSpan( offset ) );
		return bytes;
	}
}
