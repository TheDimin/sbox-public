using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfCompilerSafetyTests
{
	[TestMethod]
	public void OneBufferKeepsItsMetadataAndUnknownExtensionsWhenNoReferencesMove()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_unknown"],"buffers":[{"name":"Geometry","uri":"mesh.bin","byteLength":2,"extras":{"tag":42}}],"extensions":{"EXT_unknown":{"buffer":0}}}
			""" );
		var first = GltfCompiler.Compile( source, _ => [9, 8] );
		using var document = GltfReader.Read( first );

		Assert.AreEqual( "Geometry", document.Root.GetProperty( "buffers" )[0].GetProperty( "name" ).GetString() );
		Assert.AreEqual( 42, document.Root.GetProperty( "buffers" )[0].GetProperty( "extras" ).GetProperty( "tag" ).GetInt32() );
		Assert.AreEqual( 0, document.Root.GetProperty( "extensions" ).GetProperty( "EXT_unknown" ).GetProperty( "buffer" ).GetInt32() );
		CollectionAssert.AreEqual( first, GltfCompiler.Compile( first ) );
	}

	[TestMethod]
	public void MergingBuffersDoesNotSilentlyLosePerBufferMetadata()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"a.bin","byteLength":1},{"name":"source-two","extras":{"keep":1},"uri":"b.bin","byteLength":1}]}
			""" );
		Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( source, _ => [5] ) );
	}

	[TestMethod]
	public void BufferMergingDoesNotAssumeApplicationExtrasContainNoBufferReferences()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extras":{"applicationData":{"buffer":1}},"buffers":[{"uri":"a.bin","byteLength":1},{"uri":"b.bin","byteLength":1}]}
			""" );
		Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( source, _ => [1] ) );
	}

	[TestMethod]
	public void BufferMergingRejectsMeshoptBufferReferencesEvenWhenExtensionIsSupported()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_meshopt_compression"],"buffers":[{"uri":"a.bin","byteLength":1},{"uri":"b.bin","byteLength":1}],"bufferViews":[{"buffer":0,"byteLength":1,"extensions":{"EXT_meshopt_compression":{"buffer":1,"byteOffset":0,"byteLength":1,"byteStride":4,"count":1,"mode":"ATTRIBUTES"}}}]}
			""" );
		Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( source, _ => [1], new HashSet<string> { "EXT_meshopt_compression" } ) );
	}

	[TestMethod]
	public void UnknownGlbChunksAreNotSilentlyDropped()
	{
		var json = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"}}" );
		var jsonLength = (json.Length + 3) & ~3;
		var glb = new byte[12 + 8 + jsonLength + 12];
		BinaryPrimitives.WriteUInt32LittleEndian( glb, 0x46546c67 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 8 ), (uint)glb.Length );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 12 ), (uint)jsonLength );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( 16 ), 0x4e4f534a );
		glb.AsSpan( 20, jsonLength ).Fill( 0x20 );
		json.CopyTo( glb, 20 );
		var chunk = 20 + jsonLength;
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( chunk ), 4 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( chunk + 4 ), 0x12345678 );
		BinaryPrimitives.WriteUInt32LittleEndian( glb.AsSpan( chunk + 8 ), 0xabcdef01 );

		Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( glb ) );
	}
}
