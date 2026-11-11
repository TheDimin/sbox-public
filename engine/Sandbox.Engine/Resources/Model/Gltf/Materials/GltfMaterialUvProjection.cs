using Sandbox.Gltf;
using System.Collections.ObjectModel;

namespace Sandbox;

/// <summary>
/// Projects the UV sets consumed by a primitive's material into compact shader
/// input slots. This is only a routing plan: the backend must emit each slot
/// from the recorded upload descriptor and must bind each role's own transform.
/// </summary>
internal sealed class GltfMaterialUvProjection
{
	internal sealed record Source( int TexCoord, GltfUploadStream Stream );

	/// <summary>Source for each zero-based output slot, in slot order.</summary>
	internal IReadOnlyList<Source> Sources { get; }
	/// <summary>Output slot consumed by each present material texture role.</summary>
	internal IReadOnlyDictionary<GltfMaterialTextureRole, int> Slots { get; }

	private GltfMaterialUvProjection( Source[] sources, Dictionary<GltfMaterialTextureRole, int> slots )
	{
		Sources = Array.AsReadOnly( sources );
		Slots = new ReadOnlyDictionary<GltfMaterialTextureRole, int>( slots );
	}

	/// <param name="uv">Selection from the exact retained primitive upload.</param>
	/// <param name="shaderInputCount">Number of independently selectable UV inputs actually exposed by the selected shader.</param>
	internal static GltfMaterialUvProjection Create( GltfMaterialUvPlan uv, int shaderInputCount )
	{
		ArgumentNullException.ThrowIfNull( uv );
		ArgumentOutOfRangeException.ThrowIfNegative( shaderInputCount );
		// Stable numeric order permits sharing material state between primitives
		// with the same route; source-set numbers themselves need not be small.
		var sets = uv.Bindings.Values.Select( x => x.TexCoord ).Distinct().Order().ToArray();
		if ( sets.Length > shaderInputCount )
			throw new GltfException( $"Material requires {sets.Length} distinct UV sets, but the selected shader exposes {shaderInputCount} UV inputs." );
		var indices = sets.Select( ( set, index ) => (set, index) ).ToDictionary( x => x.set, x => x.index );
		var sources = new Source[sets.Length];
		var slots = new Dictionary<GltfMaterialTextureRole, int>();
		foreach ( var (role, binding) in uv.Bindings )
		{
			var slot = indices[binding.TexCoord];
			if ( sources[slot] is { } existing && !ReferenceEquals( existing.Stream, binding.Source ) )
				throw new GltfException( $"Material UV set TEXCOORD_{binding.TexCoord} does not refer to one retained source stream." );
			sources[slot] ??= new Source( binding.TexCoord, binding.Source );
			slots.Add( role, slot );
		}
		return new GltfMaterialUvProjection( sources, slots );
	}
}
