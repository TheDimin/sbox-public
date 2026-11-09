using NativeEngine;
using Sandbox.Gltf;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ResourceTests;

/// <summary>
/// Real model-cache ownership regressions. CopyStrongHandle preserves the same
/// native model deterministically; no assertion depends on allocator address reuse.
/// </summary>
[TestClass]
public sealed class GltfModelResourceLifetimeTests
{
	[TestMethod]
	public void DestroyedProceduralModelImmediatelyLeavesBothCanonicalIndexes()
	{
		using var fixture = new Procedural();
		var model = fixture.Model; var key = model.native.GetBindingPtr().ToInt64();
		AssertCached( key, model );
		model.Destroy();
		Assert.IsFalse( model.IsValid, "Destroy must invalidate the owned wrapper." );
		Assert.IsFalse( NativeResourceCache.TryPeek<Model>( key, out var cached ) && ReferenceEquals( cached, model ), "A dead procedural wrapper must not remain canonical before queued native release." );
		Assert.IsFalse( Model.Loaded.TryGetValue( key, out var weak ) && weak.TryGetTarget( out var loaded ) && ReferenceEquals( loaded, model ), "Main-thread destruction must remove its own legacy weak index." );
	}

	[TestMethod]
	public void RetainedNativeHandleCreatesAValidWrapperAfterOriginalDestruction()
	{
		using var fixture = new Procedural();
		var original = fixture.Model; var key = original.native.GetBindingPtr().ToInt64();
		var copied = original.native.CopyStrongHandle(); Model replacement = null;
		try
		{
			Assert.AreEqual( key, copied.GetBindingPtr().ToInt64(), "The copied handle must preserve exactly the same native binding." );
			original.Destroy();
			replacement = Model.FromNative( copied, procedural: true, name: "owned_same_binding_replacement" ); copied = default;
			Assert.IsNotNull( replacement, "A valid retained native handle must round-trip." );
			Assert.AreNotSame( original, replacement, "FromNative must never resurrect the explicitly destroyed wrapper." );
			Assert.IsTrue( replacement is { IsValid: true, IsError: false } && replacement.HasRenderMeshes(), "The new wrapper must retain a live renderable native model." );
			Assert.AreEqual( key, replacement.native.GetBindingPtr().ToInt64(), "This is a same-binding regression, not an unrelated fresh allocation." );
			AssertCached( key, replacement );
		}
		finally { replacement?.Destroy(); if ( !copied.IsNull ) copied.DestroyStrongHandle(); }
	}

	[TestMethod]
	public void DeadLegacyWeakEntryIsRemovedDuringOwnedModelDestruction()
	{
		using var fixture = new Procedural();
		var model = fixture.Model; var key = model.native.GetBindingPtr().ToInt64();
		// Short weak targets can already be cleared when Resource finalization
		// begins. Construct that index state explicitly; no GC timing is assumed.
		Model.Loaded[key] = new WeakReference<Model>( null );
		model.Destroy();
		Assert.IsFalse( Model.Loaded.ContainsKey( key ), "A captured dead legacy entry is safe to remove; live replacement owners must still be preserved." );
	}

	[TestMethod]
	public async Task DestroyingAnOlderWrapperPreservesItsSameBindingReplacement()
	{
		using var fixture = new Procedural();
		var original = fixture.Model; var key = original.native.GetBindingPtr().ToInt64();
		var copied = original.native.CopyStrongHandle(); Model replacement = null;
		try
		{
			// Only the fixture's own canonical key is removed. Both wrappers then
			// have separate valid strong handles to the exact same native model.
			AssertCached( key, original ); NativeResourceCache.Remove( key );
			replacement = Model.FromNative( copied, procedural: true, name: "owned_live_replacement" ); copied = default;
			Assert.IsTrue( replacement is { IsValid: true, IsError: false } );
			Assert.AreNotSame( original, replacement ); AssertCached( key, replacement );
			// The older wrapper can finalize away from the main thread. Its
			// queued native/legacy cleanup must preserve the already registered
			// replacement, without blocking the editor to control queue timing.
			await Task.Run( () => original.Destroy() );
			await MainThread.Wait();
			AssertCached( key, replacement );
			original.Destroy();
			await Frames();
			Assert.IsTrue( replacement.IsValid && replacement.HasRenderMeshes(), "Old-wrapper cleanup must not invalidate a newer owner's strong handle." );
			AssertCached( key, replacement );
			Assert.IsTrue( Model.Loaded.TryGetValue( key, out var weak ) && weak.TryGetTarget( out var loaded ) && ReferenceEquals( loaded, replacement ), "Queued legacy-index cleanup must preserve the replacement owner." );
		}
		finally { await MainThread.Wait(); replacement?.Destroy(); if ( !copied.IsNull ) copied.DestroyStrongHandle(); }
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RepeatedActualModelLoadsEvictOnlyTheirOwnedWrappers( bool compiled )
	{
		Assert.IsTrue( ThreadSafe.IsMainThread && !Graphics.IsActive );
		Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false } );
		var directory = Path.Combine( Path.GetTempPath(), "gltf_lifetime_cycles_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory ); var aggregate = Sandbox.FileSystem.Mounted; var mount = new LocalFileSystem( directory );
		aggregate.Mount( mount );
		try
		{
			for ( var cycle = 0; cycle < 4; cycle++ )
			{
				var name = "cycle_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				File.WriteAllBytes( Path.Combine( directory, name ), compiled ? GltfCompiler.Compile( Source ) : Source );
				var model = Model.Load( name );
				var owned = model is not null && string.Equals( model.ResourcePath, Resource.FixPath( name ), StringComparison.OrdinalIgnoreCase );
				long key = 0;
				try
				{
					Assert.IsTrue( owned && model is { IsValid: true, IsError: false } && model.HasRenderMeshes(), $"Actual load cycle {cycle} must produce a live owned model." );
					key = model.native.GetBindingPtr().ToInt64(); AssertCached( key, model );
				}
				finally { if ( owned ) { model.Destroy(); model.Destroy(); } }
				Assert.IsFalse( NativeResourceCache.TryPeek<Model>( key, out var cached ) && ReferenceEquals( model, cached ), $"Load cycle {cycle} must not leave its disposed wrapper indexed." );
			}
		}
		finally { aggregate.UnMount( mount ); mount.Dispose(); Directory.Delete( directory, true ); }
	}

	private static void AssertCached( long key, Model expected ) => Assert.IsTrue( NativeResourceCache.TryPeek<Model>( key, out var cached ) && ReferenceEquals( expected, cached ), "The canonical cache must retain the exact current owner." );
	private static async Task Frames()
	{
		var frame = Application.FrameCount; var timer = Stopwatch.StartNew();
		while ( Application.FrameCount < frame + 3 ) { Assert.IsTrue( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "The authorized engine must keep receiving normal frames." ); await Task.Delay( 20 ); await MainThread.Wait(); }
	}

	private sealed class Procedural : IDisposable
	{
		private Material material;
		private Mesh mesh;
		internal Model Model { get; private set; }
		internal Procedural()
		{
			Assert.IsTrue( ThreadSafe.IsMainThread && !Graphics.IsActive );
			try
			{
				material = Material.Load( "materials/default/white.vmat" ).CreateCopy();
				Assert.IsTrue( material is { IsValid: true, IsError: false }, "The owned fixture requires a valid copied stock material." );
				mesh = new Mesh( "owned_lifetime_triangle", material );
				mesh.CreateVertexBuffer( 3, new[] { new Vertex( Vector3.Zero ), new Vertex( Vector3.Left ), new Vertex( Vector3.Up ) }.AsSpan() );
				mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 }.AsSpan() ); mesh.Bounds = new BBox( Vector3.Zero, new Vector3( 1, 1, 1 ) );
				Model = Sandbox.Model.Builder.WithName( "owned_lifetime_" + Guid.NewGuid().ToString( "N" ) ).AddMesh( mesh ).Create();
				Assert.IsTrue( Model is { IsValid: true, IsError: false } && Model.IsProcedural, "The owned procedural precondition must hold." );
			}
			catch { Dispose(); throw; }
		}
		public void Dispose()
		{
			Model?.Destroy(); Model = null;
			if ( mesh is not null ) { GltfModelLoader.ReleaseMeshes( [mesh] ); mesh = null; }
			material?.Destroy(); material = null;
		}
	}

	private static byte[] Source => Encoding.UTF8.GetBytes( """
		{"asset":{"version":"2.0"},"buffers":[{"byteLength":36,"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAA"}],
		"bufferViews":[{"buffer":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]}],
		"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
		""" );
}
