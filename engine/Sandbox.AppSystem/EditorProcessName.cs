using System;
using System.IO;
using System.Text;

namespace Sandbox;

/// <summary>Give Linux process monitors a short project-specific editor name.</summary>
internal static class EditorProcessName
{
	internal static string FromProjectPath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		var project = Path.GetFileNameWithoutExtension( path );
		if ( string.IsNullOrWhiteSpace( project ) ) return null;

		// Linux limits comm to 15 UTF-8 bytes. Do not split a multi-byte character.
		var name = new StringBuilder( "sbox:" );
		var bytes = 5;
		foreach ( var rune in project.EnumerateRunes() )
		{
			if ( Rune.IsControl( rune ) ) continue;
			if ( bytes + rune.Utf8SequenceLength > 15 ) break;
			name.Append( rune.ToString() );
			bytes += rune.Utf8SequenceLength;
		}

		return bytes > 5 ? name.ToString() : null;
	}

	internal static void SetFromProjectPath( string path )
	{
		if ( !OperatingSystem.IsLinux() ) return;
		var name = FromProjectPath( path );
		if ( name is null ) return;

		try
		{
			// /proc/self/comm names the process leader even if startup runs on another thread.
			File.WriteAllText( "/proc/self/comm", name );
		}
		catch ( IOException ) { }
		catch ( UnauthorizedAccessException ) { }
	}
}
