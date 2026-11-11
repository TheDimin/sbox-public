using System.Buffers.Binary;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

/// <summary>The inputs of one output vertex stream: what <c>gltf_deform_cs.shader</c> reads for one draw.</summary>
internal sealed record GltfNativeVertexRoute( byte[] InputBytes, GltfGpuInputDescriptor[] Inputs, int BaseAttributeCount,
	GltfGpuOutputDescriptor[] Outputs, GltfGpuPrimitivePlan Plan )
{
	internal static GltfNativeVertexRoute For( GltfGpuPrimitivePlan plan, GltfGpuGeneratedTangentRoute tangent ) => tangent is not null
		? new( tangent.InputBytes, tangent.InputDescriptors, tangent.BaseAttributeCount, tangent.OutputDescriptors, plan )
		: new( plan.InputBytes, plan.InputDescriptors, plan.BaseAttributeCount, plan.OutputDescriptors, plan );
}

/// <summary>
/// Import-time CPU port of <c>gltf_deform_cs.shader</c> for rows without joints or morph targets. It writes the
/// vertex bytes the compute pass writes for the same route with placement fixed to identity. The compiled file
/// stores the result, so loading it does no vertex work. Keep the two in step.
/// </summary>
internal static class GltfNativeVertexBake
{
	private const uint None = uint.MaxValue;

	private static NVector3 Xyz( this NVector4 value ) => new( value.X, value.Y, value.Z );

	internal static byte[] Bake( GltfNativeVertexRoute route, NMatrix matrix, bool reverseWinding, out NVector3 min, out NVector3 max )
	{
		var plan = route.Plan;
		// Morph targets are stored as native morph deltas; the bake writes the base shape and reads no target input.
		var count = plan.SourceVertices.Length;
		var stride = plan.VertexStride;
		var output = new byte[checked(count * stride)];
		var linear = matrix; linear.M41 = 0; linear.M42 = 0; linear.M43 = 0;
		var normalSource = Find( route, 1, 0 );
		var positionSource = Find( route, 0, 0 );
		min = new NVector3( float.MaxValue ); max = new NVector3( float.MinValue );
		for ( var outputVertex = 0; outputVertex < count; outputVertex++ )
		{
			var vertex = plan.SourceVertices[outputVertex];
			var localNormal = Field( route, 1, normalSource, vertex, new NVector4( 0, 0, 1, 0 ) ).Xyz();
			NVector3 a = default, b = default, c = default;
			if ( plan.GenerateFlatNormals )
			{
				var corner = outputVertex / 3 * 3;
				a = Local( route, positionSource, plan.SourceVertices[corner] );
				b = Local( route, positionSource, plan.SourceVertices[corner + 1] );
				c = Local( route, positionSource, plan.SourceVertices[corner + 2] );
				localNormal = Unit( NVector3.Cross( Unit( b - a, NVector3.UnitX ), Unit( c - a, NVector3.UnitY ) ), NVector3.UnitZ );
			}
			var worldNormal = Normal( localNormal, linear, out var handedness );
			if ( plan.GenerateFlatNormals )
			{
				var face = NVector3.Cross( Unit( NVector3.Transform( b, matrix ) - NVector3.Transform( a, matrix ), NVector3.UnitX ),
					Unit( NVector3.Transform( c, matrix ) - NVector3.Transform( a, matrix ), NVector3.UnitY ) );
				if ( reverseWinding ) face = -face;
				worldNormal = Unit( face, worldNormal );
			}
			foreach ( var field in route.Outputs )
			{
				NVector4 value;
				if ( field.Role == 0 )
				{
					var position = NVector3.Transform( Local( route, positionSource, vertex ), matrix );
					min = NVector3.Min( min, position ); max = NVector3.Max( max, position );
					value = new NVector4( position, 1 );
				}
				else if ( field.Role == 1 ) value = new NVector4( worldNormal, 0 );
				else if ( field.Role == 2 )
				{
					if ( (normalSource == None || field.Source == None) && field.Padding0 != 1u ) value = new NVector4( Perpendicular( worldNormal ), handedness );
					else
					{
						value = Field( route, 2, field.Source, field.Padding0 == 1u ? (uint)outputVertex : vertex, new NVector4( 1, 0, 0, 1 ) );
						var tangent = NVector3.TransformNormal( value.Xyz(), linear );
						tangent -= worldNormal * NVector3.Dot( tangent, worldNormal );
						value = new NVector4( Unit( tangent, Perpendicular( worldNormal ) ), value.W * handedness );
					}
				}
				else value = Field( route, field.Role, field.Source, vertex, field.Role == 3 ? NVector4.One : NVector4.Zero );
				if ( field.Role == 3 ) value = NVector4.Clamp( value, NVector4.Zero, NVector4.One );
				var offset = outputVertex * stride + (int)field.ByteOffset;
				for ( var component = 0; component < field.Components; component++ )
					BinaryPrimitives.WriteSingleLittleEndian( output.AsSpan( offset + component * 4, 4 ), component switch { 0 => value.X, 1 => value.Y, 2 => value.Z, _ => value.W } );
			}
		}
		if ( count == 0 ) { min = NVector3.Zero; max = NVector3.Zero; }
		return output;
	}

	private static uint Find( GltfNativeVertexRoute route, uint role, uint semantic )
	{
		for ( var i = 0; i < route.BaseAttributeCount; i++ )
			if ( route.Inputs[i].Role == role && route.Inputs[i].SemanticIndex == semantic ) return (uint)i;
		return None;
	}

	private static NVector3 Local( GltfNativeVertexRoute route, uint source, uint vertex ) => Field( route, 0, source, vertex, NVector4.Zero ).Xyz();

	private static NVector4 Field( GltfNativeVertexRoute route, uint role, uint source, uint vertex, NVector4 fallback )
	{
		if ( source == None ) return fallback;
		var value = Read( route, source, vertex );
		if ( role == 3 && route.Inputs[source].Components == 3 ) value.W = 1;
		return value;
	}

	private static NVector4 Read( GltfNativeVertexRoute route, uint descriptor, uint vertex )
	{
		var d = route.Inputs[descriptor];
		var value = NVector4.Zero;
		for ( var component = 0u; component < Math.Min( d.Components, 4u ); component++ )
		{
			var x = Component( route.InputBytes, d, vertex, component );
			if ( component == 0 ) value.X = x; else if ( component == 1 ) value.Y = x; else if ( component == 2 ) value.Z = x; else value.W = x;
		}
		return value;
	}

	private static float Component( byte[] bytes, GltfGpuInputDescriptor d, uint vertex, uint component )
	{
		var size = d.ComponentType is 5120 or 5121 ? 1u : d.ComponentType is 5122 or 5123 ? 2u : 4u;
		var offset = (long)d.ByteOffset + (long)vertex * d.ByteStride + component * size;
		// The compute pass reads past the arena as zero; so does this.
		if ( offset < 0 || offset + size > bytes.Length ) return 0;
		var span = bytes.AsSpan( (int)offset, (int)size );
		return d.ComponentType switch
		{
			5126 => BinaryPrimitives.ReadSingleLittleEndian( span ),
			5125 => BinaryPrimitives.ReadUInt32LittleEndian( span ),
			5121 => d.Normalized != 0 ? span[0] / 255f : span[0],
			5123 => d.Normalized != 0 ? BinaryPrimitives.ReadUInt16LittleEndian( span ) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian( span ),
			5120 => d.Normalized != 0 ? MathF.Max( (sbyte)span[0] / 127f, -1 ) : (sbyte)span[0],
			_ => d.Normalized != 0 ? MathF.Max( BinaryPrimitives.ReadInt16LittleEndian( span ) / 32767f, -1 ) : BinaryPrimitives.ReadInt16LittleEndian( span )
		};
	}

	private static float Maximum( NVector3 v ) => MathF.Max( MathF.Abs( v.X ), MathF.Max( MathF.Abs( v.Y ), MathF.Abs( v.Z ) ) );

	private static NVector3 Unit( NVector3 value, NVector3 fallback )
	{
		var maximum = Maximum( value );
		if ( maximum == 0 || !float.IsFinite( maximum ) ) return fallback;
		var scaled = value / maximum; var lengthSquared = NVector3.Dot( scaled, scaled );
		return lengthSquared > 0 && float.IsFinite( lengthSquared ) ? scaled / MathF.Sqrt( lengthSquared ) : fallback;
	}

	private static NVector3 Perpendicular( NVector3 normal ) =>
		Unit( NVector3.Cross( normal, MathF.Abs( normal.X ) < .7f ? NVector3.UnitX : NVector3.UnitY ), NVector3.UnitY );

	private static NVector3 Normal( NVector3 normal, NMatrix m, out float handedness )
	{
		var r0 = new NVector3( m.M11, m.M12, m.M13 ); var r1 = new NVector3( m.M21, m.M22, m.M23 ); var r2 = new NVector3( m.M31, m.M32, m.M33 );
		var maximum = MathF.Max( Maximum( r0 ), MathF.Max( Maximum( r1 ), Maximum( r2 ) ) );
		if ( maximum == 0 || !float.IsFinite( maximum ) ) { handedness = 1; return NVector3.UnitX; }
		var a = r0 / maximum; var b = r1 / maximum; var c = r2 / maximum;
		var ca = NVector3.Cross( b, c ); var cb = NVector3.Cross( c, a ); var cc = NVector3.Cross( a, b );
		handedness = NVector3.Dot( a, ca ) < 0 ? -1 : 1;
		var cofactor = normal.X * ca + normal.Y * cb + normal.Z * cc;
		return Unit( cofactor * handedness, Unit( NVector3.TransformNormal( normal, m ), NVector3.UnitX ) );
	}
}
