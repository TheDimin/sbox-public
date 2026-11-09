using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox.Tests;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

[TestClass]
public sealed class GltfGpuSourceOwnershipTests
{
	private const string Fixture = """
		{"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1}}]}],
		 "accessors":[{"componentType":5126,"type":"VEC3","count":3,"min":[0,0,0],"max":[0,0,0]},
		 {"componentType":5126,"type":"VEC2","count":3}]}
		""";
	private static GltfDocument Read() => GltfReader.Read( Encoding.UTF8.GetBytes( Fixture ) );

	[TestMethod]
	public void ExactRetainedUploadAndStreamReferencesAreAccepted()
	{
		using var document = Read(); var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		plan.RequireSourceOwnership( document, 0, 0, plan.Upload );
		plan.RequireSourceStreamOwnership( "TEXCOORD_0", plan.Upload.Attributes["TEXCOORD_0"] );
		Assert.AreSame( document, plan.OwnerDocument );
		Assert.AreSame( plan.Upload.Attributes["TEXCOORD_0"], plan.Sources["TEXCOORD_0"].Stream );
	}

	[TestMethod]
	public void IdenticalForeignDocumentCannotClaimTheRetainedUpload()
	{
		using var owner = Read(); using var foreign = Read(); var plan = GltfGpuPrimitivePlan.Create( owner, 0, 0 );
		Assert.ThrowsException<GltfException>( () => plan.RequireSourceOwnership( foreign, 0, 0, plan.Upload ) );
		Assert.ThrowsException<GltfException>( () => plan.RequireSourceOwnership( owner, 0, 0, foreign.MaterializePrimitiveUpload( 0, 0 ) ) );
	}

	[TestMethod]
	public void ASecondSnapshotFromTheSameDocumentDoesNotReplaceTheBoundStreams()
	{
		using var document = Read(); var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var detached = document.MaterializePrimitiveUpload( 0, 0 );
		Assert.ThrowsException<GltfException>( () => plan.RequireSourceOwnership( document, 0, 0, detached ) );
		Assert.ThrowsException<GltfException>( () => plan.RequireSourceStreamOwnership( "TEXCOORD_0", detached.Attributes["TEXCOORD_0"] ) );
		plan.RequireSourceOwnership( document, 0, 0, plan.Upload );
		plan.RequireSourceStreamOwnership( "TEXCOORD_0", plan.Upload.Attributes["TEXCOORD_0"] );
	}

	[TestMethod]
	public void SourceMeshPrimitiveAndSemanticIdentityCannotBeSubstituted()
	{
		using var document = Read(); var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		Assert.ThrowsException<GltfException>( () => plan.RequireSourceOwnership( document, 1, 0, plan.Upload ) );
		Assert.ThrowsException<GltfException>( () => plan.RequireSourceOwnership( document, 0, 1, plan.Upload ) );
		Assert.ThrowsException<GltfException>( () => plan.RequireSourceStreamOwnership( "TEXCOORD_1", plan.Upload.Attributes["TEXCOORD_0"] ) );
		Assert.ThrowsException<GltfException>( () => plan.RequireSourceStreamOwnership( "TEXCOORD_0", plan.Upload.Attributes["POSITION"] ) );
	}

	[TestMethod]
	public void RetainedSnapshotDoesNotGrantPermissionToUseAReleasedDocument()
	{
		var document = Read(); var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 ); document.Dispose();
		Assert.ThrowsException<ObjectDisposedException>( () => plan.RequireSourceOwnership( document, 0, 0, plan.Upload ) );
		Assert.ThrowsException<ObjectDisposedException>( () => plan.RequireSourceStreamOwnership( "TEXCOORD_0", plan.Upload.Attributes["TEXCOORD_0"] ) );
	}
}
