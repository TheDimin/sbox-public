using System;
using System.Buffers.Binary;
using System.Linq;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>
/// Immutable candidate GPU inputs for one projected primitive/material pair.
/// The original primitive and rig descriptors remain untouched. Publishing this
/// packet requires the matching corner-index-aware compute shader and an atomic
/// replacement of the material, output and index resources for that draw.
/// </summary>
internal sealed class GltfGpuGeneratedTangentRoute
{
	internal sealed record CornerTopology( uint[] SourceVertices, uint[] Indices );
	internal byte[] InputBytes { get; private init; }
	internal GltfGpuInputDescriptor[] InputDescriptors { get; private init; }
	internal GltfGpuOutputDescriptor[] OutputDescriptors { get; private init; }
	internal int BaseAttributeCount { get; private init; }
	internal uint[] SourceVertices { get; private init; }
	internal uint[] Indices { get; private init; }

	/// <summary>
	/// Returns null when the selected material has no normal texture or the
	/// primitive supplies both normals and authored tangents. Otherwise generates per-corner
	/// frames. Skinning transforms each generated base frame like an authored
	/// tangent; morph targets remain blocked until their tangent frames are
	/// generated from the updated morph attributes.
	/// </summary>
	internal static GltfGpuGeneratedTangentRoute Prepare( GltfGpuPrimitivePlan plan, bool hasSkin, long maxWorkspaceBytes = 256L * 1024 * 1024 )
	{
		ArgumentNullException.ThrowIfNull( plan );
		if ( plan.MaterialUv is null ) throw new GltfException( "Generated tangent routing requires a projected material packet." );
		if ( !plan.MaterialUv.Bindings.TryGetValue( GltfMaterialTextureRole.Normal, out var normalUv ) ||
			(plan.Upload.Attributes.ContainsKey( "TANGENT" ) && plan.Upload.Attributes.ContainsKey( "NORMAL" )) ) return null;
		if ( plan.MorphTargetCount != 0 ) throw new GltfException( "Generated normal-map tangent frames for morphed primitives are not implemented." );
		if ( plan.Mode != 4 ) throw new GltfException( "Generated normal-map tangent frames require triangle topology." );
		plan.RequireSourceOwnership( plan.OwnerDocument, plan.Mesh, plan.Primitive, plan.Upload );
		plan.RequireSourceStreamOwnership( "TEXCOORD_" + normalUv.TexCoord, normalUv.Source );
		if ( !plan.Upload.Attributes.TryGetValue( "POSITION", out var position ) ) throw new GltfException( "Generated tangents require positions." );
		plan.Upload.Attributes.TryGetValue( "NORMAL", out var normal );
		var vertexCount = position.Count;
		var corners = plan.Indices.LongLength;
		// This estimate covers our decoded arrays, corner maps and the second
		// input arena. The generator accounts for its own separate workspace.
		var packetBytes = checked(plan.InputBytes.LongLength + (long)vertexCount * (normal is null ? 20 : 32) + corners * 40 + (long)(plan.InputDescriptors.Length + plan.OutputDescriptors.Length + 1) * 32);
		if ( maxWorkspaceBytes <= 0 || packetBytes >= maxWorkspaceBytes ) throw new GltfException( "Generated tangent route exceeds its admission budget." );
		var positions = Decode3( position, vertexCount );
		var normals = normal is null ? null : Decode3( normal, vertexCount );
		var uvs = Decode2( normalUv.Source, vertexCount );
		var topology = ExpandTopology( plan );
		var result = GltfGeneratedTangents.Generate( positions, normals, uvs, topology.SourceVertices, 4, normalUv.Transform.RowU, normalUv.Transform.RowV, maxWorkspaceBytes - packetBytes );
		var inputLength = checked(plan.InputBytes.Length + result.CornerTangents.Length * 16);
		if ( plan.InputBytes.Length % 4 != 0 ) throw new GltfException( "Generated tangent source arena is not aligned." );
		var bytes = new byte[inputLength]; plan.InputBytes.CopyTo( bytes, 0 );
		for ( var i = 0; i < result.CornerTangents.Length; i++ )
		{
			var value = result.CornerTangents[i]; var offset = plan.InputBytes.Length + i * 16;
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset ), value.X ); BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset + 4 ), value.Y );
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset + 8 ), value.Z ); BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset + 12 ), value.W );
		}
		var tables = InsertCornerDescriptor( plan.InputDescriptors, plan.BaseAttributeCount, plan.OutputDescriptors, (uint)plan.InputBytes.Length );
		return new() { InputBytes = bytes, InputDescriptors = tables.Inputs, OutputDescriptors = tables.Outputs, BaseAttributeCount = plan.BaseAttributeCount + 1, SourceVertices = result.SourceVertices, Indices = topology.Indices };
	}

	internal sealed record DescriptorTables( GltfGpuInputDescriptor[] Inputs, GltfGpuOutputDescriptor[] Outputs );
	internal static DescriptorTables InsertCornerDescriptor( GltfGpuInputDescriptor[] input, int baseCount, GltfGpuOutputDescriptor[] output, uint byteOffset )
	{
		ArgumentNullException.ThrowIfNull( input ); ArgumentNullException.ThrowIfNull( output );
		if ( baseCount < 0 || baseCount > input.Length || byteOffset % 4 != 0 ) throw new GltfException( "Generated tangent base descriptor boundary is invalid." );
		var descriptors = new GltfGpuInputDescriptor[input.Length + 1];
		Array.Copy( input, 0, descriptors, 0, baseCount );
		Array.Copy( input, baseCount, descriptors, baseCount + 1, input.Length - baseCount );
		descriptors[baseCount] = new GltfGpuInputDescriptor { ByteOffset = byteOffset, ByteStride = 16, ComponentType = 5126, Components = 4, Role = 2, Target = uint.MaxValue };
		var outputs = (GltfGpuOutputDescriptor[])output.Clone();
		var tangent = -1;
		for ( var i = 0; i < outputs.Length; i++ )
		{
			if ( outputs[i].Source != uint.MaxValue && outputs[i].Source >= input.Length ) throw new GltfException( "Generated route output source descriptor is invalid." );
			if ( outputs[i].Source != uint.MaxValue && outputs[i].Source >= baseCount ) outputs[i].Source++;
			if ( outputs[i].Role == 2 && outputs[i].SemanticIndex == 0 ) { if ( tangent != -1 ) throw new GltfException( "Duplicate tangent outputs in generated route." ); tangent = i; }
		}
		if ( tangent < 0 ) throw new GltfException( "Generated tangent output is missing." );
		outputs[tangent].Source = (uint)baseCount; outputs[tangent].Padding0 = 1;
		return new( descriptors, outputs );
	}

	/// <summary>Stable triangle-corner topology shared by all variant and raw override routes.</summary>
	internal static CornerTopology ExpandTopology( GltfGpuPrimitivePlan plan )
	{
		ArgumentNullException.ThrowIfNull( plan );
		if ( plan.Mode != 4 || plan.Indices.Length == 0 || plan.Indices.Length % 3 != 0 ) throw new GltfException( "Corner topology requires expanded triangles." );
		var source = new uint[plan.Indices.Length]; var indices = new uint[source.Length];
		for ( var i = 0; i < source.Length; i++ )
		{
			var index = plan.Indices[i];
			if ( index >= plan.SourceVertices.Length ) throw new GltfException( "Generated tangent route contains an invalid submitted index." );
			source[i] = plan.SourceVertices[index]; indices[i] = (uint)i;
		}
		return new( source, indices );
	}

	private static NVector3[] Decode3( GltfUploadStream stream, int count )
	{
		if ( stream.Components != 3 || stream.Count != count ) throw new GltfException( "Generated tangent VEC3 stream is invalid." );
		var result = new NVector3[count];
		for ( var i = 0; i < count; i++ ) result[i] = new( Read( stream, i, 0 ), Read( stream, i, 1 ), Read( stream, i, 2 ) );
		return result;
	}
	private static NVector2[] Decode2( GltfUploadStream stream, int count )
	{
		if ( stream.Components != 2 || stream.Count != count ) throw new GltfException( "Generated tangent UV stream is invalid." );
		var result = new NVector2[count];
		for ( var i = 0; i < count; i++ ) result[i] = new( Read( stream, i, 0 ), Read( stream, i, 1 ) );
		return result;
	}
	private static float Read( GltfUploadStream stream, int vertex, int component )
	{
		if ( stream.Storage is null || stream.ByteOffset < 0 || stream.ByteStride <= 0 ) throw new GltfException( "Generated tangent source stream has invalid storage." );
		var size = stream.ComponentType is 5120 or 5121 ? 1 : stream.ComponentType is 5122 or 5123 ? 2 : stream.ComponentType == 5126 ? 4 : 0;
		if ( size == 0 || stream.ByteStride < stream.Components * size ) throw new GltfException( "Generated tangent source component format is unsupported." );
		var offset = checked((long)stream.ByteOffset + (long)vertex * stream.ByteStride + component * size);
		if ( offset < 0 || offset + size > stream.Storage.Bytes.Length ) throw new GltfException( "Generated tangent source stream exceeds its storage." );
		var data = stream.Storage.Bytes.Span.Slice( (int)offset, size );
		float value = stream.ComponentType switch { 5120 => (sbyte)data[0], 5121 => data[0], 5122 => BinaryPrimitives.ReadInt16LittleEndian( data ), 5123 => BinaryPrimitives.ReadUInt16LittleEndian( data ), 5126 => BinaryPrimitives.ReadSingleLittleEndian( data ), _ => 0 };
		return !stream.Normalized ? value : stream.ComponentType switch { 5120 => Math.Max( -1, value / 127f ), 5121 => value / 255f, 5122 => Math.Max( -1, value / 32767f ), 5123 => value / 65535f, _ => throw new GltfException( "Invalid normalized tangent source." ) };
	}
}
