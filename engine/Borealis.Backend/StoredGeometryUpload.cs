using System.Threading;
using Borealis.Gpu;

namespace Borealis.Backend;

/// <summary>One file's already formatted rigid geometry. The decoder writes it before the renderer consumes it.</summary>
internal sealed class StoredGeometryUpload
{
	internal readonly record struct Slice( StoredGeometryUpload Batch, int VertexOffset, int IndexOffset, int VertexBytes, int IndexBytes );
	internal readonly record struct Pages( BackendDevice Device, BufferHandle Vertices, long VertexOffset, int VertexSlot,
		BufferHandle Indices, uint FirstIndex, int IndexSlot, int VertexBytes, int IndexBytes );

	byte[] vertices, indices;
	int vertexBytes, indexBytes;
	Pages? uploaded;
	int sealedBatch;

	internal Slice Reserve( int vertices, int indices )
	{
		var slice = new Slice( this, vertexBytes, indexBytes, vertices, indices );
		vertexBytes = checked(vertexBytes + ((vertices + 15) & ~15));
		indexBytes = checked(indexBytes + ((indices + 15) & ~15));
		return slice;
	}

	internal void Allocate()
	{
		vertices = new byte[vertexBytes];
		indices = new byte[indexBytes];
	}

	internal void Write( in Slice slice, ReadOnlySpan<byte> vertexData, ReadOnlySpan<byte> indexData )
	{
		vertexData.CopyTo( vertices.AsSpan( slice.VertexOffset, slice.VertexBytes ) );
		indexData.CopyTo( indices.AsSpan( slice.IndexOffset, slice.IndexBytes ) );
	}

	internal void Seal() => Volatile.Write( ref sealedBatch, 1 );
	internal void Abandon()
	{
		vertices = null;
		indices = null;
		Seal();
	}

	// Called by the render bridge on the same thread as runtime model creation.
	internal bool TryUpload( BackendDevice device )
	{
		if ( Volatile.Read( ref sealedBatch ) == 0 ) return false;
		if ( uploaded.HasValue ) return true;
		if ( vertices is not { Length: > 0 } || indices is not { Length: > 0 } ) return true;
		if ( !device.TrySpendModelBudget() ) return false;
		using var uploads = device.Gpu.DeferUploads();
		Pages pages;
		if ( device.GpuDrivenTables is { } tables )
		{
			if ( !tables.TryUploadStoredGeometry( device, vertices, indices, out pages ) ) return false;
		}
		else pages = UploadClassic( device );
		uploaded = pages;
		vertices = null;
		indices = null;
		return true;
	}

	Pages UploadClassic( BackendDevice device )
	{
		var gpu = device.Gpu;
		var vertex = gpu.CreateBuffer( new BufferInfo { Size = vertices.Length, Usage = BufferUsage.Vertex, Name = "Stored batch vertices" } );
		BufferHandle index = default;
		try
		{
			index = gpu.CreateBuffer( new BufferInfo { Size = indices.Length, Usage = BufferUsage.Index, Name = "Stored batch indices" } );
			gpu.WriteBuffer( vertex, vertices );
			gpu.WriteBuffer( index, indices );
			return new( device, vertex, 0, -1, index, 0, -1, vertices.Length, indices.Length );
		}
		catch
		{
			gpu.Destroy( vertex );
			if ( index.IsValid ) gpu.Destroy( index );
			throw;
		}
	}

	internal Pages? GetPages( BackendDevice device ) => uploaded is { } pages && ReferenceEquals( device, pages.Device ) ? pages : null;

	~StoredGeometryUpload()
	{
		if ( uploaded is { } pages ) pages.Device.ReleasedStoredGeometry.Enqueue( pages );
	}
}
