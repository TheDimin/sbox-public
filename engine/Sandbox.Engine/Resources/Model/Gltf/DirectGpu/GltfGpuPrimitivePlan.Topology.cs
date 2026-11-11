namespace Sandbox.Gltf.DirectGpu;

internal sealed partial class GltfGpuPrimitivePlan
{
	/// <summary>
	/// Reuses immutable source inputs and material route while selecting a common
	/// corner topology. All competing material packets for this primitive must
	/// use the same map and indices, including the raw native override route.
	/// </summary>
	internal GltfGpuPrimitivePlan WithCornerTopology( uint[] sourceVertices, uint[] indices )
	{
		ArgumentNullException.ThrowIfNull( sourceVertices ); ArgumentNullException.ThrowIfNull( indices );
		RequireSourceOwnership( OwnerDocument, Mesh, Primitive, Upload );
		if ( Mode != 4 || sourceVertices.Length != indices.Length || indices.Length % 3 != 0 )
			throw new GltfException( "Expanded corner topology requires matching triangle indices and source vertices." );
		for ( var i = 0; i < indices.Length; i++ )
			if ( indices[i] != (uint)i || sourceVertices[i] >= Upload.Layout.VertexCount )
				throw new GltfException( "Expanded corner topology contains invalid identity indices or source vertices." );
		return new GltfGpuPrimitivePlan
		{
			OwnerDocument = OwnerDocument,
			Mesh = Mesh,
			Primitive = Primitive,
			Upload = Upload,
			InputBytes = InputBytes,
			Sources = Sources,
			Fields = Fields,
			SourceVertices = (uint[])sourceVertices.Clone(),
			Indices = (uint[])indices.Clone(),
			VertexStride = VertexStride,
			GenerateFlatNormals = GenerateFlatNormals,
			Mode = Mode,
			SourceMode = SourceMode,
			Material = Material,
			InputDescriptors = InputDescriptors,
			OutputDescriptors = OutputDescriptors,
			BaseAttributeCount = BaseAttributeCount,
			MorphTargetCount = MorphTargetCount,
			MaterialUv = MaterialUv,
			UvProjection = UvProjection
		};
	}

	/// <summary>
	/// The same decoded primitive owned by a newer generation of its document. Only valid when the primitive, its
	/// accessors and their buffer views have the same JSON in both generations and the binary data is the same; the
	/// native compile cache keys decoded primitives by exactly that. Decoding is the expensive step, and an edit to a
	/// material or a node must not repeat it.
	/// </summary>
	internal GltfGpuPrimitivePlan WithOwner( GltfDocument document )
	{
		ArgumentNullException.ThrowIfNull( document );
		if ( ReferenceEquals( document, OwnerDocument ) ) return this;
		return new GltfGpuPrimitivePlan
		{
			OwnerDocument = document,
			Mesh = Mesh,
			Primitive = Primitive,
			Upload = Upload,
			InputBytes = InputBytes,
			Sources = Sources,
			Fields = Fields,
			SourceVertices = SourceVertices,
			Indices = Indices,
			VertexStride = VertexStride,
			GenerateFlatNormals = GenerateFlatNormals,
			Mode = Mode,
			SourceMode = SourceMode,
			Material = Material,
			InputDescriptors = InputDescriptors,
			OutputDescriptors = OutputDescriptors,
			BaseAttributeCount = BaseAttributeCount,
			MorphTargetCount = MorphTargetCount,
			MaterialUv = MaterialUv,
			UvProjection = UvProjection
		};
	}
}
