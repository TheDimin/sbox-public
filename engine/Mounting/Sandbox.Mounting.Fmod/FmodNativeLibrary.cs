using System;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting;

interface INativeLibraryLoader
{
	IntPtr Load( string path );
	IntPtr GetExport( IntPtr library, string name );
	void Free( IntPtr library );
}

sealed class NativeLibraryLoader : INativeLibraryLoader
{
	public IntPtr Load( string path ) => NativeLibrary.Load( path );
	public IntPtr GetExport( IntPtr library, string name ) => NativeLibrary.GetExport( library, name );
	public void Free( IntPtr library ) => NativeLibrary.Free( library );
}

sealed class FmodNativeLibrary : IDisposable
{
	readonly INativeLibraryLoader _loader;

	internal IntPtr CoreHandle { get; private set; }
	internal IntPtr StudioHandle { get; private set; }

	internal FmodNativeLibrary( string corePath, string studioPath, INativeLibraryLoader loader = null )
	{
		_loader = loader ?? new NativeLibraryLoader();

		try
		{
			CoreHandle = _loader.Load( corePath );
			StudioHandle = _loader.Load( studioPath );
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	internal T GetStudioExport<T>( string name ) where T : Delegate
	{
		if ( StudioHandle == IntPtr.Zero )
			throw new ObjectDisposedException( nameof( FmodNativeLibrary ) );

		return Marshal.GetDelegateForFunctionPointer<T>( _loader.GetExport( StudioHandle, name ) );
	}

	public void Dispose()
	{
		if ( StudioHandle != IntPtr.Zero )
		{
			_loader.Free( StudioHandle );
			StudioHandle = IntPtr.Zero;
		}

		if ( CoreHandle != IntPtr.Zero )
		{
			_loader.Free( CoreHandle );
			CoreHandle = IntPtr.Zero;
		}
	}
}
