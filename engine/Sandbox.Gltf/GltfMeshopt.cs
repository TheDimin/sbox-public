using Evergine.Bindings.MeshOptimizer;

namespace Sandbox.Gltf;

/// <summary>Decode meshoptimizer buffer streams using the bundled native codec.</summary>
public static class GltfMeshopt
{
	/// <summary>
	/// Decodes one stream, then applies its optional post-decompression filter.
	/// The caller must enforce extension-specific bitstream and filter restrictions,
	/// including whether the stream belongs to EXT or KHR meshopt compression.
	/// </summary>
	public static unsafe byte[] Decode( byte[] encoded, int count, int byteStride, string mode, string filter = "NONE" )
	{
		if ( encoded is null || encoded.Length == 0 ) throw new GltfException( "Meshopt stream is empty." );
		if ( count <= 0 ) throw new GltfException( "Meshopt element count must be positive." );
		if ( mode is not ("ATTRIBUTES" or "TRIANGLES" or "INDICES") )
			throw new GltfException( $"Unsupported meshopt mode '{mode}'." );
		if ( filter is not ("NONE" or "OCTAHEDRAL" or "QUATERNION" or "EXPONENTIAL" or "COLOR") )
			throw new GltfException( $"Unsupported meshopt filter '{filter}'." );
		if ( mode == "ATTRIBUTES" )
		{
			if ( byteStride < 4 || byteStride > 256 || byteStride % 4 != 0 )
				throw new GltfException( "Meshopt attribute stride must be a multiple of four between 4 and 256." );
			if ( (filter is "OCTAHEDRAL" or "COLOR") && byteStride is not (4 or 8) ||
				filter == "QUATERNION" && byteStride != 8 )
				throw new GltfException( $"Invalid stride for meshopt {filter} filter." );
		}
		else
		{
			if ( byteStride is not (2 or 4) || filter != "NONE" )
				throw new GltfException( "Meshopt index streams require 2- or 4-byte elements and no filter." );
			if ( mode == "TRIANGLES" && count % 3 != 0 )
				throw new GltfException( "Meshopt triangle index count must be divisible by three." );
		}

		// The legacy triangle codec is accepted here for existing codec users;
		// glTF extension integration must check the extension's own version rules.
		var header = encoded[0];
		if ( mode == "ATTRIBUTES" && header is not (0xa0 or 0xa1) ||
			mode == "TRIANGLES" && header is not (0xe0 or 0xe1) ||
			mode == "INDICES" && header != 0xd1 )
			throw new GltfException( "Meshopt stream header does not match its mode or a supported version." );

		int length;
		try { length = checked(count * byteStride); }
		catch ( OverflowException exception ) { throw new GltfException( "Meshopt output exceeds the supported array length.", exception ); }
		if ( length > Array.MaxLength ) throw new GltfException( "Meshopt output exceeds the supported array length." );
		var decoded = new byte[length];
		try
		{
			fixed ( byte* source = encoded, destination = decoded )
			{
				var status = mode switch
				{
					"ATTRIBUTES" => MeshOptimizer.DecodeVertexBuffer( destination, (nuint)count, (nuint)byteStride, source, (nuint)encoded.Length ),
					"TRIANGLES" => MeshOptimizer.DecodeIndexBuffer( destination, (nuint)count, (nuint)byteStride, source, (nuint)encoded.Length ),
					_ => MeshOptimizer.DecodeIndexSequence( destination, (nuint)count, (nuint)byteStride, source, (nuint)encoded.Length )
				};
				if ( status != 0 ) throw new GltfException( $"Invalid meshopt {mode} stream (decoder status {status})." );
				switch ( filter )
				{
					case "OCTAHEDRAL": MeshOptimizer.DecodeFilterOct( destination, (nuint)count, (nuint)byteStride ); break;
					case "QUATERNION": MeshOptimizer.DecodeFilterQuat( destination, (nuint)count, (nuint)byteStride ); break;
					case "EXPONENTIAL": MeshOptimizer.DecodeFilterExp( destination, (nuint)count, (nuint)byteStride ); break;
					case "COLOR": MeshOptimizer.DecodeFilterColor( destination, (nuint)count, (nuint)byteStride ); break;
				}
			}
		}
		catch ( Exception exception ) when ( exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException )
		{
			throw new GltfException( "The meshoptimizer native decoder is missing or incompatible.", exception );
		}
		return decoded;
	}
}
