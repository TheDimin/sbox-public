namespace Sandbox.Gltf.DirectGpu;

internal sealed partial class GltfGpuAsset
{
	internal sealed partial class Primitive
	{
		internal GpuBuffer RawOutputDescriptors;
		internal NativeEngine.VertexLayout RawLayout;
		internal void DisposeRawRoute()
		{
			var descriptors = RawOutputDescriptors; RawOutputDescriptors = null;
			var layout = RawLayout; RawLayout = default; var valid = layout.IsValid;
			GltfGpuCleanup.ReleaseAll( new Action[] { () => descriptors?.Dispose(), () => { if ( valid ) layout.Free(); }, () => { if ( valid ) layout.Destroy(); } } );
		}
	}
	private GltfVariantDrawAsset<GltfProjectedMaterialPacket> variantPackets;
	internal bool HasProjectedVariants => variantPackets is not null;
	private Dictionary<Primitive, GltfGpuPrimitivePlan> variantRawPlans;
	internal bool HasRawVariantRoute( Primitive primitive ) => variantRawPlans?.ContainsKey( primitive ) == true;
	internal bool HasRawVariantRoutes => variantRawPlans is { Count: > 0 };
	internal GltfGpuPrimitivePlan GetRawVariantPlan( Primitive primitive ) =>
		variantRawPlans is not null && variantRawPlans.TryGetValue( primitive, out var plan ) ? plan : primitive.Plan;
	internal GltfVariantDrawState<GltfProjectedMaterialPacket> CreateVariantState() => variantPackets?.CreateState();

	/// <summary>Explicit projected path; no compiled template is assumed by the public loader.</summary>
	internal static GltfGpuAsset PrepareProjected( GltfDocument document, string computeShaderPath, Material template,
		long encodedImageBudget, Action finalRelease = null )
	{
		// Bypass legacy source realization; projected packets prepare only their
		// selected materials through the retained image generation.
		var asset = Prepare( document, computeShaderPath, _ => null );
		try { asset.ConfigureStockProjectedVariants( template, encodedImageBudget ); asset.materialResolver = null; asset.finalRelease = finalRelease; return asset; }
		catch ( Exception error ) { GltfGpuCleanup.Rollback( error, [asset.Dispose] ); throw; }
	}

	internal static GltfGpuAsset PrepareProjected( GltfDocument document, string computeShaderPath, long encodedImageBudget,
		Func<GltfMaterial, GltfGpuPrimitivePlan, Material> selectTemplate, GltfUnlitMaterialPlan.Features unlitCapabilities,
		Action finalRelease = null, int? sceneIndex = null, bool allowUnselected = false, bool legacyLit = false, bool sourceLitFeatureSidedness = false )
	{
		var asset = Prepare( document, computeShaderPath, _ => null, sceneIndex: sceneIndex, allowUnselected: allowUnselected );
		try { asset.ConfigureProjectedSourceMaterials( encodedImageBudget, selectTemplate, unlitCapabilities, legacyLit, sourceLitFeatureSidedness ); asset.materialResolver = null; asset.finalRelease = finalRelease; return asset; }
		catch ( Exception error ) { GltfGpuCleanup.Rollback( error, [asset.Dispose] ); throw; }
	}

	private IReadOnlyList<Material> GetProjectedMaterials()
	{
		using var state = variantPackets.CreateState();
		var snapshot = state.Current; var result = new Material[MaterialSlotSourceIndices.Count];
		var assignments = new Dictionary<Primitive, Material>();
		for ( var i = 0; i < Nodes.Count; i++ )
		{
			var primitive = Nodes[i].Primitive; var material = snapshot.Bindings[i].Value.Material;
			result[primitive.MaterialSlot] ??= material;
			assignments[primitive] = material;
		}
		if ( result.Any( x => x is null ) ) throw new GltfException( "A projected material slot has no selected primitive." );
		foreach ( var (primitive, material) in assignments ) primitive.Material = material;
		return materials = Array.AsReadOnly( result );
	}

	internal bool EnsureRawVariantRoute( Primitive primitive )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !HasRawVariantRoute( primitive ) || primitive.RawOutputDescriptors is not null ) return false;
		if ( Graphics.IsActive ) throw new GltfException( "Raw material override routes cannot be created during rendering." );
		var plan = GetRawVariantPlan( primitive ); GpuBuffer descriptors = null; NativeEngine.VertexLayout layout = default;
		try
		{
			descriptors = Upload( plan.OutputDescriptors, "glTF raw override descriptors" );
			layout = NativeEngine.VertexLayout.Create( "gltf_raw_" + Guid.NewGuid().ToString( "N" ), plan.VertexStride );
			foreach ( var field in plan.Fields ) layout.Add( field.Semantic.ToLowerInvariant(), field.SemanticIndex, (uint)Format( field.Components ), field.Offset );
			layout.Build(); primitive.RawOutputDescriptors = descriptors; primitive.RawLayout = layout; return true;
		}
		catch ( Exception error )
		{
			var valid = layout.IsValid;
			GltfGpuCleanup.Rollback( error, [() => descriptors?.Dispose(), () => { if ( valid ) layout.Free(); }, () => { if ( valid ) layout.Destroy(); }] ); throw;
		}
	}

	/// <summary>
	/// Candidate source-material bridge. The template is borrowed and must be a
	/// validated five-slot shader for every selected material program. Unsupported
	/// unlit/alpha or other features still fail in the adapter rather than falling
	/// back to a different visual result.
	/// </summary>
	internal void ConfigureStockProjectedVariants( Material template, long encodedImageBudget )
	{
		ArgumentNullException.ThrowIfNull( template );
		ConfigureProjectedVariants( encodedImageBudget, (index, plan, images) =>
		{
			var material = index is int source ? Document.ReadMaterial( source ) : null;
			var prepared = GltfStockMaterialAdapter.PrepareSource( Document, material, images.ReadImage );
			return GltfStockMaterialAdapter.AcquireSource( prepared, template, plan.MaterialUv, plan.UvProjection );
		} );
	}

	/// <summary>
	/// Candidate source path with separate per-program/per-alpha templates.
	/// Capabilities must come from the actual validated unlit renderer, not from
	/// successful parameter reflection or the existence of a template.
	/// </summary>
	internal void ConfigureProjectedSourceMaterials( long encodedImageBudget,
		Func<GltfMaterial, GltfGpuPrimitivePlan, Material> selectTemplate, GltfUnlitMaterialPlan.Features unlitCapabilities, bool legacyLit = false, bool sourceLitFeatureSidedness = false )
	{
		ArgumentNullException.ThrowIfNull( selectTemplate );
		var batchMaterials = new Dictionary<int, GltfStockMaterialAdapter.SourcePrepared>();
		ConfigureProjectedVariants( encodedImageBudget, (index, plan, images) =>
		{
			var material = index is int source ? Document.ReadMaterial( source ) : null;
			if ( legacyLit && material?.Unlit != true && !GltfPublicMaterialRoute.NeedsSourceLit( material ) )
			{
				var stock = Material.FromShader( "shaders/gltf_stock_sampler.shader" );
				if ( stock is not { IsValid: true, IsError: false } ) throw new GltfException( "The glTF stock material sampler template is unavailable." );
				return GltfStockMaterialAdapter.Acquire( GltfStockMaterialAdapter.Prepare( Document, material, images.ReadImage ), stock, linearVertexColor: true );
			}
			var template = selectTemplate( material, plan ) ?? throw new GltfException( "No source material template is available for the selected program." );
			if ( material?.Unlit == true )
			{
				var unlit = GltfUnlitMaterialPlan.Create( material, index );
				unlit.Bind( Document, plan.Upload ).RequireRealizable( unlitCapabilities );
				var prepared = GltfUnlitSourceMaterialAdapter.Prepare( Document, material, index, images.ReadImage );
				return GltfUnlitSourceMaterialAdapter.Acquire( prepared, template, plan.MaterialUv, plan.UvProjection );
			}
			var lit = batchMaterials.TryGetValue( index ?? -1, out var cached ) ? cached : GltfStockMaterialAdapter.PrepareSource( Document, material, images.ReadImage );
			return GltfStockMaterialAdapter.AcquireSource( lit, template, plan.MaterialUv, plan.UvProjection, sourceLitFeatureSidedness );
		}, legacyLit ? index => index is not int source || (Document.ReadMaterial( source ) is var selected && !selected.Unlit && !GltfPublicMaterialRoute.NeedsSourceLit( selected )) : null,
		(pending, images) =>
		{
			if ( batchMaterials.Count != 0 ) throw new InvalidOperationException( "A material preparation batch is already active." );
			try
			{
				foreach ( var (_, index) in pending )
				{
					var key = index ?? -1;
					if ( batchMaterials.ContainsKey( key ) ) continue;
					var material = index is int source ? Document.ReadMaterial( source ) : null;
					if ( material?.Unlit == true || legacyLit && !GltfPublicMaterialRoute.NeedsSourceLit( material ) ) continue;
					batchMaterials.Add( key, GltfStockMaterialAdapter.PrepareSource( Document, material, images.ReadImage ) );
				}
				var leases = GltfNativeTextureCache.AcquireBatch( batchMaterials.Values.SelectMany( x => x.Resources.Values ).Select( x => x.Resource ).ToArray() );
				return new GltfMaterialLease<object>( new object(), () =>
				{
					batchMaterials.Clear(); GltfGpuCleanup.ReleaseAll( leases.Select<GltfMaterialLease<Texture>, Action>( x => x.Dispose ) );
				}, () => ThreadSafe.AssertIsMainThread() );
			}
			catch { batchMaterials.Clear(); throw; }
		} );
	}

	/// <summary>
	/// Explicit candidate setup before any GPU realization. The caller supplies
	/// the actual validated template/material acquisition; this method does not
	/// claim that a shader exists or activate the public loader capability.
	/// </summary>
	internal void ConfigureProjectedVariants( long encodedImageBudget,
		Func<int?, GltfGpuPrimitivePlan, GltfMaterialImageSnapshot, GltfMaterialLease<Material>> acquireMaterial,
		Func<int?, bool> useLegacy = null,
		Func<IReadOnlyList<(GltfMaterialVariantSlot Slot, int? Material)>, GltfMaterialImageSnapshot, IDisposable> prepareBatch = null )
	{
		ObjectDisposedException.ThrowIf( ownerReleased || disposed, this ); ThreadSafe.AssertIsMainThread();
		ArgumentNullException.ThrowIfNull( acquireMaterial );
		if ( gpuRealized || materials is not null || instances != 0 || variantPackets is not null )
			throw new InvalidOperationException( "Configure projected variants before realizing this asset or its materials." );
		var names = Document.ReadMaterialVariantNames();
		var requested = new HashSet<int?>(); var reachable = new Dictionary<Primitive, HashSet<int?>>();
		foreach ( var primitive in Primitives )
		{
			var choices = new HashSet<int?> { Document.GetMaterialForVariant( primitive.Mesh, primitive.Index, null ) };
			for ( var variant = 0; variant < names.Count; variant++ )
				choices.Add( Document.GetMaterialForVariant( primitive.Mesh, primitive.Index, variant ) );
			reachable.Add( primitive, choices ); requested.UnionWith( choices );
		}
		// Snapshot source data before publishing any new route. An unsupported
		// alternate's shader realization is deferred until it is selected.
		var images = Document.SnapshotMaterialImages( requested, encodedImageBudget );
		var rawPlans = new Dictionary<Primitive, GltfGpuPrimitivePlan>();
		foreach ( var primitive in Primitives )
		{
			var raw = GetRawVariantPlan( primitive );
			var hasAuthoredFrame = raw.Upload.Attributes.ContainsKey( "TANGENT" ) && raw.Upload.Attributes.ContainsKey( "NORMAL" );
			if ( raw.Mode == 4 && !hasAuthoredFrame && reachable[primitive].Any( Document.MaterialMayUseNormalTexture ) )
			{
				var topology = GltfGpuGeneratedTangentRoute.ExpandTopology( raw );
				raw = raw.WithCornerTopology( topology.SourceVertices, topology.Indices );
			}
			rawPlans.Add( primitive, raw );
		}
		IEnumerable<GltfMaterialTextureRole> Roles( int? materialIndex ) => useLegacy?.Invoke( materialIndex ) == true
			? [GltfMaterialTextureRole.BaseColor, GltfMaterialTextureRole.MetallicRoughness]
			: materialIndex is int index && Document.ReadMaterial( index ).Unlit ? [GltfMaterialTextureRole.BaseColor] : null;
		// Every reachable variant uses one compatible vertex declaration. Core
		// templates ignore the two additional fields when a diffuse-transmission
		// variant needs all seven independently selected source UV sets.
		var uvCount = requested.Any( index => index is int source && Document.ReadMaterial( source ).DiffuseTransmission is not null ) ? 7 : 5;
		int[] uvSemantics = uvCount == 7 ? [0, 1, 4, 5, 6, 7, 8] : [0, 1, 4, 5, 6];
		var projections = Primitives.ToDictionary( x => x, x => rawPlans[x].ProjectMaterial( rawPlans[x].Material, uvCount, Roles( rawPlans[x].Material ), uvSemantics ) );
		var slots = Nodes.Select( x => new GltfMaterialVariantSlot( x.Index, x.Primitive.Mesh, x.Primitive.Index ) ).ToArray();
		var byPrimitive = Primitives.ToDictionary( x => (x.Mesh, x.Index) );
		var packets = new GltfVariantDrawAsset<GltfProjectedMaterialPacket>( Document, slots, (slot, materialIndex) =>
		{
			var primitive = byPrimitive[(slot.Mesh, slot.Primitive)];
			var projected = rawPlans[primitive].ProjectMaterial( materialIndex, uvCount, Roles( materialIndex ), uvSemantics );
			projected.RequireSourceOwnership( Document, slot.Mesh, slot.Primitive, rawPlans[primitive].Upload );
			if ( !projected.Fields.SequenceEqual( projections[primitive].Fields ) || projected.VertexStride != projections[primitive].VertexStride )
				throw new GltfException( "Variant output layout differs from its primitive's projected declaration." );
			var hasSkin = Nodes.Any( x => ReferenceEquals( x.Primitive, primitive ) && x.Skin.HasValue );
			var ownInput = false;
			if ( useLegacy?.Invoke( materialIndex ) == true )
			{
				var (baked, _) = projected.CreateLegacyUvPlans( materialIndex );
				if ( baked is not null ) { projected = baked; ownInput = true; }
			}
			var tangent = projected.Mode is 0 or 1 && !projected.Upload.Attributes.ContainsKey( "NORMAL" ) ? null : GltfGpuGeneratedTangentRoute.Prepare( projected, hasSkin );
			var lease = acquireMaterial( materialIndex, projected, images );
			var packet = GltfProjectedMaterialPacket.Acquire( projected, lease, tangent, ownInput );
			return new GltfMaterialLease<GltfProjectedMaterialPacket>( packet, packet.Dispose, () => ThreadSafe.AssertIsMainThread() );
		}, packet => packet.Material is { IsValid: true, IsError: false }, () => ThreadSafe.AssertIsMainThread(),
		prepareBatch is null ? null : pending => prepareBatch( pending, images ) );
		foreach ( var (primitive, plan) in projections ) primitive.Plan = plan;
		variantRawPlans = rawPlans; variantPackets = packets;
	}
}
