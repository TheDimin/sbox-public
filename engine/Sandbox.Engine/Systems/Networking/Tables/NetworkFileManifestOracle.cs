using Sandbox.Network;
using System.Security.Cryptography;
using System.Text;

namespace Sandbox;

internal static class NetworkFileManifestOracle
{
	internal static string BuildRepresentation( StringTable smallFiles, StringTable largeFiles )
	{
		var lines = new List<string>( smallFiles.Entries.Count + largeFiles.Entries.Count );

		foreach ( var entry in smallFiles.Entries.Values )
		{
			var contentHash = Convert.ToHexString( SHA256.HashData( entry.Data ) );
			lines.Add( $"Small|{entry.Name}|{entry.Data.LongLength}|{contentHash}" );
		}

		foreach ( var entry in largeFiles.Entries.Values )
		{
			var info = entry.Read<LargeNetworkFiles.LargeFileInfo>();
			lines.Add( $"Large|{entry.Name}|{info.Size}|{info.CRC:X16}" );
		}

		lines.Sort( StringComparer.Ordinal );
		return string.Join( "\n", lines );
	}

	internal static string ComputeHash( StringTable smallFiles, StringTable largeFiles )
	{
		var representation = BuildRepresentation( smallFiles, largeFiles );
		return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( representation ) ) );
	}
}
