using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Sandbox.Gltf;

namespace Sandbox;

using NVector2 = System.Numerics.Vector2;

/// <summary>
/// Candidate for the legacy stock material's single shared UV input. The
/// backend must verify that upload belongs to the retained document/primitive.
/// A null result means there is no consumed texture and no stream is required.
/// This is not a replacement for independent five-role source material binding.
/// </summary>
internal sealed class GltfLegacyUvBakePlan
{
	private readonly byte[] bytes;
	internal int SourceTexCoord { get; }
	internal GltfUploadStream Source { get; }
	internal bool IsIdentityMapping { get; }
	internal int VertexCount => Source.Count;
	internal ReadOnlyMemory<byte> Bytes => bytes;

	private GltfLegacyUvBakePlan( int sourceTexCoord, GltfUploadStream source, bool isIdentityMapping, byte[] bytes )
	{
		SourceTexCoord = sourceTexCoord;
		Source = source;
		IsIdentityMapping = isIdentityMapping;
		this.bytes = bytes;
	}

	internal static GltfLegacyUvBakePlan Create( GltfDocument document, int mesh, int primitive, GltfPrimitiveUploadData upload, bool skipIdentity = false )
	{
		ArgumentNullException.ThrowIfNull( document );
		return Create( document, mesh, primitive, upload, document.ReadPrimitive( mesh, primitive ).Material, skipIdentity );
	}

	internal static GltfLegacyUvBakePlan Create( GltfDocument document, int mesh, int primitive, GltfPrimitiveUploadData upload, int? materialIndex, bool skipIdentity = false )
	{
		ArgumentNullException.ThrowIfNull( document );
		ArgumentNullException.ThrowIfNull( upload );
		var material = materialIndex is int index ? document.ReadMaterial( index ) : null;
		var plan = GltfMaterialUvPlan.CreateForRoles( material, upload,
			[GltfMaterialTextureRole.BaseColor, GltfMaterialTextureRole.MetallicRoughness], materialIndex );
		GltfMaterialUvBinding selected = null;
		foreach ( var binding in plan.Bindings.Values )
		{
			if ( selected is null ) { selected = binding; continue; }
			if ( binding.TexCoord != selected.TexCoord || binding.Transform.Offset != selected.Transform.Offset ||
				binding.Transform.Tiling != selected.Transform.Tiling || binding.Transform.Rotation != selected.Transform.Rotation )
				throw new GltfException( "The stock material cannot represent different base-color and metallic-roughness UV mappings." );
		}
		if ( selected is null ) return null;
		var identity = selected.TexCoord == 0 && selected.Transform.Offset == NVector2.Zero &&
			selected.Transform.Tiling == NVector2.One && selected.Transform.Rotation == 0;
		if ( skipIdentity && identity ) return null;
		var stream = selected.Source;
		var componentSize = stream.ComponentType switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
			_ => throw new GltfException( "Texture coordinates have an unsupported upload component type." ) };
		var bytes = new byte[checked(stream.Count * 2 * sizeof(float))];
		var source = stream.Storage.Bytes.Span;
		for ( var vertex = 0; vertex < stream.Count; vertex++ )
		{
			var offset = checked((long)stream.ByteOffset + (long)vertex * stream.ByteStride);
			if ( offset < 0 || offset + 2L * componentSize > source.Length )
				throw new GltfException( "Texture coordinate upload descriptor exceeds its retained storage." );
			var data = source.Slice( (int)offset, 2 * componentSize );
			var uv = new NVector2( Component( data, stream.ComponentType, stream.Normalized ), Component( data[componentSize..], stream.ComponentType, stream.Normalized ) );
			var transformed = selected.Transform.Apply( uv );
			if ( !float.IsFinite( transformed.X ) || !float.IsFinite( transformed.Y ) )
				throw new GltfException( "Transformed texture coordinates must be finite." );
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( vertex * 8 ), transformed.X );
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( vertex * 8 + 4 ), transformed.Y );
		}
		return new GltfLegacyUvBakePlan( selected.TexCoord, stream, identity, bytes );
	}

	private static float Component( ReadOnlySpan<byte> data, int type, bool normalized ) => type switch
	{
		5120 => normalized ? MathF.Max( unchecked((sbyte)data[0]) / 127f, -1f ) : unchecked((sbyte)data[0]),
		5121 => normalized ? data[0] / 255f : data[0],
		5122 => normalized ? MathF.Max( BinaryPrimitives.ReadInt16LittleEndian( data ) / 32767f, -1f ) : BinaryPrimitives.ReadInt16LittleEndian( data ),
		5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian( data ) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian( data ),
		5125 when !normalized => BinaryPrimitives.ReadUInt32LittleEndian( data ),
		5126 when !normalized => BinaryPrimitives.ReadSingleLittleEndian( data ),
		_ => throw new GltfException( "Texture coordinate upload descriptor has invalid normalization." )
	};
}
