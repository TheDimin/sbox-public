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
	/// <summary>Material extensions this reader does not implement. A renderer must show an error material, not a guess.</summary>
	public IReadOnlyList<string> UnsupportedExtensions { get; init; } = [];
	/// <summary>Supported thin-surface diffuse transmission; null for absent or unsupported extension.</summary>
	public GltfDiffuseTransmission DiffuseTransmission { get; init; }
	/// <summary>KHR_materials_clearcoat; null for absent or unsupported extension.</summary>
	public GltfClearcoat Clearcoat { get; init; }
	/// <summary>KHR_materials_ior; null when absent. 1.5 is the glTF default.</summary>
	public float? Ior { get; init; }
	public GltfSpecular Specular { get; init; }
	/// <summary>
	/// Glossiness factor of KHR_materials_pbrSpecularGlossiness; null for a metal/rough material. When set, the
	/// base colour fields hold the diffuse colour, and <see cref="Specular"/> holds the specular colour factor and
	/// the specular-glossiness texture (sRGB specular in RGB, linear glossiness in A) as its colour texture.
	/// </summary>
	public float? Glossiness { get; init; }
	public GltfSheen Sheen { get; init; }
	public GltfIridescence Iridescence { get; init; }
	public GltfTransmission Transmission { get; init; }
	public GltfVolume Volume { get; init; }
	/// <summary>KHR_materials_dispersion; 0 when absent.</summary>
	public float Dispersion { get; init; }
	/// <summary>KHR_materials_anisotropy; null for absent or unsupported extension.</summary>
	public GltfAnisotropy Anisotropy { get; init; }
	public GltfTextureInfo BaseColorTexture { get; init; }
	public GltfTextureInfo MetallicRoughnessTexture { get; init; }
	public GltfTextureInfo NormalTexture { get; init; }
	public GltfTextureInfo OcclusionTexture { get; init; }
	public GltfTextureInfo EmissiveTexture { get; init; }
}

/// <summary>Standard KHR_materials_diffuse_transmission Release Candidate parameters.</summary>
public sealed class GltfDiffuseTransmission
{
	public float Factor { get; init; }
	public Vector3 ColorFactor { get; init; } = Vector3.One;
	public GltfTextureInfo Texture { get; init; }
	public GltfTextureInfo ColorTexture { get; init; }
}

/// <summary>KHR_materials_clearcoat parameters. The coat factor is texture R, coat roughness is texture G.</summary>
public sealed class GltfClearcoat
{
	public float Factor { get; init; }
	public GltfTextureInfo Texture { get; init; }
	public float RoughnessFactor { get; init; }
	public GltfTextureInfo RoughnessTexture { get; init; }
	public GltfTextureInfo NormalTexture { get; init; }
}

/// <summary>KHR_materials_anisotropy parameters. Texture RG is the tangent-space direction, B scales strength.</summary>
public sealed class GltfAnisotropy
{
	public float Strength { get; init; }
	/// <summary>Counter-clockwise rotation of the direction in tangent space, in radians.</summary>
	public float Rotation { get; init; }
	public GltfTextureInfo Texture { get; init; }
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

/// <summary>KHR_materials_specular. Factor is texture A; colour is texture RGB (sRGB).</summary>
public sealed class GltfSpecular
{
	public float Factor { get; init; } = 1;
	public GltfTextureInfo Texture { get; init; }
	public Vector3 ColorFactor { get; init; } = Vector3.One;
	public GltfTextureInfo ColorTexture { get; init; }
}

/// <summary>KHR_materials_sheen. Colour is texture RGB (sRGB); roughness is texture A.</summary>
public sealed class GltfSheen
{
	public Vector3 ColorFactor { get; init; }
	public GltfTextureInfo ColorTexture { get; init; }
	public float RoughnessFactor { get; init; }
	public GltfTextureInfo RoughnessTexture { get; init; }
}

/// <summary>KHR_materials_iridescence. Factor is texture R; thickness is texture G mixed between min and max (nm).</summary>
public sealed class GltfIridescence
{
	public float Factor { get; init; }
	public GltfTextureInfo Texture { get; init; }
	public float Ior { get; init; } = 1.3f;
	public float ThicknessMinimum { get; init; } = 100;
	public float ThicknessMaximum { get; init; } = 400;
	public GltfTextureInfo ThicknessTexture { get; init; }
}

/// <summary>KHR_materials_transmission. Factor is texture R.</summary>
public sealed class GltfTransmission
{
	public float Factor { get; init; }
	public GltfTextureInfo Texture { get; init; }
}

/// <summary>KHR_materials_volume. Thickness is texture G in model units. Distance +infinity means no absorption.</summary>
public sealed class GltfVolume
{
	public float ThicknessFactor { get; init; }
	public GltfTextureInfo ThicknessTexture { get; init; }
	public float AttenuationDistance { get; init; } = float.PositiveInfinity;
	public Vector3 AttenuationColor { get; init; } = Vector3.One;
}
