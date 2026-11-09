using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

[TestClass]
public sealed class Png16ProductionBoundaryTests
{
	private static GltfImage Image() => new() { MimeType = "image/png", Bytes = Convert.FromBase64String( "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABEAYAAACksqPJAAAAGUlEQVR4nGNgZGRkamA0sGRkYmRsYLpiCAAORAJ8hmWiZwAAAABJRU5ErkJggg==" ) };
	[TestMethod]
	public void ColorRoleRemainsExplicitlyRejected() => Assert.ThrowsException<GltfException>( () => global::Sandbox.GltfTextureCompiler.Compile( Image(), true ) );
	[TestMethod]
	public void BudgetRejectsColorDataAndMetallicRoughnessBeforeDecode()
	{
		Assert.ThrowsException<GltfException>( () => global::Sandbox.GltfTextureCompiler.Compile( Image(), false, 1 ) );
		Assert.ThrowsException<GltfException>( () => global::Sandbox.GltfTextureCompiler.CompileWithMipmaps( Image(), false, 1 ) );
		Assert.ThrowsException<GltfException>( () => global::Sandbox.GltfTextureCompiler.CompileMetallicRoughness( Image(), 1, 1, true, 1 ) );
	}
}
