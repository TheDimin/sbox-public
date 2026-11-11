using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox.Gltf.DirectGpu;

using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

// Explicit row records keep the CPU/GPU matrix ABI independent of HLSL packing.
[StructLayout( LayoutKind.Sequential )]
internal struct GltfGpuMatrix
{
	public NVector4 Row0, Row1, Row2, Row3;
	internal GltfGpuMatrix( NMatrix value )
	{
		Row0 = new( value.M11, value.M12, value.M13, value.M14 ); Row1 = new( value.M21, value.M22, value.M23, value.M24 );
		Row2 = new( value.M31, value.M32, value.M33, value.M34 ); Row3 = new( value.M41, value.M42, value.M43, value.M44 );
	}
	internal readonly NVector3 Position( NVector3 value ) => new(
		value.X * Row0.X + value.Y * Row1.X + value.Z * Row2.X + Row3.X,
		value.X * Row0.Y + value.Y * Row1.Y + value.Z * Row2.Y + Row3.Y,
		value.X * Row0.Z + value.Y * Row1.Z + value.Z * Row2.Z + Row3.Z );
}

internal sealed class GltfGpuRig
{
	/// <summary>glTF metres, Y up, to engine inches, Z up.</summary>
	internal static readonly NMatrix Basis = new( 0, 1f / .0254f, 0, 0, 0, 0, 1f / .0254f, 0, 1f / .0254f, 0, 0, 0, 0, 0, 0, 1 );
	private readonly GltfDocument document;
	private readonly int node;
	private readonly GltfSkin skin;
	private readonly NVector3[] positions;
	private readonly NVector3[][] morphPositions;
	private readonly (uint[] Joints, float[] Weights)[] influences;
	internal int JointCount => skin?.Joints.Length ?? 0;
	internal GltfGpuRig( GltfDocument document, int node, int? skin, int primitive, GltfGpuPrimitivePlan plan )
	{
		this.document = document; this.node = node;
		this.skin = skin.HasValue && plan.Sources.ContainsKey( "JOINTS_0" ) ? document.ReadSkin( skin.Value ) : null;
		var position = plan.Sources["POSITION"];
		// Validate the bytes consumed by the GPU once, before realization. Pose
		// provenance checks must never decode whole arrays on each frame.
		foreach ( var descriptor in plan.InputDescriptors.Where( x => x.ComponentType == 5126 ) )
			for ( var vertex = 0; vertex < position.Stream.Count; vertex++ )
				for ( var component = 0; component < descriptor.Components; component++ )
					if ( !float.IsFinite( Read( plan.InputBytes, descriptor, vertex, component ) ) ) throw new GltfException( "GPU input contains a nonfinite float component." );
		positions = new NVector3[position.Stream.Count];
		for ( var vertex = 0; vertex < positions.Length; vertex++ ) positions[vertex] = ReadVector3( plan.InputBytes, plan.InputDescriptors[position.DescriptorIndex], vertex );
		morphPositions = new NVector3[plan.MorphTargetCount][];
		foreach ( var descriptor in plan.InputDescriptors.Where( x => x.Target != uint.MaxValue && x.Role == 0 ) )
		{
			var deltas = new NVector3[positions.Length]; morphPositions[descriptor.Target] = deltas;
			for ( var vertex = 0; vertex < deltas.Length; vertex++ ) deltas[vertex] = ReadVector3( plan.InputBytes, descriptor, vertex );
		}
		var sets = new List<(uint[], float[])>();
		if ( this.skin is not null )
		{
			var allQuantized = true; var integerSums = new long[positions.Length];
			foreach ( var joint in plan.InputDescriptors.Take( plan.BaseAttributeCount ).Where( x => x.Role == 5 ).OrderBy( x => x.SemanticIndex ) )
			{
				var weight = plan.InputDescriptors.Take( plan.BaseAttributeCount ).Single( x => x.Role == 6 && x.SemanticIndex == joint.SemanticIndex );
				var quantized = weight.Normalized != 0 && weight.ComponentType is 5121 or 5123; allQuantized &= quantized;
				var joints = new uint[positions.Length * 4]; var weights = new float[joints.Length];
				for ( var vertex = 0; vertex < positions.Length; vertex++ )
					for ( var component = 0; component < 4; component++ )
					{
						var offset = vertex * 4 + component;
						joints[offset] = (uint)Read( plan.InputBytes, joint, vertex, component ); weights[offset] = Read( plan.InputBytes, weight, vertex, component );
						if ( joints[offset] >= this.skin.Joints.Length || !float.IsFinite( weights[offset] ) || weights[offset] < 0 ) throw new GltfException( "Invalid direct GPU joint index or weight." );
						if ( quantized )
						{
							var integer = (long)Math.Round( (double)weights[offset] * (weight.ComponentType == 5121 ? 255 : 65535) );
							integerSums[vertex] += weight.ComponentType == 5121 ? integer * 257 : integer;
						}
					}
				sets.Add( (joints, weights) );
			}
			for ( var vertex = 0; vertex < positions.Length; vertex++ )
			{
				if ( allQuantized && integerSums[vertex] != 65535 ) throw new GltfException( "Quantized GPU skinning weights must sum exactly to one across all influence sets." );
				var used = new HashSet<uint>();
				foreach ( var set in sets ) for ( var component = 0; component < 4; component++ )
					if ( set.Item2[vertex * 4 + component] != 0 && !used.Add( set.Item1[vertex * 4 + component] ) ) throw new GltfException( "Duplicate nonzero GPU joint influence." );
			}
		}
		influences = sets.ToArray();
	}
	internal GltfGpuMatrix[] Evaluate( GltfPose pose, NMatrix? instanceLocal = null )
	{
		document.ValidatePoseOwnership( pose );
		var local = InstanceTransform( instanceLocal );
		if ( skin is null ) return [new GltfGpuMatrix( local * pose.GetWorldTransform( node ) * Basis )];
		var result = new GltfGpuMatrix[skin.Joints.Length];
		for ( var i = 0; i < result.Length; i++ ) result[i] = new GltfGpuMatrix( skin.InverseBindMatrices[i] * pose.GetWorldTransform( skin.Joints[i] ) * Basis );
		return result;
	}
	internal bool ReverseWinding( GltfPose pose, NMatrix placement, NMatrix? instanceLocal = null )
	{
		var localSign = Orientation( InstanceTransform( instanceLocal ) );
		var nodeSign = skin is null ? Orientation( pose.GetWorldTransform( node ) ) : 1;
		var sign = localSign * nodeSign;
		var placementSign = Orientation( placement );
		return sign != 0 && placementSign != 0 && sign != placementSign;
	}
	private NMatrix InstanceTransform( NMatrix? instanceLocal )
	{
		var local = instanceLocal ?? NMatrix.Identity;
		RequireAffine( local );
		if ( skin is not null && local != NMatrix.Identity )
			throw new GltfException( "Skinned instance-local transforms are not implemented; no transform was applied partially." );
		return local;
	}
	private static int Orientation( NMatrix value )
	{
		var maximum = new[] { value.M11, value.M12, value.M13, value.M21, value.M22, value.M23, value.M31, value.M32, value.M33 }.Max( x => Math.Abs( (double)x ) );
		if ( maximum == 0 ) return 0;
		var a = value.M11 / maximum; var b = value.M12 / maximum; var c = value.M13 / maximum;
		var d = value.M21 / maximum; var e = value.M22 / maximum; var f = value.M23 / maximum;
		var g = value.M31 / maximum; var h = value.M32 / maximum; var i = value.M33 / maximum;
		return Math.Sign( a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g) );
	}

	internal static void RequireAffine( NMatrix value )
	{
		var values = new[] { value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24, value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44 };
		if ( values.Any( x => !float.IsFinite( x ) ) || value.M14 != 0 || value.M24 != 0 || value.M34 != 0 || value.M44 != 1 ) throw new GltfException( "Placement must be a finite affine matrix." );
	}

	// This prototype computes conservative scene bounds on the CPU from the
	// source accessors and current pose. These positions are never uploaded as
	// draw geometry. This diagnostic implementation is not a performance claim.
	internal static BBox Bounds( IEnumerable<(GltfGpuRig Rig, GltfGpuMatrix[] Matrices, float[] Weights)> rows, NMatrix placement, bool padding = true )
	{
		var hasBounds = false; var minimum = NVector3.Zero; var maximum = NVector3.Zero;
		foreach ( var row in rows )
		{
			var rig = row.Rig;
			for ( var vertex = 0; vertex < rig.positions.Length; vertex++ )
			{
				var position = rig.positions[vertex];
				for ( var target = 0; target < rig.morphPositions.Length; target++ )
					if ( rig.morphPositions[target] is { } deltas ) position += deltas[vertex] * row.Weights[target];
				var point = NVector3.Zero;
				if ( rig.skin is null ) point = row.Matrices[0].Position( position );
				else foreach ( var set in rig.influences ) for ( var component = 0; component < 4; component++ )
				{
					var index = vertex * 4 + component; if ( set.Weights[index] == 0 ) continue;
					point += row.Matrices[set.Joints[index]].Position( position ) * set.Weights[index];
				}
				point = NVector3.Transform( point, placement );
				if ( !float.IsFinite( point.X ) || !float.IsFinite( point.Y ) || !float.IsFinite( point.Z ) ) throw new GltfException( "GPU instance bounds contain a nonfinite position." );
				if ( !hasBounds ) { minimum = maximum = point; hasBounds = true; }
				else { minimum = NVector3.Min( minimum, point ); maximum = NVector3.Max( maximum, point ); }
			}
		}
		if ( !hasBounds ) return new BBox( global::Vector3.Zero, global::Vector3.Zero );
		// A collapsed point/line can still submit for explicit GPU oracle checks.
		if ( padding ) { var epsilon = new NVector3( .001f ); minimum -= epsilon; maximum += epsilon; }
		return new BBox( new global::Vector3( minimum.X, minimum.Y, minimum.Z ), new global::Vector3( maximum.X, maximum.Y, maximum.Z ) );
	}
	private static NVector3 ReadVector3( byte[] data, GltfGpuInputDescriptor descriptor, int vertex ) => new( Read( data, descriptor, vertex, 0 ), Read( data, descriptor, vertex, 1 ), Read( data, descriptor, vertex, 2 ) );
	private static float Read( byte[] data, GltfGpuInputDescriptor descriptor, int vertex, int component )
	{
		var size = descriptor.ComponentType is 5120 or 5121 ? 1 : descriptor.ComponentType is 5122 or 5123 ? 2 : 4;
		var offset = checked((int)descriptor.ByteOffset + vertex * (int)descriptor.ByteStride + component * size);
		var value = descriptor.ComponentType switch
		{
			5120 => (float)(sbyte)data[offset],
			5121 => data[offset],
			5122 => BinaryPrimitives.ReadInt16LittleEndian( data.AsSpan( offset ) ),
			5123 => BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset ) ),
			5125 => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset ) ),
			5126 => BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( offset ) ),
			_ => throw new GltfException( "Unknown GPU input component type." )
		};
		return descriptor.Normalized == 0 ? value : descriptor.ComponentType switch { 5120 => Math.Max( value / 127f, -1f ), 5121 => value / 255f, 5122 => Math.Max( value / 32767f, -1f ), 5123 => value / 65535f, _ => throw new GltfException( "Invalid normalized GPU component type." ) };
	}
}
