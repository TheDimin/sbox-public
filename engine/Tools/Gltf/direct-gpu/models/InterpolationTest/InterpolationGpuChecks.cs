using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.InterpolationTestValidation;

/// <summary>Measures completed GPU output; never substitutes CPU results for GPU bytes.</summary>
internal static class InterpolationGpuChecks
{
	public sealed record NodeMeasurement( int Node, int Vertices, int Indices, int UndefinedNormalVertices,
		double MaximumPositionErrorInches, double MaximumNormalDistance, double MaximumNormalAngleDegrees,
		bool SourceTopologyMatches, bool Finite );
	public sealed record Measurement( int? Clip, float SourceTime, string Status, IReadOnlyList<NodeMeasurement> Nodes );

	public static Measurement Measure( string checkout, InterpolationReference reference, int? clip, float sourceTime,
		Matrix4x4 placement, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots ) => MeasureCore( checkout, reference, clip, sourceTime, placement, snapshots, false );

	public static Measurement MeasureComposed( string checkout, InterpolationReference reference, float sourceTime, Matrix4x4 placement, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots ) => MeasureCore( checkout, reference, null, sourceTime, placement, snapshots, true );

	private static Measurement MeasureCore( string checkout, InterpolationReference reference, int? clip, float sourceTime, Matrix4x4 placement, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots, bool composed )
	{
		if ( snapshots is null || snapshots.Count != reference.NodeCount ) throw new InvalidDataException( "Expected all ten separately posed nodes, including the plane." );
		var seen = new HashSet<int>(); var measurements = new List<NodeMeasurement>();
		var basis = new double[] { 0,1/.0254,0,0, 0,0,1/.0254,0, 1/.0254,0,0,0, 0,0,0,1 };
		var placementMatrix = InterpolationConformanceTests.Matrix( placement );
		foreach ( var row in snapshots )
		{
			if ( row.IndexDataOrigin != "gpu-readback" ) throw new InvalidDataException( "Expected actual GPU index readback, not CPU submission metadata." );
			if ( row.Node < 0 || row.Node >= reference.NodeCount || !seen.Add( row.Node ) || row.Primitive != 0 ) throw new InvalidDataException( "Missing, repeated or unknown node/primitive output." );
			var node = reference.Root.GetProperty( "nodes" )[row.Node]; var mesh = node.GetProperty( "mesh" ).GetInt32();
			if ( row.Mesh != mesh ) throw new InvalidDataException( "Output lost source mesh identity." );
			var primitive = reference.Root.GetProperty( "meshes" )[mesh].GetProperty( "primitives" )[0];
			var attributes = primitive.GetProperty( "attributes" );
			var positions = reference.FloatAccessor( attributes.GetProperty( "POSITION" ).GetInt32() );
			var normals = reference.FloatAccessor( attributes.GetProperty( "NORMAL" ).GetInt32() );
			if ( row.VertexCount < 1 || row.VertexStride < 1 || row.Bytes is null || row.Bytes.LongLength != (long)row.VertexCount * row.VertexStride || row.SourceVertices is null || row.SourceVertices.Length != row.VertexCount ) throw new InvalidDataException( "GPU byte extent or source mapping differs from declared layout." );
			var p = Field( row, "POSITION" ); var n = Field( row, "NORMAL" );
			var selectedClip = composed ? Enumerable.Range( 0, reference.ClipCount ).Where( i => reference.Channel( i ).Node == row.Node ).Select( i => (int?)i ).SingleOrDefault() : clip;
			var world = InterpolationReference.Multiply( InterpolationReference.Multiply( reference.World( row.Node, selectedClip, sourceTime ), basis ), placementMatrix );
			var expectedIndices = DrawIndices( checkout, reference.Root, primitive.GetProperty( "indices" ).GetInt32(), world );
			var topology = row.Indices is not null && row.Indices.Length == expectedIndices.Length;
			if ( topology ) for ( var index = 0; index < expectedIndices.Length; index++ ) topology &= row.Indices[index] < row.VertexCount && row.SourceVertices[row.Indices[index]] == expectedIndices[index];
			double maximumPosition = 0, maximumNormal = 0, maximumAngle = 0; var undefinedNormals = 0; var finite = true;
			for ( var outputVertex = 0; outputVertex < row.VertexCount; outputVertex++ )
			{
				var sourceVertex = row.SourceVertices[outputVertex];
				if ( sourceVertex >= positions.Length ) throw new InvalidDataException( "GPU output maps outside the authored accessor." );
				var expectedPosition = InterpolationReference.Transform( positions[sourceVertex], world );
				var observedPosition = Read3( row.Bytes, checked(outputVertex * row.VertexStride + p.Offset) );
				var observedNormal = Read3( row.Bytes, checked(outputVertex * row.VertexStride + n.Offset) );
				finite &= observedPosition.All( double.IsFinite ) && observedNormal.All( double.IsFinite );
				maximumPosition = Math.Max( maximumPosition, Distance( observedPosition, expectedPosition ) );
				var expectedNormal = Normal( normals[sourceVertex], world );
				if ( expectedNormal is null ) { undefinedNormals++; continue; }
				maximumNormal = Math.Max( maximumNormal, Distance( observedNormal, expectedNormal ) );
				var length = Math.Sqrt( observedNormal.Sum( value => value * value ) );
				if ( length == 0 || !double.IsFinite( length ) ) { finite = false; continue; }
				var cosine = observedNormal.Zip( expectedNormal, (x, y) => x * y ).Sum() / length;
				maximumAngle = Math.Max( maximumAngle, Math.Acos( Math.Clamp( cosine, -1, 1 ) ) * 180 / Math.PI );
			}
			measurements.Add( new NodeMeasurement( row.Node, row.VertexCount, row.Indices?.Length ?? 0, undefinedNormals, maximumPosition, maximumNormal, maximumAngle, topology, finite ) );
		}
		return new Measurement( clip, sourceTime, "measured-not-accepted", measurements.OrderBy( value => value.Node ).ToArray() );
	}

	/// <summary>Caller must record/review measured tolerances before treating this as acceptance.</summary>
	public static void RequireWithin( Measurement result, double positionToleranceInches, double normalTolerance, double normalAngleToleranceDegrees )
	{
		if ( !double.IsFinite( positionToleranceInches ) || positionToleranceInches < 0 || !double.IsFinite( normalTolerance ) || normalTolerance < 0 || !double.IsFinite( normalAngleToleranceDegrees ) || normalAngleToleranceDegrees < 0 ) throw new ArgumentOutOfRangeException( "Explicit finite nonnegative tolerances are required." );
		foreach ( var node in result.Nodes ) if ( !node.Finite || !node.SourceTopologyMatches || node.MaximumPositionErrorInches > positionToleranceInches || node.MaximumNormalDistance > normalTolerance || node.MaximumNormalAngleDegrees > normalAngleToleranceDegrees ) throw new InvalidDataException( "GPU readback differs from independent source: " + JsonSerializer.Serialize( node ) );
	}

	/// <summary>Literal independent triangles for a diagnostic Graphics.Draw reference, never native Model/Mesh.</summary>
	public static Sandbox.Vertex[] ReferenceTriangles( string checkout, InterpolationReference reference, int nodeIndex, int? clip, float sourceTime, Matrix4x4 placement )
	{
		var node = reference.Root.GetProperty( "nodes" )[nodeIndex];
		var primitive = reference.Root.GetProperty( "meshes" )[node.GetProperty( "mesh" ).GetInt32()].GetProperty( "primitives" )[0];
		var attributes = primitive.GetProperty( "attributes" );
		var positions = reference.FloatAccessor( attributes.GetProperty( "POSITION" ).GetInt32() );
		var normals = reference.FloatAccessor( attributes.GetProperty( "NORMAL" ).GetInt32() );
		var uvs = attributes.TryGetProperty( "TEXCOORD_0", out var uvAccessor ) ? reference.FloatAccessor( uvAccessor.GetInt32() ) : null;
		var basis = new double[] { 0,1/.0254,0,0, 0,0,1/.0254,0, 1/.0254,0,0,0, 0,0,0,1 };
		var world = InterpolationReference.Multiply( InterpolationReference.Multiply( reference.World( nodeIndex, clip, sourceTime ), basis ), InterpolationConformanceTests.Matrix( placement ) );
		return DrawIndices( checkout, reference.Root, primitive.GetProperty( "indices" ).GetInt32(), world ).Select( source =>
		{
			var p = InterpolationReference.Transform( positions[source], world );
			var n = Normal( normals[source], world ) ?? new double[] { 0, 0, 1 };
			var result = new Sandbox.Vertex( new global::Vector3( (float)p[0], (float)p[1], (float)p[2] ) )
			{
				Normal = new global::Vector3( (float)n[0], (float)n[1], (float)n[2] ),
				Color = global::Color32.White
			};
			var seed = Math.Abs( n[2] ) < .9 ? new double[] { 0, 0, 1 } : new double[] { 0, 1, 0 };
			var tangent = new double[] { seed[1]*n[2] - seed[2]*n[1], seed[2]*n[0] - seed[0]*n[2], seed[0]*n[1] - seed[1]*n[0] };
			var tangentLength = Math.Sqrt( tangent.Sum( value => value * value ) );
			result.Tangent = new global::Vector4( (float)(tangent[0] / tangentLength), (float)(tangent[1] / tangentLength), (float)(tangent[2] / tangentLength), 1 );
			if ( uvs is not null ) result.TexCoord0 = new global::Vector4( (float)uvs[source][0], (float)uvs[source][1], 0, 0 );
			return result;
		} ).ToArray();
	}

	private static GltfGpuField Field( GltfGpuPrimitiveSnapshot row, string semantic )
	{
		var fields = row.Fields.Where( field => field.Semantic == semantic && field.SemanticIndex == 0 ).ToArray();
		if ( fields.Length != 1 || fields[0].ComponentType != 5126 || fields[0].Components < 3 || fields[0].Offset < 0 || fields[0].Offset % 4 != 0 || fields[0].Offset > row.VertexStride - 12 ) throw new InvalidDataException( "Missing or invalid declared FLOAT3 output field: " + semantic );
		return fields[0];
	}
	private static double[] Read3( byte[] bytes, int offset ) => [BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset, 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 4, 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 8, 4 ) )];
	private static double Distance( double[] a, double[] b ) => Math.Sqrt( a.Zip( b, (x, y) => (x-y)*(x-y) ).Sum() );
	private static double Determinant3( double[] matrix ) => matrix[0]*(matrix[5]*matrix[10]-matrix[6]*matrix[9]) - matrix[1]*(matrix[4]*matrix[10]-matrix[6]*matrix[8]) + matrix[2]*(matrix[4]*matrix[9]-matrix[5]*matrix[8]);
	private static double[]? Normal( double[] source, double[] matrix )
	{
		var a = matrix[0]; var b = matrix[1]; var c = matrix[2]; var d = matrix[4]; var e = matrix[5]; var f = matrix[6]; var g = matrix[8]; var h = matrix[9]; var i = matrix[10];
		var determinant = a*(e*i-f*h) - b*(d*i-f*g) + c*(d*h-e*g);
		if ( determinant == 0 ) return null;
		var result = new double[] {
			(source[0]*(e*i-f*h) + source[1]*(c*h-b*i) + source[2]*(b*f-c*e)) / determinant,
			(source[0]*(f*g-d*i) + source[1]*(a*i-c*g) + source[2]*(c*d-a*f)) / determinant,
			(source[0]*(d*h-e*g) + source[1]*(b*g-a*h) + source[2]*(a*e-b*d)) / determinant };
		var length = Math.Sqrt( result.Sum( value => value * value ) );
		if ( !double.IsFinite( length ) || length == 0 ) throw new InvalidDataException( "Independent normal is invalid for a nonsingular transform." );
		return result.Select( value => value / length ).ToArray();
	}
	private static uint[] DrawIndices( string checkout, JsonElement root, int accessorIndex, double[] world )
	{
		var indices = SourceIndices( checkout, root, accessorIndex );
		// glTF front faces follow transform determinant sign. A reflected
		// instance needs reversed raster winding to retain authored front faces.
		if ( Determinant3( world ) < 0 ) for ( var triangle = 0; triangle < indices.Length; triangle += 3 ) (indices[triangle + 1], indices[triangle + 2]) = (indices[triangle + 2], indices[triangle + 1]);
		return indices;
	}
	private static uint[] SourceIndices( string checkout, JsonElement root, int accessorIndex )
	{
		var accessor = root.GetProperty( "accessors" )[accessorIndex];
		if ( accessor.GetProperty( "type" ).GetString() != "SCALAR" || accessor.GetProperty( "componentType" ).GetInt32() != 5121 || accessor.TryGetProperty( "sparse", out _ ) ) throw new InvalidDataException( "Pinned sample topology reference expects nonsparse U8 indices." );
		var view = root.GetProperty( "bufferViews" )[accessor.GetProperty( "bufferView" ).GetInt32()];
		var uri = root.GetProperty( "buffers" )[view.GetProperty( "buffer" ).GetInt32()].GetProperty( "uri" ).GetString()!;
		if ( uri != Path.GetFileName( uri ) ) throw new InvalidDataException( "Unexpected index buffer URI." );
		var relative = "Models/InterpolationTest/glTF/" + uri;
		var bytes = File.ReadAllBytes( Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets", relative ) );
		using var manifest = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( checkout, "research/gltf-validation/corpus-case-manifest.json" ) ) );
		if ( Convert.ToHexStringLower( SHA256.HashData( bytes ) ) != manifest.RootElement.GetProperty( "files" ).GetProperty( relative ).GetProperty( "sha256" ).GetString() ) throw new InvalidDataException( "Index source hash changed." );
		var offset = (view.TryGetProperty( "byteOffset", out var vo ) ? vo.GetInt32() : 0) + (accessor.TryGetProperty( "byteOffset", out var ao ) ? ao.GetInt32() : 0);
		var count = accessor.GetProperty( "count" ).GetInt32();
		if ( offset < 0 || count < 1 || (long)offset + count > bytes.Length ) throw new InvalidDataException( "Index source range exceeds its buffer." );
		return bytes.AsSpan( offset, count ).ToArray().Select( value => (uint)value ).ToArray();
	}
}
