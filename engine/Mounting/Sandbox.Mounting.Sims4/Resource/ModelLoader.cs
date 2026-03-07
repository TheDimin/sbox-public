using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Mounting.Sims4;

/// <summary>
/// Simple vertex struct for Sandbox mesh compatibility.
/// Uses VertexLayout attributes for Sandbox vertex buffers.
/// </summary>
public struct Sims4Vertex
{
	[VertexLayout.Position]
	public Vector3 Position;

	public Sims4Vertex( Vector3 position )
	{
		Position = position;
	}

	public static readonly VertexAttribute[] Layout =
	[
		new( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 )
	];
}

public class Sims4ModelLoader( DbpfPackage package, DbpfRecord record ) : ResourceLoader<GameMount>
{
	private static new Logger Log = new Logger( "Sims4-ModelLoader" );

	protected override object Load()
	{
		try
		{
			Log.Trace( $"Loading model 0x{record.InstanceId:X16}" );
			var modelData = package.ReadData( record );

			var meshes = Sims4Geom.ModelParser.LoadMeshes( package, record, modelData, Log );
			if ( meshes.Count == 0 )
				throw new InvalidOperationException( $"No GEOM meshes found for model 0x{record.InstanceId:X16}" );

			var modelName = package.TryResolveName( record.InstanceId, out var resolvedName ) && !string.IsNullOrWhiteSpace( resolvedName )
				? SanitizeModelName( resolvedName )
				: $"sims4_model_0x{record.InstanceId:X16}";

			var builder = Model.Builder.WithName( modelName );
			for ( int meshIdx = 0; meshIdx < meshes.Count; meshIdx++ )
			{
				var geomMesh = meshes[meshIdx];
				var mesh = new Mesh( $"Mesh_{meshIdx}", null );

				var vertexData = new Sims4Vertex[geomMesh.Vertices.Length];
				for ( int i = 0; i < geomMesh.Vertices.Length; i++ )
					vertexData[i] = new Sims4Vertex( geomMesh.Vertices[i] );

				mesh.CreateVertexBuffer( vertexData.Length, vertexData );
				mesh.CreateIndexBuffer( geomMesh.Indices.Length, geomMesh.Indices );
				mesh.Bounds = ComputeBounds( geomMesh.Vertices );
				builder.AddMesh( mesh );
			}

			var model = builder.Create();
			if ( model == null || !model.IsValid )
				throw new InvalidOperationException( "Failed to create valid model from parsed GEOM data" );

			Log.Trace( $"Successfully loaded model 0x{record.InstanceId:X16} with {meshes.Count} mesh(es)" );
			return model;
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to load model 0x{record.InstanceId:X16}: {ex.Message}" );
			throw;
		}
	}

	private static BBox ComputeBounds( Vector3[] vertices )
	{
		if ( vertices.Length == 0 )
			return default;

		var mins = vertices[0];
		var maxs = vertices[0];
		for ( int i = 1; i < vertices.Length; i++ )
		{
			mins = Vector3.Min( mins, vertices[i] );
			maxs = Vector3.Max( maxs, vertices[i] );
		}

		return new BBox( mins, maxs );
	}

	private static string SanitizeModelName( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return "sims4_model";

		Span<char> invalid = stackalloc char[]
		{
			'<', '>', ':', '"', '/', '\\', '|', '?', '*'
		};

		var result = value.Trim();
		foreach ( var ch in invalid )
			result = result.Replace( ch, '_' );

		return result;
	}
}

namespace Sims4Geom
{
	public sealed class GeomMesh
	{
		public uint VertexCount;
		public uint IndexCount;
		public Vector3[] Vertices = [];
		public int[] Indices = [];
	}

	internal readonly record struct ResourceKey( uint TypeId, uint GroupId, ulong InstanceId );
	internal readonly record struct ChunkLocation( int Position, int Size );

	internal sealed class RcolInfo
	{
		public List<ResourceKey> ExternalGeomKeys { get; } = [];
		public List<ChunkLocation> ChunkLocations { get; } = [];
	}

	public static class ModelParser
	{
		private const uint GeomChunkMagic = 0x4D4F4547; // 'GEOM' little-endian
		private const uint PositionElementType = 1;

		public static List<GeomMesh> LoadMeshes( DbpfPackage package, DbpfRecord rootRecord, ReadOnlySpan<byte> data, Logger log )
		{
			var result = new List<GeomMesh>( 4 );
			var visitedGeomInstances = new HashSet<ulong>();

			if ( TryParseGeomChunk( data, out var directGeom ) )
			{
				result.Add( directGeom );
				visitedGeomInstances.Add( rootRecord.InstanceId );
			}

			if ( !TryParseRcol( data, out var rcol ) )
				return result;

			for ( int i = 0; i < rcol.ChunkLocations.Count; i++ )
			{
				var chunk = rcol.ChunkLocations[i];
				if ( !TrySlice( data, chunk.Position, chunk.Size, out var chunkData ) )
					continue;

				if ( TryParseGeomChunk( chunkData, out var embeddedGeom ) )
					result.Add( embeddedGeom );
			}

			for ( int i = 0; i < rcol.ExternalGeomKeys.Count; i++ )
			{
				var key = rcol.ExternalGeomKeys[i];
				if ( !package.TryGetRecord( key.TypeId, key.GroupId, key.InstanceId, out var geomRecord ) )
					continue;

				if ( !visitedGeomInstances.Add( geomRecord.InstanceId ) )
					continue;

				if ( !package.TryReadData( geomRecord, out var geomBytes ) )
					continue;

				if ( TryParseGeomChunk( geomBytes, out var externalGeom ) )
				{
					result.Add( externalGeom );
					continue;
				}

				if ( TryParseRcol( geomBytes, out var nestedRcol ) )
				{
					for ( int c = 0; c < nestedRcol.ChunkLocations.Count; c++ )
					{
						var nestedChunk = nestedRcol.ChunkLocations[c];
						if ( !TrySlice( geomBytes, nestedChunk.Position, nestedChunk.Size, out var nestedChunkData ) )
							continue;

						if ( TryParseGeomChunk( nestedChunkData, out var nestedGeom ) )
							result.Add( nestedGeom );
					}
				}
			}

			if ( result.Count == 0 )
			{
				log.Warning( $"No usable GEOM data found in model 0x{rootRecord.InstanceId:X16}" );
			}

			return result;
		}

		private static bool TryParseRcol( ReadOnlySpan<byte> data, out RcolInfo info )
		{
			info = null;

			if ( data.Length < 20 )
				return false;

			int offset = 0;
			uint version = ReadUInt32LE( data, ref offset );
			if ( version == 0 || version > 16 )
				return false;

			offset += 8; // unknown1 + unknown2
			uint externalCount = ReadUInt32LE( data, ref offset );
			uint internalCount = ReadUInt32LE( data, ref offset );

			if ( externalCount > 32768 || internalCount > 32768 )
				return false;

			long keyBytes = ((long)internalCount + externalCount) * 16;
			long chunkBytes = (long)internalCount * 8;
			if ( offset + keyBytes + chunkBytes > data.Length )
				return false;

			info = new RcolInfo();

			for ( uint i = 0; i < internalCount; i++ )
				offset += 16;

			for ( uint i = 0; i < externalCount; i++ )
			{
				ulong instance = ReadUInt64LE( data, ref offset );
				uint typeId = ReadUInt32LE( data, ref offset );
				uint groupId = ReadUInt32LE( data, ref offset );

				if ( typeId == DbpfPackage.GeomTypeId )
					info.ExternalGeomKeys.Add( new ResourceKey( typeId, groupId, instance ) );
			}

			for ( uint i = 0; i < internalCount; i++ )
			{
				int position = unchecked( (int)ReadUInt32LE( data, ref offset ) );
				int size = unchecked( (int)ReadUInt32LE( data, ref offset ) );

				if ( position < 0 || size <= 0 )
					continue;

				if ( position > data.Length || position + size > data.Length )
					continue;

				info.ChunkLocations.Add( new ChunkLocation( position, size ) );
			}

			return info.ExternalGeomKeys.Count > 0 || info.ChunkLocations.Count > 0;
		}

		private static bool TryParseGeomChunk( ReadOnlySpan<byte> data, out GeomMesh mesh )
		{
			mesh = null;
			if ( data.Length < 32 )
				return false;

			int offset = 0;
			if ( ReadUInt32LE( data, ref offset ) != GeomChunkMagic )
				return false;

			_ = ReadUInt32LE( data, ref offset ); // version
			_ = ReadUInt32LE( data, ref offset ); // data size
			_ = ReadUInt32LE( data, ref offset ); // flags
			uint embeddedShader = ReadUInt32LE( data, ref offset );

			if ( embeddedShader != 0 )
			{
				if ( offset + 4 > data.Length )
					return false;

				int mtnfSize = unchecked( (int)ReadUInt32LE( data, ref offset ) );
				if ( mtnfSize < 8 || offset - 4 + mtnfSize > data.Length )
					return false;

				offset += mtnfSize - 4;
			}

			if ( offset + 16 > data.Length )
				return false;

			offset += 8; // merge_group + sort_order
			uint vertexCountU32 = ReadUInt32LE( data, ref offset );
			uint elementCountU32 = ReadUInt32LE( data, ref offset );

			if ( vertexCountU32 == 0 || vertexCountU32 > 2_000_000 )
				return false;

			if ( elementCountU32 == 0 || elementCountU32 > 64 )
				return false;

			int vertexCount = (int)vertexCountU32;
			int elementCount = (int)elementCountU32;
			int stride = 0;
			int positionOffset = -1;
			int positionSize = 0;

			for ( int i = 0; i < elementCount; i++ )
			{
				if ( offset + 9 > data.Length )
					return false;

				uint elementType = ReadUInt32LE( data, ref offset );
				offset += 1; // sub_type
				int byteSize = unchecked( (int)ReadUInt32BE( data, ref offset ) );
				if ( byteSize <= 0 || byteSize > 256 )
					return false;

				if ( elementType == PositionElementType && positionOffset < 0 )
				{
					positionOffset = stride;
					positionSize = byteSize;
				}

				stride += byteSize;
			}

			if ( stride <= 0 || positionOffset < 0 || positionSize < 12 )
				return false;

			long vertexBytes = (long)vertexCount * stride;
			if ( offset + vertexBytes > data.Length )
				return false;

			var vertices = new Vector3[vertexCount];
			for ( int i = 0; i < vertexCount; i++ )
			{
				int baseOffset = offset + (i * stride) + positionOffset;
				if ( baseOffset + 12 > data.Length )
					return false;

				vertices[i] = new Vector3(
					ReadSingleLE( data, baseOffset ),
					ReadSingleLE( data, baseOffset + 4 ),
					ReadSingleLE( data, baseOffset + 8 )
				);
			}

			offset += (int)vertexBytes;
			if ( offset + 9 > data.Length )
				return false;

			offset += 1; // format_type
			int indexByteSize = unchecked( (int)ReadUInt32BE( data, ref offset ) );
			uint numIndicesU32 = ReadUInt32LE( data, ref offset );
			if ( numIndicesU32 == 0 || numIndicesU32 > 12_000_000 )
				return false;

			if ( indexByteSize is not (1 or 2 or 4) )
				return false;

			int numIndices = (int)numIndicesU32;
			long indexBytes = (long)numIndices * indexByteSize;
			if ( offset + indexBytes > data.Length )
				return false;

			var indices = new int[numIndices];
			switch ( indexByteSize )
			{
				case 1:
					for ( int i = 0; i < numIndices; i++ )
						indices[i] = data[offset + i];
					break;
				case 2:
					for ( int i = 0; i < numIndices; i++ )
						indices[i] = BinaryPrimitives.ReadUInt16LittleEndian( data.Slice( offset + (i * 2), 2 ) );
					break;
				case 4:
					for ( int i = 0; i < numIndices; i++ )
						indices[i] = unchecked( (int)ReadUInt32LE( data, offset + (i * 4) ) );
					break;
			}

			mesh = new GeomMesh
			{
				VertexCount = (uint)vertices.Length,
				IndexCount = (uint)indices.Length,
				Vertices = vertices,
				Indices = indices,
			};

			return true;
		}

		private static bool TrySlice( ReadOnlySpan<byte> source, int offset, int size, out ReadOnlySpan<byte> slice )
		{
			if ( offset < 0 || size <= 0 || offset > source.Length || offset + size > source.Length )
			{
				slice = default;
				return false;
			}

			slice = source.Slice( offset, size );
			return true;
		}

		private static uint ReadUInt32LE( ReadOnlySpan<byte> data, int offset ) =>
			BinaryPrimitives.ReadUInt32LittleEndian( data.Slice( offset, 4 ) );

		private static uint ReadUInt32LE( ReadOnlySpan<byte> data, ref int offset )
		{
			uint value = BinaryPrimitives.ReadUInt32LittleEndian( data.Slice( offset, 4 ) );
			offset += 4;
			return value;
		}

		private static uint ReadUInt32BE( ReadOnlySpan<byte> data, ref int offset )
		{
			uint value = BinaryPrimitives.ReadUInt32BigEndian( data.Slice( offset, 4 ) );
			offset += 4;
			return value;
		}

		private static ulong ReadUInt64LE( ReadOnlySpan<byte> data, ref int offset )
		{
			ulong value = BinaryPrimitives.ReadUInt64LittleEndian( data.Slice( offset, 8 ) );
			offset += 8;
			return value;
		}

		private static float ReadSingleLE( ReadOnlySpan<byte> data, int offset ) =>
			BitConverter.Int32BitsToSingle( BinaryPrimitives.ReadInt32LittleEndian( data.Slice( offset, 4 ) ) );
	}
}
