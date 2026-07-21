using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting;

/// <summary>
/// Owns one FMOD Studio system and the native FMOD Core and Studio libraries that back it.
/// Dispose this object before replacing or unloading the mount assembly.
/// </summary>
public sealed class FmodManager : IDisposable
{
	/// <summary>
	/// Default installation directory for the Windows FMOD Studio API.
	/// </summary>
	public const string DefaultSdkRoot = @"C:\Program Files (x86)\FMOD SoundSystem\FMOD Studio API Windows";

	const uint HeaderVersion = 0x00020314;

	readonly object _sync = new();
	readonly string _corePath;
	readonly string _studioPath;
	readonly INativeLibraryLoader _libraryLoader;
	readonly Dictionary<string, IntPtr> _banks = new( StringComparer.OrdinalIgnoreCase );

	FmodNativeLibrary _libraries;
	FmodApi _api;
	IntPtr _studioSystem;
	bool _disposed;

	/// <summary>
	/// Creates an FMOD manager without loading native libraries. Call <see cref="Initialize"/> to load them.
	/// </summary>
	public FmodManager( string sdkRoot = null )
		: this( GetNativePath( sdkRoot, "core", "fmod.dll" ), GetNativePath( sdkRoot, "studio", "fmodstudio.dll" ), null )
	{
	}

	internal FmodManager( string corePath, string studioPath, INativeLibraryLoader libraryLoader )
	{
		_corePath = corePath;
		_studioPath = studioPath;
		_libraryLoader = libraryLoader;
	}

	/// <summary>
	/// True while the Studio system and its native libraries are loaded.
	/// </summary>
	public bool IsInitialized
	{
		get
		{
			lock ( _sync )
				return _studioSystem != IntPtr.Zero && !_disposed;
		}
	}

	/// <summary>
	/// Full paths of banks loaded through this manager.
	/// </summary>
	public IReadOnlyCollection<string> LoadedBanks
	{
		get
		{
			lock ( _sync )
				return _banks.Keys.ToArray();
		}
	}

	/// <summary>
	/// Loads FMOD Core and Studio, creates the Studio system, and initializes its audio output.
	/// </summary>
	public void Initialize( int maxChannels = 1024 )
	{
		lock ( _sync )
		{
			ThrowIfDisposed();
			if ( _studioSystem != IntPtr.Zero )
				return;

			try
			{
				_libraries = new FmodNativeLibrary( _corePath, _studioPath, _libraryLoader );
				_api = new FmodApi( _libraries );

				Check( _api.SystemCreate( out _studioSystem, HeaderVersion ), "create the Studio system" );
				Check( _api.SystemInitialize( _studioSystem, maxChannels, 0, 0, IntPtr.Zero ), "initialize the Studio system" );
			}
			catch
			{
				ReleaseNativeState();
				throw;
			}
		}
	}

	/// <summary>
	/// Loads a bank and tracks it for automatic cleanup.
	/// </summary>
	public bool LoadBank( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new ArgumentException( "A bank path is required.", nameof( path ) );

		path = Path.GetFullPath( path );

		lock ( _sync )
		{
			RequireInitialized();
			if ( _banks.ContainsKey( path ) )
				return false;

			if ( !File.Exists( path ) )
				throw new FileNotFoundException( "FMOD bank was not found.", path );

			var nativePath = Marshal.StringToCoTaskMemUTF8( path );
			try
			{
				Check( _api.SystemLoadBankFile( _studioSystem, nativePath, 0, out var bank ), $"load '{path}'" );
				_banks.Add( path, bank );
				return true;
			}
			finally
			{
				Marshal.FreeCoTaskMem( nativePath );
			}
		}
	}

	/// <summary>
	/// Unloads a bank previously loaded through this manager.
	/// </summary>
	public bool UnloadBank( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return false;

		path = Path.GetFullPath( path );

		lock ( _sync )
		{
			RequireInitialized();
			if ( !_banks.Remove( path, out var bank ) )
				return false;

			Check( _api.BankUnload( bank ), $"unload '{path}'" );
			return true;
		}
	}

	/// <summary>
	/// Loads the Master, strings, and SFX banks shipped with the FMOD Studio API examples.
	/// </summary>
	public void LoadDefaultBanks( string bankDirectory )
	{
		LoadBank( Path.Combine( bankDirectory, "Master.bank" ) );
		LoadBank( Path.Combine( bankDirectory, "Master.strings.bank" ) );
		LoadBank( Path.Combine( bankDirectory, "SFX.bank" ) );
	}

	/// <summary>
	/// Creates, starts, and releases a one-shot Studio event instance.
	/// </summary>
	public void PlayOneShot( string eventPath )
	{
		if ( string.IsNullOrWhiteSpace( eventPath ) )
			throw new ArgumentException( "An FMOD event path is required.", nameof( eventPath ) );

		lock ( _sync )
		{
			RequireInitialized();

			var nativePath = Marshal.StringToCoTaskMemUTF8( eventPath );
			try
			{
				Check( _api.SystemGetEvent( _studioSystem, nativePath, out var description ), $"find '{eventPath}'" );
				Check( _api.EventDescriptionCreateInstance( description, out var instance ), $"create '{eventPath}'" );
				Check( _api.EventInstanceStart( instance ), $"start '{eventPath}'" );
				Check( _api.EventInstanceRelease( instance ), $"release '{eventPath}'" );
				Check( _api.SystemUpdate( _studioSystem ), "update the Studio system" );
			}
			finally
			{
				Marshal.FreeCoTaskMem( nativePath );
			}
		}
	}

	/// <summary>
	/// Advances FMOD Studio's command and callback processing.
	/// </summary>
	public bool Update()
	{
		lock ( _sync )
		{
			if ( _disposed || _studioSystem == IntPtr.Zero )
				return false;

			Check( _api.SystemUpdate( _studioSystem ), "update the Studio system" );
			return true;
		}
	}

	/// <summary>
	/// Releases banks and the Studio system, then unloads Studio and FMOD Core.
	/// </summary>
	public void Dispose()
	{
		lock ( _sync )
		{
			if ( _disposed )
				return;

			_disposed = true;
			ReleaseNativeState();
		}
	}

	void ReleaseNativeState()
	{
		if ( _studioSystem != IntPtr.Zero && _api is not null )
		{
			_api.SystemUnloadAll( _studioSystem );
			_api.SystemUpdate( _studioSystem );
			_api.SystemRelease( _studioSystem );
			_studioSystem = IntPtr.Zero;
		}

		_banks.Clear();
		_api = null;
		_libraries?.Dispose();
		_libraries = null;
	}

	void RequireInitialized()
	{
		ThrowIfDisposed();
		if ( _studioSystem == IntPtr.Zero )
			throw new InvalidOperationException( "FMOD has not been initialized." );
	}

	void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf( _disposed, this );
	}

	static void Check( int result, string operation )
	{
		if ( result != 0 )
			throw new InvalidOperationException( $"FMOD failed to {operation} (result {result})." );
	}

	static string GetNativePath( string sdkRoot, string api, string fileName )
	{
		if ( !OperatingSystem.IsWindows() )
			throw new PlatformNotSupportedException( "The FMOD mount currently supports Windows only." );

		if ( string.IsNullOrWhiteSpace( sdkRoot ) )
			sdkRoot = Environment.GetEnvironmentVariable( "FMOD_SDK_ROOT" );

		if ( string.IsNullOrWhiteSpace( sdkRoot ) )
			sdkRoot = DefaultSdkRoot;

		var architecture = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.X86 => "x86",
			Architecture.Arm64 => "arm64",
			_ => throw new PlatformNotSupportedException( $"FMOD does not support {RuntimeInformation.ProcessArchitecture} in this mount." )
		};

		return Path.Combine( sdkRoot, "api", api, "lib", architecture, fileName );
	}
}
