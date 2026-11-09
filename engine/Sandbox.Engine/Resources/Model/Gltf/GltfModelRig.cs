using NativeEngine;
using Sandbox.Gltf;
using System.Text.Json;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Sandbox;

/// <summary>
/// Exact glTF palette metadata for the procedural model path. Native bind bones
/// are deliberately identity transforms: glTF inverse binds are applied once,
/// in the evaluated palette, rather than inferred from a possibly different rest pose.
/// </summary>
internal sealed class GltfModelRig
{
	internal sealed record Primitive( int Node, int Mesh, int Index, GltfDrawData Data, GltfVertexSkinning Skinning, int[] Bones, GltfModelMorphs Morphs );
	private readonly record struct PaletteBone( string Name, int Node, NMatrix InverseBind );
	private readonly PaletteBone[] bones;
	private int[] nativeToPalette;
	private int[][] renderToPalette;
	internal int BoneCount => bones.Length;
	internal IReadOnlyList<Primitive> Primitives { get; }
	internal BBox Bounds { get; }
	internal bool HasMorphs => morphBindings.Length != 0;
	private readonly GltfModelMorphs[] morphBindings;

	private GltfModelRig( PaletteBone[] bones, List<Primitive> primitives, BBox bounds )
	{
		this.bones = bones;
		Primitives = primitives.AsReadOnly();
		Bounds = bounds;
		morphBindings = primitives.Where( primitive => primitive.Morphs is not null ).GroupBy( primitive => primitive.Node ).Select( group => group.First().Morphs ).ToArray();
	}

	internal static bool RequiresRig( GltfDocument document ) => document.Root.TryGetProperty( "skins", out var skins ) && skins.GetArrayLength() != 0 ||
		document.Root.TryGetProperty( "animations", out var animations ) && animations.GetArrayLength() != 0 ||
		document.Root.TryGetProperty( "meshes", out var meshes ) && meshes.EnumerateArray().Any( mesh => mesh.GetProperty( "primitives" ).EnumerateArray().Any( primitive => primitive.TryGetProperty( "targets", out var targets ) && targets.GetArrayLength() != 0 ) );

	internal static GltfModelRig Prepare( GltfDocument document )
	{
		ArgumentNullException.ThrowIfNull( document );
		var sceneIndex = document.GetDefaultScene() ?? throw new GltfException( "The asset has no default scene to assemble." );
		var scene = Element( document, "scenes", sceneIndex );
		var selected = new List<int>();
		var seen = new HashSet<int>();
		var pending = new Stack<int>();
		if ( scene.TryGetProperty( "nodes", out var roots ) )
			foreach ( var root in roots.EnumerateArray().Reverse() ) pending.Push( Index( root ) );
		while ( pending.TryPop( out var index ) )
		{
			if ( !seen.Add( index ) ) throw new GltfException( "A selected scene node is referenced more than once." );
			var node = Element( document, "nodes", index );
			selected.Add( index );
			if ( node.TryGetProperty( "children", out var children ) )
				foreach ( var child in children.EnumerateArray().Reverse() ) pending.Push( Index( child ) );
		}
		var rest = document.EvaluatePose();
		var bones = new List<PaletteBone>();
		var skinMaps = new Dictionary<int, int[]>();
		var primitives = new List<Primitive>();
		var min = new NVector( float.PositiveInfinity );
		var max = new NVector( float.NegativeInfinity );
		foreach ( var nodeIndex in selected )
		{
			var node = Element( document, "nodes", nodeIndex );
			if ( !node.TryGetProperty( "mesh", out var meshValue ) ) continue;
			if ( node.TryGetProperty( "extensions", out var extensions ) && extensions.TryGetProperty( "EXT_mesh_gpu_instancing", out _ ) ) throw new GltfException( "The glTF animated model path does not yet support GPU instancing." );
			var meshIndex = Index( meshValue );
			var mesh = Element( document, "meshes", meshIndex );
			var sourcePrimitives = mesh.GetProperty( "primitives" );
			int[] boneMap;
			var skinned = node.TryGetProperty( "skin", out var skinValue );
			if ( skinned )
			{
				var skinIndex = Index( skinValue );
				if ( !skinMaps.TryGetValue( skinIndex, out boneMap ) )
				{
					var skin = document.ReadSkin( skinIndex );
					boneMap = new int[skin.Joints.Length];
					for ( var joint = 0; joint < boneMap.Length; joint++ )
					{
						boneMap[joint] = bones.Count;
						bones.Add( new( $"gltf_skin_{skinIndex}_joint_{joint}", skin.Joints[joint], skin.InverseBindMatrices[joint] ) );
					}
					skinMaps.Add( skinIndex, boneMap );
				}
			}
			else
			{
				boneMap = [bones.Count];
				bones.Add( new( $"gltf_node_{nodeIndex}", nodeIndex, NMatrix.Identity ) );
			}
			for ( var primitive = 0; primitive < sourcePrimitives.GetArrayLength(); primitive++ )
			{
				var data = document.CreateDrawData( meshIndex, primitive );
				var binding = skinned ? GltfVertexSkinning.Read( document, meshIndex, primitive, boneMap ) : GltfVertexSkinning.Rigid( data.Positions.Length, boneMap[0] );
				var morphs = data.HasMorphTargets ? GltfModelMorphs.Prepare( document, nodeIndex, meshIndex, primitive, GltfModelPrimitiveBuilder.SourceVertexMap( data ) ) : null;
				primitives.Add( new( nodeIndex, meshIndex, primitive, data, binding, boneMap, morphs ) );
				for ( var vertex = 0; vertex < data.Positions.Length; vertex++ )
				{
					var p = GltfGeometry.ConvertPosition( document.EvaluateVertexPosition( nodeIndex, primitive, vertex, rest ) );
					min = NVector.Min( min, p ); max = NVector.Max( max, p );
				}
			}
		}
		if ( primitives.Count == 0 ) throw new GltfException( "The glTF default scene contains no renderable geometry." );
		return new( bones.ToArray(), primitives, new BBox( min, max ) );
	}

	internal NMatrix[] EvaluateModelPalettes( GltfPose pose )
	{
		var output = new NMatrix[bones.Length];
		for ( var i = 0; i < bones.Length; i++ ) output[i] = GltfGeometry.ConvertTransform( bones[i].InverseBind * pose.GetWorldTransform( bones[i].Node ) );
		return output;
	}

	internal void AddBones( ModelBuilder builder )
	{
		foreach ( var bone in bones ) builder.AddBone( bone.Name, Vector3.Zero, Rotation.Identity, Bounds );
	}

	/// <summary>Read the compiled native mapping; declaration order is not assumed to survive compilation.</summary>
	internal unsafe void BindNative( Model model )
	{
		var morphMeshes = Primitives.Where( primitive => primitive.Morphs?.HasDeltas == true ).Select( primitive => $"gltf_node_{primitive.Node}_mesh_{primitive.Mesh}_primitive_{primitive.Index}" ).ToHashSet( StringComparer.Ordinal );
		if ( HasMorphs )
		{
			var names = model.Morphs.Names.ToHashSet( StringComparer.OrdinalIgnoreCase );
			foreach ( var target in morphBindings.SelectMany( binding => binding.Targets ) )
				if ( !names.Contains( target.PositiveName ) || !names.Contains( target.NegativeName ) ) throw new GltfException( "Native model construction dropped a glTF morph controller; target limits must be resolved before this asset can load." );
		}
		var byName = bones.Select( (bone, index) => (bone.Name, index) ).ToDictionary( x => x.Name, x => x.index, StringComparer.Ordinal );
		nativeToPalette = new int[model.BoneCount];
		for ( var i = 0; i < nativeToPalette.Length; i++ )
			if ( !byName.TryGetValue( model.GetBoneName( i ), out nativeToPalette[i] ) ) throw new GltfException( "Native model construction introduced an unknown glTF palette bone." );
		if ( nativeToPalette.Distinct().Count() != bones.Length ) throw new GltfException( "Native model construction dropped a glTF palette bone." );
		renderToPalette = new int[model.MeshCount][];
		for ( var mesh = 0; mesh < renderToPalette.Length; mesh++ )
		{
			if ( morphMeshes.Contains( model.MeshInfo.Meshes[mesh].Name ) && !RenderTools.ModelMeshHasMorphs( model.native, mesh ) ) throw new GltfException( "Native model construction dropped glTF morph vertex data." );
			int count = 0, weights = 0;
			var vertices = RenderTools.GetModelMeshSkinning( model.native, mesh, (IntPtr)(&count), (IntPtr)(&weights) );
			if ( vertices <= 0 || count <= 0 || weights <= 0 || weights > 4 ) throw new GltfException( "Native model construction did not retain the glTF skinning declaration." );
			var inverse = new Matrix3x4[count];
			var master = new int[count];
			fixed ( Matrix3x4* inversePtr = inverse )
			fixed ( int* masterPtr = master )
				if ( !RenderTools.GetModelMeshSkinningBones( model.native, mesh, (IntPtr)inversePtr, (IntPtr)masterPtr, count ) ) throw new GltfException( "Native model skinning remap is unavailable." );
			var map = new int[count];
			for ( var i = 0; i < count; i++ )
			{
				if ( (uint)master[i] >= (uint)nativeToPalette.Length ) throw new GltfException( "Native render bone mapping is outside the model skeleton." );
				if ( inverse[i].Row0 != Matrix3x4.Identity.Row0 || inverse[i].Row1 != Matrix3x4.Identity.Row1 || inverse[i].Row2 != Matrix3x4.Identity.Row2 ) throw new GltfException( "Native model construction changed the identity inverse binds used by the exact glTF palette." );
				map[i] = nativeToPalette[master[i]];
			}
			renderToPalette[mesh] = map;
		}
	}

	internal void ApplyMorphWeights( SceneModel model, GltfPose pose )
	{
		foreach ( var binding in morphBindings ) _ = binding.EvaluateBounds( pose );
		foreach ( var binding in morphBindings ) binding.ApplyWeights( model, pose );
	}

	internal void ApplyPose( SceneModel model, GltfPose pose )
	{
		if ( nativeToPalette is null ) throw new InvalidOperationException( "The glTF rig has not been bound to a native model." );
		var palettes = EvaluateModelPalettes( pose );
		var placement = (NMatrix)Matrix.FromTransform( model.Transform );
		var nativeTransforms = new Transform[palettes.Length];
		for ( var i = 0; i < palettes.Length; i++ )
		{
			palettes[i] *= placement;
			if ( !TryNativeTransform( palettes[i], out nativeTransforms[i] ) ) throw new GltfException( "The glTF skinning palette contains shear or a non-decomposable affine transform. Native SceneModel render-bone transforms accept TRS only; this model requires the managed full-affine palette path." );
		}
		for ( var i = 0; i < nativeToPalette.Length; i++ ) model.SetBoneWorldTransform( i, nativeTransforms[nativeToPalette[i]] );
		var render = new Matrix3x4[renderToPalette.Length][];
		for ( var mesh = 0; mesh < render.Length; mesh++ )
		{
			render[mesh] = new Matrix3x4[renderToPalette[mesh].Length];
			for ( var bone = 0; bone < render[mesh].Length; bone++ ) render[mesh][bone] = Matrix3x4.From( (Matrix)palettes[renderToPalette[mesh][bone]] );
		}
		model.SetGltfSkinningPalettes( render );
		// Conservative joint-transformed bounds contain every nonnegative normalized
		// blend, including intermediate animation poses and separately placed instances.
		var bounds = default( BBox );
		var first = true;
		foreach ( var primitive in Primitives )
		{
			var source = primitive.Morphs?.EvaluateBounds( pose ) ?? BBox.FromPoints( primitive.Data.Positions.ToArray().Select( p => (Vector3)p ) );
			foreach ( var bone in primitive.Bones )
			{
				// The managed GPU receives the original matrix, not its reconstructed
				// native TRS. Bounds must follow that same exact affine transform.
				var transformed = BBox.FromPoints( source.Corners.Select( corner => (Vector3)NVector.Transform( corner, palettes[bone] ) ) );
				bounds = first ? transformed : bounds.AddBBox( transformed );
				first = false;
			}
		}
		model.Bounds = bounds;
	}

	internal static bool TryNativeTransform( NMatrix matrix, out Transform transform )
	{
		transform = default;
		if ( matrix.M14 != 0 || matrix.M24 != 0 || matrix.M34 != 0 || matrix.M44 != 1 ) return false;
		// Preserve the measured ordinary/zero/reflected path exactly. Retry only
		// when Numerics' fixed small-basis threshold or float row norms lose data.
		if ( NMatrix.Decompose( matrix, out var scale, out var rotation, out var position ) &&
			TryCreateNativeTransform( matrix, scale, rotation, position, out transform ) ) return true;

		if ( !TryNormalizeTrsRow( matrix.M11, matrix.M12, matrix.M13, out var x, out var sx ) ||
			!TryNormalizeTrsRow( matrix.M21, matrix.M22, matrix.M23, out var y, out var sy ) ||
			!TryNormalizeTrsRow( matrix.M31, matrix.M32, matrix.M33, out var z, out var sz ) ) return false;
		var basis = matrix;
		basis.M11 = x.X; basis.M12 = x.Y; basis.M13 = x.Z;
		basis.M21 = y.X; basis.M22 = y.Y; basis.M23 = y.Z;
		basis.M31 = z.X; basis.M32 = z.Y; basis.M33 = z.Z;
		if ( !NMatrix.Decompose( basis, out scale, out rotation, out position ) ) return false;
		scale *= new NVector( sx, sy, sz );
		// Validate against the original affine rows, not the normalized basis.
		// The same measured per-row guard rejects shear at every source scale.
		return TryCreateNativeTransform( matrix, scale, rotation, position, out transform );
	}

	private static bool TryNormalizeTrsRow( float a, float b, float c, out NVector row, out float scale )
	{
		row = default; scale = 0;
		// Every finite float square fits in double, including subnormal inputs.
		// This avoids both float underflow and overflow without a scale epsilon.
		var length = Math.Sqrt( (double)a * a + (double)b * b + (double)c * c );
		if ( !double.IsFinite( length ) || length > float.MaxValue ) return false;
		scale = (float)length;
		if ( length == 0 ) return true;
		row = new NVector( (float)(a / length), (float)(b / length), (float)(c / length) );
		return true;
	}

	private static bool TryCreateNativeTransform( NMatrix matrix, NVector scale, System.Numerics.Quaternion rotation, NVector position, out Transform transform )
	{
		transform = default;
		var reconstructed = NMatrix.CreateScale( scale ) * NMatrix.CreateFromQuaternion( rotation ) * NMatrix.CreateTranslation( position );
		// A pure TRS at scale100 reconstructs with observed row errors up to
		// 2.384e-5 (2.384e-7 relative) in the real engine. Bound float roundoff
		// per row, so a large unrelated scale cannot hide shear on a small axis.
		if ( !MatchesTrsRow( matrix.M11, matrix.M12, matrix.M13, reconstructed.M11, reconstructed.M12, reconstructed.M13 ) ||
			!MatchesTrsRow( matrix.M21, matrix.M22, matrix.M23, reconstructed.M21, reconstructed.M22, reconstructed.M23 ) ||
			!MatchesTrsRow( matrix.M31, matrix.M32, matrix.M33, reconstructed.M31, reconstructed.M32, reconstructed.M33 ) ||
			!float.IsFinite( position.X ) || !float.IsFinite( position.Y ) || !float.IsFinite( position.Z ) ) return false;
		transform = new Transform( position, rotation, scale );
		return true;
	}

	private static bool MatchesTrsRow( float a, float b, float c, float x, float y, float z )
	{
		const float floatMachineEpsilon = 1.1920928955078125e-7f;
		var magnitude = MathF.Max( MathF.Abs( a ), MathF.Max( MathF.Abs( b ), MathF.Abs( c ) ) );
		var tolerance = magnitude * (8 * floatMachineEpsilon);
		return float.IsFinite( magnitude ) && float.IsFinite( x ) && float.IsFinite( y ) && float.IsFinite( z ) &&
			MathF.Abs( a - x ) <= tolerance && MathF.Abs( b - y ) <= tolerance && MathF.Abs( c - z ) <= tolerance;
	}

	private static int Index( JsonElement value ) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32( out var index ) && index >= 0 ? index : throw new GltfException( "Invalid glTF scene index." );
	private static JsonElement Element( GltfDocument document, string name, int index ) => document.Root.TryGetProperty( name, out var values ) && values.ValueKind == JsonValueKind.Array && (uint)index < (uint)values.GetArrayLength() && values[index].ValueKind == JsonValueKind.Object ? values[index] : throw new GltfException( $"Invalid glTF {name} index {index}." );
}
