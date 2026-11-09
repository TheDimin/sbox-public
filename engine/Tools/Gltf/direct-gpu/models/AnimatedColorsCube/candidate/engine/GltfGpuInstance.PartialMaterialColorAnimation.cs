using Sandbox.Engine;

namespace Sandbox.Gltf.DirectGpu;

internal sealed partial class GltfGpuInstance
{
	private bool hasAnimatedMaterials;
	private Dictionary<int, bool> opaqueMaterials;
	private Dictionary<Material, string> materialColorParameters;
	internal int AnimatedMaterialCopyCount { get; private set; }
	internal int AnimatedMaterialRetiredPending { get; private set; }
	private sealed record AnimatedMaterialEntry( Material Parent, Material Material, string Parameter, global::Vector4 Tint, bool New );
	private sealed class AnimatedMaterialStage
	{
		internal readonly AnimatedMaterialEntry[] Entries;
		internal readonly global::Vector4[] Previous;
		internal readonly List<int> Changed = new();
		internal readonly List<Material> Retired = new();
		internal bool RetireLease;
		internal AnimatedMaterialStage( AnimatedMaterialEntry[] entries ) { Entries = entries; Previous = new global::Vector4[entries.Length]; }
	}
	// Test-only failure injection is instance scoped; restoration bypasses it.
	internal Action<Material, string, global::Vector4> AnimatedMaterialWriter;

	// Called only on the main thread under the instance submission gate. Reuse
	// an instance-owned copy across pose changes; create a replacement only for
	// a newly animated effective source binding. Asset/variant leases retain all
	// parent material and texture resources through the instance lifetime.
	private AnimatedMaterialStage PrepareAnimatedMaterials( GltfPose nextPose, GltfVariantDrawSnapshot<GltfProjectedMaterialPacket> selected )
	{
		var entries = new AnimatedMaterialEntry[draws.Count];
		try
		{
			for ( var i = 0; i < draws.Count; i++ )
			{
				var draw = draws[i]; var primitive = draw.Node.Primitive;
				var binding = selected?.Bindings[draw.VariantRow];
				var index = binding is null ? primitive.Plan.Material : binding.MaterialIndex;
				if ( !index.HasValue || !nextPose.TryGetAnimatedBaseColorFactor( index.Value, out var color ) ) continue;
				// Alpha is ignored for OPAQUE glTF; other modes retain fallback
				// until their material factor and classification path is proven.
				opaqueMaterials ??= new();
				if ( !opaqueMaterials.TryGetValue( index.Value, out var opaque ) )
					opaqueMaterials.Add( index.Value, opaque = asset.Document.ReadMaterial( index.Value ).AlphaMode == "OPAQUE" );
				if ( !opaque ) continue;
				var parent = binding?.Value.Material ?? primitive.Material;
				materialColorParameters ??= new();
				if ( !materialColorParameters.TryGetValue( parent, out var parameter ) )
				{
					parameter = parent.HasParameter( "g_vGltfBaseColor" ) ? "g_vGltfBaseColor" : parent.HasParameter( "g_vColorTint" ) ? "g_vColorTint" : "";
					materialColorParameters.Add( parent, parameter );
				}
				if ( parameter.Length == 0 ) continue;
				var projected = parameter == "g_vGltfBaseColor";
				var tint = projected ? new global::Vector4( color.X, color.Y, color.Z, 1 ) :
					new global::Vector4( GltfStockMaterialAdapter.EncodeTint( color.X ), GltfStockMaterialAdapter.EncodeTint( color.Y ), GltfStockMaterialAdapter.EncodeTint( color.Z ), 1 );
				var reuse = ReferenceEquals( draw.AnimatedParent, parent ) && draw.AnimatedMaterial is { IsValid: true, IsError: false };
				var copy = reuse ? draw.AnimatedMaterial : parent.CreateCopy();
				if ( !reuse ) AnimatedMaterialCopyCount++;
				entries[i] = new( parent, copy, parameter, tint, !reuse );
				if ( copy is not { IsValid: true, IsError: false } ) throw new GltfException( "Could not copy animated glTF material." );
			}
			var stage = new AnimatedMaterialStage( entries );
			for ( var i = 0; i < draws.Count; i++ )
				if ( draws[i].AnimatedMaterial is { } previous && !ReferenceEquals( previous, entries[i]?.Material ) ) stage.Retired.Add( previous );
			if ( stage.Retired.Count != 0 )
			{
				_ = checked( AnimatedMaterialRetiredPending + stage.Retired.Count );
				asset.RetainInstance(); stage.RetireLease = true;
			}
			return stage;
		}
		catch ( Exception cause ) { GltfGpuCleanup.Rollback( cause, entries.Where( x => x is { New: true, Material: not null } ).Select<AnimatedMaterialEntry, Action>( x => x.Material.Destroy ) ); throw; }
	}
	private void ApplyAnimatedMaterialValues( AnimatedMaterialStage stage )
	{
		try
		{
			for ( var i = 0; i < stage.Entries.Length; i++ )
			{
				var entry = stage.Entries[i]; if ( entry is null ) continue;
				stage.Previous[i] = entry.Material.GetVector4( entry.Parameter );
				if ( stage.Previous[i] == entry.Tint ) continue;
				stage.Changed.Add( i );
				if ( AnimatedMaterialWriter is null ) entry.Material.Set( entry.Parameter, entry.Tint );
				else AnimatedMaterialWriter( entry.Material, entry.Parameter, entry.Tint );
			}
		}
		catch ( Exception cause )
		{
			try { RollbackAnimatedMaterialValues( stage ); }
			catch ( Exception rollback ) { throw new AggregateException( cause, rollback ); }
			throw;
		}
	}
	private static void RollbackAnimatedMaterialValues( AnimatedMaterialStage stage )
	{
		GltfGpuCleanup.ReleaseAll( stage.Changed.AsEnumerable().Reverse().Select<int, Action>( i => () => stage.Entries[i].Material.Set( stage.Entries[i].Parameter, stage.Previous[i] ) ) );
		stage.Changed.Clear();
	}
	private void PublishAnimatedMaterials( AnimatedMaterialStage stage )
	{
		if ( stage.RetireLease ) { RetireAnimatedMaterials( stage.Retired ); stage.RetireLease = false; }
		hasAnimatedMaterials = false;
		for ( var i = 0; i < draws.Count; i++ )
		{
			var draw = draws[i]; var entry = stage.Entries[i];
			hasAnimatedMaterials |= entry is not null;
			draw.AnimatedMaterial = entry?.Material; draw.AnimatedParent = entry?.Parent; stage.Entries[i] = null;
		}
		stage.Changed.Clear();
	}
	private void RetireAnimatedMaterials( List<Material> retired )
	{
		// The frame-end queue is drained after view rendering completes. A
		// separate asset lease retains variant packet materials and textures if
		// this instance is disposed before the queued release executes.
		AnimatedMaterialRetiredPending += retired.Count;
		EngineLoop.DisposeAtFrameEnd( Sandbox.Utility.DisposeAction.Create( () =>
		{
			using var scope = new GlobalContext.GlobalContextScope( ownerContext );
			try { GltfGpuCleanup.ReleaseAll( retired.Select<Material, Action>( x => x.Destroy ) ); }
			finally { AnimatedMaterialRetiredPending -= retired.Count; asset.ReleaseInstance(); }
		} ) );
	}
	private void DestroyStagedAnimatedMaterials( AnimatedMaterialStage stage )
	{
		if ( stage is null ) return;
		try { GltfGpuCleanup.ReleaseAll( stage.Entries.Where( x => x is { New: true, Material: not null } ).Select<AnimatedMaterialEntry, Action>( x => x.Material.Destroy ) ); }
		finally { if ( stage.RetireLease ) { stage.RetireLease = false; asset.ReleaseInstance(); } }
	}
}
