using Sandbox;

namespace GtaV;

// The two source fields have independent bit assignments. Keep every set bit
// available for lookup, including bits whose meaning is not known.
internal static class GtaFlagTags
{
	static readonly string[] Archetype =
	[
		"wet-road-reflection", "dont-fade", "draw-last", "prop-climbable-by-ai",
		"suppress-hd-txds", "fixed", "dont-write-zbuffer", "tough-for-bullets",
		"generic", "has-animation", "has-uv-animation", "shadow-only",
		"damage-model", "dont-cast-shadows", "cast-texture-shadows", "dont-collide-with-flyer",
		"tree", "object", "override-physics-bounds", "autostart-animation",
		"pre-reflected-water-proxy", "drawable-water-reflection-proxy", "no-ai-cover", "no-player-cover",
		"ladder-deprecated", "has-cloth", "door-physics", "fixed-for-navigation",
		"dont-avoid-by-peds", "use-ambient-scale", "debug", "alpha-shadow"
	];

	static readonly string[] Placement =
	[
		"full-matrix", "stream-low-priority", "dont-instance-collision", "lod-in-parent-map",
		"lod-adopt-me", "fixed", "interior-lod", null, null, null, null, null, null, null, null,
		"lod-alt-fade", "unused", "does-not-touch-water", "does-not-spawn-peds", "lights-cast-static-shadows",
		"lights-cast-dynamic-shadows", "lights-ignore-day-night", "dont-render-in-shadows", "only-render-in-shadows",
		"dont-render-in-reflections", "only-render-in-reflections", "dont-render-in-water-reflections", "only-render-in-water-reflections",
		"dont-render-in-mirror-reflections", "only-render-in-mirror-reflections", null, null
	];

	static readonly string[] ArchetypeTags = BuildTags( "gta-archetype", Archetype );
	static readonly string[] PlacementTags = BuildTags( "gta-placement", Placement );

	internal static void AddArchetype( GameObject obj, uint flags ) => Add( obj, flags, ArchetypeTags );
	internal static void AddPlacement( GameObject obj, uint flags ) => Add( obj, flags, PlacementTags );

	internal static void ApplyShadows( ModelRenderer renderer, uint archetype, uint placement )
	{
		if ( !renderer.IsValid() ) return;
		bool noShadows = (archetype & (1u << 13)) != 0 || (placement & (1u << 22)) != 0;
		bool shadowsOnly = (archetype & (1u << 11)) != 0 || (placement & (1u << 23)) != 0;
		if ( noShadows == shadowsOnly ) return;
		renderer.RenderType = noShadows ? ModelRenderer.ShadowRenderType.Off : ModelRenderer.ShadowRenderType.ShadowsOnly;
	}

	static string[] BuildTags( string prefix, string[] names )
	{
		var tags = new string[32];
		for ( int bit = 0; bit < tags.Length; bit++ )
		{
			string name = names[bit] is { } known ? $"{prefix}-{known}" : null;
			// Keep existing valid names. Long or unknown names still expose their exact bit.
			tags[bit] = name.IsValidTag() ? name : $"{prefix}-bit-{bit}";
		}
		return tags;
	}

	static void Add( GameObject obj, uint flags, string[] tags )
	{
		while ( flags != 0 )
		{
			obj.Tags.Add( tags[System.Numerics.BitOperations.TrailingZeroCount( flags )] );
			flags &= flags - 1;
		}
	}
}
