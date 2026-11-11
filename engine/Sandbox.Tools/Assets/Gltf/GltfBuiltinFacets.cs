using System;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.GltfFacetTypes;

// Each class here is one property group in the glTF inspector. To support another
// extension, copy the nearest class: the attribute names the element kind and the
// JSON path, and the properties map to JSON keys. Use the glTF spec defaults as initial values.

[GltfFacetFor( "nodes", "" ), Title( "Transform" ), Icon( "open_with" )]
public sealed class NodeTransformFacet : GltfFacet
{
	[Property] public Vector3 Translation { get; set; }
	[Property] public Rotation Rotation { get; set; } = Rotation.Identity;
	[Property] public Vector3 Scale { get; set; } = Vector3.One;
}

[GltfFacetFor( "nodes", "extensions/MSFT_lod" ), Title( "Level of Detail" ), Icon( "filter_none" )]
[Description( "Lower detail versions of this node, shown when it covers less of the screen (MSFT_lod). The levels are other nodes of this file." )]
public sealed class NodeLodFacet : GltfFacet
{
	[Property] public GltfLodLevels Levels { get; set; } = new();

	// The levels span the node (ids) and its extras (thresholds), so the facet object alone is not enough.
	// The inspector edits through GltfEditorSession.SetNodeLods.
	protected internal override void Read( JsonObject target ) => Levels = GltfLodEditing.Read( Element?.Json );

	protected internal override void Write( JsonObject target, string propertyName ) =>
		target["ids"] = new JsonArray( Levels.Levels.Select( x => (JsonNode)JsonValue.Create( x.Node ) ).ToArray() );
}

[GltfFacetFor( "nodes", "extras/sbox_lod" ), Title( "Generated LOD" ), Icon( "auto_fix_high" )]
[Description( "The import makes this level of detail from the node's mesh by removing triangles, until it keeps the ratio or reaches the error limit." )]
public sealed class NodeGeneratedLodFacet : GltfFacet
{
	/// <summary>The part of the mesh's triangles to keep.</summary>
	[Property, Range( 0.01f, 1 )] public float Ratio { get; set; } = 0.5f;
	/// <summary>The largest distance any surface may move, as a part of the mesh size. The reduction stops there, even above the ratio.</summary>
	[Property, Range( 0, 0.2f )] public float Error { get; set; } = 0.01f;
	/// <summary>Keep open edges where they are, so meshes that meet at an edge stay closed.</summary>
	[Property] public bool LockBorder { get; set; }
}

[GltfFacetFor( "nodes", "" ), Title( "Morph Weights" ), Icon( "tune" )]
[Description( "Default weights of the morph targets of this node's mesh. Empty uses the mesh defaults." )]
public sealed class NodeMorphWeightsFacet : GltfFacet
{
	[Property] public float[] Weights { get; set; } = [];
}

public enum GltfAlphaMode
{
	[JsonPropertyName( "OPAQUE" )] Opaque,
	[JsonPropertyName( "MASK" )] Mask,
	[JsonPropertyName( "BLEND" )] Blend,
}

[GltfFacetFor( "materials", "" ), Title( "Surface" ), Icon( "texture" )]
[GltfShaderParameter( "g_vGltfBaseColor" )]
public sealed class MaterialSurfaceFacet : GltfFacet
{
	[Property, GltfTextureAmount( "scale" )] public GltfTextureSlot NormalTexture { get; set; } = new();
	[Property, GltfTextureAmount( "strength" )] public GltfTextureSlot OcclusionTexture { get; set; } = new();
	[Property] public GltfTextureSlot EmissiveTexture { get; set; } = new();
	[Property] public GltfAlphaMode AlphaMode { get; set; } = GltfAlphaMode.Opaque;
	[Property][Range( 0, 1 )] public float AlphaCutoff { get; set; } = 0.5f;
	[Property] public bool DoubleSided { get; set; }
	/// <summary>
	/// The emitted light color. glTF limits emissiveFactor to 0..1, so a brighter color is written as a
	/// factor of at most 1 and a KHR_materials_emissive_strength multiplier. This shows the product of both.
	/// </summary>
	[Property, ColorUsage( false, true ), GltfRgb] public Color EmissiveFactor { get; set; } = Color.Black;

	const string StrengthExtension = "KHR_materials_emissive_strength";

	protected internal override void Read( JsonObject target )
	{
		base.Read( target );
		if ( target["extensions"]?[StrengthExtension]?["emissiveStrength"] is JsonValue value && value.TryGetValue<float>( out var strength ) )
			EmissiveFactor = new Color( EmissiveFactor.r * strength, EmissiveFactor.g * strength, EmissiveFactor.b * strength );
	}

	protected internal override void Write( JsonObject target, string propertyName )
	{
		base.Write( target, propertyName );
		if ( propertyName is not null && propertyName != nameof( EmissiveFactor ) ) return;

		var color = EmissiveFactor;
		var brightest = MathF.Max( color.r, MathF.Max( color.g, color.b ) );
		var extensions = target["extensions"] as JsonObject;
		if ( brightest > 1 )
		{
			target["emissiveFactor"] = new JsonArray( color.r / brightest, color.g / brightest, color.b / brightest );
			if ( extensions is null ) target["extensions"] = extensions = new JsonObject();
			extensions[StrengthExtension] = new JsonObject { ["emissiveStrength"] = brightest };
		}
		else if ( extensions is not null )
		{
			extensions.Remove( StrengthExtension );
			if ( extensions.Count == 0 ) target.Remove( "extensions" );
		}
	}
}

[GltfFacetFor( "materials", "pbrMetallicRoughness" ), Title( "Metallic Roughness" ), Icon( "palette" )]
public sealed class MaterialPbrFacet : GltfFacet
{
	[Property] public Color BaseColorFactor { get; set; } = Color.White;
	[Property][Range( 0, 1 )] public float MetallicFactor { get; set; } = 1;
	[Property][Range( 0, 1 )] public float RoughnessFactor { get; set; } = 1;
	[Property] public GltfTextureSlot BaseColorTexture { get; set; } = new();
	[Property] public GltfTextureSlot MetallicRoughnessTexture { get; set; } = new();
}

[GltfFacetFor( "materials", "extensions/KHR_materials_clearcoat" ), Title( "Clearcoat" ), Icon( "layers" )]
public sealed class MaterialClearcoatFacet : GltfFacet
{
	[Property, GltfShaderParameter( "g_flGltfClearcoatFactor" )][Range( 0, 1 )] public float ClearcoatFactor { get; set; }
	[Property, GltfShaderParameter( "g_flGltfClearcoatRoughnessFactor" )][Range( 0, 1 )] public float ClearcoatRoughnessFactor { get; set; }
	[Property] public GltfTextureSlot ClearcoatTexture { get; set; } = new();
	[Property] public GltfTextureSlot ClearcoatRoughnessTexture { get; set; } = new();
	[Property, GltfTextureAmount( "scale" )] public GltfTextureSlot ClearcoatNormalTexture { get; set; } = new();
}

[GltfFacetFor( "materials", "extensions/KHR_materials_anisotropy" ), Title( "Anisotropy" ), Icon( "blur_linear" )]
public sealed class MaterialAnisotropyFacet : GltfFacet
{
	[Property, GltfShaderParameter( "g_flGltfAnisotropyStrength" )][Range( 0, 1 )] public float AnisotropyStrength { get; set; }
	[Property, GltfShaderParameter( "g_vGltfAnisotropyRotation" )][Description( "Rotation in radians, counter-clockwise from the tangent." )] public float AnisotropyRotation { get; set; }
	[Property] public GltfTextureSlot AnisotropyTexture { get; set; } = new();
}

[GltfFacetFor( "materials", "extensions/KHR_materials_diffuse_transmission" ), Title( "Diffuse Transmission" ), Icon( "flare" )]
[GltfShaderSelector( "shaders/gltf_diffuse_transmission.shader" )]
public sealed class MaterialDiffuseTransmissionFacet : GltfFacet
{
	[Property, GltfShaderParameter( "g_flGltfDiffuseTransmissionFactor" )][Range( 0, 1 )] public float DiffuseTransmissionFactor { get; set; }
	[Property, GltfShaderParameter( "g_vGltfDiffuseTransmissionColorFactor" ), GltfRgb] public Color DiffuseTransmissionColorFactor { get; set; } = Color.White;
	[Property] public GltfTextureSlot DiffuseTransmissionTexture { get; set; } = new();
	[Property] public GltfTextureSlot DiffuseTransmissionColorTexture { get; set; } = new();
}

[GltfFacetFor( "materials", "extensions/KHR_materials_ior" ), Title( "Index of Refraction" ), Icon( "water_drop" )]
public sealed class MaterialIorFacet : GltfFacet
{
	[Property][Range( 1, 3 )] public float Ior { get; set; } = 1.5f;
}

[GltfFacetFor( "materials", "extensions/KHR_materials_unlit" ), Title( "Unlit" ), Icon( "light_mode" )]
[Description( "Render this material without lighting (KHR_materials_unlit). It has no properties." )]
[GltfShaderSelector( "shaders/gltf_unlit_five_uv.shader" )]
public sealed class MaterialUnlitFacet : GltfFacet
{
}

[GltfFacetFor( "cameras", "orthographic" ), Title( "Orthographic" ), Icon( "crop_square" )]
public sealed class CameraOrthographicFacet : GltfFacet
{
	[Property][Description( "Half the width of the view, in meters." )] public float Xmag { get; set; } = 1;
	[Property][Description( "Half the height of the view, in meters." )] public float Ymag { get; set; } = 1;
	[Property] public float Znear { get; set; } = 0.01f;
	[Property] public float Zfar { get; set; } = 1000;
}

[GltfFacetFor( "cameras", "perspective" ), Title( "Perspective" ), Icon( "videocam" )]
public sealed class CameraPerspectiveFacet : GltfFacet
{
	[Property][Description( "Vertical field of view in radians." )] public float Yfov { get; set; } = 0.8f;
	[Property] public float Znear { get; set; } = 0.01f;
	[Property] public float Zfar { get; set; } = 1000;
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
	[Property] public GltfLightType Type { get; set; } = GltfLightType.Point;
	[Property][GltfRgb] public Color Color { get; set; } = Color.White;
	[Property] public float Intensity { get; set; } = 1;
	[Property][Description( "Distance where the light reaches zero. Zero means no limit." )] public float Range { get; set; }
}
