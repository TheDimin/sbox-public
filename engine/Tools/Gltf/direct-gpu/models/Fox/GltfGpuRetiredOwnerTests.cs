using System;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Retain the invalid owner deliberately while checking retired CPU graph collection.</summary>
[TestClass]
public sealed class GltfGpuRetiredOwnerTests
{
	private sealed class ResolverCapture { }
	private sealed record Retired( GltfGpuAsset Asset, GltfDocument BorrowedDocument, WeakReference<byte[]> Arena, WeakReference<GltfGpuRig> Rig, WeakReference<ResolverCapture> Resolver );

	[TestMethod]
	public void InvalidOwnerDoesNotRetainRetiredGeometryOrResolverClosures()
	{
		var retired = CreateRetired();
		try
		{
			Collect();
			Assert.IsFalse( IsAlive( retired.Arena ), "Invalid retained owner still holds its retired raw CPU arena." );
			Assert.IsFalse( IsAlive( retired.Rig ), "Invalid retained owner still holds its retired decoded rig arrays." );
			Assert.IsFalse( IsAlive( retired.Resolver ), "Invalid retained owner still retains caller resolver captures." );
			Assert.IsFalse( retired.Asset.IsValid );
			Assert.AreEqual( "2.0", retired.BorrowedDocument.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString(),
				"Releasing borrowed references must not dispose the caller's document." );
			GC.KeepAlive( retired.Asset );
		}
		finally { retired.Asset.Dispose(); retired.BorrowedDocument.Dispose(); }
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static Retired CreateRetired()
	{
		var document = GltfGpuBackendContractTests.InterleavedTriangle(); var capture = new ResolverCapture();
		var asset = GltfGpuAsset.Prepare( document, "shaders/__cpu_retirement_must_not_realize.shader", _ =>
		{
			GC.KeepAlive( capture ); throw new InvalidOperationException( "CPU retirement invoked material realization." );
		} );
		var result = new Retired( asset, document, new( asset.Primitives[0].Plan.InputBytes ), new( asset.Nodes[0].Rig ), new( capture ) );
		asset.Dispose(); return result;
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static bool IsAlive<T>( WeakReference<T> value ) where T : class => value.TryGetTarget( out _ );
	private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
