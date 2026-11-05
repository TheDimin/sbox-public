using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Test.Gltf;

/// <summary>Capability alone must not authorize extension-only vertex formats in an asset.</summary>
[TestClass]
public class GltfQuantizationDeclarationTests
{
	private const string Extension = "KHR_mesh_quantization";

	[TestMethod]
	[DataRow( "POSITION", 5122, false, "VEC3" )]
	[DataRow( "NORMAL", 5120, true, "VEC3" )]
	[DataRow( "TANGENT", 5120, true, "VEC4" )]
	[DataRow( "TEXCOORD_0", 5122, false, "VEC2" )]
	public void ExtensionOnlyVertexFormatsRequireAssetDeclaration( string semantic, int component, bool normalized, string type )
	{
		foreach ( var declaration in new[] { "none", "used" } )
		{
			using var document = Read( semantic, component, normalized, type, declaration, supports: true );
			Assert.ThrowsException<GltfException>( () => document.ReadPrimitive( 0, 0 ), $"{semantic} was accepted with {declaration} declaration." );
		}
	}

	[TestMethod]
	[DataRow( "POSITION", 5122, false, "VEC3" )]
	[DataRow( "NORMAL", 5120, true, "VEC3" )]
	[DataRow( "TANGENT", 5120, true, "VEC4" )]
	[DataRow( "TEXCOORD_0", 5122, false, "VEC2" )]
	public void DeclaredAndSupportedQuantizedVertexFormatsAreAccepted( string semantic, int component, bool normalized, string type )
	{
		using var document = Read( semantic, component, normalized, type, "required", supports: true );
		var primitive = document.ReadPrimitive( 0, 0 );
		Assert.AreEqual( 3, primitive.VertexCount );
		Assert.AreEqual( 1, primitive.Attributes[semantic] );
		Assert.AreEqual( 3 * (type == "VEC2" ? 2 : type == "VEC3" ? 3 : 4), document.ReadFloatAccessor( 1 ).Length );
	}

	[TestMethod]
	public void RequiredExtensionIsRejectedWithoutCapability()
	{
		Assert.ThrowsException<GltfException>( () => { using var ignored = Read( "POSITION", 5122, false, "VEC3", "required", supports: false ); } );
	}

	[TestMethod]
	public void ExtensionOnlyLayoutIsRejectedWithoutDeclarationOrCapability()
	{
		using var document = Read( "POSITION", 5122, false, "VEC3", "none", supports: false );
		Assert.ThrowsException<GltfException>( () => document.ReadPrimitive( 0, 0 ) );
	}

	[TestMethod]
	[DataRow( 5121 )]
	[DataRow( 5123 )]
	public void CoreFloatPositionAndUnsignedNormalizedUvRemainValid( int component )
	{
		using var document = Read( "TEXCOORD_0", component, true, "VEC2", "none", supports: true );
		var primitive = document.ReadPrimitive( 0, 0 );
		Assert.AreEqual( 3, primitive.VertexCount );
		Assert.AreEqual( 0, primitive.Attributes["POSITION"] );
		Assert.AreEqual( 1, primitive.Attributes["TEXCOORD_0"] );
	}

	private static GltfDocument Read( string semantic, int component, bool normalized, string type, string declaration, bool supports )
	{
		// Independent, valid aligned attribute views: three float VEC3 positions,
		// followed by three quantized elements with an eight-byte vertex stride.
		var bytes = new byte[60];
		System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 12 ), BitConverter.SingleToInt32Bits( 1 ) );
		System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 28 ), BitConverter.SingleToInt32Bits( 1 ) );
		if ( semantic == "POSITION" ) { bytes[44] = 1; bytes[54] = 1; }
		if ( semantic == "NORMAL" ) for ( var i = 0; i < 3; i++ ) bytes[36 + i * 8 + 2] = 127;
		if ( semantic == "TANGENT" ) for ( var i = 0; i < 3; i++ ) { bytes[36 + i * 8] = 127; bytes[36 + i * 8 + 3] = 127; }
		var data = Convert.ToBase64String( bytes );
		var extensionFields = declaration switch
		{
			"required" => "\"extensionsUsed\":[\"KHR_mesh_quantization\"],\"extensionsRequired\":[\"KHR_mesh_quantization\"],",
			"used" => "\"extensionsUsed\":[\"KHR_mesh_quantization\"],",
			_ => ""
		};
		var attributes = semantic == "POSITION" ? "\"POSITION\":1" : $"\"POSITION\":0,\"{semantic}\":1";
		var bounds = semantic == "POSITION" ? ",\"min\":[0,0,0],\"max\":[1,1,0]" : "";
		var json = $$$"""
			{"asset":{"version":"2.0"},{{{extensionFields}}}
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{data}}}","byteLength":60}],
			 "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36,"byteStride":12},{"buffer":0,"byteOffset":36,"byteLength":24,"byteStride":8}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},
			  {"bufferView":1,"componentType":{{{component}}},"normalized":{{{(normalized ? "true" : "false")}}},"count":3,"type":"{{{type}}}"{{{bounds}}} }],
			 "meshes":[{"primitives":[{"attributes":{ {{{attributes}}} }}]}]}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: supports ? new HashSet<string>( StringComparer.Ordinal ) { Extension } : null );
	}
}
