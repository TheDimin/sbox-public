using System.Numerics;

namespace Sandbox.Gltf;

/// <summary>A malformed asset or an unsupported required glTF feature.</summary>
public sealed class GltfException( string message, Exception inner = null ) : FormatException( message, inner );

public sealed partial class GltfDocument : IDisposable
{
}

public sealed partial class GltfPose
{
}

public sealed class GltfPrimitive
{
	public int VertexCount { get; init; }
	public int Mode { get; init; }
	public int? Material { get; init; }
	public uint[] Indices { get; init; }
	public IReadOnlyDictionary<string, int> Attributes { get; init; }
}

public sealed class GltfImage
{
	public byte[] Bytes { get; init; }
	public string MimeType { get; init; }
}

public sealed class GltfTexture
{
	public int? Image { get; init; }
	public int? Sampler { get; init; }
}

public sealed class GltfMaterial
{
	public Vector4 BaseColorFactor { get; init; }
	public float MetallicFactor { get; init; }
	public float RoughnessFactor { get; init; }
	public Vector3 EmissiveFactor { get; init; }
	public string AlphaMode { get; init; }
	public float AlphaCutoff { get; init; }
	public bool DoubleSided { get; init; }
	public bool Unlit { get; init; }
	public GltfTextureInfo BaseColorTexture { get; init; }
	public GltfTextureInfo MetallicRoughnessTexture { get; init; }
	public GltfTextureInfo NormalTexture { get; init; }
	public GltfTextureInfo OcclusionTexture { get; init; }
	public GltfTextureInfo EmissiveTexture { get; init; }
}

public sealed partial class GltfTextureInfo
{
	public int Index { get; init; }
	public int TexCoord { get; init; }
	public bool IsSrgb { get; init; }
	public float Scale { get; init; }
	public float Strength { get; init; }
	public Vector2 Offset { get; init; }
	public Vector2 Tiling { get; init; }
	public float Rotation { get; init; }
}

public sealed class GltfSampler
{
	public int WrapS { get; init; }
	public int WrapT { get; init; }
	public int? MinFilter { get; init; }
	public int? MagFilter { get; init; }
}

public sealed class GltfSkin
{
	public int[] Joints { get; init; }
	public int? Skeleton { get; init; }
	public Matrix4x4[] InverseBindMatrices { get; init; }
}

public sealed class GltfMorphTarget
{
	public float[] PositionDeltas { get; init; }
	public float[] NormalDeltas { get; init; }
	public float[] TangentDeltas { get; init; }
}

public sealed class GltfCamera
{
	public string Type { get; init; }
	public float YFov { get; init; }
	public float? AspectRatio { get; init; }
	public float ZNear { get; init; }
	public float? ZFar { get; init; }
	public float XMagnitude { get; init; }
	public float YMagnitude { get; init; }
}

public sealed class GltfLight
{
	public string Type { get; init; }
	public Vector3 Color { get; init; }
	public float Intensity { get; init; }
	public float? Range { get; init; }
	public float InnerConeAngle { get; init; }
	public float OuterConeAngle { get; init; }
}
