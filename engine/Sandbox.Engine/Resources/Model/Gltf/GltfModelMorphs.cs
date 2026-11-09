using Sandbox.Gltf;
using NVector = System.Numerics.Vector3;

namespace Sandbox;

/// <summary>
/// Native position/normal target staging for one glTF node primitive. A signed
/// glTF weight uses independent positive and negative native controllers because
/// the stock native morph path does not render negative controller weights.
/// Deltas stay in mesh-local space until morphing is complete; node/skin normal
/// transforms must not normalize a base separately from its linear deltas.
/// </summary>
internal sealed class GltfModelMorphs
{
	internal sealed record Target( int Node, int Index, string PositiveName, string NegativeName, ReadOnlyMemory<MorphDelta> Positive, ReadOnlyMemory<MorphDelta> Negative, BBox DeltaBounds );
	internal IReadOnlyList<Target> Targets { get; }
	internal bool HasDeltas { get; }
	private readonly BBox baseBounds;
	private GltfModelMorphs( List<Target> targets, BBox baseBounds )
	{
		Targets = targets.AsReadOnly();
		this.baseBounds = baseBounds;
		foreach ( var target in targets )
			foreach ( var delta in target.Positive.Span )
				if ( delta.PositionDelta != Vector3.Zero || delta.NormalDelta != Vector3.Zero ) HasDeltas = true;
	}

	/// <summary>Stage every position and normal target using the explicit native-to-source vertex mapping.</summary>
	/// <param name="document">Source glTF document.</param>
	/// <param name="node">Node instance that owns the target weights.</param>
	/// <param name="mesh">Source mesh index.</param>
	/// <param name="primitive">Source primitive index.</param>
	/// <param name="sourceVertices">Native vertex to original accessor element, including every duplicated triangle corner.</param>
	internal static GltfModelMorphs Prepare( GltfDocument document, int node, int mesh, int primitive, ReadOnlySpan<int> sourceVertices )
	{
		ArgumentNullException.ThrowIfNull( document );
		var data = document.CreateDrawData( mesh, primitive );
		var targets = document.ReadMorphTargets( mesh, primitive );
		var output = new List<Target>();
		if ( sourceVertices.IsEmpty ) throw new GltfException( "Morph upload requires a vertex-to-accessor mapping." );
		foreach ( var source in sourceVertices ) if ( (uint)source >= (uint)data.Positions.Length ) throw new GltfException( "Morph vertex mapping is outside its source accessor." );
		for ( var target = 0; target < targets.Length; target++ )
		{
			var source = targets[target];
			if ( source.TangentDeltas?.Any( delta => delta != 0 ) == true ) throw new GltfException( "glTF tangent morph deltas require an extended deformation path; native MorphDelta exposes only position and normal deltas." );
			if ( data.Normals.IsEmpty && source.PositionDeltas?.Any( delta => delta != 0 ) == true ) throw new GltfException( "Morphing geometry without authored normals requires per-pose flat-normal generation, which the native linear target adapter cannot provide." );
			var positive = new MorphDelta[sourceVertices.Length];
			var negative = new MorphDelta[sourceVertices.Length];
			for ( var vertex = 0; vertex < sourceVertices.Length; vertex++ )
			{
				var offset = sourceVertices[vertex] * 3;
				var position = GltfGeometry.ConvertPosition( Read( source.PositionDeltas, offset ) );
				var normal = GltfGeometry.ConvertDirection( Read( source.NormalDeltas, offset ) );
				if ( !Finite( position ) || !Finite( normal ) ) throw new GltfException( "Converted morph deltas exceed the supported finite coordinate range." );
				positive[vertex] = new( vertex, position, normal );
				negative[vertex] = new( vertex, -position, -normal );
			}
			output.Add( new( node, target, $"gltf_node_{node}_morph_{target}_positive", $"gltf_node_{node}_morph_{target}_negative", positive, negative, BBox.FromPoints( positive.Select( delta => delta.PositionDelta ) ) ) );
		}
		var mappedPositions = new Vector3[sourceVertices.Length];
		for ( var i = 0; i < mappedPositions.Length; i++ ) mappedPositions[i] = data.Positions.Span[sourceVertices[i]];
		return new( output, BBox.FromPoints( mappedPositions ) );
	}

	internal BBox EvaluateBounds( GltfPose pose )
	{
		var min = baseBounds.Mins; var max = baseBounds.Maxs;
		foreach ( var target in Targets )
		{
			var weight = pose.GetMorphWeights( target.Node )[target.Index];
			_ = SplitWeight( weight );
			min += (weight < 0 ? target.DeltaBounds.Maxs : target.DeltaBounds.Mins) * weight;
			max += (weight < 0 ? target.DeltaBounds.Mins : target.DeltaBounds.Maxs) * weight;
		}
		if ( !Finite( min ) || !Finite( max ) ) throw new GltfException( "Morphed bounds exceed the supported finite coordinate range." );
		return new( min, max );
	}

	internal void AddTo( Mesh mesh )
	{
		ArgumentNullException.ThrowIfNull( mesh );
		foreach ( var target in Targets )
		{
			mesh.AddMorph( target.PositiveName, target.Positive.Span );
			mesh.AddMorph( target.NegativeName, target.Negative.Span );
		}
	}

	/// <summary>Call before native flex evaluation, using the same exact pose later applied to the skin palette.</summary>
	internal void ApplyWeights( SceneModel model, GltfPose pose )
	{
		// Preflight all source values so a malformed pose cannot partly replace the
		// last valid pair of controllers. Never clamp away a signed source weight.
		foreach ( var target in Targets ) _ = SplitWeight( pose.GetMorphWeights( target.Node )[target.Index] );
		foreach ( var target in Targets )
		{
			var (positive, negative) = SplitWeight( pose.GetMorphWeights( target.Node )[target.Index] );
			model.Morphs.Set( target.PositiveName, positive, 0 );
			model.Morphs.Set( target.NegativeName, negative, 0 );
		}
	}

	internal static (float Positive, float Negative) SplitWeight( float weight )
	{
		if ( !float.IsFinite( weight ) ) throw new GltfException( "Morph weights must be finite." );
		return (MathF.Max( weight, 0 ), MathF.Max( -weight, 0 ));
	}

	private static bool Finite( NVector value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );

	private static NVector Read( float[] values, int offset )
	{
		if ( values is null ) return NVector.Zero;
		if ( offset < 0 || offset + 3 > values.Length ) throw new GltfException( "Morph attribute exceeds its decoded accessor." );
		var value = new NVector( values[offset], values[offset + 1], values[offset + 2] );
		if ( !float.IsFinite( value.X ) || !float.IsFinite( value.Y ) || !float.IsFinite( value.Z ) ) throw new GltfException( "Morph deltas must be finite." );
		return value;
	}
}
