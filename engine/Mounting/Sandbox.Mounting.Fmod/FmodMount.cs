using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Mounting;

/// <summary>
/// s&amp;box mount hook that owns FMOD for exactly as long as the mount is enabled.
/// </summary>
public sealed class FmodMount : BaseGameMount
{
	const string DemoEventPath = "event:/Weapons/Explosion";

	readonly object _sync = new();
	string _sdkRoot;
	string _bankDirectory;
	Timer _updateTimer;

	public override string Ident => "fmod";
	public override string Title => "FMOD";

	/// <summary>
	/// The active FMOD manager, or null while this mount is disabled.
	/// </summary>
	public FmodManager Manager { get; private set; }

	protected override void Initialize( InitializeContext context )
	{
		if ( !OperatingSystem.IsWindows() )
		{
			Log.Warning( "The FMOD mount currently supports Windows only." );
			return;
		}

		_sdkRoot = Environment.GetEnvironmentVariable( "FMOD_SDK_ROOT" );
		if ( string.IsNullOrWhiteSpace( _sdkRoot ) )
			_sdkRoot = FmodManager.DefaultSdkRoot;

		var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
		{
			System.Runtime.InteropServices.Architecture.X64 => "x64",
			System.Runtime.InteropServices.Architecture.X86 => "x86",
			System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
			_ => null
		};

		if ( architecture is null )
			return;

		_bankDirectory = Path.Combine( _sdkRoot, "api", "studio", "examples", "media" );
		IsInstalled = File.Exists( Path.Combine( _sdkRoot, "api", "core", "lib", architecture, "fmod.dll" ) )
			&& File.Exists( Path.Combine( _sdkRoot, "api", "studio", "lib", architecture, "fmodstudio.dll" ) )
			&& File.Exists( Path.Combine( _bankDirectory, "Master.bank" ) )
			&& File.Exists( Path.Combine( _bankDirectory, "Master.strings.bank" ) )
			&& File.Exists( Path.Combine( _bankDirectory, "SFX.bank" ) );

		if ( !IsInstalled )
			Log.Warning( $"FMOD SDK or example banks were not found under '{_sdkRoot}'. Set FMOD_SDK_ROOT to override it." );
	}

	protected override Task Mount( MountContext context )
	{
		lock ( _sync )
		{
			FmodManager manager = null;

			try
			{
				manager = new FmodManager( _sdkRoot );
				manager.Initialize();
				manager.LoadDefaultBanks( _bankDirectory );

				Manager = manager;
				_updateTimer = new Timer( UpdateFmod, manager, TimeSpan.Zero, TimeSpan.FromMilliseconds( 16 ) );
				manager.PlayOneShot( DemoEventPath );

				IsMounted = true;
				Log.Info( $"FMOD Core and Studio loaded. Played '{DemoEventPath}'." );
			}
			catch ( Exception exception )
			{
				Log.Error( exception, "Unable to mount FMOD." );
				_updateTimer?.Dispose();
				_updateTimer = null;
				manager?.Dispose();
				Manager = null;
			}
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Plays the example explosion through the manager owned by this mount.
	/// </summary>
	public bool PlayDemoSound()
	{
		var manager = Manager;
		if ( manager is null || !manager.IsInitialized )
			return false;

		try
		{
			manager.PlayOneShot( DemoEventPath );
			return true;
		}
		catch ( Exception exception )
		{
			Log.Error( exception, $"Unable to play '{DemoEventPath}'." );
			return false;
		}
	}

	protected override void Shutdown()
	{
		lock ( _sync )
		{
			_updateTimer?.Dispose();
			_updateTimer = null;

			Manager?.Dispose();
			Manager = null;
			Log.Info( "FMOD Studio and Core unloaded." );
		}
	}

	void UpdateFmod( object state )
	{
		try
		{
			((FmodManager)state).Update();
		}
		catch ( Exception exception )
		{
			Log.Error( exception, "FMOD update failed." );
			try
			{
				_updateTimer?.Change( Timeout.Infinite, Timeout.Infinite );
			}
			catch ( ObjectDisposedException )
			{
				// Shutdown won the race with this update callback.
			}
		}
	}
}
