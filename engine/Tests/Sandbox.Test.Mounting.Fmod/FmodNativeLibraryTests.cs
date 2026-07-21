using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Mounting;
using System;
using System.Collections.Generic;
using System.IO;

namespace Sandbox.Test.Mounting.Fmod;

[TestClass]
public class FmodNativeLibraryTests
{
	[TestMethod]
	public void DisposeUnloadsStudioBeforeCoreAndCanBeRepeated()
	{
		var loader = new FakeNativeLibraryLoader();

		using ( var libraries = new FmodNativeLibrary( "sdk/core/fmod.dll", "sdk/studio/fmodstudio.dll", loader ) )
		{
			Assert.AreNotEqual( IntPtr.Zero, libraries.CoreHandle );
			Assert.AreNotEqual( IntPtr.Zero, libraries.StudioHandle );
		}

		using ( var libraries = new FmodNativeLibrary( "sdk/core/fmod.dll", "sdk/studio/fmodstudio.dll", loader ) )
		{
			libraries.Dispose();
			libraries.Dispose();
		}

		CollectionAssert.AreEqual( new[]
		{
			"load:fmod.dll:1",
			"load:fmodstudio.dll:2",
			"free:2",
			"free:1",
			"load:fmod.dll:3",
			"load:fmodstudio.dll:4",
			"free:4",
			"free:3"
		}, loader.Calls );
	}

	[TestMethod]
	public void StudioLoadFailureUnloadsCore()
	{
		var loader = new FakeNativeLibraryLoader( failOnLoad: 2 );

		Assert.ThrowsException<DllNotFoundException>( () =>
			new FmodNativeLibrary( "sdk/core/fmod.dll", "sdk/studio/fmodstudio.dll", loader ) );

		CollectionAssert.AreEqual( new[]
		{
			"load:fmod.dll:1",
			"load:fmodstudio.dll:failed",
			"free:1"
		}, loader.Calls );
	}

	sealed class FakeNativeLibraryLoader : INativeLibraryLoader
	{
		int _nextHandle = 1;
		readonly int _failOnLoad;

		internal List<string> Calls { get; } = [];

		internal FakeNativeLibraryLoader( int failOnLoad = 0 )
		{
			_failOnLoad = failOnLoad;
		}

		public IntPtr Load( string path )
		{
			if ( _nextHandle == _failOnLoad )
			{
				Calls.Add( $"load:{Path.GetFileName( path )}:failed" );
				throw new DllNotFoundException( path );
			}

			var handle = new IntPtr( _nextHandle++ );
			Calls.Add( $"load:{Path.GetFileName( path )}:{handle}" );
			return handle;
		}

		public IntPtr GetExport( IntPtr library, string name ) => throw new NotSupportedException();

		public void Free( IntPtr library )
		{
			Calls.Add( $"free:{library}" );
		}
	}
}
