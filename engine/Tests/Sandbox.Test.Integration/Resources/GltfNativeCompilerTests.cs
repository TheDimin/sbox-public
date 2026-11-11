using Sandbox.Gltf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ResourceTests;

/// <summary>
/// The native compiler writes ready-to-upload model data into the compiled glTF, and the loader builds an ordinary
/// native model from it. A second compile with the same cache reuses every unit whose inputs did not change.
/// </summary>
[TestClass]
public sealed class GltfNativeCompilerTests
{
	// One triangle (positions and normals), used by a base node with a two-level MSFT_lod chain and by a plain node.
	private static string Source( string baseColor = "[1,0.5,0.25,1]" )
	{
		var positions = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 };
		var normals = new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 };
		var bytes = MemoryMarshal.AsBytes( positions.Concat( normals ).ToArray().AsSpan() ).ToArray();
		var uri = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes );
		return $$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["MSFT_lod"],
			 "buffers":[{"byteLength":72,"uri":"{{{uri}}}"}],
			 "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},
			              {"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"}],
			 "materials":[{"pbrMetallicRoughness":{"baseColorFactor":{{{baseColor}}}}}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1},"material":0}]}],
			 "nodes":[{"mesh":0,"extensions":{"MSFT_lod":{"ids":[1]}},"extras":{"MSFT_screencoverage":[0.5,0.1]}},{"mesh":0},{"mesh":0,"translation":[3,0,0]}],
			 "scenes":[{"nodes":[0,2]}],"scene":0}
			""";
	}

	private static GltfDocument Read( string source ) => GltfReader.Read( Encoding.UTF8.GetBytes( source ), supportedExtensions: GltfModelLoader.SupportedExtensions );

	[TestMethod]
	public void CompilesEveryDrawWithItsNativeLodLevel()
	{
		using var document = Read( Source() );
		var (header, blobs) = GltfNativeCompiler.Compile( document, scene: false );
		Assert.AreEqual( 3, header.Meshes.Length );
		var byNode = header.Meshes.ToDictionary( x => x.Node );
		Assert.AreEqual( byNode[0].Vertices, byNode[1].Vertices, "rows with the same bake share one stored blob" );
		Assert.AreEqual( 1, byNode[0].LodMask, "base level" );
		Assert.AreEqual( 2, byNode[1].LodMask, "lower level" );
		Assert.AreEqual( 0xFF, byNode[2].LodMask, "plain node draws at every level" );
		// Two levels, and an empty third level past the last threshold (0.1), where the chain is hidden.
		Assert.AreEqual( 3, header.LodDistances.Length );
		Assert.AreEqual( 0, header.LodDistances[0] );
		Assert.IsTrue( header.LodDistances[1] > 0 && header.LodDistances[2] > header.LodDistances[1] );
		Assert.AreEqual( 1, header.Materials.Length, "one shared material" );
		Assert.AreEqual( "shaders/gltf_surface_five_uv.shader", header.Materials[0].Shader );
		foreach ( var mesh in header.Meshes )
		{
			Assert.AreEqual( 3, mesh.VertexCount );
			Assert.AreEqual( 3, mesh.IndexCount );
			Assert.AreEqual( mesh.Stride * mesh.VertexCount, mesh.Vertices.Length );
			Assert.AreEqual( mesh.IndexCount * 4, mesh.Indices.Length );
			CollectionAssert.Contains( mesh.Fields.Select( x => x.Semantic ).ToArray(), "position" );
		}
		// The plain node is translated 3 m along glTF +X; positions are in engine inches with Z up.
		Assert.AreEqual( 3 / 0.0254f, byNode[2].Min[1], 0.01f );
	}

	[TestMethod]
	public void SecondCompileOfAMaterialChangeReusesGeometry()
	{
		var cache = new GltfNativeCompileCache();
		using ( var first = Read( Source() ) ) GltfNativeCompiler.Compile( first, cache: cache );
		Assert.AreEqual( 1, cache.Compiled.Rows, "a scene stores the mesh once, relative to each node, so all three rows share one bake" );

		using var second = Read( Source( "[0,1,0,1]" ) );
		var (header, _) = GltfNativeCompiler.Compile( second, cache: cache );
		Assert.AreEqual( 0, cache.Compiled.Primitives, "no primitive decoded again" );
		Assert.AreEqual( 0, cache.Compiled.Rows, "no vertices baked again" );
		Assert.AreEqual( 3, cache.Reused.Rows );
		var color = header.Materials[0].Vectors.Single( x => x.Key == "g_vGltfBaseColor" ).Value;
		CollectionAssert.AreEqual( new[] { 0f, 1f, 0f, 1f }, color, "the new material value is in the recipe" );
	}

	[TestMethod]
	public void LoaderBuildsANativeModelFromTheAppendedChunk()
	{
		using var document = Read( Source() );
		var glb = GltfCompiler.Compile( Encoding.UTF8.GetBytes( Source() ), _ => throw new InvalidOperationException(), GltfModelLoader.SupportedExtensions, 256L * 1024 * 1024, true );
		var (singleHeader, singleBlobs) = GltfNativeCompiler.Compile( document, scene: false );
		var compiled = GltfNativePack.Append( glb, singleHeader, singleBlobs );
		using ( var reread = GltfModelLoader.ReadDocument( compiled ) )
			Assert.AreEqual( 3, reread.Root.GetProperty( "nodes" ).GetArrayLength(), "the GLB stays readable as glTF" );
		var pack = GltfNativePack.TryRead( compiled );
		Assert.IsNotNull( pack );
		var model = GltfNativeModelBuilder.Build( pack, "gltf_native_compiler_test.glb" );
		Assert.IsNotNull( model );
		Assert.IsTrue( model.IsGltfNative );
		Assert.IsNull( model.GltfResource, "drawn by the native renderer, not the glTF callback" );
		Assert.AreEqual( 3, model.MeshCount );
		var masks = model.MeshInfo.Meshes.Select( x => x.LodMask ).Order().ToArray();
		CollectionAssert.AreEqual( new[] { 1, 2, 0xFF }, masks );
		Assert.AreEqual( "shaders/gltf_surface_five_uv.shader", model.Materials[0].Shader.ResourcePath );
	}

	[TestMethod]
	public void StaticSceneSharesGeometryAndKeepsEachChainAsOneLodModel()
	{
		using var document = Read( Source() );
		var (header, _) = GltfNativeCompiler.Compile( document );
		// Node 0 with its LOD level node 1 is one placement; node 2 is another.
		Assert.AreEqual( 2, header.Nodes.Length );
		Assert.AreEqual( 2, header.Geometries.Length, "the chain and the plain node have different meshes and LODs" );
		var chain = header.Geometries[header.Nodes.Single( x => x.Node == 0 ).Geometry];
		CollectionAssert.AreEqual( new[] { 1, 2 }, chain.Meshes.Select( x => header.Meshes[x].LodMask ).ToArray() );
		Assert.AreEqual( 3, chain.LodDistances.Length, "two levels and the hidden level past the last threshold" );
		var plain = header.Nodes.Single( x => x.Node == 2 );
		Assert.AreEqual( 3 / 0.0254f, plain.Position[1], 0.01f, "the translation is the node's placement, not baked into its vertices" );
		Assert.AreEqual( 1, header.Geometries[plain.Geometry].LodDistances.Length );
	}

	[TestMethod]
	public void MeshesThatShareAccessorsWithOtherMaterialsShareOneGeometry()
	{
		var source = Source().Replace( "\"materials\":[{\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,0.5,0.25,1]}}]", "\"materials\":[{\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1]}},{\"pbrMetallicRoughness\":{\"baseColorFactor\":[0,0,0,1]}}]" )
			.Replace( "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1},\"material\":0}]}]",
				"\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1},\"material\":0}]},{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"NORMAL\":1},\"material\":1}]}]" )
			.Replace( "{\"mesh\":0,\"translation\":[3,0,0]}", "{\"mesh\":1,\"translation\":[3,0,0]}" )
			.Replace( "\"extensionsUsed\":[\"MSFT_lod\"],", "" ).Replace( ",\"extensions\":{\"MSFT_lod\":{\"ids\":[1]}},\"extras\":{\"MSFT_screencoverage\":[0.5,0.1]}", "" );
		using var document = Read( source );
		var (header, _) = GltfNativeCompiler.Compile( document );
		var white = header.Nodes.Single( x => x.Node == 0 );
		var black = header.Nodes.Single( x => x.Node == 2 );
		Assert.AreEqual( white.Geometry, black.Geometry, "black and white pieces are one geometry" );
		Assert.AreNotEqual( white.Group, black.Group, "and differ by material group" );
		Assert.AreEqual( 2, header.Geometries[white.Geometry].Groups.Length );
		Assert.AreEqual( 2, header.Materials.Length );
	}

	[TestMethod]
	public void LoaderBuildsAStaticSceneOfSharedGeometryModels()
	{
		using var document = Read( Source() );
		var glb = GltfCompiler.Compile( Encoding.UTF8.GetBytes( Source() ), _ => throw new InvalidOperationException(), GltfModelLoader.SupportedExtensions, 256L * 1024 * 1024, true );
		var model = GltfModelLoader.TryBuildNative( GltfNativeCompiler.AppendTo( glb, document ), "gltf_native_scene_test.glb", "test" );
		Assert.IsNotNull( model?.GltfNativeScene );
		Assert.AreEqual( 2, model.GltfNativeScene.Nodes.Length );
		Assert.AreEqual( 0, model.MeshCount, "the scene model draws nothing itself" );
		Assert.IsTrue( model.Bounds.Size.Length > 0 );
		var chain = model.GltfNativeScene.Geometries[model.GltfNativeScene.Nodes.Single( x => x.Index == 0 ).Geometry];
		CollectionAssert.AreEqual( new[] { 1, 2 }, chain.MeshInfo.Meshes.Select( x => x.LodMask ).Order().ToArray(), "the chain is one native model with LOD levels" );
	}

	/// <summary>A flat grid of quads with normals and UVs; node 1 is a generated LOD of node 0 that keeps the given ratio.</summary>
	private static string GridSource( float ratio )
	{
		const int n = 16;
		var positions = new List<float>(); var normals = new List<float>(); var uvs = new List<float>(); var indices = new List<uint>();
		for ( var y = 0; y <= n; y++ )
			for ( var x = 0; x <= n; x++ ) { positions.AddRange( [x / (float)n, 0, y / (float)n] ); normals.AddRange( [0, 1, 0] ); uvs.AddRange( [x / (float)n, y / (float)n] ); }
		for ( var y = 0; y < n; y++ )
			for ( var x = 0; x < n; x++ )
			{
				var a = (uint)(y * (n + 1) + x); var b = a + 1; var c = a + n + 1; var d = c + 1;
				indices.AddRange( [a, c, b, b, c, d] );
			}
		var bytes = MemoryMarshal.AsBytes( positions.ToArray().AsSpan() ).ToArray().Concat( MemoryMarshal.AsBytes( normals.ToArray().AsSpan() ).ToArray() )
			.Concat( MemoryMarshal.AsBytes( uvs.ToArray().AsSpan() ).ToArray() ).Concat( MemoryMarshal.AsBytes( indices.ToArray().AsSpan() ).ToArray() ).ToArray();
		var count = (n + 1) * (n + 1);
		var p = count * 12; var u = count * 8; var i = indices.Count * 4;
		var uri = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes );
		return $$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["MSFT_lod"],
			 "buffers":[{"byteLength":{{{bytes.Length}}},"uri":"{{{uri}}}"}],
			 "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":{{{p}}}},{"buffer":0,"byteOffset":{{{p}}},"byteLength":{{{p}}}},{"buffer":0,"byteOffset":{{{2 * p}}},"byteLength":{{{u}}}},{"buffer":0,"byteOffset":{{{2 * p + u}}},"byteLength":{{{i}}}}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":{{{count}}},"type":"VEC3","min":[0,0,0],"max":[1,0,1]},{"bufferView":1,"componentType":5126,"count":{{{count}}},"type":"VEC3"},
			              {"bufferView":2,"componentType":5126,"count":{{{count}}},"type":"VEC2"},{"bufferView":3,"componentType":5125,"count":{{{indices.Count}}},"type":"SCALAR"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},"indices":3}]}],
			 "nodes":[{"mesh":0,"extensions":{"MSFT_lod":{"ids":[1]} } },{"mesh":0,"extras":{"sbox_lod":{"ratio":{{{ratio.ToString( System.Globalization.CultureInfo.InvariantCulture )}}},"error":0.5} } }],
			 "scenes":[{"nodes":[0]}],"scene":0}
			""";
	}

	[TestMethod]
	public void GeneratedLodKeepsFewerTrianglesAndSharesTheBaseVertices()
	{
		using var document = Read( GridSource( 0.25f ) );
		var (header, _) = GltfNativeCompiler.Compile( document );
		var geometry = header.Geometries.Single();
		var levels = geometry.Meshes.Select( x => header.Meshes[x] ).OrderBy( x => x.LodMask ).ToArray();
		Assert.AreEqual( 2, levels.Length );
		Assert.AreEqual( 16 * 16 * 2 * 3, levels[0].IndexCount );
		Assert.IsTrue( levels[1].IndexCount <= levels[0].IndexCount * 0.3, $"LOD 1 keeps about a quarter of the triangles: {levels[1].IndexCount} of {levels[0].IndexCount}" );
		Assert.IsTrue( levels[1].IndexCount >= 3 );
		Assert.AreEqual( levels[0].Vertices, levels[1].Vertices, "the generated level reuses the base vertex buffer" );
	}

	[TestMethod]
	public void PreviewWithACacheIsNativeAndRecompilesOnlyWhatChanged()
	{
		var cache = new GltfNativeCompileCache();
		var first = GltfModelLoader.LoadPreview( "gltf_native_preview.gltf", Encoding.UTF8.GetBytes( Source() ), cache );
		Assert.IsTrue( first.IsGltfNative );
		var second = GltfModelLoader.LoadPreview( "gltf_native_preview.gltf", Encoding.UTF8.GetBytes( Source( "[0,0,1,1]" ) ), cache );
		Assert.IsTrue( second.IsGltfNative );
		Assert.AreEqual( 0, cache.Compiled.Rows, "a material edit bakes no vertices" );
		Assert.AreEqual( 0, cache.Compiled.Primitives, "a material edit decodes no primitive" );
		Assert.IsFalse( GltfModelLoader.LoadPreview( "gltf_native_preview.gltf", Encoding.UTF8.GetBytes( Source() ) ).IsGltfNative, "without a cache the preview keeps the source path" );
	}
}

/// <summary>Import and load cost of a real asset. Set GLTF_NATIVE_MEASURE to a .glb path.</summary>
[TestClass]
public sealed class GltfNativeCompilerMeasurement
{
	[TestMethod]
	public void MeasureRealAsset()
	{
		var path = Environment.GetEnvironmentVariable( "GLTF_NATIVE_MEASURE" );
		if ( string.IsNullOrEmpty( path ) ) Assert.Inconclusive( "Set GLTF_NATIVE_MEASURE to a .glb file." );
		var source = System.IO.File.ReadAllBytes( path );
		var clock = System.Diagnostics.Stopwatch.StartNew();
		var glb = GltfCompiler.Compile( source, _ => throw new InvalidOperationException(), GltfModelLoader.SupportedExtensions, 1024L * 1024 * 1024, true );
		var packageMs = clock.Elapsed.TotalMilliseconds;
		using var document = GltfModelLoader.ReadDocument( glb );
		clock.Restart();
		var cache = new GltfNativeCompileCache();
		var compiled = GltfNativeCompiler.AppendTo( glb, document, cache: cache );
		var compileMs = clock.Elapsed.TotalMilliseconds;
		var phases = cache.Timings;
		clock.Restart();
		var pack = GltfNativePack.TryRead( compiled );
		var readMs = clock.Elapsed.TotalMilliseconds;
		clock.Restart();
		var model = GltfNativeModelBuilder.Build( pack, "gltf_native_measure.glb" );
		var buildMs = clock.Elapsed.TotalMilliseconds;
		clock.Restart();
		GltfNativeModelBuilder.Build( pack, "gltf_native_measure_2.glb" );
		var secondBuildMs = clock.Elapsed.TotalMilliseconds;
		var secondTimings = GltfNativeModelBuilder.LastTimings;
		clock.Restart();
		using ( var again = GltfModelLoader.ReadDocument( glb ) ) GltfNativeCompiler.Compile( again, cache: cache );
		var recompileMs = clock.Elapsed.TotalMilliseconds;
		var vertexBytes = pack.Header.Meshes.Select( x => x.Vertices ).Distinct().Sum( x => (long)x.Length );
		var textureBytes = pack.Header.Textures.Sum( x => (long)x.Length );
		long lz4Textures = 0, lz4Vertices = 0; double decodeMs = 0;
		foreach ( var blob in pack.Header.Textures )
		{
			var raw = pack.Get( blob ).ToArray();
			var packed = Sandbox.Compression.LZ4.CompressBlock( raw, System.IO.Compression.CompressionLevel.Optimal );
			lz4Textures += packed.Length;
			var output = new byte[raw.Length];
			var decode = System.Diagnostics.Stopwatch.StartNew();
			Sandbox.Compression.LZ4.DecompressBlock( packed, output );
			decodeMs += decode.Elapsed.TotalMilliseconds;
		}
		foreach ( var blob in pack.Header.Meshes.Select( x => x.Vertices ).Distinct() )
			lz4Vertices += Sandbox.Compression.LZ4.CompressBlock( pack.Get( blob ).ToArray(), System.IO.Compression.CompressionLevel.Optimal ).Length;
		Assert.Inconclusive( $"lz4 textures {lz4Textures:N0} B (decode {decodeMs:0} ms), lz4 vertices {lz4Vertices:N0} B; source {source.Length:N0} B, package {packageMs:0} ms, native compile {compileMs:0} ms {phases}, compiled {compiled.Length:N0} B " +
			$"(vertices {vertexBytes:N0} B, textures {textureBytes:N0} B in {pack.Header.Textures.Length}), read {readMs:0} ms, build {buildMs:0} ms {GltfNativeModelBuilder.LastTimings}, " +
			$"second build {secondBuildMs:0} ms {secondTimings}, meshes {model.MeshCount}, materials {pack.Header.Materials.Length}, recompile with cache {recompileMs:0} ms " +
			$"(compiled {cache.Compiled}, reused {cache.Reused}), warnings [{string.Join( "; ", pack.Header.Warnings )}]" );
	}
}

/// <summary>
/// Skinned and animated Khronos samples through the native path: each clip played halfway gives the same bone world
/// transforms as the glTF pose. Set GLTF_NATIVE_SAMPLES to the khronos folder and run with SBOX_TEST_GRAPHICS=1.
/// </summary>
[TestClass]
public sealed class GltfNativeSkeletonSamples
{
	[TestMethod]
	public void ClipsMatchTheGltfPose()
	{
		var folder = Environment.GetEnvironmentVariable( "GLTF_NATIVE_SAMPLES" );
		if ( string.IsNullOrEmpty( folder ) ) Assert.Inconclusive( "Set GLTF_NATIVE_SAMPLES to the khronos sample folder." );
		var report = new StringBuilder();
		var failures = new List<string>();
		foreach ( var name in new[] { "BoxAnimated", "RiggedSimple", "RiggedFigure", "CesiumMan", "Fox", "BrainStem", "SimpleSkin", "AnimatedCube", "SimpleMorph", "MorphPrimitivesTest", "AnimatedMorphCube" } )
		{
			var path = System.IO.Path.Combine( folder, name, "glTF-Binary", name + ".glb" );
			if ( !System.IO.File.Exists( path ) ) { report.Append( $"{name}: no glb; " ); continue; }
			try
			{
				var glb = GltfCompiler.Compile( System.IO.File.ReadAllBytes( path ), _ => throw new InvalidOperationException(), GltfModelLoader.SupportedExtensions, 1024L * 1024 * 1024, true );
				using var document = GltfModelLoader.ReadDocument( glb );
				GltfNativePackHeader header; List<byte[]> blobs;
				try { (header, blobs) = GltfNativeCompiler.Compile( document ); }
				catch ( GltfNativeUnsupportedException e ) { report.Append( $"{name}: source path ({e.Message}); " ); continue; }
				var pack = GltfNativePack.TryRead( GltfNativePack.Append( glb, header, blobs ) );
				var model = GltfNativeModelBuilder.Build( pack, $"gltf_native_sample_{name}.glb" );
				var worst = 0f;
				var world = new SceneWorld();
				try
				{
					var scene = new SceneModel( world, model, Transform.Zero ) { UseAnimGraph = false };
					foreach ( var clip in GltfModelAnimation.ReadClips( document ) )
					{
						var time = MathF.Round( clip.Duration * 0.5f * GltfNativeSkeleton.FrameRate ) / GltfNativeSkeleton.FrameRate;
						scene.CurrentSequence.Name = clip.Name;
						scene.CurrentSequence.Time = time;
						scene.Update( 0 );
						var pose = document.EvaluatePose( clip.Index, time );
						for ( var bone = 0; bone < header.Bones.Length; bone++ )
						{
							var node = NodeOf( document, header.Bones[bone].Name );
							if ( node < 0 ) continue;
							var expected = GltfNativeSkeleton.Engine( pose.GetWorldTransform( node ) ).Translation;
							var actual = scene.GetBoneWorldTransform( bone ).Position;
							worst = MathF.Max( worst, (new Vector3( expected.X, expected.Y, expected.Z ) - actual).Length );
						}
					}
				}
				finally { world.Delete(); }
				report.Append( $"{name}: native, {header.Bones.Length} bones, {header.Animations.Length} clips, {header.Meshes.Sum( x => x.Morphs.Length )} morphs, worst bone error {worst:0.###}; " );
				if ( worst > 0.05f ) failures.Add( $"{name} bone error {worst}" );
			}
			catch ( Exception e ) { failures.Add( $"{name}: {e.GetType().Name} {e.Message}" ); report.Append( $"{name}: FAILED {e.Message}; " ); }
		}
		Console.WriteLine( report );
		if ( failures.Count != 0 ) Assert.Fail( string.Join( " | ", failures ) + " || " + report );
		Assert.Inconclusive( report.ToString() );
	}

	[TestMethod]
	public void VariantsBecomeMaterialGroups()
	{
		var folder = Environment.GetEnvironmentVariable( "GLTF_NATIVE_SAMPLES" );
		if ( string.IsNullOrEmpty( folder ) ) Assert.Inconclusive( "Set GLTF_NATIVE_SAMPLES to the khronos sample folder." );
		var path = System.IO.Path.Combine( folder, "MaterialsVariantsShoe", "glTF-Binary", "MaterialsVariantsShoe.glb" );
		var glb = GltfCompiler.Compile( System.IO.File.ReadAllBytes( path ), _ => throw new InvalidOperationException(), GltfModelLoader.SupportedExtensions, 1024L * 1024 * 1024, true );
		using var document = GltfModelLoader.ReadDocument( glb );
		var (header, blobs) = GltfNativeCompiler.Compile( document, scene: false );
		var model = GltfNativeModelBuilder.Build( GltfNativePack.TryRead( GltfNativePack.Append( glb, header, blobs ) ), "gltf_native_variants.glb" );
		// As a scene, each node shows a variant through a material group of its geometry.
		var (sceneHeader, _) = GltfNativeCompiler.Compile( document );
		Assert.IsTrue( sceneHeader.Nodes.All( x => x.VariantGroups.Length == header.Variants.Length ) );
		Assert.IsTrue( sceneHeader.Nodes.Any( x => x.VariantGroups.Any( g => g != x.Group ) ), "some variant shows another material group" );
		CollectionAssert.AreEqual( document.ReadMaterialVariantNames().ToArray(), model.MaterialVariantNames.ToArray() );
		Assert.AreEqual( header.Variants.Length + 1, model.MaterialGroupCount );
		Assert.AreEqual( "default", model.GetMaterialGroupName( 0 ) );
		var defaults = model.GetMaterials( 0 ).ToArray();
		var report = new StringBuilder( $"variants {string.Join( ",", header.Variants )}; default [{string.Join( ",", defaults.Select( x => x.Name ) )}]" );
		for ( var group = 1; group < model.MaterialGroupCount; group++ )
		{
			var materials = model.GetMaterials( group ).ToArray();
			Assert.AreEqual( defaults.Length, materials.Length, "one replacement per default material" );
			report.Append( $"; {model.GetMaterialGroupName( group )} [{string.Join( ",", materials.Select( x => x.Name ) )}]" );
		}
		Assert.IsTrue( Enumerable.Range( 1, model.MaterialGroupCount - 1 ).Any( g => !model.GetMaterials( g ).SequenceEqual( defaults ) ), "some variant swaps a material" );
		Assert.Inconclusive( report.ToString() );
	}

	private static int NodeOf( GltfDocument document, string boneName )
	{
		var nodes = document.Root.GetProperty( "nodes" );
		for ( var i = 0; i < nodes.GetArrayLength(); i++ )
		{
			var name = nodes[i].TryGetProperty( "name", out var value ) && value.GetString() is { Length: > 0 } text ? text : $"node_{i}";
			if ( name == boneName ) return i;
		}
		return boneName.StartsWith( "node_" ) && int.TryParse( boneName[5..], out var index ) ? index : -1;
	}
}
