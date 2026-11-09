using System;
using NativeEngine;
using Sandbox;

namespace Editor;

/// <summary>Reads a bounded copy of one compiled model draw for editor tools.</summary>
public static class ModelReadback
{
	/// <summary>Hard limits per call. Callers must also bound totals across a model.</summary>
	public const int MaxVertices = 250_000;
	public const int MaxIndices = 750_000;

	[Flags]
	public enum Streams
	{
		Position = 1, Normal = 2, Tangent = 4, TexCoord0 = 8, TexCoord1 = 16, Color = 32
	}

	public enum Status
	{
		Success, WrongThread, InvalidModel, InvalidMesh, InvalidDraw, InvalidLimits,
		EmptyOrUnavailable, LimitExceeded, ReadFailed, InvalidData
	}

	/// <summary>
	/// Owned managed arrays. Vertices are in model-local space and indices are rebased into
	/// Vertices. Missing streams contain defaults; inspect AvailableStreams before using them.
	/// No skin weights, morphs, source-vertex IDs or source-part correspondence are returned.
	/// </summary>
	public sealed class Draw
	{
		internal Draw( Vertex[] vertices, uint[] indices, Streams streams )
		{
			Vertices = vertices;
			Indices = indices;
			AvailableStreams = streams;
		}

		public Vertex[] Vertices { get; }
		public uint[] Indices { get; }
		public Streams AvailableStreams { get; }
	}

	/// <summary>
	/// Read on the engine main thread while the model is valid. The returned arrays remain
	/// owned by the caller after the model is reloaded or disposed. Limits bound each managed
	/// allocation; native internal allocations and total work across draws are not bounded here.
	/// The native compaction order is unspecified and does not establish alignment with another
	/// reader. A false status never returns geometry.
	/// </summary>
	public static unsafe Status ReadDraw( Model model, int mesh, int draw, int maxVertices, int maxIndices, out Draw result )
	{
		result = null;
		if ( !ThreadSafe.IsMainThread ) return Status.WrongThread;
		if ( model is null || !model.IsValid || model.IsError ) return Status.InvalidModel;
		if ( maxVertices <= 0 || maxVertices > MaxVertices || maxIndices <= 0 || maxIndices > MaxIndices ) return Status.InvalidLimits;
		if ( mesh < 0 || mesh >= model.MeshCount ) return Status.InvalidMesh;
		model.native.GetMeshInfo( mesh, out _, out _, out int drawCount, out _, out _, out _, out _ );
		if ( draw < 0 || draw >= drawCount ) return Status.InvalidDraw;
		int vertexCount = MeshGlue.GetDrawCallVertexCount( model.native, mesh, draw );
		int indexCount = MeshGlue.GetDrawCallIndexCount( model.native, mesh, draw );
		if ( vertexCount <= 0 || indexCount <= 0 ) return Status.EmptyOrUnavailable;
		if ( vertexCount > maxVertices || indexCount > maxIndices ) return Status.LimitExceeded;
		var vertices = new Vertex[vertexCount];
		var indices = new uint[indexCount];
		uint used;
		fixed ( Vertex* vp = &vertices[0] )
		fixed ( uint* ip = &indices[0] )
		{
			if ( !MeshGlue.ReadDrawCall( model.native, mesh, draw, (IntPtr)vp, (uint)vertexCount, (IntPtr)ip, (uint)indexCount, out used ) )
				return Status.ReadFailed;
		}
		if ( used == 0 || used > (uint)vertexCount ) return Status.InvalidData;
		foreach ( uint index in indices )
			if ( index >= used ) return Status.InvalidData;
		var streams = (Streams)MeshGlue.GetDrawCallStreams( model.native, mesh, draw );
		const Streams known = Streams.Position | Streams.Normal | Streams.Tangent | Streams.TexCoord0 | Streams.TexCoord1 | Streams.Color;
		if ( (streams & Streams.Position) == 0 || (streams & ~known) != 0 ) return Status.InvalidData;
		if ( used != (uint)vertexCount ) Array.Resize( ref vertices, (int)used );
		result = new Draw( vertices, indices, streams );
		return Status.Success;
	}
}
