using System.Runtime.InteropServices;
using Sandbox.Engine;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

/// <summary>
/// Builds a native <see cref="Model"/> from the native data of a compiled glTF. It passes the stored blobs to the
/// engine as they are: compiled textures to the texture loader, vertex and index bytes to the mesh. Material recipes
/// become shared materials. The model is an ordinary native model: the scene system culls, batches and draws it.
/// </summary>
internal static class GltfNativeModelBuilder
{
	/// <summary>Milliseconds of the last build per phase: texture decode, texture load, materials, meshes, model.</summary>
	internal static (double Decode, double Textures, double Materials, double Meshes, double Model) LastTimings;

	/// <summary>The whole pack as one model, with its skeleton, clips, LOD levels and material groups.</summary>
	internal static Model Build( GltfNativePack pack, string name )
	{
		ArgumentNullException.ThrowIfNull( pack );
		RequireBuildThread();
		var (materials, leases) = RealizeMaterials( pack );
		try
		{
			var clock = System.Diagnostics.Stopwatch.StartNew();
			var model = CreateModel( pack, pack.Header.Meshes, materials, true, name, out var meshes, out var meshMs, pack.Header.LodDistances );
			LastTimings = (LastTimings.Decode, LastTimings.Textures, LastTimings.Materials, meshMs, clock.Elapsed.TotalMilliseconds - meshMs);
			model.SetGltfLifetime( GlobalContext.Current, leases.ToList(), meshes );
			model.GltfNativeVariants = Array.AsReadOnly( pack.Header.Variants );
			return model;
		}
		catch ( Exception original )
		{
			GltfGpuCleanup.Rollback( original, leases.Select<GltfMaterialLease<Material>, Action>( x => x.Dispose ) );
			throw;
		}
	}

	/// <summary>
	/// One static model per node, for previews that place each node themselves. The textures and materials are
	/// realized once and shared; each model holds them until it is destroyed.
	/// </summary>
	internal static Dictionary<int, Model> BuildPerNode( GltfNativePack pack, Func<int, string> name )
	{
		ArgumentNullException.ThrowIfNull( pack );
		RequireBuildThread();
		var (materials, leases) = RealizeMaterials( pack );
		var users = new int[leases.Length];
		var result = new Dictionary<int, Model>();
		try
		{
			foreach ( var group in pack.Header.Meshes.GroupBy( x => x.Node ) )
			{
				var entries = group.ToArray();
				var model = CreateModel( pack, entries, materials, false, name( group.Key ), out var meshes, out _ );
				var used = entries.Select( x => x.Material ).Concat( entries.SelectMany( x => x.VariantMaterials ) ).Distinct().ToArray();
				foreach ( var material in used ) users[material]++;
				var shared = used.Select( material => new GltfMaterialLease<Material>( materials[material], () => Release( material ), () => ThreadSafe.AssertIsMainThread() ) ).ToList();
				model.SetGltfLifetime( GlobalContext.Current, shared, meshes );
				model.GltfNativeVariants = Array.AsReadOnly( pack.Header.Variants );
				result.Add( group.Key, model );
			}
		}
		catch ( Exception original )
		{
			GltfGpuCleanup.Rollback( original, result.Values.Select<Model, Action>( x => x.Destroy ).Concat( leases.Select<GltfMaterialLease<Material>, Action>( x => x.Dispose ) ) );
			throw;
		}
		// A material that no model uses is released now; the others when their last model goes.
		for ( var material = 0; material < leases.Length; material++ ) if ( users[material] == 0 ) leases[material].Dispose();
		return result;

		void Release( int material ) { if ( --users[material] == 0 ) leases[material].Dispose(); }
	}

	/// <summary>
	/// A static scene: one shared native model per geometry, and a scene model that lists where each node places one.
	/// A <see cref="ModelRenderer"/> draws each node as its own scene object, so pieces cull and switch LOD on their own.
	/// The scene model holds the materials; destroying it destroys the geometry models.
	/// </summary>
	internal static Model BuildScene( GltfNativePack pack, string name )
	{
		ArgumentNullException.ThrowIfNull( pack );
		RequireBuildThread();
		var header = pack.Header;
		var (materials, leases) = RealizeMaterials( pack );
		var geometries = new List<Model>();
		var meshes = new List<Mesh>();
		try
		{
			for ( var i = 0; i < header.Geometries.Length; i++ )
			{
				var geometry = header.Geometries[i];
				var model = CreateModel( pack, geometry.Meshes.Select( x => header.Meshes[x] ).ToArray(), materials, false, $"{name}#{geometry.Name}", out var geometryMeshes, out _,
					geometry.LodDistances, geometry.Groups );
				geometries.Add( model ); meshes.AddRange( geometryMeshes );
			}
			var nodes = header.Nodes.Select( x => new GltfNativeScene.Node( x.Node, x.Geometry,
				new Transform( new Vector3( x.Position[0], x.Position[1], x.Position[2] ), new Rotation( x.Rotation[0], x.Rotation[1], x.Rotation[2], x.Rotation[3] ), new Vector3( x.Scale[0], x.Scale[1], x.Scale[2] ) ),
				header.Geometries[x.Geometry].Groups[x.Group].Name, x.VariantGroups.Select( g => header.Geometries[x.Geometry].Groups[g].Name ).ToArray() ) ).ToArray();
			var boxes = nodes.Select( x => geometries[x.Geometry].RenderBounds.Transform( x.Transform ) ).ToArray();
			var bounds = boxes.Length == 0 ? new BBox( Vector3.Zero, Vector3.Zero ) : BBox.FromBoxes( boxes );
			var scene = Model.Builder.WithName( name ).WithViewBounds( bounds ).Create();
			scene.SetGltfLifetime( GlobalContext.Current, leases.ToList(), meshes );
			scene.GltfNativeVariants = Array.AsReadOnly( header.Variants );
			scene.GltfNativeScene = new GltfNativeScene( geometries.ToArray(), nodes, bounds, materials.Distinct().ToArray() );
			return scene;
		}
		catch ( Exception original )
		{
			GltfGpuCleanup.Rollback( original, geometries.Select<Model, Action>( x => x.Destroy ).Concat( leases.Select<GltfMaterialLease<Material>, Action>( x => x.Dispose ) ) );
			throw;
		}
	}

	private static void RequireBuildThread()
	{
		ThreadSafe.AssertIsMainThread();
		if ( Graphics.IsActive ) throw new GltfException( "glTF models cannot be built during rendering." );
	}

	/// <summary>Decode and load the textures in one batch, then acquire one shared material per recipe.</summary>
	private static (Material[] Materials, GltfMaterialLease<Material>[] Leases) RealizeMaterials( GltfNativePack pack )
	{
		var header = pack.Header;
		var leases = new List<GltfMaterialLease<Material>>();
		try
		{
			var clock = System.Diagnostics.Stopwatch.StartNew();
			double Lap() { var ms = clock.Elapsed.TotalMilliseconds; clock.Restart(); return ms; }
			// Decoding is the only CPU work here; the textures are independent, so decode them together.
			var textures = new byte[header.Textures.Length][];
			System.Threading.Tasks.Parallel.For( 0, textures.Length, i => textures[i] = pack.Get( header.Textures[i] ).ToArray() );
			var decode = Lap();
			// Load every texture in one batch, so a failed native lookup refreshes the resource database once, not per material.
			var loaded = GltfNativeTextureCache.AcquireBatch( textures );
			var textureMs = Lap();
			var materials = new Material[header.Materials.Length];
			try
			{
				for ( var i = 0; i < materials.Length; i++ )
				{
					var entry = header.Materials[i];
					var recipe = new GltfNativeMaterialRecipe( entry.Shader, entry.TemplateFeatures, entry.Features,
						entry.Vectors.Select( x => new KeyValuePair<string, Vector4>( x.Key, new Vector4( x.Value[0], x.Value[1], x.Value[2], x.Value[3] ) ) ).ToArray(),
						entry.Scalars,
						entry.Textures.Select( x => new GltfNativeTextureSlot( x.Parameter, textures[x.Texture], x.Sampler.ToState(), x.SamplerParameter ) ).ToArray() );
					var lease = recipe.Realize( GltfNativeMaterialTemplates.Get( entry.Shader, entry.TemplateFeatures ) );
					leases.Add( lease );
					materials[i] = lease.Value;
				}
			}
			finally
			{
				// Each material holds its own lease on its textures; the batch lease only kept them loaded until then.
				foreach ( var texture in loaded ) texture.Dispose();
			}
			LastTimings = (decode, textureMs, Lap(), 0, 0);
			return (materials, leases.ToArray());
		}
		catch ( Exception original )
		{
			GltfGpuCleanup.Rollback( original, leases.Select<GltfMaterialLease<Material>, Action>( x => x.Dispose ) );
			throw;
		}
	}

	private static Model CreateModel( GltfNativePack pack, IReadOnlyList<GltfNativePackMesh> entries, Material[] materials, bool skeleton, string name, out List<Mesh> meshes, out double meshMs,
		IReadOnlyList<float> lodDistances = null, IReadOnlyList<GltfNativePackMaterialGroup> groups = null )
	{
		var header = pack.Header;
		var clock = System.Diagnostics.Stopwatch.StartNew();
		meshes = new List<Mesh>();
		var builder = Model.Builder;
		if ( skeleton )
		{
			foreach ( var bone in header.Bones )
				builder.AddBone( bone.Name, new Vector3( bone.Position[0], bone.Position[1], bone.Position[2] ),
					new Rotation( bone.Rotation[0], bone.Rotation[1], bone.Rotation[2], bone.Rotation[3] ), bone.Parent >= 0 ? header.Bones[bone.Parent].Name : null );
			foreach ( var clip in header.Animations )
			{
				var frames = MemoryMarshal.Cast<byte, Transform>( pack.Get( clip.Frames ).Span );
				if ( frames.Length != clip.FrameCount * clip.BoneCount || clip.BoneCount != header.Bones.Length ) throw new GltfException( $"Native glTF clip '{clip.Name}' has the wrong frame data length." );
				var animation = builder.AddAnimation( clip.Name, clip.FrameRate ).WithLooping( clip.Looping );
				for ( var frame = 0; frame < clip.FrameCount; frame++ )
					animation.AddFrame( MemoryMarshal.CreateSpan( ref MemoryMarshal.GetReference( frames.Slice( frame * clip.BoneCount, clip.BoneCount ) ), clip.BoneCount ) );
			}
		}
		var bounds = new List<BBox>();
		foreach ( var entry in entries )
		{
			var primitive = entry.Mode == 0 ? MeshPrimitiveType.Points : entry.Mode == 1 ? MeshPrimitiveType.Lines : MeshPrimitiveType.Triangles;
			var mesh = new Mesh( entry.Name, materials[entry.Material], primitive );
			meshes.Add( mesh );
			var fields = entry.Fields.Select( x => new RawVertexField( x.Semantic, x.Index, x.Offset, x.Format ) ).ToArray();
			mesh.CreateRawVertexBuffer( entry.Stride, entry.VertexCount, pack.Get( entry.Vertices ).Span, fields );
			var indices = MemoryMarshal.Cast<byte, int>( pack.Get( entry.Indices ).Span );
			if ( indices.Length != entry.IndexCount ) throw new GltfException( "A native glTF index blob has the wrong length." );
			// The native call only reads the span; this avoids copying the stored index bytes.
			mesh.CreateIndexBuffer( entry.IndexCount, MemoryMarshal.CreateSpan( ref MemoryMarshal.GetReference( indices ), indices.Length ) );
			var box = new BBox( new Vector3( entry.Min[0], entry.Min[1], entry.Min[2] ), new Vector3( entry.Max[0], entry.Max[1], entry.Max[2] ) );
			mesh.Bounds = box; bounds.Add( box );
			foreach ( var morph in entry.Morphs )
			{
				var deltas = MemoryMarshal.Cast<byte, MorphDelta>( pack.Get( morph.Deltas ).Span );
				if ( deltas.Length != morph.Count ) throw new GltfException( $"Native glTF morph '{morph.Name}' has the wrong delta length." );
				mesh.AddMorph( morph.Name, deltas );
			}
			builder.AddMeshWithLodMask( mesh, lodDistances is { Count: > 1 } ? entry.LodMask : 0xFF );
		}
		groups ??= header.Variants.Length != 0 ? GltfNativeCompiler.MaterialGroups( entries, header.Variants.Length ) : [];
		if ( groups.Count > 1 || header.Variants.Length != 0 )
			foreach ( var group in groups )
			{
				var target = builder.AddMaterialGroup( group.Name );
				foreach ( var material in group.Materials ) target.AddMaterial( materials[material] );
			}
		if ( lodDistances is not null ) for ( var level = 1; level < lodDistances.Count; level++ ) builder.WithLodDistance( level, lodDistances[level] );
		if ( bounds.Count != 0 ) builder.WithViewBounds( BBox.FromBoxes( bounds ) );
		meshMs = clock.Elapsed.TotalMilliseconds;
		return builder.WithName( name ).Create();
	}
}
