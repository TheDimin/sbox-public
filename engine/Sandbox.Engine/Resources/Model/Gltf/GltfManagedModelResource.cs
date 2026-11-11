using Sandbox.Engine;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;
using System.Threading;

namespace Sandbox;

/// <summary>
/// A Model resource's ownership of one prepared glTF generation. CPU preparation
/// is independent of graphics realization; existing renderer instances retain
/// their generation after this resource owner is explicitly invalidated.
/// </summary>
internal sealed class GltfManagedModelResource
{
	internal const string ComputeShaderPath = "shaders/gltf_deform_cs.shader";
	private int ownerReleased;
	internal GlobalContext Context { get; }
	internal GltfGpuAsset Asset { get; }
	internal GltfManagedModelMetadata Metadata { get; }
	internal GltfVariantCatalog Variants { get; }
	internal bool IsValid => Volatile.Read( ref ownerReleased ) == 0;
	internal int MeshCount => Asset.MeshCount;
	internal BBox Bounds => Asset.RestBounds;
	internal IReadOnlyList<int?> MaterialSlotSourceIndices => Asset.MaterialSlotSourceIndices;
	internal int? SelectedSceneIndex => Asset.SelectedSceneIndex;
	internal int? DefaultSceneIndex { get; }
	internal int SceneCount { get; }

	/// <summary>
	/// Ownership of document transfers only after the backend has prepared
	/// successfully. The caller still owns it if this constructor throws.
	/// </summary>
	internal GltfManagedModelResource( GltfDocument document, int? sceneIndex = null )
	{
		ArgumentNullException.ThrowIfNull( document );
		ThreadSafe.AssertIsMainThread();
		Context = GlobalContext.Current;
		DefaultSceneIndex = document.GetDefaultScene();
		SceneCount = document.Root.TryGetProperty( "scenes", out var scenes ) ? scenes.GetArrayLength() : 0;
		Variants = new GltfVariantCatalog( document.ReadMaterialVariantNames() );
		Metadata = new GltfManagedModelMetadata( document, sceneIndex ?? document.GetDefaultScene() );
		if ( GltfPublicMaterialRoute.RequiresProjected( document, sceneIndex ?? document.GetDefaultScene() ) )
		{
			var templates = new Dictionary<string, Material>( StringComparer.Ordinal );
			Material SelectTemplate( GltfMaterial material, GltfGpuPrimitivePlan _ )
			{
				if ( material is null ) throw new GltfException( "A projected source material must be explicit." );
				var mode = material.AlphaMode;
				if ( !material.Unlit && mode == "BLEND" && material.DiffuseTransmission is not null ) throw new GltfException( "Source-lit BLEND with diffuse transmission is not yet supported." );
				var key = (material.Unlit ? "unlit:" : "lit:") + mode + (material.DoubleSided ? ":double" : ":single") + (material.DiffuseTransmission is null ? "" : ":diffuse-transmission")
					+ (material.Transmission is null ? "" : ":transmission") + (material.Anisotropy is null ? "" : ":anisotropy");
				if ( templates.TryGetValue( key, out var existing ) ) return existing;
				var source = Material.FromShader( material.Unlit ? "shaders/gltf_unlit_five_uv.shader" : material.DiffuseTransmission is not null ? "shaders/gltf_diffuse_transmission.shader" : "shaders/gltf_surface_five_uv.shader" );
				if ( source is not { IsValid: true, IsError: false } ) throw new GltfException( "The glTF source material template is unavailable." );
				var template = source.CreateCopy( "gltf_unlit_template_" + Guid.NewGuid().ToString( "N" ) );
				try
				{
					template.SetFeature( "F_ALPHA_TEST", mode == "MASK" ? 1 : 0 );
					template.SetFeature( "F_TRANSLUCENT", mode == "BLEND" || (material.Transmission is not null && material.DiffuseTransmission is null && !material.Unlit) ? 1 : 0 );
					template.SetFeature( "F_RENDER_BACKFACES", material.DoubleSided ? 1 : 0 );
					if ( material.DiffuseTransmission is null && !material.Unlit )
					{
						template.SetFeature( "F_GLTF_TRANSMISSION", material.Transmission is null ? 0 : 1 );
						// Only transmission and anisotropy select a shader variant; every other layer is a uniform branch.
						template.SetFeature( "F_GLTF_ANISOTROPY", material.Anisotropy is null ? 0 : 1 );
					}
					templates.Add( key, template ); return template;
				}
				catch { template.Destroy(); throw; }
			}
			void ReleaseProjected()
			{
				var releases = templates.Values.Select<Material, Action>( template => template.Destroy ).Append( Metadata.ReleaseDocument );
				GltfGpuCleanup.ReleaseAll( releases );
			}
			Asset = GltfGpuAsset.PrepareProjected( document, ComputeShaderPath, 256L * 1024 * 1024,
				SelectTemplate, GltfUnlitMaterialPlan.Features.All, ReleaseProjected, sceneIndex, allowUnselected: true, legacyLit: true, sourceLitFeatureSidedness: true );
		}
		else Asset = GltfGpuAsset.Prepare( document, ComputeShaderPath, finalRelease: Metadata.ReleaseDocument, sceneIndex: sceneIndex, allowUnselected: true );
	}

	/// <summary>
	/// Explicit projected-source candidate. The caller supplies templates and
	/// independently established unlit capabilities; no shader is inferred here.
	/// Document ownership transfers only if the full preparation succeeds.
	/// </summary>
	internal GltfManagedModelResource( GltfDocument document, long encodedImageBudget,
		Func<GltfMaterial, GltfGpuPrimitivePlan, Material> selectTemplate, GltfUnlitMaterialPlan.Features unlitCapabilities )
	{
		ArgumentNullException.ThrowIfNull( document ); ThreadSafe.AssertIsMainThread();
		Context = GlobalContext.Current;
		DefaultSceneIndex = document.GetDefaultScene();
		SceneCount = document.Root.TryGetProperty( "scenes", out var scenes ) ? scenes.GetArrayLength() : 0;
		Variants = new GltfVariantCatalog( document.ReadMaterialVariantNames() );
		Metadata = new GltfManagedModelMetadata( document, document.GetDefaultScene() );
		Asset = GltfGpuAsset.PrepareProjected( document, ComputeShaderPath, encodedImageBudget, selectTemplate, unlitCapabilities, Metadata.ReleaseDocument );
	}

	internal IReadOnlyList<Material> GetMaterials()
	{
		ObjectDisposedException.ThrowIf( !IsValid, this );
		using var scope = new GlobalContext.GlobalContextScope( Context );
		return Asset.GetMaterials();
	}

	internal GltfGpuInstance CreateInstance( SceneWorld world, bool initiallyVisible = true )
	{
		ObjectDisposedException.ThrowIf( !IsValid, this );
		using var scope = new GlobalContext.GlobalContextScope( Context );
		return Asset.CreateInstance( world, initiallyVisible );
	}

	internal void ReleaseOwner()
	{
		if ( Interlocked.Exchange( ref ownerReleased, 1 ) != 0 ) return;
		using var scope = new GlobalContext.GlobalContextScope( Context );
		MainThread.Queue( () =>
		{
			using var ownerScope = new GlobalContext.GlobalContextScope( Context );
			if ( Graphics.IsActive ) EngineLoop.DisposeAtFrameEnd( Utility.DisposeAction.Create( Asset.Dispose ) );
			else Asset.Dispose();
		} );
	}
}
