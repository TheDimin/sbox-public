using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfArtifactDependenciesTests
{
	[TestMethod]
	public void CollectsExternalImagesAtDecodedRelativePathsAndSkipsEmbeddedImages()
	{
		using var json = JsonDocument.Parse( """
			{"images":[{"uri":"textures/paint%20blue.png"},{"uri":"textures/sub/detail.jpg"},
			           {"uri":"data:image/png;base64,AA=="},{"bufferView":0,"mimeType":"image/png"}]}
			""" );
		var requests = new List<string>();
		var dependencies = GltfArtifactDependencies.CollectImages( json.RootElement, uri =>
		{
			requests.Add( uri );
			return uri.EndsWith( ".png", StringComparison.Ordinal ) ? [1, 2] : [3, 4];
		} );

		CollectionAssert.AreEqual( new[] { "textures/paint%20blue.png", "textures/sub/detail.jpg" }, requests );
		CollectionAssert.AreEqual( new byte[] { 1, 2 }, dependencies["textures/paint blue.png"] );
		CollectionAssert.AreEqual( new byte[] { 3, 4 }, dependencies["textures/sub/detail.jpg"] );
		Assert.AreEqual( 2, dependencies.Count );
	}

	[TestMethod]
	[DataRow( "../outside.png" )]
	[DataRow( "%2e%2e/outside.png" )]
	[DataRow( "textures/%2e%2e/outside.png" )]
	[DataRow( "/absolute.png" )]
	[DataRow( "//host/file.png" )]
	[DataRow( "https://example.com/file.png" )]
	[DataRow( "textures%5coutside.png" )]
	[DataRow( "textures/file.png?query=1" )]
	[DataRow( "textures/file.png#fragment" )]
	[DataRow( "textures/%GG.png" )]
	public void RejectsUnsafeDependencyPaths( string uri )
	{
		Assert.ThrowsException<GltfException>( () => GltfArtifactDependencies.RelativePath( uri ) );
	}

	[TestMethod]
	public void RewritesValidSourceUrisToDeterministicContentAddressedSidecars()
	{
		var source = System.Text.Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"images":[{"uri":"../textures/shared%23x.png"},{"uri":"./local%20file.png"}]}
			""" );
		var glb = GltfCompiler.Compile( source );
		var requested = new List<string>();
		var bundle = GltfArtifactDependencies.Prepare( glb, uri =>
		{
			requested.Add( uri );
			return uri.StartsWith( "../", StringComparison.Ordinal ) ? [1, 2, 3] : [4, 5, 6];
		} );
		CollectionAssert.AreEqual( new[] { "../textures/shared%23x.png", "./local%20file.png" }, requested );
		using var original = GltfReader.Read( glb );
		using var rewritten = GltfReader.Read( bundle.Artifact );
		Assert.AreEqual( "../textures/shared%23x.png", original.Root.GetProperty( "images" )[0].GetProperty( "uri" ).GetString() );
		var first = rewritten.Root.GetProperty( "images" )[0].GetProperty( "uri" ).GetString();
		var second = rewritten.Root.GetProperty( "images" )[1].GetProperty( "uri" ).GetString();
		StringAssert.StartsWith( first, "gltf-images/" );
		StringAssert.StartsWith( second, "gltf-images/" );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, bundle.Dependencies[first] );
		CollectionAssert.AreEqual( new byte[] { 4, 5, 6 }, bundle.Dependencies[second] );
		var repeated = GltfArtifactDependencies.Prepare( glb, uri => uri.StartsWith( "../", StringComparison.Ordinal ) ? [1, 2, 3] : [4, 5, 6] );
		CollectionAssert.AreEqual( bundle.Artifact, repeated.Artifact );
	}

	[TestMethod]
	public void RewritingImageUriRejectsOpaqueReferencesToTheOriginalPath()
	{
		var source = System.Text.Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_unknown"],"extensions":{"EXT_unknown":{"uri":"../image.png"}},"images":[{"uri":"../image.png"}]}
			""" );
		var glb = GltfCompiler.Compile( source );
		Assert.ThrowsException<GltfException>( () => GltfArtifactDependencies.Prepare( glb, _ => [1] ) );
	}

	[TestMethod]
	public void FailedPublishKeepsPreviousArtifactAndSidecarUntouched()
	{
		var directory = Path.Combine( Path.GetTempPath(), "sbox-gltf-atomic-test-" + Guid.NewGuid().ToString( "N" ) );
		try
		{
			var artifact = Path.Combine( directory, "model.glb_c" );
			GltfArtifactDependencies.WritePackage( artifact, [1, 2], new Dictionary<string, byte[]> { ["gltf-images/old.png"] = [3] } );
			var attempted = new Dictionary<string, byte[]> { ["gltf-images/new.png"] = [4], ["gltf-images/old.png"] = [9] };
			Assert.ThrowsException<GltfException>( () => GltfArtifactDependencies.WritePackage( artifact, [8], attempted ) );
			CollectionAssert.AreEqual( new byte[] { 1, 2 }, File.ReadAllBytes( artifact ) );
			CollectionAssert.AreEqual( new byte[] { 3 }, File.ReadAllBytes( Path.Combine( directory, "gltf-images", "old.png" ) ) );
		}
		finally { if ( Directory.Exists( directory ) ) Directory.Delete( directory, recursive: true ); }
	}

	[TestMethod]
	public void SidecarWriteFailureAfterPartialProgressDoesNotReplacePriorArtifact()
	{
		var directory = Path.Combine( Path.GetTempPath(), "sbox-gltf-atomic-test-" + Guid.NewGuid().ToString( "N" ) );
		try
		{
			var artifact = Path.Combine( directory, "model.glb_c" );
			GltfArtifactDependencies.WritePackage( artifact, [1, 2], new Dictionary<string, byte[]> { ["gltf-images/old.png"] = [3] } );
			File.WriteAllBytes( Path.Combine( directory, "blocked" ), [6] );
			var attempted = new Dictionary<string, byte[]> { ["gltf-images/new.png"] = [4], ["blocked/image.png"] = [9] };
			Assert.ThrowsException<IOException>( () => GltfArtifactDependencies.WritePackage( artifact, [8], attempted ) );
			CollectionAssert.AreEqual( new byte[] { 4 }, File.ReadAllBytes( Path.Combine( directory, "gltf-images", "new.png" ) ) );
			CollectionAssert.AreEqual( new byte[] { 1, 2 }, File.ReadAllBytes( artifact ) );
			CollectionAssert.AreEqual( new byte[] { 3 }, File.ReadAllBytes( Path.Combine( directory, "gltf-images", "old.png" ) ) );
		}
		finally { if ( Directory.Exists( directory ) ) Directory.Delete( directory, recursive: true ); }
	}

	[TestMethod]
	public void WritesAndResolvesSidecarsRelativeToTheCompiledArtifact()
	{
		var directory = Path.Combine( Path.GetTempPath(), "sbox-gltf-package-test-" + Guid.NewGuid().ToString( "N" ) );
		try
		{
			var artifact = Path.Combine( directory, "model.glb_c" );
			var dependencies = new Dictionary<string, byte[]> { ["textures/paint blue.png"] = [7, 8, 9] };
			GltfArtifactDependencies.WritePackage( artifact, [1, 2, 3], dependencies );
			CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, File.ReadAllBytes( artifact ) );
			CollectionAssert.AreEqual( new byte[] { 7, 8, 9 }, File.ReadAllBytes( Path.Combine( directory, "textures", "paint blue.png" ) ) );
			CollectionAssert.AreEqual( new byte[] { 7, 8, 9 }, GltfArtifactDependencies.ResolvePackaged( artifact, "textures/paint%20blue.png" ) );
			Assert.ThrowsException<GltfException>( () => GltfArtifactDependencies.ResolvePackaged( artifact, "../outside.png" ) );
			Assert.ThrowsException<GltfException>( () => GltfArtifactDependencies.ResolvePackaged( artifact, "textures/missing.png" ) );
		}
		finally { if ( Directory.Exists( directory ) ) Directory.Delete( directory, recursive: true ); }
	}

	[TestMethod]
	public void ImageSidecarCannotOverwriteTheCompiledArtifact()
	{
		var artifact = Path.Combine( Path.GetTempPath(), "sbox-gltf-package-test-" + Guid.NewGuid().ToString( "N" ), "model.glb_c" );
		Assert.ThrowsException<GltfException>( () => GltfArtifactDependencies.WritePackage( artifact, [1], new Dictionary<string, byte[]> { ["model.glb_c"] = [2] } ) );
	}

	[TestMethod]
	public void MissingDependencyIsAnError()
	{
		using var json = JsonDocument.Parse( """{"images":[{"uri":"missing.png"}]}""" );
		Assert.ThrowsException<GltfException>( () => GltfArtifactDependencies.CollectImages( json.RootElement, _ => null ) );
	}

	[TestMethod]
	public void DifferentUrisCannotSilentlyOverwriteTheSamePathWithDifferentBytes()
	{
		using var json = JsonDocument.Parse( """{"images":[{"uri":"paint%20blue.png"},{"uri":"paint blue.png"}]}""" );
		Assert.ThrowsException<GltfException>( () => GltfArtifactDependencies.CollectImages( json.RootElement, uri => uri.Contains( '%' ) ? [1] : [2] ) );
	}
}
