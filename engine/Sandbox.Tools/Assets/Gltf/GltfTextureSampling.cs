using System.Text.Json.Nodes;

namespace Editor;

public enum GltfMagFilter { Default = 0, Nearest = 9728, Linear = 9729 }

public enum GltfMinFilter
{
	Default = 0, Nearest = 9728, Linear = 9729,
	NearestMipmapNearest = 9984, LinearMipmapNearest = 9985, NearestMipmapLinear = 9986, LinearMipmapLinear = 9987,
}

public enum GltfWrap { Repeat = 10497, ClampToEdge = 33071, MirroredRepeat = 33648 }

/// <summary>
/// The sampling settings of a texture. In glTF these live in a shared sampler element; the editor shows
/// them on the texture and finds or creates the matching sampler (see GltfEditorSession.SetTextureSampling).
/// </summary>
public readonly record struct GltfTextureSampling( GltfMagFilter MagFilter, GltfMinFilter MinFilter, GltfWrap WrapS, GltfWrap WrapT )
{
	public static GltfTextureSampling Default => new( GltfMagFilter.Default, GltfMinFilter.Default, GltfWrap.Repeat, GltfWrap.Repeat );

	public bool IsDefault => this == Default;

	internal static GltfTextureSampling Read( JsonObject sampler )
	{
		if ( sampler is null ) return Default;
		int Get( string key, int fallback ) => sampler[key] is JsonValue v && v.TryGetValue<int>( out var i ) ? i : fallback;
		return new( (GltfMagFilter)Get( "magFilter", 0 ), (GltfMinFilter)Get( "minFilter", 0 ), (GltfWrap)Get( "wrapS", 10497 ), (GltfWrap)Get( "wrapT", 10497 ) );
	}

	internal JsonObject ToJson()
	{
		var json = new JsonObject();
		if ( MagFilter != GltfMagFilter.Default ) json["magFilter"] = (int)MagFilter;
		if ( MinFilter != GltfMinFilter.Default ) json["minFilter"] = (int)MinFilter;
		if ( WrapS != GltfWrap.Repeat ) json["wrapS"] = (int)WrapS;
		if ( WrapT != GltfWrap.Repeat ) json["wrapT"] = (int)WrapT;
		return json;
	}

	/// <summary>True if the sampler JSON has these settings. Names and extras do not count.</summary>
	internal bool Matches( JsonObject sampler ) => Read( sampler ) == this;
}
