using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using NativeEngine;
using Sandbox.Gltf;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>A byte range in the pack's blob area. When <see cref="RawLength"/> is not zero, the range is an LZ4 block of that many bytes.</summary>
internal readonly record struct GltfNativeBlob( long Offset, int Length, int RawLength = 0 );

internal sealed record GltfNativeVertexField( string Semantic, int Index, int Offset, ColorFormat Format );

/// <summary>The serialized fields of a <see cref="SamplerState"/>. Reading SamplerState.Index registers a bindless sampler, so the state is never serialized directly.</summary>
internal sealed record GltfNativeSampler( FilterMode Filter, TextureAddressMode U, TextureAddressMode V, TextureAddressMode W, float MipLodBias, byte? MaxLod, int MaxAnisotropy, float[] BorderColor )
{
	internal static GltfNativeSampler From( SamplerState state ) => new( state.Filter, state.AddressModeU, state.AddressModeV, state.AddressModeW,
		state.MipLodBias, state.MaxLod, state.MaxAnisotropy, [state.BorderColor.r, state.BorderColor.g, state.BorderColor.b, state.BorderColor.a] );

	internal SamplerState ToState() => new()
	{
		Filter = Filter,
		AddressModeU = U,
		AddressModeV = V,
		AddressModeW = W,
		MipLodBias = MipLodBias,
		MaxLod = MaxLod,
		MaxAnisotropy = MaxAnisotropy,
		BorderColor = new Color( BorderColor[0], BorderColor[1], BorderColor[2], BorderColor[3] )
	};
}

internal sealed record GltfNativePackTexture( string Parameter, int Texture, GltfNativeSampler Sampler, string SamplerParameter );

internal sealed record GltfNativePackMaterial( string Shader, KeyValuePair<string, int>[] TemplateFeatures, KeyValuePair<string, int>[] Features,
	KeyValuePair<string, float[]>[] Vectors, KeyValuePair<string, float>[] Scalars, GltfNativePackTexture[] Textures );

/// <summary>A native bone: parent-relative bind position and rotation in engine units. Native bones have no scale.</summary>
internal sealed record GltfNativePackBone( string Name, int Parent, float[] Position, float[] Rotation );

/// <summary>A sampled clip: <see cref="FrameCount"/> frames of one <see cref="Transform"/> per bone, stored raw.</summary>
internal sealed record GltfNativePackAnimation( string Name, float FrameRate, bool Looping, int FrameCount, int BoneCount, GltfNativeBlob Frames );

/// <summary>A morph controller of one mesh: raw <see cref="MorphDelta"/> records for the mesh's stored vertices.</summary>
internal sealed record GltfNativePackMorph( string Name, GltfNativeBlob Deltas, int Count );

/// <summary>One draw: final interleaved vertices and 32-bit indices, in the layout and space the native mesh takes.</summary>
internal sealed record GltfNativePackMesh
{
	public string Name { get; init; }
	public int Node { get; init; }
	public int Mesh { get; init; }
	public int Primitive { get; init; }
	public int Material { get; init; }
	public GltfNativeBlob Vertices { get; init; }
	public int Stride { get; init; }
	public int VertexCount { get; init; }
	public GltfNativeVertexField[] Fields { get; init; }
	public GltfNativeBlob Indices { get; init; }
	public int IndexCount { get; init; }
	/// <summary>glTF topology after expansion: 0 points, 1 lines, 4 triangles.</summary>
	public int Mode { get; init; }
	public float[] Min { get; init; }
	public float[] Max { get; init; }
	/// <summary>Native LOD levels this draw belongs to, one bit per level.</summary>
	public int LodMask { get; init; }
	public GltfNativePackMorph[] Morphs { get; init; } = [];
	/// <summary>Material of this draw per glTF variant, in variant order.</summary>
	public int[] VariantMaterials { get; init; } = [];
}

/// <summary>A native material group: one material per slot of the "default" group, in the same order.</summary>
internal sealed record GltfNativePackMaterialGroup( string Name, int[] Materials );

/// <summary>A shared native model of a static scene: its meshes (stored relative to their placing node), material groups and LOD switch distances.</summary>
internal sealed record GltfNativePackGeometry( string Name, int[] Meshes, GltfNativePackMaterialGroup[] Groups, float[] LodDistances );

/// <summary>
/// A placed geometry of a static scene: engine-space position, rotation and scale relative to the model, the
/// material group it shows, and its group per glTF variant.
/// </summary>
internal sealed record GltfNativePackNode( int Node, int Geometry, float[] Position, float[] Rotation, float[] Scale, int Group, int[] VariantGroups );

internal sealed record GltfNativePackHeader
{
	public int Version { get; init; }
	public GltfNativeBlob[] Textures { get; init; } = [];
	public GltfNativePackMaterial[] Materials { get; init; } = [];
	public GltfNativePackMesh[] Meshes { get; init; } = [];
	/// <summary>Native LOD switch distance per level, in model units. Level 0 is always zero.</summary>
	public float[] LodDistances { get; init; } = [];
	public string[] Warnings { get; init; } = [];
	/// <summary>Bones in parent-first order; empty for a static model.</summary>
	public GltfNativePackBone[] Bones { get; init; } = [];
	public GltfNativePackAnimation[] Animations { get; init; } = [];
	/// <summary>glTF material variant names in source order.</summary>
	public string[] Variants { get; init; } = [];
	public GltfNativePackMaterialGroup[] MaterialGroups { get; init; } = [];
	/// <summary>Static scene: shared geometries and the nodes that place them. Empty for a single model, such as a skinned one.</summary>
	public GltfNativePackGeometry[] Geometries { get; init; } = [];
	public GltfNativePackNode[] Nodes { get; init; } = [];
}

/// <summary>
/// The native model data of a compiled glTF: a private GLB chunk after BIN. Loaders that do not know it ignore
/// it, as the GLB format requires. Every blob is ready for the engine: compiled texture resources, interleaved
/// vertex buffers and 32-bit index buffers. Loading it does no per-vertex or per-texel work.
/// </summary>
internal sealed class GltfNativePack
{
	internal const int CurrentVersion = 3;
	/// <summary>GLB chunk type "SBXN".</summary>
	internal const uint ChunkType = 0x4E584253;
	private const uint Magic = 0x314E4253; // "SBN1"
	private const int Alignment = 16;

	internal static readonly JsonSerializerOptions Json = new()
	{
		IncludeFields = false,
		Converters = { new JsonStringEnumConverter() },
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
	};

	internal GltfNativePackHeader Header { get; }
	private readonly ReadOnlyMemory<byte> blobs;

	private GltfNativePack( GltfNativePackHeader header, ReadOnlyMemory<byte> blobs ) { Header = header; this.blobs = blobs; }

	/// <summary>The bytes of a blob. A compressed blob is decoded into a new array; a raw blob is a slice of the file.</summary>
	internal ReadOnlyMemory<byte> Get( GltfNativeBlob blob )
	{
		if ( blob.Offset < 0 || blob.Length < 0 || blob.Offset + blob.Length > blobs.Length ) throw new GltfException( "A native glTF blob is outside its pack." );
		var stored = blobs.Slice( (int)blob.Offset, blob.Length );
		if ( blob.RawLength == 0 ) return stored;
		var raw = new byte[blob.RawLength];
		if ( Sandbox.Compression.LZ4.DecompressBlock( stored.Span, raw ) != raw.Length ) throw new GltfException( "A compressed native glTF blob has the wrong length." );
		return raw;
	}

	/// <summary>
	/// LZ4 each blob that shrinks by at least an eighth, in parallel. Returns the stored bytes and, per blob, its raw
	/// length or zero when it stays raw. Compiled textures from Basis compress about five times.
	/// </summary>
	internal static (byte[][] Stored, int[] RawLengths) Compress( IReadOnlyList<byte[]> blobs )
	{
		var stored = new byte[blobs.Count][];
		var raw = new int[blobs.Count];
		System.Threading.Tasks.Parallel.For( 0, blobs.Count, i =>
		{
			var source = blobs[i];
			var packed = source.Length < 4096 ? null : Sandbox.Compression.LZ4.CompressBlock( source, System.IO.Compression.CompressionLevel.Optimal );
			if ( packed is not null && packed.Length <= source.Length - source.Length / 8 ) { stored[i] = packed; raw[i] = source.Length; }
			else stored[i] = source;
		} );
		return (stored, raw);
	}

	/// <summary>A pack over blobs in memory, for a compile that is built at once and never written, like the editor preview.</summary>
	internal static GltfNativePack InMemory( GltfNativePackHeader header, IReadOnlyList<byte[]> blobs )
	{
		var layout = Layout( blobs );
		var area = new byte[layout.Length == 0 ? 0 : layout[^1].Offset + layout[^1].Length];
		for ( var i = 0; i < blobs.Count; i++ ) blobs[i].CopyTo( area, layout[i].Offset );
		return new GltfNativePack( header, area );
	}

	/// <summary>Find and read the native chunk of a GLB. Returns null when the file has none or an older version.</summary>
	internal static GltfNativePack TryRead( byte[] glb )
	{
		if ( glb is null || glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian( glb ) != 0x46546C67 ) return null;
		var offset = 12;
		while ( offset + 8 <= glb.Length )
		{
			var length = (int)BinaryPrimitives.ReadUInt32LittleEndian( glb.AsSpan( offset ) );
			var type = BinaryPrimitives.ReadUInt32LittleEndian( glb.AsSpan( offset + 4 ) );
			offset += 8;
			if ( length < 0 || length > glb.Length - offset ) throw new GltfException( "A GLB chunk exceeds the file." );
			if ( type == ChunkType ) return Parse( glb.AsMemory( offset, length ) );
			offset += length;
		}
		return null;
	}

	private static GltfNativePack Parse( ReadOnlyMemory<byte> chunk )
	{
		var span = chunk.Span;
		if ( span.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian( span ) != Magic ) throw new GltfException( "The native glTF chunk has an unknown format." );
		var jsonLength = BinaryPrimitives.ReadInt32LittleEndian( span[4..] );
		if ( jsonLength < 2 || 8 + jsonLength > span.Length ) throw new GltfException( "The native glTF chunk header is truncated." );
		var header = JsonSerializer.Deserialize<GltfNativePackHeader>( span.Slice( 8, jsonLength ), Json );
		if ( header is null || header.Version != CurrentVersion ) return null;
		var start = Align( 8 + jsonLength );
		return new GltfNativePack( header, start <= chunk.Length ? chunk[start..] : ReadOnlyMemory<byte>.Empty );
	}

	/// <summary>Append a native chunk to a GLB that has none. The input stays unchanged.</summary>
	internal static byte[] Append( byte[] glb, GltfNativePackHeader header, IReadOnlyList<byte[]> blobs )
	{
		ArgumentNullException.ThrowIfNull( glb ); ArgumentNullException.ThrowIfNull( header ); ArgumentNullException.ThrowIfNull( blobs );
		if ( glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian( glb ) != 0x46546C67 ) throw new GltfException( "Native data can only be appended to a GLB." );
		if ( TryRead( glb ) is not null ) throw new GltfException( "The GLB already has native data." );
		var json = JsonSerializer.SerializeToUtf8Bytes( header, Json );
		var blobStart = Align( 8 + json.Length );
		long size = blobStart;
		foreach ( var blob in blobs ) size = Align( size ) + blob.Length;
		size = Align( size );
		var total = checked(glb.Length + 8 + size);
		if ( total > int.MaxValue ) throw new GltfException( "The compiled glTF with native data exceeds 2 GiB." );
		var output = new byte[total];
		glb.CopyTo( output, 0 );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 8 ), (uint)total );
		var chunk = output.AsSpan( glb.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk, (uint)size );
		BinaryPrimitives.WriteUInt32LittleEndian( chunk[4..], ChunkType );
		var body = chunk[8..];
		BinaryPrimitives.WriteUInt32LittleEndian( body, Magic );
		BinaryPrimitives.WriteInt32LittleEndian( body[4..], json.Length );
		json.CopyTo( body[8..] );
		long offset = 0;
		foreach ( var blob in blobs )
		{
			offset = Align( offset );
			blob.CopyTo( body[(int)(blobStart + offset)..] );
			offset += blob.Length;
		}
		return output;
	}

	/// <summary>Lay out blobs in order: returns each blob's range, matching the layout <see cref="Append"/> writes.</summary>
	internal static GltfNativeBlob[] Layout( IReadOnlyList<byte[]> blobs, IReadOnlyList<int> rawLengths = null )
	{
		var result = new GltfNativeBlob[blobs.Count];
		long offset = 0;
		for ( var i = 0; i < blobs.Count; i++ )
		{
			offset = Align( offset );
			result[i] = new GltfNativeBlob( offset, blobs[i].Length, rawLengths?[i] ?? 0 );
			offset += blobs[i].Length;
		}
		return result;
	}

	private static int Align( int value ) => (value + Alignment - 1) & ~(Alignment - 1);
	private static long Align( long value ) => (value + Alignment - 1) & ~(long)(Alignment - 1);
}
