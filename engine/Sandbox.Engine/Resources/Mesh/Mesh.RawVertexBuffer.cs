using NativeEngine;
using System.Text;

namespace Sandbox;

/// <summary>A single attribute in a caller-described, single-stream vertex layout.</summary>
internal readonly record struct RawVertexField( string Semantic, int SemanticIndex, int Offset, ColorFormat Format );

internal static class RawVertexLayout
{
	internal static void Validate( int stride, int vertexCount, int byteCount, ReadOnlySpan<RawVertexField> fields )
	{
		if ( stride <= 0 || vertexCount <= 0 || (long)stride * vertexCount > int.MaxValue )
			throw new ArgumentException( "Vertex count and stride exceed the supported buffer capacity." );
		if ( fields.Length == 0 ) throw new ArgumentException( "Vertex layout must contain at least one field." );
		var names = new HashSet<(string, int)>();
		var lastFieldEnd = 0;
		foreach ( var field in fields )
		{
			if ( string.IsNullOrEmpty( field.Semantic ) || field.SemanticIndex < 0 ) throw new ArgumentException( "Vertex semantic and index are invalid." );
			foreach ( var c in field.Semantic )
				if ( !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_') )
					throw new ArgumentException( "Vertex semantic must be an ASCII identifier." );
			if ( !names.Add( (field.Semantic.ToUpperInvariant(), field.SemanticIndex) ) ) throw new ArgumentException( "Duplicate vertex semantic and index." );
			var size = FormatSize( field.Format );
			if ( size == 0 || field.Offset < 0 || (long)field.Offset + size > stride )
				throw new ArgumentException( "Vertex field format or offset is invalid for the stride." );
			lastFieldEnd = Math.Max( lastFieldEnd, field.Offset + size );
		}
		var minimum = (long)(vertexCount - 1) * stride + lastFieldEnd;
		var capacity = (long)vertexCount * stride;
		if ( byteCount < minimum || byteCount > capacity )
			throw new ArgumentException( "Vertex data must include every field and fit the buffer capacity." );
	}

	internal static byte[] PadTrailingBytes( ReadOnlySpan<byte> data, int capacity )
	{
		if ( capacity < data.Length ) throw new ArgumentException( "Data exceeds buffer capacity." );
		var padded = new byte[capacity];
		data.CopyTo( padded );
		return padded;
	}

	private static int FormatSize( ColorFormat format ) => format switch
	{
		ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT or ColorFormat.COLOR_FORMAT_R32G32B32A32_UINT or ColorFormat.COLOR_FORMAT_R32G32B32A32_SINT => 16,
		ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT or ColorFormat.COLOR_FORMAT_R32G32B32_UINT or ColorFormat.COLOR_FORMAT_R32G32B32_SINT => 12,
		ColorFormat.COLOR_FORMAT_R32G32_FLOAT or ColorFormat.COLOR_FORMAT_R32G32_UINT or ColorFormat.COLOR_FORMAT_R32G32_SINT or
		ColorFormat.COLOR_FORMAT_R16G16B16A16_FLOAT or ColorFormat.COLOR_FORMAT_R16G16B16A16_UNORM or ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT or ColorFormat.COLOR_FORMAT_R16G16B16A16_SNORM or ColorFormat.COLOR_FORMAT_R16G16B16A16_SINT => 8,
		ColorFormat.COLOR_FORMAT_R32_FLOAT or ColorFormat.COLOR_FORMAT_R32_UINT or ColorFormat.COLOR_FORMAT_R32_SINT or
		ColorFormat.COLOR_FORMAT_R16G16_FLOAT or ColorFormat.COLOR_FORMAT_R16G16_UNORM or ColorFormat.COLOR_FORMAT_R16G16_UINT or ColorFormat.COLOR_FORMAT_R16G16_SNORM or ColorFormat.COLOR_FORMAT_R16G16_SINT or
		ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM or ColorFormat.COLOR_FORMAT_R8G8B8A8_UINT or ColorFormat.COLOR_FORMAT_R8G8B8A8_SNORM or ColorFormat.COLOR_FORMAT_R8G8B8A8_SINT => 4,
		ColorFormat.COLOR_FORMAT_R16_FLOAT or ColorFormat.COLOR_FORMAT_R16_UNORM or ColorFormat.COLOR_FORMAT_R16_UINT or ColorFormat.COLOR_FORMAT_R16_SNORM or ColorFormat.COLOR_FORMAT_R16_SINT or
		ColorFormat.COLOR_FORMAT_R8G8_UNORM or ColorFormat.COLOR_FORMAT_R8G8_UINT or ColorFormat.COLOR_FORMAT_R8G8_SNORM or ColorFormat.COLOR_FORMAT_R8G8_SINT => 2,
		ColorFormat.COLOR_FORMAT_R8_UNORM or ColorFormat.COLOR_FORMAT_R8_UINT or ColorFormat.COLOR_FORMAT_R8_SNORM or ColorFormat.COLOR_FORMAT_R8_SINT => 1,
		_ => 0
	};
}

internal partial struct VertexBufferHandle
{
	// An inaccessible type prevents typed SetData/Lock from interpreting bytes as
	// stride-sized records. Raw buffers are populated through the byte-sized API.
	private struct RawVertexMarker { }
	internal static Type RawElementType => typeof( RawVertexMarker );

	internal static unsafe VertexBufferHandle CreateRaw( int stride, int vertexCount, ReadOnlySpan<RawVertexField> layout )
	{
		var fields = new VertexField[layout.Length];
		var names = new StringBuilder();
		for ( int i = 0; i < layout.Length; ++i )
		{
			var field = layout[i];
			fields[i] = new VertexField { Format = field.Format, Offset = field.Offset, NameOffset = names.Length, NameSize = field.Semantic.Length, SemanticIndex = field.SemanticIndex };
			names.Append( field.Semantic );
		}
		fixed ( VertexField* pointer = fields )
		{
			var handle = MeshGlue.CreateVertexBuffer( stride, vertexCount, names.ToString(), (IntPtr)pointer, fields.Length, IntPtr.Zero, 0 );
			if ( handle == IntPtr.Zero ) throw new Exception( "Failed to create raw vertex buffer." );
			return new VertexBufferHandle { _native = handle, _elementCount = vertexCount, _elementSize = stride, _elementType = RawElementType };
		}
	}
}

public partial class Mesh
{
	/// <summary>Create a single vertex stream from explicitly described fields without repacking its bytes.</summary>
	internal unsafe void CreateRawVertexBuffer( int stride, int vertexCount, ReadOnlySpan<byte> data, ReadOnlySpan<RawVertexField> fields )
	{
		if ( vb.IsValid ) throw new InvalidOperationException( "Vertex buffer has already been created." );
		var snapshot = fields.ToArray();
		RawVertexLayout.Validate( stride, vertexCount, data.Length, snapshot );
		var capacity = checked( stride * vertexCount );
		// Complete only missing tail padding. Preserve all supplied source bytes.
		if ( data.Length != capacity ) data = RawVertexLayout.PadTrailingBytes( data, capacity );
		vb = VertexBufferHandle.CreateRaw( stride, vertexCount, snapshot );
		vertexType = VertexBufferHandle.RawElementType;
		MeshGlue.SetMeshVertexBuffer( native, vb, IntPtr.Zero, 0 );
		fixed ( byte* bytes = data ) MeshGlue.SetVertexBufferData( vb, (IntPtr)bytes, data.Length, 0 );
		SetVertexRange( 0, vertexCount );
	}
}
