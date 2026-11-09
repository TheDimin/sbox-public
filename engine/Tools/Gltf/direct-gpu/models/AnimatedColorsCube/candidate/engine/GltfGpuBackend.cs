using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using NativeEngine;
using Sandbox.Engine;
using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu;

using NMatrix = System.Numerics.Matrix4x4;

/// <summary>Attempt every release and keep the original acquisition failure.</summary>
internal static class GltfGpuCleanup
{
	internal static void Rollback( Exception original, IEnumerable<Action> releases )
	{
		var errors = new List<Exception> { original };
		foreach ( var release in releases ) { try { release(); } catch ( Exception error ) { errors.Add( error ); } }
		if ( errors.Count != 1 ) throw new AggregateException( "glTF acquisition and rollback failed.", errors );
		System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture( original ).Throw();
	}
	internal static void ReleaseAll( IEnumerable<Action> releases )
	{
		List<Exception> errors = null;
		foreach ( var release in releases ) { try { release(); } catch ( Exception error ) { (errors ??= new()).Add( error ); } }
		if ( errors is not null ) throw new AggregateException( "glTF resource cleanup failed.", errors );
	}
}

internal sealed record GltfGpuField( string Semantic, int SemanticIndex, int Offset, int Components, int ComponentType = 5126 );
internal sealed record GltfGpuSourceBinding( GltfUploadStream Stream, int ArenaByteOffset, int DescriptorIndex );

internal sealed class GltfGpuPrimitiveSnapshot
{
	public int Node { get; init; }
	public int? InstanceIndex { get; init; }
	public int Mesh { get; init; }
	public int Primitive { get; init; }
	public int VertexCount { get; init; }
	public int VertexStride { get; init; }
	public IReadOnlyList<GltfGpuField> Fields { get; init; }
	public byte[] Bytes { get; init; }
	public uint[] SourceVertices { get; init; }
	public uint[] Indices { get; init; }
	public string IndexDataOrigin { get; init; }
	public int SourceMode { get; init; }
	public Graphics.PrimitiveType? SubmittedPrimitiveType { get; init; }
	public int DrawCalls { get; init; }
	public bool Visible { get; init; }
	public long ComputedRevision { get; init; }
	public long SubmittedRevision { get; init; }
	public int? SubmittedMaterialIndex { get; init; }
	public Material SubmittedMaterial { get; init; }
	public ulong SubmittedMaterialFrame { get; init; }
	public long? SubmittedVariantRevision { get; init; }
	public bool SubmittedMaterialOverride { get; init; }
}

// These are protocol records, not a vertex struct. All byte-address loads use
// values in these records populated from the actual glTF upload streams.
[StructLayout( LayoutKind.Sequential )]
internal struct GltfGpuInputDescriptor
{
	public uint ByteOffset, ByteStride, ComponentType, Components;
	public uint Normalized, Role, SemanticIndex, Target;
}

[StructLayout( LayoutKind.Sequential )]
internal struct GltfGpuOutputDescriptor
{
	public uint Source, ByteOffset, Components, Role;
	public uint SemanticIndex, Padding0, Padding1, Padding2;
}

/// <summary>CPU upload plan retaining its source generation; allocates no GPU/native resources.</summary>
internal sealed partial class GltfGpuPrimitivePlan
{
	internal GltfDocument OwnerDocument { get; private init; }
	internal int Mesh { get; private init; }
	internal int Primitive { get; private init; }
	internal GltfPrimitiveUploadData Upload { get; private init; }
	public byte[] InputBytes { get; private init; }
	public IReadOnlyDictionary<string, GltfGpuSourceBinding> Sources { get; private init; }
	public IReadOnlyList<GltfGpuField> Fields { get; private init; }
	public uint[] SourceVertices { get; private init; }
	public uint[] Indices { get; private init; }
	public int VertexStride { get; private init; }
	public bool GenerateFlatNormals { get; private init; }
	public int Mode { get; private init; }
	public int SourceMode { get; private init; }
	public int? Material { get; private init; }
	internal GltfGpuInputDescriptor[] InputDescriptors { get; private init; }
	internal GltfGpuOutputDescriptor[] OutputDescriptors { get; private init; }
	internal int BaseAttributeCount { get; private init; }
	internal int MorphTargetCount { get; private init; }
	internal GltfMaterialUvPlan MaterialUv { get; private init; }
	internal GltfMaterialUvProjection UvProjection { get; private init; }

	public static GltfGpuPrimitivePlan Create( GltfDocument document, int mesh, int primitive ) => Create( document, mesh, primitive, null );

	internal static GltfGpuPrimitivePlan Create( GltfDocument document, int mesh, int primitive, int? shaderUvInputs = null, IEnumerable<GltfMaterialTextureRole> consumedRoles = null )
	{
		ArgumentNullException.ThrowIfNull( document );
		var source = document.MaterializePrimitiveUpload( mesh, primitive );
		var geometry = document.ReadPrimitive( mesh, primitive );
		if ( !shaderUvInputs.HasValue && consumedRoles is not null )
			throw new ArgumentException( "Consumed material roles require an explicit shader UV input count.", nameof( consumedRoles ) );
		if ( !source.Attributes.ContainsKey( "POSITION" ) ) throw new GltfException( "Direct GPU drawing requires POSITION." );
		var targets = document.MaterializeMorphTargetsUpload( mesh, primitive );
		var arena = new GltfGpuInputArenaBuilder();
		var inputs = new List<GltfGpuInputDescriptor>();
		var bindings = new Dictionary<string, GltfGpuSourceBinding>( StringComparer.Ordinal );
		GltfGpuSourceBinding Append( string name, GltfUploadStream stream, uint target )
		{
			var start = arena.Add( stream.Storage );
			var (role, semanticIndex) = Semantic( name );
			var offset = checked(start + stream.ByteOffset);
			var binding = new GltfGpuSourceBinding( stream, offset, inputs.Count );
			inputs.Add( new GltfGpuInputDescriptor { ByteOffset = (uint)offset, ByteStride = (uint)stream.ByteStride,
				ComponentType = (uint)stream.ComponentType, Components = (uint)stream.Components, Normalized = stream.Normalized ? 1u : 0,
				Role = role, SemanticIndex = (uint)semanticIndex, Target = target } );
			return binding;
		}
		foreach ( var pair in source.Attributes.OrderBy( x => x.Key, StringComparer.Ordinal ) ) bindings.Add( pair.Key, Append( pair.Key, pair.Value, uint.MaxValue ) );
		var baseCount = inputs.Count;
		for ( var target = 0; target < targets.Targets.Count; target++ )
			foreach ( var pair in targets.Targets[target].OrderBy( x => x.Key, StringComparer.Ordinal ) ) _ = Append( pair.Key, pair.Value, (uint)target );
		// ByteAddressBuffer.Load reads aligned words, including the last partial
		// byte/short component. Tail padding is ours, never read past allocation.
		var bytes = arena.Build();

		var fields = new List<GltfGpuField>(); var output = new List<GltfGpuOutputDescriptor>(); var stride = 0;
		void Field( string name, uint role, int index, int components )
		{
			var inputName = role switch { 0 => "POSITION", 1 => "NORMAL", 2 => "TANGENT", 3 => "COLOR_" + index, 4 => "TEXCOORD_" + index, _ => throw new InvalidOperationException() };
			var descriptor = role == 2 && !source.Attributes.ContainsKey( "NORMAL" ) ? uint.MaxValue
				: bindings.TryGetValue( inputName, out var binding ) ? (uint)binding.DescriptorIndex : uint.MaxValue;
			fields.Add( new GltfGpuField( name, index, stride, components ) );
			output.Add( new GltfGpuOutputDescriptor { Source = descriptor, ByteOffset = (uint)stride, Components = (uint)components, Role = role, SemanticIndex = (uint)index } );
			stride = checked(stride + components * 4);
		}
		Field( "POSITION", 0, 0, 3 ); Field( "NORMAL", 1, 0, 3 ); Field( "TANGENT", 2, 0, 4 );
		var colors = source.Attributes.Keys.Where( x => x.StartsWith( "COLOR_", StringComparison.Ordinal ) ).Select( x => Semantic( x ).Index ).Append( 0 ).Distinct().Order().ToArray();
		foreach ( var index in colors ) Field( "COLOR", 3, index, 4 );
		var uvs = source.Attributes.Keys.Where( x => x.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) ).Select( x => Semantic( x ).Index ).Append( 0 ).Distinct().Order().ToArray();
		foreach ( var index in uvs ) Field( "TEXCOORD", 4, index, 2 );
		var indices = GltfGeometry.ExpandIndices( geometry.Indices, geometry.Mode );
		var mode = geometry.Mode == 0 ? 0 : geometry.Mode is 1 or 2 or 3 ? 1 : 4;
		var flat = !source.Attributes.ContainsKey( "NORMAL" ) && mode == 4;
		var sourceVertices = flat ? indices.ToArray() : Enumerable.Range( 0, source.Layout.VertexCount ).Select( x => (uint)x ).ToArray();
		if ( flat ) indices = Enumerable.Range( 0, indices.Length ).Select( x => (uint)x ).ToArray();
		var plan = new GltfGpuPrimitivePlan { OwnerDocument = document, Mesh = mesh, Primitive = primitive, Upload = source,
			InputBytes = bytes, Sources = bindings, Fields = fields.AsReadOnly(), SourceVertices = sourceVertices,
			Indices = indices, VertexStride = stride, GenerateFlatNormals = flat, Mode = mode, SourceMode = source.Layout.SourceMode, Material = geometry.Material,
			InputDescriptors = inputs.ToArray(), OutputDescriptors = output.ToArray(), BaseAttributeCount = baseCount, MorphTargetCount = targets.Targets.Count };
		return shaderUvInputs.HasValue
			? plan.ProjectMaterial( geometry.Material, shaderUvInputs.Value, consumedRoles )
			: plan;
	}

	/// <summary>
	/// Build a separate immutable output route for a selected material using the
	/// exact retained upload. A variant owner must publish its descriptor buffer,
	/// matching material and instance revision atomically; this method alone does
	/// not change an active instance or grant a document lifetime lease.
	/// </summary>
	internal GltfGpuPrimitivePlan ProjectMaterial( int? materialIndex, int shaderUvInputs,
		IEnumerable<GltfMaterialTextureRole> consumedRoles = null, IReadOnlyList<int> shaderUvSemantics = null )
	{
		RequireSourceOwnership( OwnerDocument, Mesh, Primitive, Upload );
		var material = materialIndex is int selectedMaterialIndex ? OwnerDocument.ReadMaterial( selectedMaterialIndex ) : null;
		var materialUv = consumedRoles is null ? GltfMaterialUvPlan.Create( material, Upload, materialIndex )
			: GltfMaterialUvPlan.CreateForRoles( material, Upload, consumedRoles, materialIndex );
		var projection = GltfMaterialUvProjection.Create( materialUv, shaderUvInputs );
		if ( shaderUvSemantics is not null && (shaderUvSemantics.Count != shaderUvInputs || shaderUvSemantics.Any( x => x < 0 ) || shaderUvSemantics.Distinct().Count() != shaderUvInputs) )
			throw new ArgumentException( "Shader UV semantics must be non-negative, unique and match the shader input count.", nameof( shaderUvSemantics ) );
		var fields = new List<GltfGpuField>(); var output = new List<GltfGpuOutputDescriptor>(); var stride = 0;
		for ( var index = 0; index < Fields.Count; index++ )
		{
			var field = Fields[index]; if ( field.Semantic == "TEXCOORD" ) continue;
			var descriptor = OutputDescriptors[index]; descriptor.ByteOffset = (uint)stride;
			fields.Add( field with { Offset = stride } ); output.Add( descriptor ); stride = checked(stride + field.Components * 4);
		}
		for ( var slot = 0; slot < shaderUvInputs; slot++ )
		{
			var sourceIndex = uint.MaxValue;
			if ( slot < projection.Sources.Count )
			{
				var selected = projection.Sources[slot]; var semantic = "TEXCOORD_" + selected.TexCoord;
				RequireSourceStreamOwnership( semantic, selected.Stream );
				sourceIndex = (uint)Sources[semantic].DescriptorIndex;
			}
			var semanticIndex = shaderUvSemantics is null ? slot : shaderUvSemantics[slot];
			fields.Add( new GltfGpuField( "TEXCOORD", semanticIndex, stride, 2 ) );
			output.Add( new GltfGpuOutputDescriptor { Source = sourceIndex, ByteOffset = (uint)stride, Components = 2, Role = 4, SemanticIndex = (uint)semanticIndex } );
			stride = checked(stride + 8);
		}
		return new GltfGpuPrimitivePlan { OwnerDocument = OwnerDocument, Mesh = Mesh, Primitive = Primitive, Upload = Upload,
			InputBytes = InputBytes, Sources = Sources, Fields = fields.AsReadOnly(), SourceVertices = SourceVertices, Indices = Indices,
			VertexStride = stride, GenerateFlatNormals = GenerateFlatNormals, Mode = Mode, SourceMode = SourceMode, Material = materialIndex,
			InputDescriptors = InputDescriptors, OutputDescriptors = output.ToArray(), BaseAttributeCount = BaseAttributeCount,
			MorphTargetCount = MorphTargetCount, MaterialUv = materialUv, UvProjection = projection };
	}

	/// <summary>
	/// Material packets must reference the exact retained generation. Detached
	/// snapshots with identical accessor metadata are not ownership evidence.
	/// The owning document must still be live; retaining bytes grants no lease.
	/// </summary>
	internal void RequireSourceOwnership( GltfDocument document, int mesh, int primitive, GltfPrimitiveUploadData upload )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentNullException.ThrowIfNull( upload );
		_ = OwnerDocument.Root;
		if ( !ReferenceEquals( OwnerDocument, document ) || mesh != Mesh || primitive != Primitive || !ReferenceEquals( Upload, upload ) )
			throw new GltfException( "Material source does not belong to this retained glTF primitive generation." );
	}
	internal void RequireSourceStreamOwnership( string semantic, GltfUploadStream stream )
	{
		ArgumentNullException.ThrowIfNull( semantic ); ArgumentNullException.ThrowIfNull( stream );
		_ = OwnerDocument.Root;
		if ( !Upload.Attributes.TryGetValue( semantic, out var expected ) || !ReferenceEquals( expected, stream ) )
			throw new GltfException( "Material stream does not belong to the requested retained glTF primitive semantic." );
	}

	private static (uint Role, int Index) Semantic( string name )
	{
		if ( name == "POSITION" ) return (0, 0);
		if ( name == "NORMAL" ) return (1, 0);
		if ( name == "TANGENT" ) return (2, 0);
		foreach ( var item in new[] { (Prefix: "COLOR_", Role: 3u), (Prefix: "TEXCOORD_", Role: 4u), (Prefix: "JOINTS_", Role: 5u), (Prefix: "WEIGHTS_", Role: 6u) } )
			if ( name.StartsWith( item.Prefix, StringComparison.Ordinal ) && int.TryParse( name.AsSpan( item.Prefix.Length ), out var index ) && index >= 0 ) return (item.Role, index);
		return (7, 0);
	}
}

/// <summary>Shared C# owned input buffers, runtime vertex declarations and stock material leases.</summary>
internal sealed partial class GltfGpuAsset : IDisposable
{
	internal sealed partial class Primitive : IDisposable
	{
		internal int Mesh, Index, MaterialSlot;
		internal GltfGpuPrimitivePlan Plan;
		internal Material Material;
		internal GpuBuffer Input, InputDescriptors, OutputDescriptors, SourceVertices;
		internal NativeEngine.VertexLayout Layout;
		public void Dispose()
		{
			var buffers = new[] { Input, InputDescriptors, OutputDescriptors, SourceVertices };
			Input = InputDescriptors = OutputDescriptors = SourceVertices = null;
			var layout = Layout; Layout = default;
			GltfGpuCleanup.ReleaseAll( buffers.Where( x => x is not null ).Select<GpuBuffer, Action>( x => x.Dispose )
				.Concat( layout.IsValid ? new Action[] { () => layout.Free(), () => layout.Destroy() } : Array.Empty<Action>() ).Concat( [DisposeRawRoute] ) );
		}
	}
	internal sealed record Node( int Index, int? Skin, Primitive Primitive, GltfGpuRig Rig, GltfGpuInstancingRow Instance )
	{
		public int? InstanceIndex => Instance.InstanceIndex;
		public NMatrix LocalTransform => Instance.LocalTransform;
	}
	internal GltfDocument Document { get; private set; }
	internal int? SelectedSceneIndex { get; private set; }
	internal readonly List<Node> Nodes = new();
	internal readonly List<Primitive> Primitives = new();
	internal const string DefaultComputeShaderPath = "shaders/gltf_deform_cs.shader";
	internal Material ComputeMaterial { get; private set; }
	private readonly List<IDisposable> materialLeases = new();
	private readonly Dictionary<int, GltfStockMaterialAdapter.Prepared> preparedMaterials = new();
	private readonly string computeShaderPath;
	private Func<int?, Material> materialResolver;
	private readonly GlobalContext ownerContext;
	private IReadOnlyList<Material> materials;
	private Action finalRelease;
	private int instances;
	private bool ownerReleased, disposed, gpuRealized;
	public bool IsValid => !ownerReleased && !disposed;
	public bool HasGpuResources => gpuRealized;
	public int MeshCount => Nodes.Count;
	public BBox RestBounds { get; private set; }
	public IReadOnlyList<int?> MaterialSlotSourceIndices { get; private set; } = Array.Empty<int?>();

	private GltfGpuAsset( GltfDocument document, string computeShaderPath, Func<int?, Material> resolver )
	{
		Document = document; this.computeShaderPath = computeShaderPath; materialResolver = resolver; ownerContext = GlobalContext.Current;
	}
	public static GltfGpuAsset Create( GltfDocument document, string computeShaderPath, Func<int?, Material> materialResolver = null )
		=> Prepare( document, computeShaderPath, materialResolver );
	public static GltfGpuAsset Create( GltfDocument document, Material computeMaterial, Func<int?, Material> materialResolver = null )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentNullException.ThrowIfNull( computeMaterial ); ThreadSafe.AssertIsMainThread();
		if ( !computeMaterial.IsValid || computeMaterial.IsError ) throw new GltfException( "Borrowed direct GPU deformation material is invalid." );
		var asset = Prepare( document, DefaultComputeShaderPath, materialResolver ); asset.ComputeMaterial = computeMaterial; return asset;
	}

	/// <summary>
	/// Snapshot CPU geometry/material inputs and exact rest metadata without a GPU
	/// device. Callback ownership transfers only on success. Renderer instances
	/// retain this generation after its resource owner releases it.
	/// </summary>
	public static GltfGpuAsset Prepare( GltfDocument document, string computeShaderPath, Func<int?, Material> materialResolver = null, Action finalRelease = null, int? sceneIndex = null, bool allowUnselected = false )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentException.ThrowIfNullOrWhiteSpace( computeShaderPath );
		ThreadSafe.AssertIsMainThread();
		var asset = new GltfGpuAsset( document, computeShaderPath, materialResolver );
		try
		{
			var scene = sceneIndex ?? document.GetDefaultScene();
			if ( sceneIndex.HasValue && (!document.Root.TryGetProperty( "scenes", out var scenes ) || sceneIndex.Value < 0 || sceneIndex.Value >= scenes.GetArrayLength()) )
				throw new ArgumentOutOfRangeException( nameof( sceneIndex ), "The requested glTF scene does not exist." );
			if ( !scene.HasValue && !allowUnselected ) throw new GltfException( "The asset has no default scene to draw. Select an explicit scene." );
			asset.SelectedSceneIndex = scene;
			var rest = document.EvaluatePose();
			var cache = new Dictionary<(int, int), Primitive>();
			var pending = new Stack<int>();
			var selected = new HashSet<int>();
			if ( scene.HasValue )
			{
				var sceneJson = document.Root.GetProperty( "scenes" )[scene.Value];
				if ( sceneJson.TryGetProperty( "nodes", out var roots ) ) foreach ( var node in roots.EnumerateArray().Reverse() ) pending.Push( node.GetInt32() );
			}
			while ( pending.Count != 0 )
			{
				var nodeIndex = pending.Pop(); if ( !selected.Add( nodeIndex ) ) throw new GltfException( "Selected node is repeated." );
				var node = document.Root.GetProperty( "nodes" )[nodeIndex];
				if ( node.TryGetProperty( "children", out var children ) ) foreach ( var child in children.EnumerateArray().Reverse() ) pending.Push( child.GetInt32() );
				if ( !node.TryGetProperty( "mesh", out _ ) ) continue;
				var instancing = GltfGpuInstancingPlan.Create( document, nodeIndex );
				if ( instancing.AppliesInstancing && node.TryGetProperty( "skin", out _ ) )
					throw new GltfException( "EXT_mesh_gpu_instancing with a skinned mesh node is not implemented; instance transforms cannot be ignored." );
				var mesh = instancing.Mesh; var primitives = document.Root.GetProperty( "meshes" )[mesh].GetProperty( "primitives" );
				for ( var index = 0; index < primitives.GetArrayLength(); index++ )
				{
					// Core recommends skipping primitives with no POSITION; their
					// source metadata remains in Document rather than fake geometry.
					if ( !primitives[index].GetProperty( "attributes" ).TryGetProperty( "POSITION", out _ ) ) { _ = document.ReadPrimitive( mesh, index ); continue; }
					if ( !cache.TryGetValue( (mesh, index), out var primitive ) )
					{
						var plan = GltfGpuPrimitivePlan.Create( document, mesh, index );
						primitive = new Primitive { Mesh = mesh, Index = index, Plan = plan };
						asset.Primitives.Add( primitive ); cache.Add( (mesh, index), primitive );
					}
					int? skinIndex = node.TryGetProperty( "skin", out var skin ) ? skin.GetInt32() : null;
					var rig = new GltfGpuRig( document, nodeIndex, skinIndex, index, primitive.Plan );
					// One primitive and base rig own immutable input once. Row expansion
					// preserves semantics; it is not hardware-instancing performance proof.
					foreach ( var row in instancing.Instances ) asset.Nodes.Add( new Node( nodeIndex, skinIndex, primitive, rig, row ) );
				}
			}
			asset.MaterialSlotSourceIndices = Array.AsReadOnly( asset.Primitives.Select( x => x.Plan.Material ).Distinct().OrderBy( x => x.HasValue ? 0 : 1 ).ThenBy( x => x ?? 0 ).ToArray() );
			foreach ( var primitive in asset.Primitives ) primitive.MaterialSlot = asset.MaterialSlotSourceIndices.ToList().IndexOf( primitive.Plan.Material );
			if ( materialResolver is null )
			{
				// Legacy stock materials share one UV input. Bake that source mapping
				// per primitive, and keep the unmodified route for native overrides.
				var rawPlans = new Dictionary<Primitive, GltfGpuPrimitivePlan>();
				foreach ( var primitive in asset.Primitives )
				{
					var (baked, raw) = primitive.Plan.CreateLegacyUvPlans();
					if ( baked is null ) continue;
					primitive.Plan = baked; rawPlans.Add( primitive, raw );
				}
				if ( rawPlans.Count != 0 ) asset.variantRawPlans = rawPlans;
				foreach ( var index in asset.MaterialSlotSourceIndices ) asset.preparedMaterials.Add( index ?? -1, GltfStockMaterialAdapter.Prepare( document, index.HasValue ? document.ReadMaterial( index.Value ) : null ) );
			}
			asset.RestBounds = GltfGpuRig.Bounds( asset.Nodes.Select( x => (x.Rig, x.Rig.Evaluate( rest, x.LocalTransform ), rest.GetMorphWeights( x.Index )) ), NMatrix.Identity, padding: false );
			asset.finalRelease = finalRelease;
			return asset;
		}
		catch ( Exception error ) { GltfGpuCleanup.Rollback( error, [asset.Dispose] ); throw; }
	}

	/// <summary>Realize only native materials, retaining the prepared CPU generation.</summary>
	public IReadOnlyList<Material> GetMaterials()
	{
		ObjectDisposedException.ThrowIf( disposed, this ); ThreadSafe.AssertIsMainThread();
		if ( materials is not null ) return materials;
		if ( Graphics.IsActive ) throw new GltfException( "glTF materials cannot be realized during rendering." );
		using var scope = new GlobalContext.GlobalContextScope( ownerContext );
		if ( variantPackets is not null ) return GetProjectedMaterials();
		var result = new Material[MaterialSlotSourceIndices.Count]; var acquired = new List<IDisposable>();
		try
		{
			for ( var slot = 0; slot < result.Length; slot++ )
			{
				var index = MaterialSlotSourceIndices[slot];
				if ( materialResolver is not null ) result[slot] = materialResolver( index );
				else
				{
					var template = Material.FromShader( "shaders/gltf_stock_sampler.shader" );
					if ( template is null || !template.IsValid || template.IsError ) throw new GltfException( "The glTF stock material sampler template is unavailable." );
					var lease = GltfStockMaterialAdapter.Acquire( preparedMaterials[index ?? -1], template, linearVertexColor: true ); acquired.Add( lease ); result[slot] = lease.Value;
				}
				if ( result[slot] is null || !result[slot].IsValid || result[slot].IsError ) throw new GltfException( "Resolved stock glTF material is invalid." );
			}
			// The compute output supplies white COLOR_0 when absent, so a shared
			// material slot behaves correctly for colored and uncolored primitives.
			foreach ( var primitive in Primitives )
				primitive.Material = result[primitive.MaterialSlot];
			materialLeases.AddRange( acquired ); materials = Array.AsReadOnly( result ); return materials;
		}
		catch ( Exception error ) { GltfGpuCleanup.Rollback( error, acquired.Select<IDisposable, Action>( x => x.Dispose ) ); throw; }
	}

	private void EnsureGpuResources()
	{
		if ( gpuRealized || Primitives.Count == 0 ) return;
		if ( !Graphics.IsAvailable ) throw new GltfException( "Rendering a prepared glTF model requires an available graphics device." );
		if ( Graphics.IsActive ) throw new GltfException( "glTF GPU resources cannot be realized during rendering." );
		using var scope = new GlobalContext.GlobalContextScope( ownerContext );
		_ = GetMaterials();
		// FromShader entries and explicitly supplied anonymous materials are
		// borrowed. This asset never destroys their engine/caller-owned handles.
		ComputeMaterial ??= Material.FromShader( computeShaderPath );
		if ( ComputeMaterial is null || !ComputeMaterial.IsValid || ComputeMaterial.IsError ) throw new GltfException( "The compiled glTF deformation shader is unavailable." );
		try
		{
			foreach ( var primitive in Primitives )
			{
				var plan = primitive.Plan;
				primitive.Input = UploadBytes( plan.InputBytes, GpuBuffer.UsageFlags.ByteAddress, "glTF source arena" );
				primitive.InputDescriptors = Upload( plan.InputDescriptors, "glTF source descriptors" );
				primitive.OutputDescriptors = Upload( plan.OutputDescriptors, "glTF output descriptors" );
				primitive.SourceVertices = Upload( plan.SourceVertices, "glTF output source indices" );
				primitive.Layout = NativeEngine.VertexLayout.Create( "gltf_direct_" + Guid.NewGuid().ToString( "N" ), plan.VertexStride );
				foreach ( var field in plan.Fields ) primitive.Layout.Add( field.Semantic.ToLowerInvariant(), field.SemanticIndex, (uint)Format( field.Components ), field.Offset );
				primitive.Layout.Build();
			}
			gpuRealized = true;
		}
		catch ( Exception error ) { GltfGpuCleanup.Rollback( error, Primitives.Select<Primitive, Action>( x => x.Dispose ) ); throw; }
	}

	public GltfGpuInstance CreateInstance( SceneWorld world ) => CreateInstance( world, initiallyVisible: true );

	internal GltfGpuInstance CreateInstance( SceneWorld world, bool initiallyVisible )
	{
		ObjectDisposedException.ThrowIf( ownerReleased || disposed, this ); ThreadSafe.AssertIsMainThread();
		if ( !SelectedSceneIndex.HasValue ) throw new GltfException( "The asset has no default scene to draw. Select an explicit scene before creating a renderer instance." );
		EnsureGpuResources();
		var instance = new GltfGpuInstance( this, world, initiallyVisible ); instances++; return instance;
	}
	internal void RetainInstance()
	{
		ThreadSafe.AssertIsMainThread();
		if ( disposed || instances <= 0 ) throw new InvalidOperationException( "A live glTF instance lease is required to retain resources." );
		instances = checked( instances + 1 );
	}

	internal void ReleaseInstance()
	{
		ThreadSafe.AssertIsMainThread();
		if ( instances <= 0 ) throw new InvalidOperationException( "glTF GPU instance lease was released twice." );
		instances--; if ( ownerReleased && instances == 0 ) ReleaseResources();
	}
	public void Dispose()
	{
		if ( ownerReleased ) return; ThreadSafe.AssertIsMainThread(); ownerReleased = true;
		if ( instances == 0 ) ReleaseResources();
	}
	private void ReleaseResources()
	{
		if ( disposed ) return; disposed = true;
		void Release()
		{
			using var scope = new GlobalContext.GlobalContextScope( ownerContext );
			List<Exception> errors = null;
			void Attempt( Action action ) { try { action(); } catch ( Exception error ) { (errors ??= new()).Add( error ); } }
			foreach ( var primitive in Primitives ) Attempt( primitive.Dispose );
			foreach ( var lease in materialLeases ) Attempt( lease.Dispose );
			if ( variantPackets is not null ) Attempt( variantPackets.Dispose ); variantPackets = null; variantRawPlans = null;
			// The invalid resource wrapper can remain held by user code. Retire
			// its large CPU graphs only after every live instance lease is gone.
			Nodes.Clear(); Primitives.Clear(); materialLeases.Clear(); materials = null;
			preparedMaterials.Clear(); ComputeMaterial = null; materialResolver = null; Document = null; gpuRealized = false;
			var callback = System.Threading.Interlocked.Exchange( ref finalRelease, null ); if ( callback is not null ) Attempt( callback );
			if ( errors is not null ) throw new AggregateException( "glTF GPU asset cleanup failed.", errors );
		}
		if ( Graphics.IsActive ) EngineLoop.DisposeAtFrameEnd( Sandbox.Utility.DisposeAction.Create( Release ) );
		else Release();
	}
	internal static GpuBuffer Upload<T>( T[] values, string name ) where T : unmanaged
	{
		var buffer = new GpuBuffer( Math.Max( 1, values.Length ), Marshal.SizeOf<T>(), GpuBuffer.UsageFlags.Structured, name );
		try { if ( values.Length != 0 ) buffer.SetData<T>( values ); return buffer; }
		catch ( Exception error ) { GltfGpuCleanup.Rollback( error, [buffer.Dispose] ); throw; }
	}
	internal static GpuBuffer UploadBytes( byte[] values, GpuBuffer.UsageFlags flags, string name )
	{
		var buffer = new GpuBuffer( Math.Max( 1, values.Length / 4 ), 4, flags, name );
		try { if ( values.Length != 0 ) buffer.SetData<byte>( values ); return buffer; }
		catch ( Exception error ) { GltfGpuCleanup.Rollback( error, [buffer.Dispose] ); throw; }
	}
	private static ColorFormat Format( int components ) => components switch { 1 => ColorFormat.COLOR_FORMAT_R32_FLOAT, 2 => ColorFormat.COLOR_FORMAT_R32G32_FLOAT, 3 => ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT, 4 => ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT, _ => throw new GltfException( "Invalid output field dimensions." ) };
}

/// <summary>Independent pose, GPU outputs and raw scene draw callback. No native model/mesh.</summary>
internal sealed partial class GltfGpuInstance : IDisposable
{
	private sealed partial class Draw : IDisposable
	{
		internal GltfGpuAsset.Node Node;
		internal GltfGpuRig Rig;
		internal GpuBuffer Output, Palette, Weights, Indices;
		internal bool Reversed;
		internal RenderAttributes Attributes = new();
		internal RenderAttributes RenderAttributes = new();
		internal bool HasVertexState;
		internal long ComputedRevision = -1;
		internal Material EffectiveMaterial;
		internal Material AnimatedMaterial, AnimatedParent;
		internal bool IsTranslucent, IsAlphaTest;
		internal BBox WorldBounds;
		internal SceneCustomObject SortObject;
		internal int DrawCalls;
		internal long SubmittedRevision = -1;
		internal Graphics.PrimitiveType? SubmittedPrimitiveType;
		internal int? SubmittedMaterialIndex;
		internal Material SubmittedMaterial;
		internal ulong SubmittedMaterialFrame;
		internal long? SubmittedVariantRevision;
		internal bool SubmittedMaterialOverride;
		public void Dispose()
		{
			var buffers = new[] { Output, Palette, Weights, Indices }; Output = Palette = Weights = Indices = null;
			var child = SortObject; SortObject = null;
			var animated = AnimatedMaterial; AnimatedMaterial = AnimatedParent = null;
			GltfGpuCleanup.ReleaseAll( new Action[] { () => { if ( child.IsValid() ) { child.RenderOverride = null; child.Delete(); } } }
				.Concat( buffers.Where( x => x is not null ).Select<GpuBuffer, Action>( x => x.Dispose ) ).Concat( [DisposeRawOutput, () => animated?.Destroy()] ) );
		}
	}
	private readonly GltfGpuAsset asset;
	private readonly GlobalContext ownerContext;
	private readonly SceneWorld world;
	private bool manualRendering;
	private Material manualMaterial;
	private bool explicitFrameBufferCopy, automaticFrameBufferCopy;
	private readonly List<Draw> draws = new();
	private readonly GltfGpuSubmissionGate submissionGate = new();
	private readonly SceneCustomObject sceneObject;
	private readonly ConcurrentQueue<string> errors = new();
	private readonly GpuBuffer placementBuffer;
	private readonly Material[] materialOverrides;
	private bool[] nodeVisibility;
	private GltfPose pose;
	private NMatrix placement = NMatrix.Identity;
	private bool disposed;
	public long PoseRevision { get; private set; }
	public long SubmittedRevision { get; private set; } = -1;
	public int DrawCalls { get; private set; }
	public string[] CallbackErrors => errors.ToArray();
	public SceneCustomObject SceneObject => sceneObject;
	public NMatrix Placement
	{
		get => placement;
		set { ObjectDisposedException.ThrowIf( disposed, this ); GltfGpuRig.RequireAffine( value ); ApplyPose( pose, value ); }
	}

	// These are ordered internal commands applied to a final slot vector. The
	// component facade owns persistent whole-versus-slot override precedence.
	public void ClearMaterialOverrides()
	{
		ObjectDisposedException.ThrowIf( disposed, this ); ThreadSafe.AssertIsMainThread(); ChangeOverrides( values => Array.Clear( values ) );
	}
	public void SetMaterialOverride( Material material )
	{
		ObjectDisposedException.ThrowIf( disposed, this ); ThreadSafe.AssertIsMainThread(); ValidateMaterial( material );
		ChangeOverrides( values => Array.Fill( values, material ), material is null ? null : () => EnsureRawOverrideOutputs() );
	}
	public void SetMaterialOverride( int slot, Material material )
	{
		ObjectDisposedException.ThrowIf( disposed, this ); ThreadSafe.AssertIsMainThread();
		if ( (uint)slot >= materialOverrides.Length ) throw new ArgumentOutOfRangeException( nameof( slot ) );
		ValidateMaterial( material );
		ChangeOverrides( values => values[slot] = material, material is null ? null : () => EnsureRawOverrideOutputs( slot ) );
	}
	internal void SetMaterialOverrides( IReadOnlyList<Material> overrides )
	{
		ObjectDisposedException.ThrowIf( disposed, this ); ThreadSafe.AssertIsMainThread();
		ArgumentNullException.ThrowIfNull( overrides );
		if ( overrides.Count != materialOverrides.Length ) throw new ArgumentException( "Override vector does not match material slots.", nameof( overrides ) );
		var candidate = overrides.ToArray();
		foreach ( var material in candidate ) ValidateMaterial( material );
		ChangeOverrides( values => Array.Copy( candidate, values, values.Length ), candidate.Any( material => material is not null ) ? () => EnsureRawOverrideOutputs() : null );
	}
	private void ChangeOverrides( Action<Material[]> change, Action prepare = null ) => submissionGate.Run( () => ChangeOverridesExclusive( change, prepare ) );

	private void ChangeOverridesExclusive( Action<Material[]> change, Action prepare )
	{
		var before = (Material[])materialOverrides.Clone();
		var candidate = (Material[])before.Clone(); change( candidate );
		PreflightClassification( variantState?.Current, candidate );
		prepare?.Invoke();
		try { Array.Copy( candidate, materialOverrides, candidate.Length ); RefreshMaterialClassification(); }
		catch ( Exception error )
		{
			Array.Copy( before, materialOverrides, before.Length );
			GltfGpuCleanup.Rollback( error, [RefreshMaterialClassification] ); throw;
		}
	}
	private void RefreshMaterialClassification() => submissionGate.Run( RefreshMaterialClassificationExclusive );

	private void RefreshMaterialClassificationExclusive()
	{
		var selected = variantState?.Current;
		var states = draws.Select( draw =>
		{
			var primitive = draw.Node.Primitive;
			var material = materialOverrides[primitive.MaterialSlot] ?? selected?.Bindings[draw.VariantRow].Value.Material ?? primitive.Material;
			return (Draw: draw, Material: material, Translucent: material.Flags.IsTranslucent, AlphaTest: material.Flags.IsAlphaTest,
				Raw: asset.HasRawVariantRoute( primitive ) && materialOverrides[primitive.MaterialSlot] is not null);
		} ).ToArray();
		var created = new List<(Draw Draw, SceneCustomObject Object)>();
		try
		{
			// Allocate all required sort units before publishing any class change.
			foreach ( var state in states )
			{
				if ( !state.Translucent || state.Draw.SortObject.IsValid() ) continue;
				if ( Graphics.IsActive ) throw new GltfException( "Transparent sort objects cannot be created during rendering." );
				var draw = state.Draw; var child = new SceneCustomObject( world ) { RenderingEnabled = false };
				created.Add( (draw, child) ); child.Bounds = draw.WorldBounds; child.RenderOverride = _ => Render( draw );
			}
			foreach ( var item in created ) item.Draw.SortObject = item.Object;
			var changed = false;
			foreach ( var state in states )
			{
				var draw = state.Draw;
				if ( ReferenceEquals( draw.EffectiveMaterial, state.Material ) && draw.IsTranslucent == state.Translucent && draw.IsAlphaTest == state.AlphaTest && draw.LastRawRoute == state.Raw ) continue;
				draw.EffectiveMaterial = state.Material; draw.IsTranslucent = state.Translucent; draw.IsAlphaTest = state.AlphaTest; draw.LastRawRoute = state.Raw;
				draw.SubmittedRevision = -1; changed = true;
			}
			var opaque = states.Any( x => !x.Translucent ); var translucent = states.Any( x => x.Translucent );
			var alphaTest = states.Any( x => !x.Translucent && x.AlphaTest );
			var wantsFrameBufferCopy = states.Any( x => x.Translucent && x.Material.Flags.GetInt( "bWantsFBCopyTexture" ) != 0 );
			automaticFrameBufferCopy = wantsFrameBufferCopy;
			ApplyFrameBufferCopyRequirement();
			if ( sceneObject.Flags.IsOpaque != opaque ) sceneObject.Flags.IsOpaque = opaque;
			if ( sceneObject.Flags.IsTranslucent != translucent ) sceneObject.Flags.IsTranslucent = translucent;
			if ( sceneObject.Flags.HasFlag( SceneObjectFlags.AlphaTestZPrepass ) != alphaTest ) sceneObject.Flags.SetFlag( SceneObjectFlags.AlphaTestZPrepass, alphaTest );
			SyncSortObjects();
			if ( changed ) SubmittedRevision = -1;
		}
		catch
		{
			foreach ( var item in created ) { if ( ReferenceEquals( item.Draw.SortObject, item.Object ) ) item.Draw.SortObject = null; if ( item.Object.IsValid() ) { item.Object.RenderOverride = null; item.Object.Delete(); } }
			throw;
		}
	}

	private void ApplyFrameBufferCopyRequirement()
	{
		var value = explicitFrameBufferCopy || automaticFrameBufferCopy;
		if ( sceneObject.Flags.WantsFrameBufferCopy != value )
			sceneObject.Flags.SetFlag( SceneObjectFlags.WantsFrameBufferCopyTexture, value );
	}

	private void RequestFrameBufferCopy( bool value ) => submissionGate.Run( () =>
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		var before = explicitFrameBufferCopy; explicitFrameBufferCopy = value;
		try { ApplyFrameBufferCopyRequirement(); }
		catch ( Exception error ) { explicitFrameBufferCopy = before; GltfGpuCleanup.Rollback( error, [ApplyFrameBufferCopyRequirement] ); throw; }
	} );

	// Mirror public primary presentation before native collection. Attributes are
	// merged live from the primary in the callback; in-place edits need no copy.
	private void OnPrimaryStateChanged( SceneObjectChange change )
	{
		if ( (change & SceneObjectChange.Removed) != 0 )
		{
			if ( ThreadSafe.IsMainThread ) Dispose(); else MainThread.Queue( () => Dispose() );
			return;
		}
		if ( (change & (SceneObjectChange.Flags | SceneObjectChange.Visibility | SceneObjectChange.Tint | SceneObjectChange.Tags | SceneObjectChange.Settings)) == 0 ) return;
		submissionGate.Run( () => { if ( !disposed ) SyncSortObjects(); } );
	}

	internal void SyncSortObjects()
	{
		if ( !sceneObject.IsValid() ) return;
		const SceneObjectFlags mask = SceneObjectFlags.IsDecal | SceneObjectFlags.ViewModelLayer | SceneObjectFlags.Skybox3DLayer |
			SceneObjectFlags.DisabledInLowQuality | SceneObjectFlags.EffectsBloomLayer | SceneObjectFlags.GameOverlayLayer | SceneObjectFlags.ExcludeGameLayer |
			SceneObjectFlags.UIOverlayLayer | SceneObjectFlags.ToolsUnlitLayer | SceneObjectFlags.ToolSceneOverlayLayer |
			SceneObjectFlags.HammerPrefabStencilLayer | SceneObjectFlags.HammerSelectionStencilLayer | SceneObjectFlags.HammerEnabledStencilLayer |
			SceneObjectFlags.WantsFrameBufferCopyTexture | SceneObjectFlags.StaticObject | SceneObjectFlags.EnvironmentMapped |
			SceneObjectFlags.DoesNotAcceptDecals | SceneObjectFlags.NoZPrepass | SceneObjectFlags.ForwardLayerOnly |
			SceneObjectFlags.NoOcclusionCulling | SceneObjectFlags.NoPVSCulling | SceneObjectFlags.HideInCubemaps |
			SceneObjectFlags.ExecuteBefore | SceneObjectFlags.ExecuteAfter | SceneObjectFlags.NeedsLightProbe;
		var flags = sceneObject.native.GetFlags() & mask;
		var batchTranslucent = (flags & (SceneObjectFlags.ExecuteBefore | SceneObjectFlags.ExecuteAfter)) != 0;
		foreach ( var draw in draws )
		{
			var child = draw.SortObject; if ( !child.IsValid() ) continue;
			child.native.ChangeFlags( flags, mask ); child.NotifyChanged( SceneObjectChange.Flags );
			child.Flags.IsOpaque = false; child.Flags.IsTranslucent = true;
			child.Flags.CastShadows = false; child.ColorTint = sceneObject.ColorTint;
			child.Tags.SetFrom( sceneObject.Tags ); child.CallbackTarget = sceneObject.CallbackTarget; child.Component = sceneObject.Component;
			child.RenderingEnabled = sceneObject.RenderingEnabled && !batchTranslucent && draw.IsTranslucent && nodeVisibility[draw.Node.Index];
		}
	}

	private static void ValidateMaterial( Material material )
	{
		if ( material is not null && (!material.IsValid || material.IsError) ) throw new GltfException( "Material override is invalid." );
	}

	internal GltfGpuInstance( GltfGpuAsset asset, SceneWorld world, bool initiallyVisible = true )
	{
		this.asset = asset; this.world = world; ownerContext = GlobalContext.Current;
		nodeVisibility = asset.Document.EvaluateNodeVisibility();
		materialOverrides = new Material[asset.MaterialSlotSourceIndices.Count];
		try
		{
			placementBuffer = GltfGpuAsset.Upload( new[] { new GltfGpuMatrix( NMatrix.Identity ) }, "glTF instance placement" );
			foreach ( var node in asset.Nodes )
			{
				var draw = new Draw { Node = node, Rig = node.Rig, VariantRow = draws.Count }; draws.Add( draw );
				var plan = node.Primitive.Plan;
				draw.Output = new GpuBuffer( plan.SourceVertices.Length, plan.VertexStride, GpuBuffer.UsageFlags.ByteAddress | GpuBuffer.UsageFlags.Vertex, "glTF instance output" );
				draw.Palette = GltfGpuAsset.Upload( new GltfGpuMatrix[Math.Max( 1, draw.Rig.JointCount )], "glTF instance affine palette" );
				draw.Weights = GltfGpuAsset.Upload( new float[Math.Max( 1, plan.MorphTargetCount )], "glTF instance morph weights" );
				draw.Indices = new GpuBuffer( plan.Indices.Length, 4, GpuBuffer.UsageFlags.Index, "glTF instance winding indices" ); draw.Indices.SetData<uint>( plan.Indices );
				var attributes = draw.Attributes; var primitive = node.Primitive;
				attributes.Set( "InputBytes", primitive.Input ); attributes.Set( "InputDescriptors", primitive.InputDescriptors );
				attributes.Set( "OutputDescriptors", primitive.OutputDescriptors ); attributes.Set( "SourceVertices", primitive.SourceVertices );
				attributes.Set( "OutputBytes", draw.Output ); attributes.Set( "Palette", draw.Palette ); attributes.Set( "MorphWeights", draw.Weights );
				attributes.Set( "Placement", placementBuffer );
				attributes.Set( "VertexCount", plan.SourceVertices.Length ); attributes.Set( "VertexStride", plan.VertexStride );
				attributes.Set( "InputCount", plan.InputDescriptors.Length ); attributes.Set( "BaseAttributeCount", plan.BaseAttributeCount );
				attributes.Set( "OutputCount", plan.OutputDescriptors.Length ); attributes.Set( "JointCount", draw.Rig.JointCount );
				attributes.Set( "GenerateFlatNormals", plan.GenerateFlatNormals ? 1 : 0 );
			}
			variantState = asset.CreateVariantState();
			sceneObject = new SceneCustomObject( world ) { RenderingEnabled = false };
			sceneObject.DependentStateChanged += OnPrimaryStateChanged; sceneObject.ManualRenderScope = RenderManually; sceneObject.FrameBufferCopyRequest = RequestFrameBufferCopy; RefreshMaterialClassification();
			sceneObject.RenderOverride = _ => Render();
			SetPose( asset.Document.EvaluatePose() );
			sceneObject.RenderingEnabled = initiallyVisible;
		}
		catch ( Exception error )
		{
			GltfGpuCleanup.Rollback( error, draws.Select<Draw, Action>( x => x.Dispose ).Concat( new Action[]
			{
				() => variantState?.Dispose(), () => placementBuffer?.Dispose(), () => { if ( sceneObject.IsValid() ) { sceneObject.DependentStateChanged -= OnPrimaryStateChanged; sceneObject.ManualRenderScope = null; sceneObject.FrameBufferCopyRequest = null; sceneObject.RenderOverride = null; sceneObject.Delete(); } }
			} ) ); throw;
		}
	}

	/// <summary>Publish a complete inherited visibility evaluation for this instance.</summary>
	internal void SetNodeVisibilityOverrides( IReadOnlyDictionary<int, bool> overrides )
	{
		ObjectDisposedException.ThrowIf( disposed, this ); ThreadSafe.AssertIsMainThread();
		submissionGate.Run( () => SetNodeVisibilityOverridesExclusive( overrides ) );
	}

	private void SetNodeVisibilityOverridesExclusive( IReadOnlyDictionary<int, bool> overrides )
	{
		var next = asset.Document.EvaluateNodeVisibility( overrides );
		var changed = false;
		foreach ( var draw in draws )
		{
			var wasVisible = nodeVisibility[draw.Node.Index]; var isVisible = next[draw.Node.Index];
			if ( wasVisible == isVisible ) continue;
			// A reveal must dispatch and submit the current pose even if the pose
			// clock did not change while the row was hidden.
			if ( isVisible ) { draw.ComputedRevision = -1; draw.RawComputedRevision = -1; }
			draw.SubmittedRevision = -1; changed = true;
		}
		nodeVisibility = next;
		if ( changed ) { SubmittedRevision = -1; SyncSortObjects(); }
	}

	public void SetPose( GltfPose value )
	{
		ObjectDisposedException.ThrowIf( disposed, this ); ArgumentNullException.ThrowIfNull( value ); ApplyPose( value, placement );
	}

	private void ApplyPose( GltfPose value, NMatrix nextPlacement )
	{
		ThreadSafe.AssertIsMainThread();
		submissionGate.Run( () => ApplyPoseExclusive( value, nextPlacement ) );
	}

	private void ApplyPoseExclusive( GltfPose value, NMatrix nextPlacement )
	{
		asset.Document.ValidatePoseOwnership( value );
		// Preflight all rows before publishing a new revision or touching GPU state.
		var updates = draws.Select( draw => (Draw: draw, Matrices: draw.Rig.Evaluate( value, draw.Node.LocalTransform ), Weights: value.GetMorphWeights( draw.Node.Index ), Reversed: draw.Rig.ReverseWinding( value, nextPlacement, draw.Node.LocalTransform )) ).ToArray();
		foreach ( var update in updates )
			if ( update.Weights.Length != update.Draw.Node.Primitive.Plan.MorphTargetCount ) throw new GltfException( "Morph weight count does not match GPU target streams." );
		// Keep one conservative posed bound per draw for view-local transparent sorting.
		// Compute before publishing the pose so invalid bounds cannot partially update it.
		var drawBounds = updates.Select( x => GltfGpuRig.Bounds( [(x.Draw.Rig, x.Matrices, x.Weights)], nextPlacement ) ).ToArray();
		var bounds = drawBounds.Length == 0 ? new BBox( global::Vector3.Zero, global::Vector3.Zero ) : BBox.FromBoxes( drawBounds );
		var animated = value.HasAnimatedBaseColorFactors || hasAnimatedMaterials ? PrepareAnimatedMaterials( value, variantState?.Current ) : null;
		var colorsApplied = false;
		try
		{
			if ( animated is not null )
			{
				ApplyAnimatedMaterialValues( animated );
				colorsApplied = true;
			}
			placementBuffer.SetData<GltfGpuMatrix>( new[] { new GltfGpuMatrix( nextPlacement ) } );
			for ( var updateIndex = 0; updateIndex < updates.Length; updateIndex++ )
			{
				var update = updates[updateIndex];
				update.Draw.Palette.SetData<GltfGpuMatrix>( update.Matrices );
				if ( update.Weights.Length != 0 ) update.Draw.Weights.SetData<float>( update.Weights );
				if ( update.Draw.Reversed != update.Reversed )
				{
					var indices = (uint[])update.Draw.Node.Primitive.Plan.Indices.Clone();
					if ( update.Reversed && update.Draw.Node.Primitive.Plan.Mode == 4 )
						for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
					update.Draw.Indices.SetData<uint>( indices ); update.Draw.Reversed = update.Reversed;
				}
			}
			for ( var index = 0; index < updates.Length; index++ )
			{
				updates[index].Draw.WorldBounds = drawBounds[index];
				if ( updates[index].Draw.SortObject.IsValid() ) updates[index].Draw.SortObject.Bounds = drawBounds[index];
			}
			if ( animated is not null )
			{
				PublishAnimatedMaterials( animated );
				animated = null;
			}
		}
		catch ( Exception cause ) when ( colorsApplied )
		{
			GltfGpuCleanup.Rollback( cause, [() => RollbackAnimatedMaterialValues( animated )] ); throw;
		}
		finally { DestroyStagedAnimatedMaterials( animated ); }
		pose = value; placement = nextPlacement; PoseRevision++;
		// Empty instances have no required GPU rows; readiness is vacuous,
		// and DrawCalls stays zero rather than fabricating a submission.
		if ( draws.Count == 0 ) SubmittedRevision = PoseRevision;
		// Keep per-instance conservative full-affine bounds. This avoids a GPU
		// readback in the animation loop; exact bounds are tested on diagnostics.
		sceneObject.Bounds = bounds;
	}

	private IEnumerable<Draw> DrawsForCurrentView()
	{
		// Sort for this actual view without mutating the shared source order. Native
		// translucent objects use nearest-AABB distance, truncated to hundredths;
		// OrderByDescending is stable for equal keys and preserves draw ordinal.
		var layer = Graphics.LayerType;
		if ( layer is SceneLayerType.Translucent or SceneLayerType.EffectsTranslucent )
		{
			var camera = Graphics.CameraPosition;
			return draws.Where( x => x.IsTranslucent ).OrderByDescending( x => TranslucentDistance( x.WorldBounds, camera ) );
		}
		if ( layer is SceneLayerType.Opaque or SceneLayerType.DepthPrepass or SceneLayerType.EffectsOpaque or SceneLayerType.Shadow )
			return draws.Where( x => !x.IsTranslucent );
		// Preserve explicit/manual submission contexts with no native layer.
		// These do not establish native alpha pass or shadow conformance.
		return draws;
	}

	private static uint TranslucentDistance( BBox bounds, global::Vector3 camera )
	{
		var offset = global::Vector3.Max( (camera - bounds.Center).Abs() - bounds.Extents, global::Vector3.Zero );
		var distance = MathF.Sqrt( offset.LengthSquared );
		return (uint)Math.Clamp( 100.0f * distance, 0, 4294967040.0f );
	}

	internal void RenderManually( Transform? transform, Color? color, Material material )
	{
		// The current output has world placement baked in. Do not silently ignore
		// caller overrides until a temporary placement/tint path is implemented.
		if ( transform.HasValue || color.HasValue )
			throw new GltfException( "Manual glTF transform/color overrides are not yet supported." );
		submissionGate.Run( () =>
		{
			if ( material is not null && draws.Any( draw => asset.HasRawVariantRoute( draw.Node.Primitive ) && draw.RawOutput is null ) )
				throw new GltfException( "Manual material override requires a prepared raw route; assign the override before entering a render block." );
			var previous = manualRendering; var previousMaterial = manualMaterial;
			manualRendering = true; manualMaterial = material;
			try { RenderExclusive( null ); } finally { manualRendering = previous; manualMaterial = previousMaterial; }
		} );
	}

	private void Render( Draw only = null )
	{
		// A native frame can submit the same custom object concurrently for
		// different views. Draw attributes, compute revisions and counters are
		// mutable per instance, so keep the whole callback in one CPU boundary.
		submissionGate.Run( () => RenderExclusive( only ) );
	}

	private void RenderExclusive( Draw only )
	{
		// A callback already queued by native can enter after Dispose detached
		// RenderOverride. Check under the same gate that protects retirement.
		if ( disposed ) return;
		try
		{
			if ( !Graphics.IsActive ) throw new InvalidOperationException( "Direct GPU draw callback has no graphics context." );
			var selected = variantState?.Current;
			var overrides = (Material[])materialOverrides.Clone();
			var layer = Graphics.LayerType;
			// Native command-list callbacks bracket one contiguous native batch.
			// Retain the prior one-object path when callbacks are installed.
			var batchTranslucent = sceneObject.Flags.WantsExecuteBefore || sceneObject.Flags.WantsExecuteAfter;
			// A child callback may already be queued when a state transition hides it.
			if ( only is not null && (batchTranslucent || !sceneObject.RenderingEnabled || !only.IsTranslucent) ) return;
			// Sort the actual submission sequence for this view. Classification is
			// still resolved below from the captured effective variant/override,
			// rather than trusting cached flags during a material transition.
			IEnumerable<Draw> submission = draws;
			if ( manualRendering )
			{
				var camera = Graphics.CameraPosition;
				bool IsManualTranslucent( Draw draw )
				{
					var primitive = draw.Node.Primitive;
					return (manualMaterial ?? overrides[primitive.MaterialSlot] ?? selected?.Bindings[draw.VariantRow].Value.Material ?? primitive.Material).Flags.IsTranslucent;
				}
				submission = draws.OrderBy( x => IsManualTranslucent( x ) ? 1 : 0 )
					.ThenByDescending( x => IsManualTranslucent( x ) ? TranslucentDistance( x.WorldBounds, camera ) : 0 );
			}
			else if ( layer is SceneLayerType.Translucent or SceneLayerType.EffectsTranslucent )
			{
				var camera = Graphics.CameraPosition;
				submission = draws.OrderByDescending( x => TranslucentDistance( x.WorldBounds, camera ) );
			}
			foreach ( var draw in submission )
			{
				if ( only is not null && !ReferenceEquals( only, draw ) ) continue;
				if ( !nodeVisibility[draw.Node.Index] ) continue;
				var primitive = draw.Node.Primitive;
				var packet = selected?.Bindings[draw.VariantRow].Value;
				var overridden = manualRendering && manualMaterial is not null ? manualMaterial : overrides[primitive.MaterialSlot];
				var material = overridden ?? draw.AnimatedMaterial ?? packet?.Material ?? primitive.Material;
				var translucent = material.Flags.IsTranslucent;
				if ( only is not null && (!translucent || layer is not (SceneLayerType.Translucent or SceneLayerType.EffectsTranslucent)) ) continue;
				if ( only is null && !manualRendering && !batchTranslucent && translucent && layer is (SceneLayerType.Translucent or SceneLayerType.EffectsTranslucent) ) continue;
				if ( (layer is SceneLayerType.Translucent or SceneLayerType.EffectsTranslucent) && !translucent && !manualRendering ) continue;
				if ( (layer is SceneLayerType.Opaque or SceneLayerType.DepthPrepass or SceneLayerType.EffectsOpaque or SceneLayerType.Shadow) && translucent && !manualRendering ) continue;
				var raw = overridden is not null && asset.HasRawVariantRoute( primitive );
				var plan = raw ? asset.GetRawVariantPlan( primitive ) : packet?.Plan ?? primitive.Plan;
				var output = raw ? draw.RawOutput : draw.Output;
				var descriptors = raw ? primitive.RawOutputDescriptors : packet?.OutputDescriptors ?? primitive.OutputDescriptors;
				var layout = raw ? primitive.RawLayout : primitive.Layout;
				var computed = raw ? draw.RawComputedRevision : draw.ComputedRevision;
				if ( computed != PoseRevision || (!raw && !ReferenceEquals( draw.ComputedPacket, packet )) )
				{
					if ( output is null || descriptors is null ) throw new GltfException( "The selected material route has no output resources." );
					draw.Attributes.Set( "InputBytes", raw ? primitive.Input : packet?.InputBytes ?? primitive.Input );
					draw.Attributes.Set( "InputDescriptors", raw ? primitive.InputDescriptors : packet?.InputDescriptors ?? primitive.InputDescriptors );
					draw.Attributes.Set( "InputCount", raw ? plan.InputDescriptors.Length : packet?.InputCount ?? plan.InputDescriptors.Length );
					draw.Attributes.Set( "BaseAttributeCount", raw ? plan.BaseAttributeCount : packet?.BaseAttributeCount ?? plan.BaseAttributeCount );
					draw.Attributes.Set( "OutputBytes", output ); draw.Attributes.Set( "OutputDescriptors", descriptors );
					draw.Attributes.Set( "VertexStride", plan.VertexStride ); draw.Attributes.Set( "OutputCount", plan.OutputDescriptors.Length );
					draw.Attributes.Set( "ReverseWinding", draw.Reversed ? 1 : 0 );
					var hasVertexState = raw ? draw.RawHasVertexState : draw.HasVertexState;
					Graphics.ResourceBarrierTransition( output, hasVertexState ? ResourceState.VertexOrIndexBuffer : ResourceState.GenericRead, ResourceState.UnorderedAccess );
					RenderTools.Compute( Graphics.Context, draw.Attributes.Get(), asset.ComputeMaterial.native.GetMode(), plan.SourceVertices.Length, 1, 1 );
					Graphics.ResourceBarrierTransition( output, ResourceState.UnorderedAccess, ResourceState.VertexOrIndexBuffer );
					if ( raw ) { draw.RawHasVertexState = true; draw.RawComputedRevision = PoseRevision; }
					else { draw.HasVertexState = true; draw.ComputedRevision = PoseRevision; draw.ComputedPacket = packet; }
				}
				draw.RenderAttributes.Clear(); material.Attributes.MergeTo( draw.RenderAttributes );
				if ( only is not null ) sceneObject.CreatedAttributes?.MergeTo( draw.RenderAttributes );
				Graphics.Attributes.MergeTo( draw.RenderAttributes );
				if ( overridden is null && (material.ShaderName.Equals( "shaders/gltf_stock_sampler.shader", StringComparison.OrdinalIgnoreCase ) || material.ShaderName.Equals( "shaders/gltf_surface_five_uv.shader", StringComparison.OrdinalIgnoreCase ) || material.ShaderName.Equals( "shaders/gltf_diffuse_transmission.shader", StringComparison.OrdinalIgnoreCase )) )
					draw.RenderAttributes.Set( "GltfPointLineNoNormal", plan.Mode is 0 or 1 && !plan.Upload.Attributes.ContainsKey( "NORMAL" ) ? 1f : 0f );
				if ( !RenderTools.SetRenderState( Graphics.Context, draw.RenderAttributes.Get(), Graphics.ModeFor( material ), layout, Graphics.Stats ) ) throw new GltfException( "Stock material rejected the runtime GPU vertex declaration." );
				Graphics.Context.BindVertexBuffer( 0, output.native, 0, plan.VertexStride );
				Graphics.Context.BindIndexBuffer( draw.Indices.native, 4, 0 );
				var mode = plan.Mode == 0 ? Graphics.PrimitiveType.Points : plan.Mode == 1 ? Graphics.PrimitiveType.Lines : Graphics.PrimitiveType.Triangles;
				Graphics.Context.DrawIndexed( (RenderPrimitiveType)mode, 0, plan.Indices.Length, 0, 0 ); DrawCalls++;
				draw.DrawCalls++; draw.SubmittedPrimitiveType = mode; draw.SubmittedRevision = PoseRevision;
				draw.SubmittedMaterialIndex = selected?.Bindings[draw.VariantRow].MaterialIndex ?? primitive.Plan.Material;
				draw.SubmittedMaterial = material; draw.SubmittedMaterialFrame = Application.FrameCount;
				draw.SubmittedVariantRevision = selected?.Revision; draw.SubmittedMaterialOverride = overridden is not null;
			}
			// Compute success or a partial pass must not claim every row submitted.
			if ( draws.Where( x => nodeVisibility[x.Node.Index] ).All( x => x.SubmittedRevision == PoseRevision ) ) SubmittedRevision = PoseRevision;
		}
		catch ( Exception exception ) { if ( errors.Count < 16 ) errors.Enqueue( exception.ToString() ); }
	}

	public IReadOnlyList<GltfGpuPrimitiveSnapshot> Readback()
	{
		ObjectDisposedException.ThrowIf( disposed, this ); ThreadSafe.AssertIsMainThread();
		if ( SubmittedRevision != PoseRevision || !errors.IsEmpty ) throw new InvalidOperationException( "Current pose has not completed a successful GPU draw submission: " + string.Join( "\n", errors ) );
		var rows = new List<GltfGpuPrimitiveSnapshot>();
		var selected = variantState?.Current;
		foreach ( var draw in draws )
		{
			var primitive = draw.Node.Primitive;
			var packet = selected?.Bindings[draw.VariantRow].Value;
			var raw = materialOverrides[primitive.MaterialSlot] is not null && asset.HasRawVariantRoute( primitive );
			var plan = raw ? asset.GetRawVariantPlan( primitive ) : packet?.Plan ?? primitive.Plan;
			var output = raw ? draw.RawOutput : draw.Output; var visible = nodeVisibility[draw.Node.Index];
			byte[] bytes = null; uint[] indices = null;
			if ( visible )
			{
				bytes = new byte[checked(plan.SourceVertices.Length * plan.VertexStride)]; output.GetData<byte>( bytes );
				indices = new uint[plan.Indices.Length]; draw.Indices.GetData<uint>( indices );
			}
			rows.Add( new GltfGpuPrimitiveSnapshot { Node = draw.Node.Index, InstanceIndex = draw.Node.InstanceIndex, Mesh = draw.Node.Primitive.Mesh, Primitive = draw.Node.Primitive.Index,
				VertexCount = plan.SourceVertices.Length, VertexStride = plan.VertexStride, Fields = plan.Fields, Bytes = bytes,
				SourceVertices = (uint[])plan.SourceVertices.Clone(), Indices = indices, IndexDataOrigin = visible ? "gpu-readback" : null,
				SourceMode = plan.SourceMode, SubmittedPrimitiveType = draw.SubmittedPrimitiveType, DrawCalls = draw.DrawCalls,
				Visible = visible, ComputedRevision = raw ? draw.RawComputedRevision : draw.ComputedRevision, SubmittedRevision = draw.SubmittedRevision,
				SubmittedMaterialIndex = draw.SubmittedMaterialIndex, SubmittedMaterial = draw.SubmittedMaterial,
				SubmittedMaterialFrame = draw.SubmittedMaterialFrame, SubmittedVariantRevision = draw.SubmittedVariantRevision,
				SubmittedMaterialOverride = draw.SubmittedMaterialOverride } );
		}
		return rows.AsReadOnly();
	}

	public void Dispose()
	{
		if ( disposed ) return; ThreadSafe.AssertIsMainThread();
		submissionGate.Run( () =>
		{
			if ( disposed ) return;
			disposed = true;
			// Prevent new callbacks immediately. A previously captured callback
			// waits for this gate and observes disposed before touching any draw.
			if ( sceneObject is not null ) { sceneObject.DependentStateChanged -= OnPrimaryStateChanged; sceneObject.ManualRenderScope = null; sceneObject.FrameBufferCopyRequest = null; sceneObject.RenderOverride = null; }
			foreach ( var draw in draws ) if ( draw.SortObject.IsValid() ) draw.SortObject.RenderOverride = null;
			void Release()
			{
				submissionGate.Run( () =>
				{
					using var scope = new GlobalContext.GlobalContextScope( ownerContext );
					try
					{
						GltfGpuCleanup.ReleaseAll( new Action[] { () => { if ( sceneObject.IsValid() ) sceneObject.Delete(); } }
							.Concat( draws.Select<Draw, Action>( x => x.Dispose ) ).Concat( new Action[] { () => variantState?.Dispose(), placementBuffer.Dispose, asset.ReleaseInstance } ) );
					}
					finally
					{
						draws.Clear(); pose = null;
						hasAnimatedMaterials = false; opaqueMaterials = null; materialColorParameters = null; AnimatedMaterialWriter = null;
					}
				} );
			}
			// Preserve the existing native GPU retirement path. The CPU gate
			// prevents concurrent access; it does not wait for GPU execution.
			if ( Graphics.IsActive || hasAnimatedMaterials ) EngineLoop.DisposeAtFrameEnd( Sandbox.Utility.DisposeAction.Create( Release ) ); else Release();
		} );
	}
}
