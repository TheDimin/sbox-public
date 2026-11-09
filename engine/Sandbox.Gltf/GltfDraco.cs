using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using NativeDraco = Evergine.Bindings.Draco.Draco;

namespace Sandbox.Gltf;

public sealed class GltfDracoMesh
{
	public int VertexCount { get; init; }
	public uint[] Indices { get; init; }
	public IReadOnlyDictionary<uint, GltfDracoAttribute> Attributes { get; init; }
}

public sealed class GltfDracoAttribute
{
	public int ComponentType { get; init; }
	public int Components { get; init; }
	public byte[] Bytes { get; init; }
}

/// <summary>A tightly packed decoded base stream in its original component format.</summary>
public sealed class GltfDracoStream
{
	/// <summary>Source accessor provenance; null for topology without an authored index accessor.</summary>
	public int? AccessorIndex { get; internal init; }
	/// <summary>Actual decoded element count, which can differ from a source strip index count.</summary>
	public int Count { get; internal init; }
	public int ComponentType { get; internal init; }
	public int Components { get; internal init; }
	/// <summary>glTF interpretation metadata; the returned component bytes are not normalized.</summary>
	public bool Normalized { get; internal init; }
	public int ByteStride { get; internal init; }
	/// <summary>Apply this source accessor's sparse replacements to the decoded base before use.</summary>
	public bool HasSparseOverrides { get; internal init; }
	/// <summary>
	/// Caller-owned mutable bytes, detached from the decoder cache. Aliases of the same
	/// decoded attribute ID share this array within one result; use copy-on-write when
	/// their source accessors require different sparse replacements.
	/// </summary>
	public byte[] Bytes { get; internal init; }
}

/// <summary>Primitive-scoped decoded base attributes and triangle-list topology.</summary>
public sealed class GltfDracoPrimitiveData
{
	public int VertexCount { get; internal init; }
	public int SourceMode { get; internal init; }
	public int Mode => 4;
	/// <summary>Only attributes mapped by this primitive's selected Draco extension.</summary>
	public IReadOnlyDictionary<string, GltfDracoStream> Attributes { get; internal init; }
	public GltfDracoStream Indices { get; internal init; }
	/// <summary>Total bytes in distinct returned arrays, including packed topology.</summary>
	public long OwnedBytes { get; internal init; }
}

/// <summary>Decodes triangle geometry into managed-owned arrays.</summary>
public static class GltfDraco
{
	// The upstream managed convenience API drops this return status. Initialize
	// the result ourselves and check status before invoking any native getter.
	[DllImport( "draco_tiny_dec", EntryPoint = "Draco_Decompress", CallingConvention = CallingConvention.Cdecl )]
	private static extern int Decompress( IntPtr input, nuint length, ref NativeDraco.Mesh mesh );

	public static unsafe GltfDracoMesh Decode( byte[] encoded, long maxDecodedBytes = long.MaxValue )
	{
		if ( encoded is null || encoded.Length < 5 || !encoded.AsSpan( 0, 5 ).SequenceEqual( "DRACO"u8 ) )
			throw new GltfException( "Draco input is empty or has an invalid header." );
		if ( maxDecodedBytes < 0 ) throw new GltfException( "Draco output budget cannot be negative." );
		NativeDraco.Mesh mesh = default;
		try
		{
			fixed ( byte* data = encoded )
			{
				var status = Decompress( (IntPtr)data, (nuint)encoded.Length, ref mesh );
				if ( status != 0 || mesh.meshPtr == IntPtr.Zero ) throw new GltfException( $"Invalid Draco triangle mesh (decoder status {status})." );
			}
			var vertices = mesh.numVertices;
			var faces = mesh.numFaces;
			var attributeCount = mesh.numAttributes;
			if ( vertices == 0 || vertices > int.MaxValue || faces == 0 || attributeCount > int.MaxValue )
				throw new GltfException( "Draco mesh has unsupported vertex, face or attribute counts." );
			var indexBytes = (long)faces * 3 * sizeof( uint );
			CheckOutputSize( indexBytes, maxDecodedBytes );
			var descriptors = new List<(uint Id, NativeDraco.Attribute Native, int Type, int Components, int Bytes)>();
			var ids = new HashSet<uint>();
			var total = indexBytes;
			// GetAttributeByUniqueId in the pinned C ABI narrows IDs to uint8.
			// Enumerating attributes preserves the full uint32 ID instead.
			for ( var i = 0; i < attributeCount; i++ )
			{
				var attribute = mesh.GetAttribute( i );
				if ( attribute.internalPtr == IntPtr.Zero ) throw new GltfException( "Draco returned an invalid attribute." );
				var type = attribute.dataType switch
				{
					NativeDraco.DataType.DT_INT8 => 5120, NativeDraco.DataType.DT_UINT8 => 5121,
					NativeDraco.DataType.DT_INT16 => 5122, NativeDraco.DataType.DT_UINT16 => 5123,
					NativeDraco.DataType.DT_UINT32 => 5125, NativeDraco.DataType.DT_FLOAT32 => 5126,
					_ => throw new GltfException( "Draco attribute type cannot be represented by a core glTF accessor." )
				};
				var components = attribute.numComponents;
				if ( components is < 1 or > 16 ) throw new GltfException( "Unsupported Draco attribute component count." );
				var size = type is 5120 or 5121 ? 1 : type is 5122 or 5123 ? 2 : 4;
				var length = (long)vertices * components * size;
				CheckOutputSize( length, maxDecodedBytes - total );
				total += length;
				var id = attribute.uniqueId;
				if ( !ids.Add( id ) ) throw new GltfException( "Draco attribute IDs are not unique." );
				descriptors.Add( (id, attribute, type, (int)components, (int)length) );
			}
			// All native uint32 byte-count products were checked before the C ABI
			// allocates/copies attribute or index arrays. Initial mesh decode remains
			// a native operation and is not covered by this managed output budget.
			var indexData = mesh.GetIndices();
			byte[] packedIndices;
			try
			{
				if ( indexData.dataType is not (NativeDraco.DataType.DT_INT32 or NativeDraco.DataType.DT_UINT32) )
					throw new GltfException( "Draco returned unsupported index data." );
				packedIndices = CopyData( indexData, (int)indexBytes );
			}
			finally { if ( indexData.data != IntPtr.Zero ) NativeDraco.Release( indexData ); }
			var indices = new uint[packedIndices.Length / 4];
			for ( var i = 0; i < indices.Length; i++ )
			{
				indices[i] = BinaryPrimitives.ReadUInt32LittleEndian( packedIndices.AsSpan( i * 4, 4 ) );
				if ( indices[i] >= vertices ) throw new GltfException( "Draco returned an out-of-range vertex index." );
			}
			var attributes = new Dictionary<uint, GltfDracoAttribute>();
			foreach ( var descriptor in descriptors )
			{
				var attributeData = NativeDraco.GetData( mesh, descriptor.Native );
				try
				{
					if ( attributeData.dataType != descriptor.Native.dataType ) throw new GltfException( "Draco attribute data type changed during extraction." );
					attributes.Add( descriptor.Id, new GltfDracoAttribute
					{
						ComponentType = descriptor.Type, Components = descriptor.Components,
						Bytes = CopyData( attributeData, descriptor.Bytes )
					} );
				}
				finally { if ( attributeData.data != IntPtr.Zero ) NativeDraco.Release( attributeData ); }
			}
			return new GltfDracoMesh { VertexCount = (int)vertices, Indices = indices, Attributes = new ReadOnlyDictionary<uint, GltfDracoAttribute>( attributes ) };
		}
		catch ( Exception e ) when ( e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException )
		{
			throw new GltfException( "The Draco native decoder is missing or incompatible with this platform.", e );
		}
		finally { if ( mesh.meshPtr != IntPtr.Zero ) NativeDraco.Release( mesh ); }
	}

	private static void CheckOutputSize( long bytes, long remaining )
	{
		if ( bytes < 0 || bytes > Array.MaxLength || bytes > uint.MaxValue || bytes > remaining )
			throw new GltfException( "Draco decoded output exceeds its byte budget or supported array size." );
	}

	private static byte[] CopyData( NativeDraco.Data data, int expected )
	{
		if ( data.data == IntPtr.Zero || data.dataSize != expected ) throw new GltfException( "Draco returned an invalid output pointer or byte count." );
		var bytes = new byte[expected];
		Marshal.Copy( data.data, bytes, 0, expected );
		return bytes;
	}
}
