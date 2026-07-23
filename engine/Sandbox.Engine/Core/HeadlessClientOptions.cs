using Sandbox.Utility;
using System.Net;

namespace Sandbox;

/// <summary>
/// Command-line validation shared by the client launcher and networking layer.
/// Headless multiplayer clients are intentionally restricted to local test sessions.
/// </summary>
internal readonly record struct HeadlessClientOptions( int InstanceId, string Target )
{
	internal const int DefaultFrameRate = 60;
	internal const int MinimumFrameRate = 10;

	internal static bool WasRequested => CommandLine.HasSwitch( "-headless" );

	internal static bool IsActive =>
		Application.IsHeadless && !Application.IsDedicatedServer && !Application.IsStandalone;

	internal static bool TryParse( out HeadlessClientOptions options, out string error )
	{
		options = default;
		error = null;

		var joinLocal = CommandLine.HasSwitch( "-joinlocal" );
		var hasConnect = CommandLine.HasSwitch( "+connect" );

		if ( joinLocal == hasConnect )
		{
			error = "Headless clients require exactly one target: -joinlocal or +connect <loopback address>.";
			return false;
		}

		var instanceId = CommandLine.GetSwitchInt( "+instanceid", 0 );
		if ( instanceId <= 0 )
		{
			error = "Headless clients require a positive +instanceid.";
			return false;
		}

		var target = joinLocal ? "local" : CommandLine.GetSwitch( "+connect", "" ).Trim().Trim( '"' );
		if ( !IsLoopbackTarget( target ) )
		{
			error = $"Headless clients can only connect to local targets; '{target}' is not a loopback address.";
			return false;
		}

		options = new HeadlessClientOptions( instanceId, target );
		return true;
	}

	internal static int GetFrameRate()
	{
		return Math.Max( MinimumFrameRate, CommandLine.GetSwitchInt( "+headless_fps", DefaultFrameRate ) );
	}

	internal static bool IsLoopbackTarget( string target )
	{
		if ( string.IsNullOrWhiteSpace( target ) )
			return false;

		target = target.Trim().Trim( '"' );
		if ( target.Equals( "local", StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( !TrySplitHostAndPort( target, out var host, out var port ) )
			return false;

		if ( port is not null && (port < 1 || port > ushort.MaxValue) )
			return false;

		if ( host.Equals( "localhost", StringComparison.OrdinalIgnoreCase ) )
			return true;

		return IPAddress.TryParse( host, out var address ) && IPAddress.IsLoopback( address );
	}

	private static bool TrySplitHostAndPort( string target, out string host, out int? port )
	{
		host = target;
		port = null;

		if ( target.StartsWith( '[' ) )
		{
			var closeBracket = target.IndexOf( ']' );
			if ( closeBracket < 0 )
				return false;

			host = target[1..closeBracket];
			if ( closeBracket == target.Length - 1 )
				return true;

			if ( target[closeBracket + 1] != ':' || !int.TryParse( target[(closeBracket + 2)..], out var bracketPort ) )
				return false;

			port = bracketPort;
			return true;
		}

		var colonCount = target.Count( x => x == ':' );
		if ( colonCount == 1 )
		{
			var separator = target.LastIndexOf( ':' );
			if ( !int.TryParse( target[(separator + 1)..], out var parsedPort ) )
				return false;

			host = target[..separator];
			port = parsedPort;
		}

		return colonCount <= 1 || IPAddress.TryParse( target, out _ );
	}
}
