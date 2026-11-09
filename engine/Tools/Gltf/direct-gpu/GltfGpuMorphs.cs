using System;
using System.Collections.Generic;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>Authored target identity plus its source-derived upload descriptor.</summary>
public sealed record GltfGpuMorphBinding( int Node, int TargetIndex, string Semantic, GltfUploadStream Stream );

/// <summary>Pure morph binding/weight staging; GPU resource ownership belongs to the backend.</summary>
public static class GltfGpuMorphs
{
	public static IReadOnlyList<GltfGpuMorphBinding> CreateBindings( int node, GltfMorphTargetsUploadData upload )
	{
		ArgumentNullException.ThrowIfNull( upload );
		if ( node < 0 ) throw new ArgumentOutOfRangeException( nameof( node ) );
		var result = new List<GltfGpuMorphBinding>();
		for ( var target = 0; target < upload.Targets.Count; target++ )
			foreach ( var (semantic, stream) in upload.Targets[target] )
				result.Add( new GltfGpuMorphBinding( node, target, semantic, stream ) );
		return result.AsReadOnly();
	}

	/// <summary>
	/// Preserve authored signed/unbounded weights; never clamp, normalize, truncate
	/// or share mutable instance state. The same pose must also supply skin palettes.
	/// </summary>
	public static float[] ResolveWeights( GltfPose pose, int node, int targetCount )
	{
		ArgumentNullException.ThrowIfNull( pose );
		if ( targetCount < 0 ) throw new ArgumentOutOfRangeException( nameof( targetCount ) );
		var weights = pose.GetMorphWeights( node );
		if ( weights.Length != targetCount ) throw new GltfException( "Pose weights and GPU morph target counts must match." );
		foreach ( var weight in weights )
			if ( !float.IsFinite( weight ) ) throw new GltfException( "GPU morph weights must be finite." );
		return weights;
	}
}
