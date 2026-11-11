using Sandbox.Gltf;
using System.Collections.ObjectModel;

namespace Sandbox;

using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

/// <summary>Canonical source texture roles shared by managed material preparation.</summary>
internal enum GltfMaterialTextureRole
{
	BaseColor,
	MetallicRoughness,
	Normal,
	Occlusion,
	Emissive,
	DiffuseTransmission,
	DiffuseTransmissionColor,
	Clearcoat,
	ClearcoatRoughness,
	ClearcoatNormal,
	SheenColor,
	SheenRoughness,
	Specular,
	SpecularColor,
	Iridescence,
	IridescenceThickness,
	Transmission,
	Thickness,
	Anisotropy
}

/// <summary>
/// Source UV transformation, independent of native shader uniforms or vertex
/// packing. The authored properties remain available alongside affine rows.
/// </summary>
internal readonly struct GltfMaterialUvTransform
{
	internal NVector2 Offset { get; }
	internal NVector2 Tiling { get; }
	internal float Rotation { get; }
	/// <summary>Dot with (u,v,1) to obtain the transformed U coordinate.</summary>
	internal NVector3 RowU { get; }
	/// <summary>Dot with (u,v,1) to obtain the transformed V coordinate.</summary>
	internal NVector3 RowV { get; }

	internal GltfMaterialUvTransform( GltfTextureInfo texture )
	{
		ArgumentNullException.ThrowIfNull( texture );
		if ( !Finite( texture.Offset ) || !Finite( texture.Tiling ) || !float.IsFinite( texture.Rotation ) )
			throw new GltfException( "Material UV offset, scale and rotation must be finite." );
		Offset = texture.Offset;
		Tiling = texture.Tiling;
		Rotation = texture.Rotation;
		var cosine = MathF.Cos( Rotation );
		var sine = MathF.Sin( Rotation );
		// Match GltfTextureInfo.TransformUv and the Khronos Sample Renderer.
		RowU = new NVector3( cosine * Tiling.X, sine * Tiling.Y, Offset.X );
		RowV = new NVector3( -sine * Tiling.X, cosine * Tiling.Y, Offset.Y );
	}

	/// <summary>Evaluate the declared map; this never modifies or repacks a source stream.</summary>
	internal NVector2 Apply( NVector2 uv ) => new(
		RowU.X * uv.X + RowU.Y * uv.Y + RowU.Z,
		RowV.X * uv.X + RowV.Y * uv.Y + RowV.Z );

	private static bool Finite( NVector2 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y );
}

/// <summary>
/// One consumed texture role and its exact materialized source UV descriptor.
/// No image is decoded, sampler approximated or native resource allocated.
/// </summary>
internal sealed class GltfMaterialUvBinding
{
	internal GltfMaterialTextureRole Role { get; }
	internal GltfTextureInfo TextureInfo { get; }
	internal int TextureIndex => TextureInfo.Index;
	internal int TexCoord => TextureInfo.TexCoord;
	internal bool IsSrgb => TextureInfo.IsSrgb;
	internal GltfUploadStream Source { get; }
	internal GltfMaterialUvTransform Transform { get; }

	internal GltfMaterialUvBinding( GltfMaterialTextureRole role, GltfTextureInfo textureInfo, GltfUploadStream source )
	{
		Role = role;
		TextureInfo = textureInfo;
		Source = source;
		Transform = new GltfMaterialUvTransform( textureInfo );
	}
}

/// <summary>
/// Complete immutable UV binding selection for a primitive/material pair.
/// Independent roles may use different source sets, formats and transforms.
/// Creating another selection never bakes into shared source data or modifies a
/// previous plan. Variant owners publish completed selections atomically.
/// </summary>
internal sealed class GltfMaterialUvPlan
{
	private static readonly GltfMaterialTextureRole[] AllRoles =
	[
		GltfMaterialTextureRole.BaseColor,
		GltfMaterialTextureRole.MetallicRoughness,
		GltfMaterialTextureRole.Normal,
		GltfMaterialTextureRole.Occlusion,
		GltfMaterialTextureRole.Emissive,
		GltfMaterialTextureRole.DiffuseTransmission,
		GltfMaterialTextureRole.DiffuseTransmissionColor,
		GltfMaterialTextureRole.Clearcoat,
		GltfMaterialTextureRole.ClearcoatRoughness,
		GltfMaterialTextureRole.ClearcoatNormal,
		GltfMaterialTextureRole.Anisotropy,
		GltfMaterialTextureRole.SheenColor,
		GltfMaterialTextureRole.SheenRoughness,
		GltfMaterialTextureRole.Specular,
		GltfMaterialTextureRole.SpecularColor,
		GltfMaterialTextureRole.Iridescence,
		GltfMaterialTextureRole.IridescenceThickness,
		GltfMaterialTextureRole.Transmission,
		GltfMaterialTextureRole.Thickness
	];

	internal int? MaterialIndex { get; }
	internal IReadOnlyDictionary<GltfMaterialTextureRole, GltfMaterialUvBinding> Bindings { get; }

	private GltfMaterialUvPlan( int? materialIndex, Dictionary<GltfMaterialTextureRole, GltfMaterialUvBinding> bindings )
	{
		MaterialIndex = materialIndex;
		Bindings = new ReadOnlyDictionary<GltfMaterialTextureRole, GltfMaterialUvBinding>( bindings );
	}

	/// <summary>Prepare every present core texture role; a null material has no textures.</summary>
	internal static GltfMaterialUvPlan Create( GltfMaterial material, GltfPrimitiveUploadData primitive, int? materialIndex = null ) =>
		CreateForRoles( material, primitive, AllRoles, materialIndex );

	/// <summary>
	/// Prepare only roles actually consumed by the selected material program. For
	/// example, KHR_materials_unlit consumes base color and ignores other roles.
	/// Ignored textures do not cause UV validation or resource resolution here.
	/// </summary>
	internal static GltfMaterialUvPlan CreateForRoles( GltfMaterial material, GltfPrimitiveUploadData primitive,
		IEnumerable<GltfMaterialTextureRole> consumedRoles, int? materialIndex = null )
	{
		ArgumentNullException.ThrowIfNull( primitive );
		ArgumentNullException.ThrowIfNull( consumedRoles );
		if ( materialIndex < 0 ) throw new ArgumentOutOfRangeException( nameof( materialIndex ) );
		if ( primitive.Layout is null || primitive.Attributes is null ) throw new ArgumentException( "Primitive upload layout and attributes are required.", nameof( primitive ) );
		var roles = new HashSet<GltfMaterialTextureRole>();
		foreach ( var role in consumedRoles )
		{
			if ( !Enum.IsDefined( role ) ) throw new ArgumentOutOfRangeException( nameof( consumedRoles ), "Unknown material texture role." );
			if ( !roles.Add( role ) ) throw new ArgumentException( "Consumed material roles must be unique.", nameof( consumedRoles ) );
		}

		var bindings = new Dictionary<GltfMaterialTextureRole, GltfMaterialUvBinding>();
		foreach ( var role in roles.Order() )
		{
			var texture = TextureForRole( material, role );
			if ( texture is null ) continue;
			if ( texture.Index < 0 || texture.TexCoord < 0 ) throw new GltfException( $"Material {Identity( materialIndex )} role {role} has a negative texture or coordinate index." );
			var semantic = "TEXCOORD_" + texture.TexCoord;
			if ( !primitive.Attributes.TryGetValue( semantic, out var source ) )
				throw new GltfException( $"Material {Identity( materialIndex )} role {role} requires missing {semantic}." );
			if ( source is null || source.Storage is null || source.Components != 2 || source.Count != primitive.Layout.VertexCount )
				throw new GltfException( $"Material {Identity( materialIndex )} role {role} has an invalid source descriptor for {semantic}." );
			bindings.Add( role, new GltfMaterialUvBinding( role, texture, source ) );
		}
		return new GltfMaterialUvPlan( materialIndex, bindings );
	}

	/// <summary>
	/// Resolve the standards-defined variant/default material and prepare a new
	/// complete plan. The upload must belong to this document/mesh/primitive.
	/// Detached upload data carries no document-owner token: matching accessor
	/// indices or layouts does not prove ownership. The owning backend must check
	/// its document/primitive identity and exact source stream references before
	/// calling this method; this factory deliberately treats that as a caller
	/// precondition rather than accepting a misleading metadata-only check.
	/// Callers with a restricted program pass its consumed roles explicitly, then
	/// publish all affected primitive plans together after successful preparation.
	/// </summary>
	internal static GltfMaterialUvPlan CreateForVariant( GltfDocument document, int mesh, int primitive,
		GltfPrimitiveUploadData upload, int? variant, IEnumerable<GltfMaterialTextureRole> consumedRoles = null )
	{
		ArgumentNullException.ThrowIfNull( document );
		var materialIndex = document.GetMaterialForVariant( mesh, primitive, variant );
		var material = materialIndex.HasValue ? document.ReadMaterial( materialIndex.Value ) : null;
		return CreateForRoles( material, upload, consumedRoles ?? AllRoles, materialIndex );
	}

	private static string Identity( int? materialIndex ) => materialIndex.HasValue ? materialIndex.Value.ToString( System.Globalization.CultureInfo.InvariantCulture ) : "<default>";

	private static GltfTextureInfo TextureForRole( GltfMaterial material, GltfMaterialTextureRole role ) => role switch
	{
		GltfMaterialTextureRole.BaseColor => material?.BaseColorTexture,
		GltfMaterialTextureRole.MetallicRoughness => material?.MetallicRoughnessTexture,
		GltfMaterialTextureRole.Normal => material?.NormalTexture,
		GltfMaterialTextureRole.Occlusion => material?.OcclusionTexture,
		GltfMaterialTextureRole.Emissive => material?.EmissiveTexture,
		GltfMaterialTextureRole.DiffuseTransmission => material?.DiffuseTransmission?.Texture,
		GltfMaterialTextureRole.DiffuseTransmissionColor => material?.DiffuseTransmission?.ColorTexture,
		GltfMaterialTextureRole.Clearcoat => material?.Clearcoat?.Texture,
		GltfMaterialTextureRole.ClearcoatRoughness => material?.Clearcoat?.RoughnessTexture,
		GltfMaterialTextureRole.ClearcoatNormal => material?.Clearcoat?.NormalTexture,
		GltfMaterialTextureRole.Anisotropy => material?.Anisotropy?.Texture,
		GltfMaterialTextureRole.SheenColor => material?.Sheen?.ColorTexture,
		GltfMaterialTextureRole.SheenRoughness => material?.Sheen?.RoughnessTexture,
		GltfMaterialTextureRole.Specular => material?.Specular?.Texture,
		GltfMaterialTextureRole.SpecularColor => material?.Specular?.ColorTexture,
		GltfMaterialTextureRole.Iridescence => material?.Iridescence?.Texture,
		GltfMaterialTextureRole.IridescenceThickness => material?.Iridescence?.ThicknessTexture,
		GltfMaterialTextureRole.Transmission => material?.Transmission?.Texture,
		GltfMaterialTextureRole.Thickness => material?.Volume?.ThicknessTexture,
		_ => throw new ArgumentOutOfRangeException( nameof( role ) )
	};
}
