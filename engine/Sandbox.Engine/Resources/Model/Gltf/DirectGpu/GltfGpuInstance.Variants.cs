using Sandbox.Engine;

namespace Sandbox.Gltf.DirectGpu;

internal sealed partial class GltfGpuInstance
{
	private sealed partial class Draw
	{
		internal int VariantRow;
		internal GpuBuffer RawOutput;
		internal bool RawHasVertexState;
		internal bool LastRawRoute;
		internal GltfProjectedMaterialPacket ComputedPacket;
		internal long RawComputedRevision = -1;
		internal void DisposeRawOutput() { var output = RawOutput; RawOutput = null; output?.Dispose(); }
	}

	private GltfVariantDrawState<GltfProjectedMaterialPacket> variantState;
	public int? ActiveVariant => variantState?.Current.ActiveVariant;

	public void SetVariant( int? variant )
	{
		ObjectDisposedException.ThrowIf( disposed, this ); ThreadSafe.AssertIsMainThread();
		submissionGate.Run( () => SetVariantExclusive( variant ) );
	}

	private void SetVariantExclusive( int? variant )
	{
		using var scope = new GlobalContext.GlobalContextScope( ownerContext );
		if ( variantState is null )
		{
			if ( variant is not null ) throw new NotSupportedException( "Projected glTF material variants are not configured for this asset." );
			return;
		}
		var before = variantState.Current; variantState.SetVariant( variant, next => PreflightClassification( next, materialOverrides ) );
		if ( ReferenceEquals( before, variantState.Current ) ) return;
		try
		{
			foreach ( var draw in draws ) { draw.ComputedRevision = -1; draw.SubmittedRevision = -1; }
			SubmittedRevision = -1; RefreshMaterialClassification();
		}
		catch ( Exception error )
		{
			variantState.Publish( before );
			GltfGpuCleanup.Rollback( error, [RefreshMaterialClassification] ); throw;
		}
	}

	private void PreflightClassification( GltfVariantDrawSnapshot<GltfProjectedMaterialPacket> selected, Material[] overrides )
	{
		foreach ( var draw in draws )
		{
			var primitive = draw.Node.Primitive;
			var material = overrides[primitive.MaterialSlot] ?? selected?.Bindings[draw.VariantRow].Value.Material ?? primitive.Material;
			if ( material is null || !material.IsValid || material.IsError ) throw new GltfException( "The selected material is invalid." );
			_ = material.Flags.IsTranslucent; _ = material.Flags.IsAlphaTest;
		}
	}

	private void EnsureRawOverrideOutputs( int? slot = null )
	{
		if ( !asset.HasRawVariantRoutes ) return;
		if ( Graphics.IsActive ) throw new GltfException( "Material override routes cannot be created during rendering." );
		var staged = new List<(Draw Draw, GpuBuffer Output)>( draws.Count );
		var routes = new List<GltfGpuAsset.Primitive>( draws.Count );
		try
		{
			foreach ( var draw in draws )
			{
				var primitive = draw.Node.Primitive;
				if ( slot.HasValue && primitive.MaterialSlot != slot.Value ) continue;
				if ( !asset.HasRawVariantRoute( primitive ) ) continue;
				if ( draw.RawOutput is not null ) continue;
				if ( asset.EnsureRawVariantRoute( primitive ) ) routes.Add( primitive );
				var plan = asset.GetRawVariantPlan( primitive );
				var output = new GpuBuffer( plan.SourceVertices.Length, plan.VertexStride,
					GpuBuffer.UsageFlags.ByteAddress | GpuBuffer.UsageFlags.Vertex, "glTF raw override output" );
				staged.Add( (draw, output) );
			}
			foreach ( var (draw, output) in staged ) draw.RawOutput = output;
		}
		catch ( Exception error )
		{
			GltfGpuCleanup.Rollback( error, staged.Select<(Draw Draw, GpuBuffer Output), Action>( x => x.Output.Dispose )
				.Concat( routes.Select<GltfGpuAsset.Primitive, Action>( x => x.DisposeRawRoute ) ) ); throw;
		}
	}
}
