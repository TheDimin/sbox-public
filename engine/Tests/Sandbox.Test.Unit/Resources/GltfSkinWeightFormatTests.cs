using NativeEngine;
using Sandbox.Gltf;
using System;
using System.Collections.Generic;

namespace Sandbox.Tests;

[TestClass]
public class GltfSkinWeightFormatTests
{
	[TestMethod]
	[DataRow( (int)ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM, (int)GltfSkinWeightFormat.Unorm8 )]
	[DataRow( (int)ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT, (int)GltfSkinWeightFormat.Float32 )]
	[DataRow( (int)ColorFormat.COLOR_FORMAT_R16G16B16A16_UNORM, (int)GltfSkinWeightFormat.Unorm16 )]
	public void WeightDecoderComesFromTheActualNativeDeclaration( int format, int expected )
	{
		RawVertexField[] declaration = [new( "position", 0, 16, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT ), new( "blendindices", 0, 40, ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT ), new( "blendweight", 0, 60, (ColorFormat)format )];
		Assert.AreEqual( (GltfSkinWeightFormat)expected, GltfSkinningMeshFormats.FromDeclaration( declaration ) );
	}

	[TestMethod]
	public void ReorderedCompiledMeshesKeepTheirOwnFormatByValidatedName()
	{
		var prepared = new Dictionary<string, GltfSkinWeightFormat>( StringComparer.Ordinal )
		{
			["first"] = GltfSkinWeightFormat.Unorm8, ["second"] = GltfSkinWeightFormat.Float32, ["third"] = GltfSkinWeightFormat.Unorm16
		};
		var actual = GltfSkinningMeshFormats.MatchNativeMeshes( ["third", "first", "second"], prepared );
		CollectionAssert.AreEqual( new[] { GltfSkinWeightFormat.Unorm16, GltfSkinWeightFormat.Unorm8, GltfSkinWeightFormat.Float32 }, actual );
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.MatchNativeMeshes( ["first", "first", "third"], prepared ) );
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.MatchNativeMeshes( ["first", "second", "renamed"], prepared ) );
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.MatchNativeMeshes( ["first", "second"], prepared ) );
	}

	[TestMethod]
	public void MissingDuplicateOrUnsupportedWeightDeclarationsCannotSelectADecoder()
	{
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.FromDeclaration( [] ) );
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.FromDeclaration( [new( "blendweight", 0, 0, ColorFormat.COLOR_FORMAT_R32G32_FLOAT )] ) );
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.FromDeclaration( [new( "blendweight", 0, 0, ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM ), new( "blendweight", 0, 4, ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT )] ) );
	}

	[TestMethod]
	public void StridedLayoutsValidateAlignmentRangesAndIndexWidthIndependently()
	{
		RawVertexField[] fields = [new( "position", 0, 0, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT ), new( "blendindices", 0, 24, ColorFormat.COLOR_FORMAT_R8G8B8A8_UINT ), new( "blendweight", 0, 36, ColorFormat.COLOR_FORMAT_R16G16B16A16_UNORM )];
		Assert.AreEqual( GltfSkinWeightFormat.Unorm16, GltfSkinningMeshFormats.FromLayout( new( 52, 3, 156, fields ) ) );
		fields[1] = fields[1] with { Format = ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT };
		Assert.AreEqual( GltfSkinWeightFormat.Unorm16, GltfSkinningMeshFormats.FromLayout( new( 52, 3, 156, fields ) ) );
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.FromLayout( new( 51, 3, 153, fields ) ) );
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.FromLayout( new( 40, 3, 120, fields ) ) );
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.FromLayout( new( 52, 3, 140, fields ) ) );
		fields[2] = fields[2] with { Offset = 34 };
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.FromLayout( new( 52, 3, 156, fields ) ) );
		fields[2] = fields[2] with { Offset = 36, Format = ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT };
		Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.FromLayout( new( 52, 3, 156, fields ) ) );
	}
}
