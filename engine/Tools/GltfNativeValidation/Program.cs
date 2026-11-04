using System.Reflection;
using System.Text;
using Editor;
using Sandbox;
using Sandbox.Internal;
using Sandbox.Resources;

namespace GltfNativeValidation;

/// <summary>
/// Separate tools-mode experiment. A successful result proves only that a temporary
/// native parent compile can produce/load an in-memory child material; it does not
/// prove that a raw GLB carries native dependency metadata or is ready to ship.
/// </summary>
public static class Program
{
	private const string Prefix = "gltf_native_validation";
	// This standalone process follows ShaderCompiler's hard-exit lifetime. Keep
	// ownership at process scope; native tools teardown is deliberately not run.
	private static ProbeToolAppSystem probeAppSystem;

	[STAThread]
	public static int Main( string[] args )
	{
		var exitCode = 1;
		try
		{
			var root = Argument( args, "--repo-root" );
			var runDirectory = Argument( args, "--run-dir" );
			var expectedParent = Path.GetFullPath( Path.Combine( root, "Temp", "gltf-validation", "native-compiler" ) );
			runDirectory = Path.GetFullPath( runDirectory );
			if ( !IsInside( expectedParent, runDirectory ) || !Directory.Exists( runDirectory ) )
				throw new InvalidOperationException( "Run directory must already exist below Temp/gltf-validation/native-compiler." );
			var whitePath = Path.Combine( root, "game", "core", "materials", "default", "white.vmat" );
			var white = File.ReadAllText( whitePath );
			const string originalTint = "[1.000000 1.000000 1.000000 0.000000]";
			const string markerTint = "[0.125000 0.375000 0.625000 0.000000]";
			if ( !white.Contains( originalTint, StringComparison.Ordinal ) ) throw new InvalidOperationException( "Canonical white material no longer has the expected tint." );
			white = white.Replace( originalTint, markerTint, StringComparison.Ordinal );
			AssertNoLinks( expectedParent, runDirectory );
			probeAppSystem = new ProbeToolAppSystem();
			RegisterProbeCompiler();
			MountTemporaryAssets( runDirectory );

			var childDirectory = Path.Combine( runDirectory, Prefix );
			Directory.CreateDirectory( childDirectory );
			ProbeCompiler.ChildPath = Path.Combine( childDirectory, "child.vmat" );
			if ( File.Exists( ProbeCompiler.ChildPath ) ) throw new InvalidOperationException( "Negative control requires that the generated source material does not exist." );

			// This must reach the actual native-to-managed callback and fail deliberately.
			ProbeCompiler.Invocations = 0;
			var invalidRoot = Path.Combine( runDirectory, Prefix, "invalid.gltfprobe" );
			var invalidResult = AssetSystem.CompileResource( invalidRoot, "invalid parent input" );
			Require( ProbeCompiler.Invocations > 0, "Native compiler did not invoke the registered parent compiler for the negative control." );
			Require( ProbeCompiler.LastParentInput == "invalid parent input", "Native parent callback did not receive the supplied negative-control input." );
			Require( !invalidResult, "Invalid parent input unexpectedly compiled successfully." );

			// The child compiler must also reject invalid material data, with no source file.
			ProbeCompiler.Invocations = 0;
			ProbeCompiler.ChildSource = "this is not valid material source";
			ProbeCompiler.ChildPath = Path.Combine( childDirectory, "invalid-child.vmat" );
			ProbeCompiler.ChildLogicalPath = $"{Prefix}/invalid-child.vmat";
			var invalidChild = AssetSystem.CompileResource( Path.Combine( childDirectory, "invalid-child-parent.gltfprobe" ), ProbeCompiler.Magic );
			Require( ProbeCompiler.Invocations > 0, "Native compiler did not invoke the parent compiler for the invalid child." );
			Require( ProbeCompiler.LastParentInput == ProbeCompiler.Magic && ProbeCompiler.ChildReturned, "Invalid child control did not reach and return from the child compiler." );
			Require( !invalidChild && !ProbeCompiler.ChildCompileSucceeded, "Invalid in-memory child material unexpectedly compiled." );
			Require( !File.Exists( ProbeCompiler.ChildPath ), "Invalid control unexpectedly created a source .vmat file." );

			ProbeCompiler.ChildSource = white;
			ProbeCompiler.ChildPath = Path.Combine( childDirectory, "child.vmat" );
			ProbeCompiler.ChildLogicalPath = $"{Prefix}/child.vmat";
			ProbeCompiler.Invocations = 0;
			var parentPath = Path.Combine( runDirectory, Prefix, "parent.gltfprobe" );
			var parentResult = AssetSystem.CompileResource( parentPath, ProbeCompiler.Magic );
			Require( ProbeCompiler.Invocations > 0, "Native compiler did not invoke the registered parent compiler for the positive case." );
			Require( ProbeCompiler.LastParentInput == ProbeCompiler.Magic && ProbeCompiler.ChildReturned, "Positive case did not reach and return from the child compiler." );
			Require( parentResult, "Parent resource compile failed." );
			Require( ProbeCompiler.ChildCompileSucceeded, "Native in-memory child material compile failed." );
			Require( !File.Exists( ProbeCompiler.ChildPath ), "Unexpected source .vmat file was created." );
			var compiled = Directory.GetFiles( runDirectory, "*.vmat_c", SearchOption.AllDirectories );
			Console.WriteLine( $"Observed compiled child files: {string.Join( ", ", compiled )}" );
			Require( compiled.Length == 1, "Expected one observable compiled .vmat_c below the isolated run directory." );

			var material = Material.Load( ProbeCompiler.ChildLogicalPath );
			Require( material is { IsValid: true }, "Generated material could not be loaded through its logical path." );
			Require( material.ShaderName?.Contains( "complex.shader", StringComparison.OrdinalIgnoreCase ) == true, $"Generated material loaded an unexpected shader: {material.ShaderName}" );
			var tint = material.GetVector4( "g_vColorTint" );
			Require( Near( tint.x, 0.125f ) && Near( tint.y, 0.375f ) && Near( tint.z, 0.625f ), $"Loaded material did not contain the in-memory marker tint: {tint}" );
			Console.WriteLine( "PASS: in-memory material child compiled and loaded; raw GLB dependency packaging remains untested." );
			exitCode = 0;
			GC.KeepAlive( probeAppSystem );
		}
		catch ( Exception e )
		{
			Console.Error.WriteLine( "FAIL: " + e );
		}
		Console.Out.Flush();
		Console.Error.Flush();
		// The existing ShaderCompiler deliberately hard-exits before native tools teardown.
		Environment.Exit( exitCode );
		return exitCode;
	}

	private static void RegisterProbeCompiler()
	{
		// The editor owns this internal registry; use its existing methods without changing
		// engine visibility merely for this standalone experiment.
		var property = typeof( GlobalToolsNamespace ).GetProperty( "EditorTypeLibrary", BindingFlags.Static | BindingFlags.Public )!;
		var libraryType = property.PropertyType;
		var library = Activator.CreateInstance( libraryType, nonPublic: true )!;
		var flags = BindingFlags.Instance | BindingFlags.NonPublic;
		libraryType.GetMethod( "AddIntrinsicTypes", flags )!.Invoke( library, null );
		var addAssembly = libraryType.GetMethod( "AddAssembly", flags )!;
		addAssembly.Invoke( library, [typeof( Material ).Assembly, false] );
		addAssembly.Invoke( library, [typeof( AssetSystem ).Assembly, true] );
		addAssembly.Invoke( library, [typeof( ProbeCompiler ).Assembly, true] );
		property.SetValue( null, library );
	}

	private static void MountTemporaryAssets( string path )
	{
		// This is the existing editor mount path, which registers both the managed asset
		// filesystem and native search path. Any startup/mount failure is a failed probe.
		var type = typeof( Material ).Assembly.GetType( "Sandbox.EngineFileSystem", throwOnError: true )!;
		var method = type.GetMethod( "AddAssetPath", BindingFlags.Static | BindingFlags.NonPublic )!;
		method.Invoke( null, [Prefix, path] );
	}

	private static string Argument( string[] args, string name )
	{
		var index = Array.IndexOf( args, name );
		return index >= 0 && index + 1 < args.Length ? Path.GetFullPath( args[index + 1] ) : throw new ArgumentException( $"Missing {name}." );
	}

	private static bool IsInside( string parent, string path )
	{
		var relative = Path.GetRelativePath( parent, path );
		return relative != "." && relative != ".." && !relative.StartsWith( ".." + Path.DirectorySeparatorChar, StringComparison.Ordinal ) && !Path.IsPathRooted( relative );
	}

	private static void AssertNoLinks( string parent, string path )
	{
		var current = new DirectoryInfo( path );
		while ( current is not null && IsInside( parent, current.FullName ) )
		{
			if ( current.LinkTarget is not null || (current.Attributes & FileAttributes.ReparsePoint) != 0 )
				throw new InvalidOperationException( $"Probe directory contains a link: {current.FullName}" );
			current = current.Parent;
		}
		if ( current is null || current.LinkTarget is not null || (current.Attributes & FileAttributes.ReparsePoint) != 0 )
			throw new InvalidOperationException( "Probe output root is missing or is a link." );
	}

	private static bool Near( float actual, float expected ) => float.IsFinite( actual ) && MathF.Abs( actual - expected ) < 0.0001f;
	private static void Require( bool condition, string error ) { if ( !condition ) throw new InvalidOperationException( error ); }
}

/// <summary>Uses the same tool app-system startup as the shader compiler, then initializes editor compiler interop.</summary>
internal sealed class ProbeToolAppSystem : ToolAppSystem
{
	public override void Init()
	{
		base.Init();
		CreateEditor();
	}
}

[ResourceCompiler.ResourceIdentity( "gltfprobe" )]
public sealed class ProbeCompiler : ResourceCompiler
{
	internal const string Magic = "gltf-native-material-child-probe-v1";
	internal static int Invocations;
	internal static string ChildSource;
	internal static string ChildPath;
	internal static string ChildLogicalPath;
	internal static bool ChildCompileSucceeded;
	internal static bool ChildReturned;
	internal static string LastParentInput;

	protected override Task<bool> Compile()
	{
		Invocations++;
		ChildCompileSucceeded = false;
		ChildReturned = false;
		LastParentInput = Context.ReadSourceAsString();
		if ( LastParentInput != Magic ) return Task.FromResult( false );
		var child = Context.CreateChild( ChildPath );
		child.SetInputData( ChildSource );
		var success = child.Compile();
		ChildReturned = true;
		if ( !success ) return Task.FromResult( false );
		ChildCompileSucceeded = true;
		Context.AddRuntimeReference( ChildLogicalPath );
		Context.AddGameFileReference( ChildLogicalPath + "_c" );
		Context.Data.Write( Encoding.UTF8.GetBytes( ChildLogicalPath ) );
		return Task.FromResult( true );
	}
}
