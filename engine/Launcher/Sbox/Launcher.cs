using System.Threading.Tasks;

namespace Sandbox;

public static class Launcher
{
	public static int Main()
	{
		var headless = HeadlessClientOptions.WasRequested;
		var options = default( HeadlessClientOptions );
		if ( headless && !HeadlessClientOptions.TryParse( out options, out var error ) )
		{
			System.Console.Error.WriteLine( error );
			return 1;
		}

		if ( headless )
		{
			System.Console.WriteLine( $"Starting headless client {options.InstanceId} for {options.Target} at {HeadlessClientOptions.GetFrameRate()} fps." );
		}

		if ( headless )
		{
			return RunHeadless( options );
		}

		new GameAppSystem().Run();

		return 0;
	}

	private static int RunHeadless( HeadlessClientOptions options )
	{
		using var instanceLock = new System.Threading.Mutex( true, $"sbox-headless-client-{options.InstanceId}", out var ownsInstanceId );
		if ( !ownsInstanceId )
		{
			System.Console.Error.WriteLine( $"Headless client instance id {options.InstanceId} is already in use." );
			return 1;
		}

		try
		{
			new GameAppSystem( true ).Run();
			return 0;
		}
		finally
		{
			instanceLock.ReleaseMutex();
		}
	}
}

public class GameAppSystem : AppSystem
{
	private readonly bool _headless;

	public GameAppSystem( bool headless = false )
	{
		_headless = headless;
	}

	public override void Init()
	{
		LoadSteamDll();
		TestSystemRequirements();

		base.Init();

		CreateGame();
		if ( !_headless )
		{
			CreateMenu();
		}

		var createInfo = new AppSystemCreateInfo()
		{
			WindowTitle = "s&box",
			Flags = AppSystemFlags.IsGameApp | (_headless ? AppSystemFlags.IsConsoleApp : AppSystemFlags.None)
		};

		InitGame( createInfo );
	}
}
