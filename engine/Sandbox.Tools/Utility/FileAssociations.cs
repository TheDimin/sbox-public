using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Editor;

internal class FileAssociations
{
	/// <summary>
	/// Creates/updates file associations between .sbproj files and the editor
	/// </summary>
	internal static void Create()
	{
		if ( OperatingSystem.IsLinux() )
			CreateLinux();
		else
			CreateWindows();
	}

	private static void CreateLinux()
	{
		try
		{
			// On Linux this is $XDG_DATA_HOME, or ~/.local/share when it is not set.
			var data = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );

			// Writes a file only when its content changes, so a normal start does not refresh the desktop caches.
			bool Write( string path, byte[] content )
			{
				path = Path.Combine( data, path );
				if ( File.Exists( path ) && File.ReadAllBytes( path ).AsSpan().SequenceEqual( content ) ) return false;

				Directory.CreateDirectory( Path.GetDirectoryName( path ) );
				File.WriteAllBytes( path, content );
				return true;
			}

			string ReadTemplate( string name )
			{
				using var stream = typeof( FileAssociations ).Assembly.GetManifestResourceStream( $"Sandbox.Tools.Linux.{name}" );
				using var reader = new StreamReader( stream );
				return reader.ReadToEnd().ReplaceLineEndings( "\n" );
			}

			var mime = ReadTemplate( "sandbox-project.xml" );
			var desktop = ReadTemplate( "sandbox-project.desktop" )
				.Replace( "@EDITOR@", FileSystem.Root.GetFullPath( "sbox-dev" ) );
			var changed = Write( "mime/packages/sandbox-project.xml", Encoding.UTF8.GetBytes( mime ) );
			changed |= Write( "applications/sandbox-project.desktop", Encoding.UTF8.GetBytes( desktop ) );

			// The file manager finds the MIME icon by name in the hicolor theme. A missing logo must not block the association.
			var logo = FileSystem.Root.GetFullPath( "core/tools/images/logo_rounded.png" );
			if ( File.Exists( logo ) ) changed |= Write( "icons/hicolor/128x128/mimetypes/application-x-sbproj.png", File.ReadAllBytes( logo ) );

			if ( !changed ) return;

			// Refresh the desktop caches. Each tool is optional, so one shell runs them in order and ignores failures.
			Process.Start( "sh", ["-c", "update-mime-database \"$0/mime\"; gtk-update-icon-cache -f -t \"$0/icons/hicolor\"; xdg-mime default sandbox-project.desktop application/x-sbproj", data] )?.Dispose();
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "Failed to associate project file extension" );
		}
	}

	private static void CreateWindows()
	{
		var sboxExeFilePath = $"\"{FileSystem.Root.GetFullPath( "sbox-dev.exe" )}\"";

		try
		{
			RegistryKey fileTypeKey = Registry.CurrentUser.CreateSubKey( @"SOFTWARE\Classes\Sandbox.ProjectFile" );
			fileTypeKey.SetValue( "", "Sandbox Project File" );
			fileTypeKey.CreateSubKey( "DefaultIcon" ).SetValue( "", sboxExeFilePath );

			RegistryKey shellKey = fileTypeKey.CreateSubKey( "shell" );
			RegistryKey shellOpenKey = shellKey.CreateSubKey( "open" );
			shellOpenKey.SetValue( "", "Open" );
			shellOpenKey.CreateSubKey( "command" ).SetValue( "", sboxExeFilePath + " -project \"%1\"" );

			RegistryKey fileExtensionKey = Registry.CurrentUser.CreateSubKey( @"SOFTWARE\Classes\.sbproj" );
			fileExtensionKey.SetValue( "", "Sandbox.ProjectFile" );

			// Tell explorer the file association has been changed
			SHChangeNotify( 0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "Failed to associate project file extension" );
		}
	}

	[DllImport( "shell32.dll", CharSet = CharSet.Auto, SetLastError = true )]
	public static extern void SHChangeNotify( uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2 );
}
