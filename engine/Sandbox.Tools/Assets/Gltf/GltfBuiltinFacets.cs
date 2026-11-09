using System;
using System.Text.Json.Serialization;

namespace Editor.GltfFacetTypes;

// Each class here is one property group in the glTF inspector. To support another
// extension, copy the nearest class: the attribute names the element kind and the
// JSON path, and the properties map to JSON keys. Use the glTF spec defaults as initial values.

[GltfFacetFor( "nodes", "" ), Title( "Transform" ), Icon( "open_with" )]
public sealed class NodeTransformFacet : GltfFacet
{
	public Vector3 Translation { get; set; }
	public Rotation Rotation { get; set; } = Rotation.Identity;
	public Vector3 Scale { get; set; } = Vector3.One;
}

[GltfFacetFor( "nodes", "extensions/MSFT_lod" ), Title( "Level of Detail" ), Icon( "filter_none" )]
[Description( "Lower detail versions of this node, as node indices from highest to lowest detail (MSFT_lod)." )]
public sealed class NodeLodFacet : GltfFacet
{
	public int[] Ids { get; set; } = [];
}

public enum GltfAlphaMode
{
	[JsonPropertyName( "OPAQUE" )] Opaque,
	[JsonPropertyName( "MASK" )] Mask,
	[JsonPropertyName( "BLEND" )] Blend,
}

[GltfFacetFor( "materials", "" ), Title( "Surface" ), Icon( "texture" )]
public sealed class MaterialSurfaceFacet : GltfFacet
{
	public GltfAlphaMode AlphaMode { get; set; } = GltfAlphaMode.Opaque;
	[Range( 0, 1 )] public float AlphaCutoff { get; set; } = 0.5f;
	public bool DoubleSided { get; set; }
	[GltfRgb] public Color EmissiveFactor { get; set; } = Color.Black;
}

[GltfFacetFor( "materials", "pbrMetallicRoughness" ), Title( "Metallic Roughness" ), Icon( "palette" )]
public sealed class MaterialPbrFacet : GltfFacet
{
	public Color BaseColorFactor { get; set; } = Color.White;
	[Range( 0, 1 )] public float MetallicFactor { get; set; } = 1;
	[Range( 0, 1 )] public float RoughnessFactor { get; set; } = 1;
}

[GltfFacetFor( "materials", "extensions/KHR_materials_emissive_strength" ), Title( "Emissive Strength" ), Icon( "wb_sunny" )]
public sealed class MaterialEmissiveStrengthFacet : GltfFacet
{
	[Range( 0, 100 )] public float EmissiveStrength { get; set; } = 1;
}

[GltfFacetFor( "materials", "extensions/KHR_materials_clearcoat" ), Title( "Clearcoat" ), Icon( "layers" )]
public sealed class MaterialClearcoatFacet : GltfFacet
{
	[Range( 0, 1 )] public float ClearcoatFactor { get; set; }
	[Range( 0, 1 )] public float ClearcoatRoughnessFactor { get; set; }
}

[GltfFacetFor( "materials", "extensions/KHR_materials_ior" ), Title( "Index of Refraction" ), Icon( "water_drop" )]
public sealed class MaterialIorFacet : GltfFacet
{
	[Range( 1, 3 )] public float Ior { get; set; } = 1.5f;
}

[GltfFacetFor( "materials", "extensions/KHR_materials_unlit" ), Title( "Unlit" ), Icon( "light_mode" )]
[Description( "Render this material without lighting (KHR_materials_unlit). It has no properties." )]
public sealed class MaterialUnlitFacet : GltfFacet
{
}

[GltfFacetFor( "cameras", "perspective" ), Title( "Perspective" ), Icon( "videocam" )]
public sealed class CameraPerspectiveFacet : GltfFacet
{
	[Description( "Vertical field of view in radians." )] public float Yfov { get; set; } = 0.8f;
	public float Znear { get; set; } = 0.01f;
	public float Zfar { get; set; } = 1000;
}

public enum GltfLightType
{
	[JsonPropertyName( "directional" )] Directional,
	[JsonPropertyName( "point" )] Point,
	[JsonPropertyName( "spot" )] Spot,
}

[GltfFacetFor( "extensions/KHR_lights_punctual/lights", "" ), Title( "Light" ), Icon( "lightbulb" )]
public sealed class LightFacet : GltfFacet
{
	public GltfLightType Type { get; set; } = GltfLightType.Point;
	[GltfRgb] public Color Color { get; set; } = Color.White;
	public float Intensity { get; set; } = 1;
	[Description( "Distance where the light reaches zero. Zero means no limit." )] public float Range { get; set; }
}
