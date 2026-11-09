using Sandbox.Gltf;

namespace Sandbox;

/// <summary>
/// Explicit source-accessor to model-bone remapping. Weight storage remains in
/// its authored format, including FLOAT values; no influence is quantized or dropped.
/// </summary>
internal sealed class GltfVertexSkinning
{
	internal sealed record Set( ushort[] Joints, GltfUploadStream Weights );
	internal IReadOnlyList<Set> Sets { get; private init; }
	internal int Influences => Sets.Count * 4;
	internal int VertexCount { get; private init; }
	internal int? RigidBone { get; private init; }

	internal static GltfVertexSkinning Rigid( int vertices, int bone )
	{
		if ( vertices <= 0 || (uint)bone > ushort.MaxValue ) throw new GltfException( "Rigid skinning vertex count or bone index exceeds the native range." );
		return new() { Sets = [], VertexCount = vertices, RigidBone = bone };
	}

	internal static GltfVertexSkinning Read( GltfDocument document, int mesh, int primitive, ReadOnlySpan<int> boneMap )
	{
		var geometry = document.ReadPrimitive( mesh, primitive );
		var upload = document.MaterializePrimitiveUpload( mesh, primitive );
		var sets = new List<Set>();
		for ( var set = 0; geometry.Attributes.TryGetValue( "JOINTS_" + set, out var accessor ); set++ )
		{
			var joints = document.ReadFloatAccessor( accessor );
			var weights = document.ReadFloatAccessor( geometry.Attributes["WEIGHTS_" + set] );
			if ( joints.Length != geometry.VertexCount * 4 || weights.Length != joints.Length ) throw new GltfException( "Skinning accessor count does not match the vertex stream." );
			var mapped = new ushort[joints.Length];
			for ( var i = 0; i < joints.Length; i++ )
			{
				var index = joints[i];
				if ( !float.IsFinite( index ) || index != MathF.Truncate( index ) || index < 0 || index >= boneMap.Length ) throw new GltfException( "Skinning joint index is outside the selected skin." );
				if ( !float.IsFinite( weights[i] ) || weights[i] < 0 ) throw new GltfException( "Skinning weight is not finite and nonnegative." );
				var bone = boneMap[(int)index];
				if ( (uint)bone > ushort.MaxValue ) throw new GltfException( "The model bone index exceeds the native 16-bit skinning range." );
				mapped[i] = (ushort)bone;
			}
			sets.Add( new( mapped, upload.Attributes["WEIGHTS_" + set] ) );
		}
		if ( sets.Count == 0 ) throw new GltfException( "A skinned primitive requires joint and weight accessors." );
		return new() { Sets = sets.AsReadOnly(), VertexCount = geometry.VertexCount };
	}
}
