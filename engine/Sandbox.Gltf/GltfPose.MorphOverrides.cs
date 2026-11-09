namespace Sandbox.Gltf;

public sealed partial class GltfPose
{
	/// <summary>
	/// Create an independent pose snapshot with per-node morph weights replaced.
	/// Values are finite but may be signed and unbounded. Supplied arrays are
	/// copied, other weights and all world matrices remain immutable, and source
	/// document defaults are untouched. An empty map returns this snapshot.
	/// Like the existing pose getters, this uses only snapshot data and remains
	/// usable after the owning document is disposed; document ownership is retained.
	/// </summary>
	public GltfPose WithMorphWeights( IReadOnlyDictionary<int, float[]> overrides )
	{
		ArgumentNullException.ThrowIfNull( overrides );
		if ( overrides.Count == 0 ) return this;
		var weights = (float[][])poseWeights.Clone();
		foreach ( var (node, supplied) in overrides )
		{
			if ( (uint)node >= (uint)poseWeights.Length ) throw new GltfException( "Morph override pose node index is out of range." );
			if ( supplied is null ) throw new GltfException( "Morph override weights must be a finite array." );
			if ( supplied.Length != poseWeights[node].Length ) throw new GltfException( "Morph override weights must match the node's target count." );
			var copy = (float[])supplied.Clone();
			foreach ( var weight in copy )
				if ( !float.IsFinite( weight ) ) throw new GltfException( "Morph override weights must be finite." );
			weights[node] = copy;
		}
		return new GltfPose( Owner, poseWorlds, weights );
	}
}
