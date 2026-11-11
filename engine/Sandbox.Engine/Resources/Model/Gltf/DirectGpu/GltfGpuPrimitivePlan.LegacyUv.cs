using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu;

internal sealed partial class GltfGpuPrimitivePlan
{
	/// <summary>
	/// Prepare legacy stock and native-override UV routes without changing any
	/// retained source stream. Both routes use the same appended input arena and
	/// descriptors; only the stock route's TEXCOORD0 output references the bake.
	/// Returns null routes if no remapping is necessary.
	/// </summary>
	internal (GltfGpuPrimitivePlan Baked, GltfGpuPrimitivePlan Raw) CreateLegacyUvPlans()
		=> CreateLegacyUvPlans( Material );

	internal (GltfGpuPrimitivePlan Baked, GltfGpuPrimitivePlan Raw) CreateLegacyUvPlans( int? materialIndex )
	{
		RequireSourceOwnership( OwnerDocument, Mesh, Primitive, Upload );
		var bake = GltfLegacyUvBakePlan.Create( OwnerDocument, Mesh, Primitive, Upload, materialIndex, skipIdentity: true );
		if ( bake is null || bake.IsIdentityMapping ) return (null, null);
		RequireSourceStreamOwnership( "TEXCOORD_" + bake.SourceTexCoord, bake.Source );
		if ( InputBytes.Length % 4 != 0 ) throw new GltfException( "Source arena is not aligned for a baked UV stream." );
		var bytes = new byte[checked(InputBytes.Length + bake.Bytes.Length)];
		InputBytes.CopyTo( bytes, 0 ); bake.Bytes.Span.CopyTo( bytes.AsSpan( InputBytes.Length ) );
		var descriptors = new GltfGpuInputDescriptor[checked(InputDescriptors.Length + 1)];
		Array.Copy( InputDescriptors, 0, descriptors, 0, BaseAttributeCount );
		descriptors[BaseAttributeCount] = new GltfGpuInputDescriptor
		{
			ByteOffset = (uint)InputBytes.Length,
			ByteStride = 8,
			ComponentType = 5126,
			Components = 2,
			Normalized = 0,
			Role = 4,
			SemanticIndex = 0,
			Target = uint.MaxValue
		};
		Array.Copy( InputDescriptors, BaseAttributeCount, descriptors, BaseAttributeCount + 1, InputDescriptors.Length - BaseAttributeCount );
		var rawOutput = (GltfGpuOutputDescriptor[])OutputDescriptors.Clone();
		for ( var index = 0; index < rawOutput.Length; index++ )
			if ( rawOutput[index].Source != uint.MaxValue && rawOutput[index].Source >= BaseAttributeCount ) rawOutput[index].Source++;
		var bakedOutput = (GltfGpuOutputDescriptor[])rawOutput.Clone();
		var uv0 = -1;
		for ( var index = 0; index < Fields.Count; index++ )
			if ( Fields[index].Semantic == "TEXCOORD" && Fields[index].SemanticIndex == 0 )
			{
				if ( uv0 >= 0 ) throw new GltfException( "Duplicate TEXCOORD0 output fields." );
				uv0 = index;
			}
		if ( uv0 < 0 ) throw new GltfException( "The stock material requires a TEXCOORD0 output field." );
		bakedOutput[uv0].Source = (uint)BaseAttributeCount;
		return (Copy( bakedOutput ), Copy( rawOutput ));

		GltfGpuPrimitivePlan Copy( GltfGpuOutputDescriptor[] output ) => new()
		{
			OwnerDocument = OwnerDocument,
			Mesh = Mesh,
			Primitive = Primitive,
			Upload = Upload,
			InputBytes = bytes,
			Sources = Sources,
			Fields = Fields,
			SourceVertices = SourceVertices,
			Indices = Indices,
			VertexStride = VertexStride,
			GenerateFlatNormals = GenerateFlatNormals,
			Mode = Mode,
			SourceMode = SourceMode,
			Material = Material,
			InputDescriptors = descriptors,
			OutputDescriptors = output,
			BaseAttributeCount = checked(BaseAttributeCount + 1),
			MorphTargetCount = MorphTargetCount,
			MaterialUv = MaterialUv,
			UvProjection = UvProjection
		};
	}
}
