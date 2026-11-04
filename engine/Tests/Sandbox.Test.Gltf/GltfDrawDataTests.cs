using System.Numerics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfDrawDataTests
{
	[TestMethod]
	public void DrawDataReportsDeformationInsteadOfImplyingAnEvaluatedStaticPose()
	{
		using var document = GltfReader.Read( System.Text.Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"accessors":[
			{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			{"componentType":5121,"count":3,"type":"VEC4"},
			{"componentType":5126,"count":3,"type":"VEC4"}],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0,"JOINTS_0":1,"WEIGHTS_0":2},"targets":[{"POSITION":0}]}]}]}
			""" ) );
		var data = document.CreateDrawData( 0, 0 );
		Assert.IsTrue( data.HasSkinning );
		Assert.IsTrue( data.HasMorphTargets );
	}

	[TestMethod]
	public void DrawDataOwnsConvertedPositionsAndPreservesFloatVertexColor()
	{
		GltfDrawData data;
		using ( var document = Read( new Dictionary<string, float[]>
		{
			["POSITION"] = [1, 2, 3, 0, 0, 0, 0, 1, 0],
			["COLOR_0"] = [0.123456f, 0.5f, 0.9f, 1, 0, 0, 0, 1, 0]
		} ) ) data = document.CreateDrawData( 0, 0 );

		Assert.AreEqual( 3, data.Positions.Length );
		AssertVector( new Vector3( 3, 1, 2 ) / 0.0254f, data.Positions.Span[0] );
		Assert.AreEqual( new Vector4( 0.123456f, 0.5f, 0.9f, 1 ), data.Colors.Span[0] );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, data.Indices.ToArray() );
		Assert.IsTrue( data.Normals.IsEmpty );
		Assert.IsTrue( data.Tangents.IsEmpty );
		Assert.IsFalse( data.HasSkinning );
		Assert.IsFalse( data.HasMorphTargets );
	}

	[TestMethod]
	public void DirectionsAreNotScaledAndTangentHandednessSurvives()
	{
		using var document = Read( new Dictionary<string, float[]>
		{
			["POSITION"] = new float[9],
			["NORMAL"] = [0, 1, 0, 0, 1, 0, 0, 1, 0],
			["TANGENT"] = [1, 0, 0, -1, 1, 0, 0, 1, 1, 0, 0, -1]
		} );
		var data = document.CreateDrawData( 0, 0 );
		Assert.AreEqual( Vector3.UnitZ, data.Normals.Span[0] );
		Assert.AreEqual( new Vector4( 0, 1, 0, -1 ), data.Tangents.Span[0] );
		Assert.AreEqual( Vector4.One, data.Colors.Span[1] );
	}

	[TestMethod]
	public void EveryUvSetSurvivesWithoutImplicitVFlip()
	{
		using var document = Read( new Dictionary<string, float[]>
		{
			["POSITION"] = new float[9],
			["TEXCOORD_0"] = [0, 0, 1, 0, 0, 1],
			["TEXCOORD_1"] = [-1, 2, 3, 4, 5, 6],
			["TEXCOORD_2"] = [0.25f, 0.75f, 0, 0, 0, 0]
		} );
		var data = document.CreateDrawData( 0, 0 );
		Assert.AreEqual( 3, data.TexCoords.Count );
		Assert.AreEqual( new Vector2( -1, 2 ), data.TexCoords[1].Span[0] );
		Assert.AreEqual( new Vector2( 0.25f, 0.75f ), data.TexCoords[2].Span[0] );
	}

	[TestMethod]
	[DataRow( 0, 0, 4 )]
	[DataRow( 1, 1, 4 )]
	[DataRow( 2, 1, 8 )]
	[DataRow( 3, 1, 6 )]
	[DataRow( 4, 4, 6 )]
	[DataRow( 5, 4, 6 )]
	[DataRow( 6, 4, 6 )]
	public void CoreModesBecomeListsWithoutChangingWinding( int mode, int listMode, int count )
	{
		var vertices = mode == 4 ? 6 : 4;
		using var document = Read( new Dictionary<string, float[]> { ["POSITION"] = new float[vertices * 3] }, mode );
		var data = document.CreateDrawData( 0, 0 );
		Assert.AreEqual( listMode, data.Mode );
		Assert.AreEqual( count, data.Indices.Length );
		if ( mode == 5 ) CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 2, 1, 3 }, data.Indices.ToArray() );
		if ( mode == 6 ) CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 0, 2, 3 }, data.Indices.ToArray() );
	}

	private static void AssertVector( Vector3 expected, Vector3 actual )
	{
		Assert.IsTrue( Vector3.Distance( expected, actual ) < 0.0001f, $"Expected {expected}, got {actual}." );
	}

	private static GltfDocument Read( Dictionary<string, float[]> attributes, int mode = 4 )
	{
		var binary = new List<byte>();
		var views = new List<object>();
		var accessors = new List<object>();
		var indices = new Dictionary<string, int>();
		foreach ( var entry in attributes )
		{
			var components = entry.Key == "TANGENT" ? 4 : entry.Key.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) ? 2 : 3;
			indices.Add( entry.Key, accessors.Count );
			views.Add( new { buffer = 0, byteOffset = binary.Count, byteLength = entry.Value.Length * 4 } );
			var accessor = new Dictionary<string, object>
			{
				["bufferView"] = accessors.Count, ["componentType"] = 5126,
				["count"] = entry.Value.Length / components, ["type"] = "VEC" + components
			};
			if ( entry.Key == "POSITION" ) { accessor["min"] = new float[3]; accessor["max"] = new float[3]; }
			accessors.Add( accessor );
			foreach ( var value in entry.Value ) binary.AddRange( BitConverter.GetBytes( value ) );
		}
		return GltfReader.Read( JsonSerializer.SerializeToUtf8Bytes( new
		{
			asset = new { version = "2.0" }, buffers = new[] { new { uri = "mesh.bin", byteLength = binary.Count } },
			bufferViews = views, accessors, meshes = new[] { new { primitives = new[] { new { attributes = indices, mode } } } }
		} ), _ => binary.ToArray() );
	}
}
