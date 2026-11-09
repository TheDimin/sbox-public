using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>
/// One immutable primitive/material route: its material and output descriptors
/// must be selected as a unit. The asset owns the lease; instances own output
/// vertex buffers. This type does not imply shader compilation or validation.
/// </summary>
internal sealed class GltfProjectedMaterialPacket : IDisposable
{
	internal GltfGpuPrimitivePlan Plan { get; }
	internal Material Material => material.Value;
	internal GpuBuffer OutputDescriptors { get; }
	internal GpuBuffer InputBytes { get; }
	internal GpuBuffer InputDescriptors { get; }
	internal int BaseAttributeCount { get; }
	internal int InputCount { get; }
	private readonly GltfMaterialLease<Material> material;
	private bool disposed;

	private GltfProjectedMaterialPacket( GltfGpuPrimitivePlan plan, GltfMaterialLease<Material> material, GpuBuffer outputDescriptors,
		GpuBuffer inputBytes, GpuBuffer inputDescriptors, int baseAttributeCount, int inputCount )
	{
		Plan = plan; this.material = material; OutputDescriptors = outputDescriptors; InputBytes = inputBytes; InputDescriptors = inputDescriptors;
		BaseAttributeCount = baseAttributeCount; InputCount = inputCount;
	}

	/// <summary>
	/// Takes ownership after non-null arguments and the main-thread check succeed;
	/// the caller retains the lease when those preconditions fail. Later failures
	/// release it. The caller must check the plan against the requested asset,
	/// primitive and upload; the local check only verifies its own live source.
	/// </summary>
	internal static GltfProjectedMaterialPacket Acquire( GltfGpuPrimitivePlan plan, GltfMaterialLease<Material> material, GltfGpuGeneratedTangentRoute tangent = null, bool ownInput = false )
	{
		ArgumentNullException.ThrowIfNull( plan ); ArgumentNullException.ThrowIfNull( material );
		ThreadSafe.AssertIsMainThread(); GpuBuffer descriptors = null, inputBytes = null, inputDescriptors = null;
		try
		{
			if ( Graphics.IsActive ) throw new GltfException( "Projected material packets cannot be acquired during rendering." );
			plan.RequireSourceOwnership( plan.OwnerDocument, plan.Mesh, plan.Primitive, plan.Upload );
			if ( plan.MaterialUv is null || plan.UvProjection is null || plan.Fields.Count != plan.OutputDescriptors.Length )
				throw new GltfException( "A projected material packet requires a complete matching UV/output plan." );
			if ( material.Value is null || !material.Value.IsValid || material.Value.IsError )
				throw new GltfException( "Projected material packet has an invalid material." );
			if ( tangent is not null )
			{
				if ( !tangent.SourceVertices.SequenceEqual( plan.SourceVertices ) || !tangent.Indices.SequenceEqual( plan.Indices ) )
					throw new GltfException( "Generated tangents do not use the primitive's shared corner topology." );
				inputBytes = GltfGpuAsset.UploadBytes( tangent.InputBytes, GpuBuffer.UsageFlags.ByteAddress, "glTF tangent source arena" );
				inputDescriptors = GltfGpuAsset.Upload( tangent.InputDescriptors, "glTF tangent input descriptors" );
			}
			else if ( ownInput )
			{
				inputBytes = GltfGpuAsset.UploadBytes( plan.InputBytes, GpuBuffer.UsageFlags.ByteAddress, "glTF material source arena" );
				inputDescriptors = GltfGpuAsset.Upload( plan.InputDescriptors, "glTF material input descriptors" );
			}
			descriptors = GltfGpuAsset.Upload( tangent?.OutputDescriptors ?? plan.OutputDescriptors, "glTF projected output descriptors" );
			return new GltfProjectedMaterialPacket( plan, material, descriptors, inputBytes, inputDescriptors,
				tangent?.BaseAttributeCount ?? plan.BaseAttributeCount, tangent?.InputDescriptors.Length ?? plan.InputDescriptors.Length );
		}
		catch ( Exception error )
		{
			GltfGpuCleanup.Rollback( error, new Action[] { () => descriptors?.Dispose(), () => inputDescriptors?.Dispose(), () => inputBytes?.Dispose(), material.Dispose } ); throw;
		}
	}

	public void Dispose()
	{
		if ( disposed ) return;
		ThreadSafe.AssertIsMainThread(); disposed = true;
		GltfGpuCleanup.ReleaseAll( [OutputDescriptors.Dispose, () => InputDescriptors?.Dispose(), () => InputBytes?.Dispose(), material.Dispose] );
	}
}
