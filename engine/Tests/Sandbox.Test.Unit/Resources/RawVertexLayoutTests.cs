using NativeEngine;
using System;

namespace Sandbox.Tests;

[TestClass]
public class RawVertexLayoutTests
{
	[TestMethod]
	public void AcceptsExplicitOffsetsStrideAndNormalizedOrIntegerFormats()
	{
		RawVertexLayout.Validate( 32, 2, 64,
		[
			new( "texcoord", 1, 20, ColorFormat.COLOR_FORMAT_R16G16_UNORM ),
			new( "position", 0, 4, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT ),
			new( "blendindices", 0, 24, ColorFormat.COLOR_FORMAT_R8G8B8A8_UINT ),
			new( "blendweight", 0, 28, ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM )
		] );
	}

	[TestMethod]
	public void RejectsDuplicateSemanticsAndMalformedIdentifiers()
	{
		RawVertexField field = new( "texcoord", 1, 0, ColorFormat.COLOR_FORMAT_R32G32_FLOAT );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 1, 16, [field, field] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 1, 16, [field, field with { Semantic = "TEXCOORD" }] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 1, 16, [field with { Semantic = "position\0other" }] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 1, 16, [field with { SemanticIndex = -1 }] ) );
	}

	[TestMethod]
	public void RejectsUnsupportedFormatsAndFieldsOutsideStride()
	{
		RawVertexField field = new( "position", 0, 8, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 1, 16, [field] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 1, 16, [field with { Offset = -1 }] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 1, 16, [field with { Offset = 0, Format = ColorFormat.COLOR_FORMAT_BC1_UNORM }] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 1, 16, [field with { Offset = 0, Format = ColorFormat.COLOR_FORMAT_UNKNOWN }] ) );
	}

	[TestMethod]
	public void RejectsInconsistentByteCountsAndOverflow()
	{
		RawVertexField field = new( "position", 0, 0, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 2, 27, [field] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 2, 33, [field] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 0, 0, [field] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( int.MaxValue, 2, 0, [field] ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 16, 1, 16, [] ) );
	}

	[TestMethod]
	public void AllowsMissingFinalPaddingAndFillsItWithoutChangingSourceBytes()
	{
		RawVertexField[] fields =
		[
			new( "position", 0, 0, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT ),
			new( "normal", 0, 12, ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT )
		];
		var input = new byte[80];
		for ( int i = 0; i < input.Length; ++i ) input[i] = (byte)(i + 1);
		var original = (byte[])input.Clone();
		RawVertexLayout.Validate( 28, 3, input.Length, fields );
		RawVertexLayout.Validate( 28, 3, 84, fields );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 28, 3, 79, fields ) );
		Assert.ThrowsException<ArgumentException>( () => RawVertexLayout.Validate( 28, 3, 85, fields ) );
		var padded = RawVertexLayout.PadTrailingBytes( input, 84 );
		Assert.AreEqual( 84, padded.Length );
		CollectionAssert.AreEqual( original, input );
		CollectionAssert.AreEqual( original, padded[..80] );
		CollectionAssert.AreEqual( new byte[4], padded[80..] );
	}

	[TestMethod]
	public void ValidationDoesNotAssumeTheLegacyManagedSixteenFieldArrayIsANativeLimit()
	{
		var fields = new RawVertexField[17];
		for ( int i = 0; i < fields.Length; ++i ) fields[i] = new( "texcoord", i, 0, ColorFormat.COLOR_FORMAT_R32G32_FLOAT );
		RawVertexLayout.Validate( 8, 1, 8, fields );
	}
}
