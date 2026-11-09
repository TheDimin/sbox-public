using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;

/// <summary>Actual GPU generated-vs-authored tangent frames under a two-joint weighted transform.</summary>
internal static class GeneratedTangentSkinProbe
{
	/// <summary>
	/// Independent unequal-weight, missing-NORMAL oracle. Position 0 and 1 use
	/// identity; position 2 uses a +90-degree X rotation. A producer probe
	/// must compare all three normals to this deformed face normal.
	/// </summary>
	public static int FlatNormalOracleControls()
	{
		var p0 = NVector3.Zero; var p1 = NVector3.UnitX; var p2 = NVector3.UnitZ;
		var expected = NVector3.Normalize( NVector3.Cross( p1 - p0, p2 - p0 ) );
		if ( NVector3.Distance( expected, -NVector3.UnitY ) > .000001f ) throw new InvalidOperationException( "Unequal-skin flat-normal literal is wrong." );
		// The current local-cross then per-vertex-normal-transform approach
		// incorrectly returns +Z at the identity-weighted first vertex.
		if ( NVector3.Distance( expected, NVector3.UnitZ ) < 1f ) throw new InvalidOperationException( "Flat-normal oracle failed its stale-local-normal negative control." );
		return 2;
	}
	public static async Task<int> Run( SceneWorld world, string cornerComputeShaderPath, Material litTemplate, Func<Task> awaitFrame )
	{
		ArgumentNullException.ThrowIfNull( awaitFrame );
		using var generatedDocument = Fixture( false ); using var authoredDocument = Fixture( true );
		using var generatedAsset = GltfGpuAsset.PrepareProjected( generatedDocument, cornerComputeShaderPath, 1024 * 1024, (_, _) => litTemplate, GltfUnlitMaterialPlan.Features.None );
		using var authoredAsset = GltfGpuAsset.PrepareProjected( authoredDocument, cornerComputeShaderPath, 1024 * 1024, (_, _) => litTemplate, GltfUnlitMaterialPlan.Features.None );
		using var generated = generatedAsset.CreateInstance( world ); using var authored = authoredAsset.CreateInstance( world );
		await awaitFrame();
		if ( generated.SubmittedRevision != generated.PoseRevision || authored.SubmittedRevision != authored.PoseRevision || generated.DrawCalls == 0 || authored.DrawCalls == 0 || generated.CallbackErrors.Any() || authored.CallbackErrors.Any() )
			throw new InvalidOperationException( "Generated or authored skin fixture did not submit its own output." );
		var a = Tangents( generated.Readback().Single() ); var b = Tangents( authored.Readback().Single() );
		// JOINT0 is identity with .25 weight; JOINT1 rotates +90deg around
		// source Z with .75 weight. Source T=+X maps to normalized (.25,.75,0).
		// The documented source-to-engine direction mapping is (x,y,z)->(z,x,y).
		var expected = new NVector4( 0, .316227766f, .948683298f, -1 );
		if ( a.Length != 3 || b.Length != 3 ) throw new InvalidOperationException( "Expected three output corners in each fixture." );
		for ( var i = 0; i < a.Length; i++ )
			if ( NVector4.Distance( a[i], expected ) > .0001f || NVector4.Distance( b[i], expected ) > .0001f || NVector4.Distance( a[i], b[i] ) > .00001f )
				throw new InvalidOperationException( $"Weighted generated/authored tangent mismatch at {i}: generated {a[i]}, authored {b[i]}, expected {expected}." );
		return 3;
	}

	/// <summary>Two forms and three placements each; actual GPU evidence only after frame submission.</summary>
	public static async Task<int> RunUnequalWeightFlatNormals( SceneWorld world, string cornerComputeShaderPath, Material litTemplate, Func<Task> awaitFrame )
	{
		ArgumentNullException.ThrowIfNull( awaitFrame ); var cases = 0;
		foreach ( var compiled in new[] { false, true } )
		{
			using var document = Fixture( false, missingNormals: true, unequalWeights: true, compiled: compiled );
			using var asset = GltfGpuAsset.PrepareProjected( document, cornerComputeShaderPath, 1024 * 1024, (_, _) => litTemplate, GltfUnlitMaterialPlan.Features.None );
			using var instance = asset.CreateInstance( world );
			foreach ( var placement in new[] { NMatrix.Identity, NMatrix.CreateScale( -1, 1, 1 ), NMatrix.CreateScale( 2, 3, 4 ) } )
			{
				instance.Placement = placement; var revision = instance.PoseRevision; await awaitFrame();
				if ( instance.SubmittedRevision != revision || instance.CallbackErrors.Any() ) throw new InvalidOperationException( "Flat-normal fixture did not submit." );
				var row = instance.Readback().Single(); var normal = row.Fields.Single( x => x.Semantic == "NORMAL" && x.SemanticIndex == 0 );
				if ( row.VertexCount != 3 || row.Indices.Length != 3 ) throw new InvalidOperationException( "Flat-normal topology changed." );
				var expectedIndices = placement.M11 < 0 ? new uint[] { 0, 2, 1 } : new uint[] { 0, 1, 2 };
				if ( !row.Indices.SequenceEqual( expectedIndices ) ) throw new InvalidOperationException( "Submitted winding differs from flat-normal oracle." );
				for ( var vertex = 0; vertex < 3; vertex++ )
				{
					var offset = vertex * row.VertexStride + normal.Offset;
					var actual = new NVector3( BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan(offset) ), BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan(offset+4) ), BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan(offset+8) ) );
					if ( !float.IsFinite(actual.X) || !float.IsFinite(actual.Y) || !float.IsFinite(actual.Z) || NVector3.Distance( actual, -NVector3.UnitZ ) > .0001f )
						throw new InvalidOperationException( $"Expected deformed face normal (0,0,-1), got {actual}; compiled={compiled}, placement={placement}." );
				}
				var tangent = Tangents( row ); var expectedTangent = new NVector4( 0, 1, 0, placement.M11 < 0 ? 1 : -1 );
				if ( tangent.Any( value => NVector4.Distance( value, expectedTangent ) > .0001f ) ) throw new InvalidOperationException( "Generated tangent is not orthogonal to the deformed face normal." );
				cases++;
			}
		}
		return cases;
	}

	private static NVector4[] Tangents( GltfGpuPrimitiveSnapshot row )
	{
		var field = row.Fields.Single( x => x.Semantic == "TANGENT" && x.SemanticIndex == 0 );
		if ( field.Components != 4 || row.Bytes.Length != row.VertexCount * row.VertexStride ) throw new InvalidOperationException( "Malformed tangent output." );
		var result = new NVector4[row.VertexCount];
		for ( var i = 0; i < result.Length; i++ )
		{
			var offset = i * row.VertexStride + field.Offset;
			float F( int component ) => BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( offset + component * 4 ) );
			result[i] = new( F(0), F(1), F(2), F(3) );
			if ( !float.IsFinite(result[i].X) || !float.IsFinite(result[i].Y) || !float.IsFinite(result[i].Z) || !float.IsFinite(result[i].W) ) throw new InvalidOperationException( "Nonfinite GPU tangent." );
		}
		return result;
	}

	private static GltfDocument Fixture( bool authored, bool missingNormals = false, bool unequalWeights = false, bool compiled = false )
	{
		var data = new byte[210];
		float[] positions = [0,0,0, 1,0,0, 0,1,0]; float[] uv = [0,0, 1,0, 0,1];
		for ( var i=0; i<positions.Length; i++ ) Write( i*4, positions[i] );
		for ( var i=0; i<3; i++ ) { Write( 36+i*12+8, 1 ); data[96+i*4+1]=1; Write(108+i*16,unequalWeights ? (i == 2 ? 0 : 1) : .25f); Write(108+i*16+4,unequalWeights ? (i == 2 ? 1 : 0) : .75f); Write(156+i*16,1); Write(156+i*16+12,-1); }
		for ( var i=0; i<uv.Length; i++ ) Write( 72+i*4, uv[i] );
		for ( var i=0; i<3; i++ ) BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan(204+i*2), (ushort)i );
		var tangent = authored ? ",\"TANGENT\":5" : "";
		var normal = missingNormals ? "" : ",\"NORMAL\":1";
		var jointRotation = unequalWeights ? "[0.7071067811865476,0,0,0.7071067811865476]" : "[0,0,0.7071067811865476,0.7071067811865476]";
		const string pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAABGdBTUEAAE4g3rEiDgAAAAxJREFUCNdjYOj4DwACEwGItIREUAAAAABJRU5ErkJggg==";
		var json = $$$"""
		{"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0,1,2]}],
		"nodes":[{"mesh":0,"skin":0},{},{"rotation":{{{jointRotation}}}}],"skins":[{"joints":[1,2]}],
		"buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String(data)}}}","byteLength":210}],
		"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":24},
		{"buffer":0,"byteOffset":96,"byteLength":12},{"buffer":0,"byteOffset":108,"byteLength":48},{"buffer":0,"byteOffset":156,"byteLength":48},{"buffer":0,"byteOffset":204,"byteLength":6}],
		"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},
		{"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":3,"type":"VEC2"},
		{"bufferView":3,"componentType":5121,"count":3,"type":"VEC4"},{"bufferView":4,"componentType":5126,"count":3,"type":"VEC4"},
		{"bufferView":5,"componentType":5126,"count":3,"type":"VEC4"},{"bufferView":6,"componentType":5123,"count":3,"type":"SCALAR"}],
		"images":[{"uri":"data:image/png;base64,{{{pixel}}}"}],"textures":[{"source":0}],"materials":[{"normalTexture":{"index":0}}],
		"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":2,"JOINTS_0":3,"WEIGHTS_0":4{{{normal}}}{{{tangent}}}},"indices":6,"material":0}]}]}
		""";
		var source = Encoding.UTF8.GetBytes( json );
		return GltfReader.Read( compiled ? GltfCompiler.Compile( source ) : source );
		void Write( int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan(offset), value );
	}
}
