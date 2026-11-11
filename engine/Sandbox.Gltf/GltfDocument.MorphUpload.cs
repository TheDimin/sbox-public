using System.Collections.ObjectModel;
using System.Text.Json;

namespace Sandbox.Gltf;

/// <summary>
/// Owned, source-described streams for each authored morph target. Optional and
/// application semantics are retained; their presence does not imply renderer support.
/// No GPU resource is allocated and no delta is converted to a fixed vertex layout.
/// </summary>
public sealed class GltfMorphTargetsUploadData
{
	public IReadOnlyList<IReadOnlyDictionary<string, GltfUploadStream>> Targets { get; internal init; }
	public int VertexCount { get; internal init; }
	/// <summary>Total bytes of distinct returned storage objects, excluding base geometry.</summary>
	public long OwnedBytes { get; internal init; }
}

public sealed partial class GltfDocument
{
	/// <summary>
	/// Copy each referenced morph view once or materialize a sparse/implicit-zero
	/// accessor in its original component format. Offsets are relative to returned
	/// owned storage, never the original buffer origin. Meshopt is handled by the
	/// existing buffer-view reader; Draco does not encode morph target streams.
	/// The budget covers returned copies, separately from source/decoder memory.
	/// </summary>
	public GltfMorphTargetsUploadData MaterializeMorphTargetsUpload( int mesh, int primitive, long maxOwnedBytes = 256L * 1024 * 1024 )
	{
		if ( maxOwnedBytes < 0 ) throw new GltfException( "Morph upload owned-byte budget cannot be negative." );
		var baseLayout = DescribePrimitiveUpload( mesh, primitive );
		var primitiveValue = SceneArrayElement( ElementAt( "meshes", mesh ), "primitives", primitive );
		if ( !primitiveValue.TryGetProperty( "targets", out var targetValues ) )
			return new GltfMorphTargetsUploadData { Targets = Array.Empty<IReadOnlyDictionary<string, GltfUploadStream>>(), VertexCount = baseLayout.VertexCount };
		if ( targetValues.ValueKind != JsonValueKind.Array || targetValues.GetArrayLength() == 0 )
			throw new GltfException( "Morph targets must be a nonempty array." );

		var targets = new List<Dictionary<string, GltfUploadBinding>>();
		var viewLengths = new Dictionary<int, int>();
		var accessorLengths = new Dictionary<int, int>();
		long required = 0;
		foreach ( var targetValue in targetValues.EnumerateArray() )
		{
			if ( targetValue.ValueKind != JsonValueKind.Object ) throw new GltfException( "A morph target must be an object." );
			var bindings = new Dictionary<string, GltfUploadBinding>( StringComparer.Ordinal );
			foreach ( var attribute in targetValue.EnumerateObject() )
			{
				if ( !baseLayout.Attributes.ContainsKey( attribute.Name ) )
					throw new GltfException( $"Morph target '{attribute.Name}' has no corresponding base attribute." );
				var index = GltfJson.Integer( attribute.Value, "morph target accessor" );
				var accessor = Accessor( index );
				ValidateMorphUploadAttribute( attribute.Name, accessor );
				if ( accessor.Count != baseLayout.VertexCount ) throw new GltfException( "Morph target and base attribute counts must match." );
				var binding = UploadBinding( index, accessor, false, false );
				if ( !bindings.TryAdd( attribute.Name, binding ) ) throw new GltfException( $"Duplicate morph target attribute '{attribute.Name}'." );
				if ( NeedsMorphAccessorCopy( binding ) )
				{
					if ( accessorLengths.ContainsKey( index ) ) continue;
					var length = (long)accessor.Count * accessor.Stride;
					if ( length > Array.MaxLength ) throw new GltfException( "Morph upload accessor exceeds the supported array length." );
					accessorLengths.Add( index, (int)length );
					required += length;
				}
				else
				{
					var view = binding.SourceBufferView ?? throw new GltfException( "Morph upload binding has no source view." );
					if ( viewLengths.ContainsKey( view ) ) continue;
					var length = binding.ViewByteLength ?? throw new GltfException( "Morph upload binding has no view length." );
					viewLengths.Add( view, length );
					required += length;
				}
				if ( required > maxOwnedBytes ) throw new GltfException( "Morph upload owned-byte budget exceeded." );
			}
			targets.Add( bindings );
		}

		// Source buffers were resolved by the document constructor. All binding
		// validation/budget accounting precedes target decoding and owned copies.
		var views = new Dictionary<int, GltfUploadStorage>();
		var accessors = new Dictionary<int, GltfUploadStorage>();
		GltfUploadStream Copy( GltfUploadBinding binding )
		{
			if ( NeedsMorphAccessorCopy( binding ) )
			{
				if ( !accessors.TryGetValue( binding.AccessorIndex, out var storage ) )
				{
					storage = new GltfUploadStorage( MaterializeUploadAccessor( binding.AccessorIndex, accessorLengths[binding.AccessorIndex] ) );
					accessors.Add( binding.AccessorIndex, storage );
				}
				return new GltfUploadStream
				{
					Binding = binding,
					Storage = storage,
					ByteOffset = 0,
					ByteStride = Accessor( binding.AccessorIndex ).Stride,
					Count = binding.Count,
					ComponentType = binding.ComponentType,
					Components = binding.Components,
					Normalized = binding.Normalized,
					IsMaterialized = true
				};
			}
			var view = binding.SourceBufferView.Value;
			if ( !views.TryGetValue( view, out var viewStorage ) )
			{
				var bytes = BufferView( view );
				if ( bytes.Length != viewLengths[view] ) throw new GltfException( "Morph upload view length changed during decoding." );
				viewStorage = new GltfUploadStorage( bytes.ToArray() );
				views.Add( view, viewStorage );
			}
			return new GltfUploadStream
			{
				Binding = binding,
				Storage = viewStorage,
				ByteOffset = binding.AccessorByteOffset ?? throw new GltfException( "Morph upload source offset is unavailable." ),
				ByteStride = binding.ByteStride ?? throw new GltfException( "Morph upload source stride is unavailable." ),
				Count = binding.Count,
				ComponentType = binding.ComponentType,
				Components = binding.Components,
				Normalized = binding.Normalized,
				IsMaterialized = (binding.Materialization & GltfUploadMaterialization.Meshopt) != 0
			};
		}

		var output = new List<IReadOnlyDictionary<string, GltfUploadStream>>( targets.Count );
		foreach ( var target in targets )
		{
			var attributes = new Dictionary<string, GltfUploadStream>( StringComparer.Ordinal );
			foreach ( var (semantic, binding) in target )
			{
				var stream = Copy( binding );
				ValidateUploadFloatComponents( semantic, stream );
				attributes.Add( semantic, stream );
			}
			output.Add( new ReadOnlyDictionary<string, GltfUploadStream>( attributes ) );
		}
		return new GltfMorphTargetsUploadData { Targets = output.AsReadOnly(), VertexCount = baseLayout.VertexCount, OwnedBytes = required };
	}

	private static bool NeedsMorphAccessorCopy( GltfUploadBinding binding ) =>
		(binding.Materialization & (GltfUploadMaterialization.Sparse | GltfUploadMaterialization.ZeroInitialized)) != 0;

	private void ValidateMorphUploadAttribute( string semantic, AccessorLayout layout )
	{
		if ( layout.ComponentType == 5125 ) throw new GltfException( "Morph vertex attributes cannot use UNSIGNED_INT components." );
		var type = GltfJson.String( layout.Json, "type" );
		var floating = layout.ComponentType == 5126 && !layout.Normalized;
		var normalizedInteger = layout.Normalized && layout.ComponentType is 5120 or 5121 or 5122 or 5123;
		var signedInteger = layout.ComponentType is 5120 or 5122;
		var quantized = SupportsExtension( "KHR_mesh_quantization" ) && SceneRequiresQuantization();
		bool valid;
		if ( semantic == "POSITION" )
		{
			valid = type == "VEC3" && (floating || quantized && signedInteger);
			foreach ( var bound in new[] { "min", "max" } )
				_ = SceneFloatArray( GltfJson.Required( layout.Json, bound ), 3, "morph POSITION bounds" );
		}
		else if ( semantic is "NORMAL" or "TANGENT" )
			valid = type == "VEC3" && (floating || quantized && signedInteger && layout.Normalized);
		else if ( semantic.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) )
		{
			_ = AttributeSetIndex( semantic, "TEXCOORD_" );
			valid = type == "VEC2" && (floating || normalizedInteger || quantized && signedInteger);
		}
		else if ( semantic.StartsWith( "COLOR_", StringComparison.Ordinal ) )
		{
			_ = AttributeSetIndex( semantic, "COLOR_" );
			valid = type is "VEC3" or "VEC4" && (floating || normalizedInteger);
		}
		else valid = semantic.StartsWith( "_", StringComparison.Ordinal );
		if ( !valid ) throw new GltfException( $"Invalid morph target accessor layout for '{semantic}'." );
	}
}
