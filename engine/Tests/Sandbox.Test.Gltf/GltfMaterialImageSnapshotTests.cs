using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfMaterialImageSnapshotTests
{
	private const string Source = """
		{"asset":{"version":"2.0"},"images":[{"uri":"image.png"}],"textures":[{"source":0}],
		 "materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}}},
		 {"normalTexture":{"index":0},"emissiveTexture":{"index":0}}]}
		""";

	[TestMethod]
	public void SharedReachableImageIsSnapshottedOnceAndIsIndependentOfSourceAndCallers()
	{
		byte[] source = [137, 80, 78, 71, 13, 10, 26, 10, 7]; var reads = 0;
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ), uri => { reads++; Assert.AreEqual( "image.png", uri ); return source; } );
		var snapshot = document.SnapshotMaterialImages( [0, 1], 9 );
		Assert.AreEqual( 1, reads ); Assert.AreEqual( 9L, snapshot.EncodedBytes );
		source[8] = 99;
		var first = snapshot.ReadImage( 0 ); Assert.AreEqual( (byte)7, first.Bytes[8] ); Assert.AreEqual( "image/png", first.MimeType );
		first.Bytes[8] = 42;
		Assert.AreEqual( (byte)7, snapshot.ReadImage( 0 ).Bytes[8] ); Assert.AreEqual( 1, reads );
	}

	[TestMethod]
	public void SnapshotEnforcesAggregateBudgetAndNeverReturnsPartialResult()
	{
		byte[] source = [137, 80, 78, 71, 13, 10, 26, 10, 7];
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ), _ => source );
		Assert.ThrowsException<GltfException>( () => document.SnapshotMaterialImages( [0], 8 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => document.SnapshotMaterialImages( [0], -1 ) );
		Assert.AreEqual( 9L, document.SnapshotMaterialImages( [0], 9 ).EncodedBytes );
	}

	[TestMethod]
	public void AbsentOrUnselectedImageCannotBeSilentlyReadFromSource()
	{
		var reads = 0;
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ), _ => { reads++; return [137, 80, 78, 71, 13, 10, 26, 10]; } );
		var snapshot = document.SnapshotMaterialImages( [null], 0 );
		Assert.AreEqual( 0, reads );
		Assert.ThrowsException<GltfException>( () => snapshot.ReadImage( 0 ) );
		Assert.AreEqual( 0, reads );
	}

	[TestMethod]
	public void UnselectedVariantImageKeepsOriginalBytesAfterExternalFileChanges()
	{
		var path = Path.Combine( Path.GetTempPath(), "gltf_variant_image_" + Guid.NewGuid().ToString( "N" ) );
		byte[] original = [137, 80, 78, 71, 13, 10, 26, 10, 7];
		try
		{
			File.WriteAllBytes( path, original );
			using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ), _ => File.ReadAllBytes( path ) );
			// Material 1 is an alternate; capture its dependency at generation creation.
			var snapshot = document.SnapshotMaterialImages( [0, 1], 9 );
			File.WriteAllBytes( path, [137, 80, 78, 71, 13, 10, 26, 10, 99] );
			Assert.AreEqual( (byte)7, snapshot.ReadImage( 0 ).Bytes[8] );
			Assert.AreEqual( (byte)99, document.ReadImage( 0 ).Bytes[8] );
		}
		finally { File.Delete( path ); }
	}

	[TestMethod]
	public void DistinctImagesShareOneAggregateBudgetAndSnapshotOutlivesDocument()
	{
		const string json = """
			{"asset":{"version":"2.0"},"images":[{"uri":"one.png"},{"uri":"two.png"}],
			 "textures":[{"source":0},{"source":1}],"materials":[{"pbrMetallicRoughness":{
			 "baseColorTexture":{"index":0},"metallicRoughnessTexture":{"index":1}}}]}
			""";
		var reads = 0; byte[] source = [137, 80, 78, 71, 13, 10, 26, 10, 7];
		var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => { reads++; return source; } );
		try
		{
			Assert.ThrowsException<GltfException>( () => document.SnapshotMaterialImages( [0, 0], 17 ) );
			reads = 0; var snapshot = document.SnapshotMaterialImages( [0, 0], 18 );
			Assert.AreEqual( 2, reads ); Assert.AreEqual( 18L, snapshot.EncodedBytes );
			document.Dispose(); source[8] = 99;
			Assert.AreEqual( (byte)7, snapshot.ReadImage( 0 ).Bytes[8] );
			Assert.AreEqual( (byte)7, snapshot.ReadImage( 1 ).Bytes[8] );
		}
		finally { document.Dispose(); }
	}

	[TestMethod]
	public void SupportedUnlitDoesNotResolveIgnoredLightingTextures()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_unlit"],
			 "images":[{"uri":"base.png"},{"uri":"ignored.png"}],"textures":[{"source":0},{"source":1}],
			 "materials":[{"extensions":{"KHR_materials_unlit":{}},"pbrMetallicRoughness":{
			 "baseColorTexture":{"index":0},"metallicRoughnessTexture":{"index":1}},
			 "normalTexture":{"index":1},"occlusionTexture":{"index":1},"emissiveTexture":{"index":1}}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), uri =>
		{
			Assert.AreEqual( "base.png", uri, "Unlit must not resolve fallback-only lighting textures." );
			return [137, 80, 78, 71, 13, 10, 26, 10];
		}, new HashSet<string> { "KHR_materials_unlit" } );
		var snapshot = document.SnapshotMaterialImages( [0], 8 );
		Assert.AreEqual( 8L, snapshot.EncodedBytes );
		Assert.ThrowsException<GltfException>( () => snapshot.ReadImage( 1 ) );
	}

	[TestMethod]
	public void SharedImageMustSatisfyEverySelectedTextureFormat()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_texture_webp"],"images":[{"uri":"shared.png"}],
			 "textures":[{"source":0},{"source":0,"extensions":{"EXT_texture_webp":{"source":0}}}],
			 "materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0},"metallicRoughnessTexture":{"index":1}}}]}
			""";
		var reads = 0;
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ =>
		{
			reads++; return [137, 80, 78, 71, 13, 10, 26, 10];
		}, new HashSet<string> { "EXT_texture_webp" } );
		var snapshot = document.SnapshotMaterialImages( [0], 8 );
		document.ValidateTextureImage( 0, snapshot.ReadImage( 0 ) );
		Assert.ThrowsException<GltfException>( () => document.ValidateTextureImage( 1, snapshot.ReadImage( 0 ) ) );
		Assert.AreEqual( 1, reads, "Shared image is read once even though each texture is validated." );
	}

	[TestMethod]
	public void MissingAlternateImageRetainsItsFailureWithoutBlockingOtherMaterialsOrRereading()
	{
		const string json = """
			{"asset":{"version":"2.0"},"images":[{"uri":"base.png"},{"uri":"alternate.png"}],
			 "textures":[{"source":0},{"source":1}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}}},
			 {"pbrMetallicRoughness":{"baseColorTexture":{"index":1}}}]}
			""";
		var reads = 0; var available = false;
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), uri =>
		{
			reads++; if ( uri == "alternate.png" && !available ) throw new System.IO.FileNotFoundException( uri );
			return [137, 80, 78, 71, 13, 10, 26, 10];
		} );
		var snapshot = document.SnapshotMaterialImages( [0, 1], 16 );
		Assert.AreEqual( 8, snapshot.ReadImage( 0 ).Bytes.Length ); Assert.AreEqual( 2, reads );
		available = true;
		Assert.ThrowsException<GltfException>( () => snapshot.ReadImage( 1 ) );
		Assert.AreEqual( 2, reads, "A failed alternate must not later read mutable external data." );
		using var replacement = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ =>
		{
			reads++; return [137, 80, 78, 71, 13, 10, 26, 10];
		} );
		var next = replacement.SnapshotMaterialImages( [0, 1], 16 );
		Assert.AreEqual( 8, next.ReadImage( 1 ).Bytes.Length ); Assert.AreEqual( 4, reads );
	}
}
