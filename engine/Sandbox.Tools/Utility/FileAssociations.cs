using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Editor;

internal class FileAssociations
{
	/// <summary>
	/// Creates/updates file associations between .sbproj files and the editor
	/// </summary>
	internal static void Create()
	{
		if ( OperatingSystem.IsWindows() )
		{
			CreateWindows();
		}
		else if ( OperatingSystem.IsLinux() )
		{
			CreateLinux();
		}
	}

	internal const string DesktopFileName = "sandbox-project.desktop";
	private const string MimeType = "application/x-sbproj";

	/// <summary>
	/// Writes the shared-mime-info type, icon and desktop entry that make .sbproj files open in the editor.
	/// Returns the commands that refresh the desktop caches. The caller runs them.
	/// </summary>
	internal static string[][] WriteLinuxFiles( string dataHome, string editorPath, string iconSourcePath )
	{
		var mimeRoot = Path.Combine( dataHome, "mime" );
		var iconRoot = Path.Combine( dataHome, "icons", "hicolor" );
		var mimePackages = Path.Combine( mimeRoot, "packages" );
		var applications = Path.Combine( dataHome, "applications" );
		var iconFolder = Path.Combine( iconRoot, "128x128", "mimetypes" );

		foreach ( var folder in new[] { mimePackages, applications, iconFolder } )
			Directory.CreateDirectory( folder );

		// The file manager finds the MIME icon by name in the hicolor theme.
		File.Copy( iconSourcePath, Path.Combine( iconFolder, "application-x-sbproj.png" ), true );

		WriteUnixText( Path.Combine( mimePackages, "sandbox-project.xml" ), """
			<?xml version="1.0" encoding="UTF-8"?>
			<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
			  <mime-type type="application/x-sbproj">
			    <comment>Sandbox Project File</comment>
			    <icon name="application-x-sbproj"/>
			    <glob pattern="*.sbproj"/>
			  </mime-type>
			</mime-info>
			""" );

		// Desktop entry values are quoted with double quotes, and \ " ` $ must be escaped inside them.
		var quotedPath = editorPath.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" ).Replace( "`", "\\`" ).Replace( "$", "\\$" );

		WriteUnixText( Path.Combine( applications, DesktopFileName ), $"""
			[Desktop Entry]
			Type=Application
			Name=s&box
			Comment=Open a Sandbox project
			Icon=application-x-sbproj
			Exec="{quotedPath}" -project %f
			MimeType={MimeType};
			NoDisplay=true
			Terminal=false
			Categories=Development;Game;
			""" );

		return
		[
			["update-mime-database", mimeRoot],
			["gtk-update-icon-cache", "-f", "-t", iconRoot],
			["xdg-mime", "default", DesktopFileName, MimeType],
		];
	}

	/// <summary>
	/// Desktop files need LF line endings, and source files may use CRLF inside raw strings.
	/// </summary>
	private static void WriteUnixText( string path, string text )
	{
		File.WriteAllText( path, text.ReplaceLineEndings( "\n" ) + "\n" );
	}

	private static void CreateLinux()
	{
		try
		{
			var dataHome = Environment.GetEnvironmentVariable( "XDG_DATA_HOME" );
			if ( string.IsNullOrEmpty( dataHome ) )
				dataHome = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".local", "share" );

			var commands = WriteLinuxFiles( dataHome, FileSystem.Root.GetFullPath( "sbox-dev" ), FileSystem.Root.GetFullPath( "core/tools/images/logo_rounded.png" ) );

			// Every tool is optional, and each can take seconds, so keep them off the startup path.
			_ = Task.Run( () => Array.ForEach( commands, RunTool ) );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "Failed to associate project file extension" );
		}
	}

	private static void RunTool( string[] command )
	{
		try
		{
			using var process = Process.Start( new ProcessStartInfo( command[0], command[1..] ) { UseShellExecute = false } );
			if ( process is null || process.WaitForExit( 5000 ) ) return;

			process.Kill();
			process.WaitForExit();
		}
		catch ( Exception ex )
		{
			Log.Info( $"Could not run {command[0]}: {ex.Message}" );
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
