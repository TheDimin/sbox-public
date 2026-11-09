using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

[TestClass]
public sealed class Png16PublicModelTests
{
	[TestMethod]
	public void SourceAndCanonicalModelsLoadAndRealizeMetallicRoughnessMaterial()
	{
		var dir = Path.Combine( Path.GetTempPath(), "gltf_png16_" + Guid.NewGuid().ToString( "N" ) );
		LocalFileSystem mount = null; Model model = null;
		var name = "gltf_png16_" + Guid.NewGuid().ToString( "N" ) + ".gltf";
		try
		{
			Directory.CreateDirectory( dir );
			File.WriteAllText( Path.Combine( dir, name ), Source );
			File.WriteAllBytes( Path.Combine( dir, "mr.png" ), Convert.FromBase64String( "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABEAYAAACksqPJAAAAGUlEQVR4nGNgZGRkamA0sGRkYmRsYLpiCAAORAJ8hmWiZwAAAABJRU5ErkJggg==" ) );
			mount = new LocalFileSystem( dir ); FileSystem.Mounted.Mount( mount ); model = Model.Load( name );
			Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null, "Public source glTF must load through managed model route." );
			Assert.AreEqual( 1, model.GltfResource.Asset.MeshCount );
			Assert.IsTrue( model.Materials.Length == 1 && model.Materials.All( x => x is { IsValid: true, IsError: false } ), "Actual source material must realize." );
			model.Destroy(); model = null;
			var compiled = GltfCompiler.Compile( Encoding.UTF8.GetBytes( Source ), uri => uri == "mr.png" ? File.ReadAllBytes( Path.Combine( dir, uri ) ) : throw new Exception( "Unexpected dependency " + uri ), new HashSet<string>() );
			name = "gltf_png16_" + Guid.NewGuid().ToString( "N" ) + ".glb_c";
			File.WriteAllBytes( Path.Combine( dir, name ), compiled ); model = Model.Load( name );
			Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null && model.GltfResource.Asset.MeshCount == 1, "Canonical compiled model must load through managed model route." );
			Assert.IsTrue( model.Materials.Length == 1 && model.Materials.All( x => x is { IsValid: true, IsError: false } ), "Canonical compiled material must realize." );
		}
		finally
		{
			if ( model?.ResourcePath == name ) model.Destroy();
			if ( mount is not null ) { FileSystem.Mounted.UnMount( mount ); mount.Dispose(); }
			if ( Directory.Exists( dir ) ) Directory.Delete( dir, true );
		}
	}
	private const string Source = """
		{"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1},"material":0}]}],"buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/","byteLength":60}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":24}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},{"bufferView":1,"componentType":5126,"count":3,"type":"VEC2"}],"images":[{"uri":"mr.png"}],"samplers":[{"magFilter":9729,"minFilter":9729}],"textures":[{"source":0,"sampler":0}],"materials":[{"pbrMetallicRoughness":{"metallicFactor":0.75,"roughnessFactor":0.5,"metallicRoughnessTexture":{"index":0}}}]}
		""";
}
