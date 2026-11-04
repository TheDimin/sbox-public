using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sandbox.Gltf;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>Runs real managed data readers; results are separate from renderer/reference proofs.</summary>
internal sealed class TestGltfCorpus
{
	private static readonly HashSet<string> ReaderCapabilities = new( StringComparer.Ordinal )
	{
		"KHR_texture_transform", "KHR_materials_unlit", "KHR_materials_variants",
		"KHR_mesh_quantization", "KHR_lights_punctual", "EXT_mesh_gpu_instancing",
		"EXT_meshopt_compression", "KHR_meshopt_compression",
		"KHR_draco_mesh_compression",
		// Source selection and encoded bytes only. This adapter does not transcode or render images.
		"KHR_texture_basisu"
	};

	private sealed record Stage( string Status, string Error = null, Dictionary<string, long> Operations = null );
	private sealed record Case( string Id, string Path, string Scope, string SourceSha256, string[] EncodedSourceOnlyExtensions, Stage DataRead, Stage ManagedContainerRoundtrip );
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

	internal ExitCode Run( string corpusPath, string outputPath, string prefix, bool compileGlb )
	{
		try
		{
			var corpus = Path.TrimEndingDirectorySeparator( Path.GetFullPath( corpusPath ) );
			RequireNoLinks( corpus );
			var reportPath = Path.GetFullPath( outputPath );
			if ( IsWithin( corpus, reportPath ) ) throw new InvalidDataException( "Report must be outside the sample checkout." );
			RequireNoLinks( reportPath );
			var inventoryBytes = FreshInventory( corpus );
			using var inventory = JsonDocument.Parse( inventoryBytes );
			var expectedCommit = inventory.RootElement.GetProperty( "corpus_commit" ).GetString();
			var actualCommit = GitHead( corpus );
			if ( string.IsNullOrEmpty( expectedCommit ) || expectedCommit != actualCommit ) throw new InvalidDataException( "Inventory and checked-out corpus revisions differ." );
			var inventoryCases = inventory.RootElement.GetProperty( "cases" );
			var totalCases = inventoryCases.GetArrayLength();
			var totalRequired = inventoryCases.EnumerateArray().Count( x => x.GetProperty( "scope" ).GetString() == "required" );
			var inventoryErrors = inventoryCases.EnumerateArray().Count( x => x.GetProperty( "scope" ).GetString() == "error" );
			var results = new List<Case>();
			var ids = new HashSet<string>( StringComparer.Ordinal );
			foreach ( var item in inventoryCases.EnumerateArray() )
			{
				var id = item.GetProperty( "id" ).GetString();
				var idComponents = ValidCaseId( id );
				if ( !ids.Add( id ) ) throw new InvalidDataException( $"Duplicate inventory case '{id}'." );
				if ( prefix is not null && !id.StartsWith( prefix, StringComparison.Ordinal ) ) continue;
				var relative = item.GetProperty( "path" ).GetString();
				var scope = item.GetProperty( "scope" ).GetString();
				var read = new Stage( "not-run" );
				var compiled = new Stage( "not-requested" );
				string hash = null;
				string[] sourceOnly = [];
				try
				{
					if ( scope == "error" ) throw new InvalidDataException( item.GetProperty( "error" ).GetString() );
					if ( scope is not ("required" or "excluded") ) throw new InvalidDataException( "Unknown inventory scope." );
					var extensionsUsed = item.GetProperty( "extensions_used" ).EnumerateArray().Select( x => x.GetString() ).ToArray();
					sourceOnly = extensionsUsed.Where( x => x == "KHR_texture_basisu" ).ToArray();
					var asset = CorpusFile( corpus, relative );
					var bytes = File.ReadAllBytes( asset );
					hash = Convert.ToHexStringLower( SHA256.HashData( bytes ) );
					byte[] Resolve( string uri ) => File.ReadAllBytes( CorpusFile( corpus,
						Path.GetRelativePath( corpus, Path.Combine( Path.GetDirectoryName( asset ), Uri.UnescapeDataString( uri ) ) ) ) );
					using ( var document = GltfReader.Read( bytes, Resolve, ReaderCapabilities, maxDecodedBufferBytes: 1024L * 1024 * 1024 ) )
					{
						var operations = Inspect( document );
						var fallback = document.UnsupportedOptionalExtensions;
						read = fallback.Count == 0 ? new Stage( "pass", Operations: operations ) :
							new Stage( "fallback", "Unsupported optional extensions: " + string.Join( ", ", fallback ), operations );
					}
					if ( compileGlb )
					{
						try
						{
							var output = GltfCompiler.Compile( bytes, Resolve, ReaderCapabilities );
							using var roundTrip = GltfReader.Read( output, Resolve, ReaderCapabilities );
							compiled = new Stage( "pass", Operations: Inspect( roundTrip ) );
							var artifactRoot = Path.GetFullPath( Path.Combine( Path.GetDirectoryName( reportPath ), "compiled" ) );
							var artifact = Path.GetFullPath( Path.Combine( artifactRoot, idComponents[0], idComponents[1], "model.glb_c" ) );
							if ( !IsWithin( artifactRoot, artifact ) || IsWithin( corpus, artifact ) )
								throw new InvalidDataException( "Compiled artifact path escapes its output directory." );
							RequireNoLinks( artifact );
							Directory.CreateDirectory( Path.GetDirectoryName( artifact ) );
							File.WriteAllBytes( artifact, output );
						}
						catch ( Exception e ) when ( e is not OutOfMemoryException ) { compiled = new Stage( "fail", e.GetType().Name + ": " + e.Message ); }
					}
				}
				catch ( Exception e ) when ( e is not OutOfMemoryException ) { read = new Stage( "fail", e.GetType().Name + ": " + e.Message ); }
				results.Add( new Case( id, relative, scope, hash, sourceOnly, read, compiled ) );
				if ( scope == "required" && (read.Status != "pass" || compileGlb && compiled.Status != "pass") )
					Log.Error( $"{id}: {read.Error ?? compiled.Error}" );
			}
			var required = results.Where( x => x.Scope == "required" ).ToArray();
			var pass = required.Length > 0 && results.All( x => x.Scope != "error" ) &&
				required.All( x => x.DataRead.Status == "pass" && (!compileGlb || x.ManagedContainerRoundtrip.Status == "pass") );
			Directory.CreateDirectory( Path.GetDirectoryName( reportPath ) );
			var inventoryPath = reportPath + ".inventory.json";
			RequireNoLinks( inventoryPath );
			File.WriteAllBytes( inventoryPath, inventoryBytes );
			File.WriteAllText( reportPath, JsonSerializer.Serialize( new
			{
				Adapter = "sbox-managed-gltf-data-readers",
				SourceSha256 = ImplementationHash(),
				LoadedReaderAssemblyFile = typeof( GltfReader ).Assembly.Location,
				LoadedReaderAssemblyFileSha256 = AssemblyHash( typeof( GltfReader ).Assembly.Location ),
				LoadedAdapterAssemblyFile = typeof( TestGltfCorpus ).Assembly.Location,
				LoadedAdapterAssemblyFileSha256 = AssemblyHash( typeof( TestGltfCorpus ).Assembly.Location ),
				AssemblyHashQualification = "on-disk assembly paths at report time; not an in-memory attestation",
				NativeMeshoptimizerBinary = "not-attested",
				InventorySha256 = Convert.ToHexStringLower( SHA256.HashData( inventoryBytes ) ),
				InventoryPath = inventoryPath,
				CorpusCommit = actualCommit,
				Platform = RuntimeInformation.RuntimeIdentifier,
				Selection = prefix,
				TotalIndexedCases = totalCases,
				TotalRequiredCases = totalRequired,
				InventoryErrorCases = inventoryErrors,
				SelectedCases = results.Count,
				SelectedRequiredCases = required.Length,
				SelectedDataReadersPass = pass,
				AllRequiredDataReadersPass = prefix is null && pass && inventoryErrors == 0,
				BasisuTranscode = "not-tested",
				CompiledStageMeaning = "managed-container-roundtrip-against-source-resolver",
				IndependentCompiledValidation = "not-tested", PackagedDependencies = "not-tested",
				ImageDecoding = "not-tested", NumericReferenceParity = "not-tested", RenderParity = "not-tested",
				GpuLayoutOptimization = "not-tested", Performance = "not-tested", FullGoalPass = false,
				ReaderCapabilities = ReaderCapabilities.Order( StringComparer.Ordinal ),
				Cases = results
			}, JsonOptions ) );
			Log.Info( $"Read {required.Count( x => x.DataRead.Status == "pass" )}/{required.Length} required cases; {results.Count( x => x.Scope == "excluded" )} excluded-scope cases also inspected. Report: {outputPath}" );
			return pass ? ExitCode.Success : ExitCode.Failure;
		}
		catch ( Exception e ) when ( e is not OutOfMemoryException )
		{
			Log.Error( "glTF corpus setup failed: " + e.Message );
			return ExitCode.Failure;
		}
	}

	private static Dictionary<string, long> Inspect( GltfDocument document )
	{
		var root = document.Root;
		var operations = new Dictionary<string, long>();
		int Count( string name ) => root.TryGetProperty( name, out var array ) ? array.GetArrayLength() : 0;
		void Add( string name, long count = 1 ) => operations[name] = operations.GetValueOrDefault( name ) + count;
		_ = document.GetDefaultScene();
		for ( var i = 0; i < Count( "accessors" ); i++ ) { _ = document.ReadFloatAccessor( i ); Add( "accessors" ); }
		for ( var i = 0; i < Count( "samplers" ); i++ ) { _ = document.ReadSampler( i ); Add( "samplers" ); }
		for ( var i = 0; i < Count( "images" ); i++ ) { _ = document.ReadImage( i ); Add( "encoded_images" ); }
		for ( var i = 0; i < Count( "textures" ); i++ ) { _ = document.ReadTexture( i ); Add( "texture_sources" ); }
		for ( var i = 0; i < Count( "materials" ); i++ ) { _ = document.ReadMaterial( i ); Add( "materials" ); }
		var variantCount = root.TryGetProperty( "extensions", out var extensions ) && extensions.TryGetProperty( "KHR_materials_variants", out var variants )
			? variants.GetProperty( "variants" ).GetArrayLength() : 0;
		for ( var mesh = 0; mesh < Count( "meshes" ); mesh++ )
		{
			for ( var primitive = 0; primitive < root.GetProperty( "meshes" )[mesh].GetProperty( "primitives" ).GetArrayLength(); primitive++ )
			{
				_ = document.ReadPrimitive( mesh, primitive );
				_ = document.ReadMorphTargets( mesh, primitive );
				_ = document.GetMaterialForVariant( mesh, primitive, null );
				for ( var variant = 0; variant < variantCount; variant++ ) _ = document.GetMaterialForVariant( mesh, primitive, variant );
				Add( "primitives" );
			}
		}
		_ = document.EvaluatePose();
		for ( var node = 0; node < Count( "nodes" ); node++ )
		{
			_ = document.ReadInstances( node );
			_ = document.GetMorphWeights( node );
			Add( "nodes" );
		}
		for ( var skin = 0; skin < Count( "skins" ); skin++ ) { _ = document.ReadSkin( skin ); Add( "skins" ); }
		for ( var camera = 0; camera < Count( "cameras" ); camera++ ) { _ = document.ReadCamera( camera ); Add( "cameras" ); }
		if ( root.TryGetProperty( "extensions", out extensions ) && extensions.TryGetProperty( "KHR_lights_punctual", out var lights ) )
			for ( var i = 0; i < lights.GetProperty( "lights" ).GetArrayLength(); i++ ) { _ = document.ReadLight( i ); Add( "lights" ); }
		for ( var animation = 0; animation < Count( "animations" ); animation++ )
		{
			foreach ( var time in new[] { 0f, 0.125f, 1f, 10f } ) { _ = document.EvaluatePose( animation, time ); Add( "pose_samples" ); }
		}
		return operations;
	}

	private static string CorpusFile( string corpus, string relative )
	{
		var path = Path.GetFullPath( Path.Combine( corpus, relative ) );
		if ( !IsWithin( corpus, path ) || !File.Exists( path ) )
			throw new InvalidDataException( "Missing or out-of-corpus resource: " + relative );
		RequireNoLinks( path );
		return path;
	}

	private static bool IsWithin( string directory, string path )
	{
		directory = Path.TrimEndingDirectorySeparator( Path.GetFullPath( directory ) );
		path = Path.GetFullPath( path );
		var prefix = Path.EndsInDirectorySeparator( directory ) ? directory : directory + Path.DirectorySeparatorChar;
		return path == directory || path.StartsWith( prefix, StringComparison.Ordinal );
	}

	private static void RequireNoLinks( string path )
	{
		path = Path.GetFullPath( path );
		var root = Path.GetPathRoot( path );
		var current = root;
		foreach ( var component in path[root.Length..].Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries ) )
		{
			current = Path.Combine( current, component );
			try
			{
				if ( (File.GetAttributes( current ) & FileAttributes.ReparsePoint) != 0 )
					throw new InvalidDataException( "Symbolic links are not allowed in corpus or output paths: " + current );
			}
			catch ( FileNotFoundException ) { }
			catch ( DirectoryNotFoundException ) { }
		}
	}

	private static string[] ValidCaseId( string id )
	{
		var parts = id?.Split( '/' );
		if ( parts is null || parts.Length != 2 || parts.Any( part => string.IsNullOrEmpty( part ) || part is "." or ".." ||
			part.Contains( '\\' ) || part.IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0 ) )
			throw new InvalidDataException( "Invalid inventory case identifier: " + id );
		return parts;
	}

	private static byte[] FreshInventory( string corpus )
	{
		var script = Path.GetFullPath( "research/gltf-validation/validate.py" );
		var info = new ProcessStartInfo( "python3" )
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = System.Text.Encoding.UTF8,
			StandardErrorEncoding = System.Text.Encoding.UTF8
		};
		foreach ( var argument in new[] { "-B", script, "--corpus", corpus } ) info.ArgumentList.Add( argument );
		using var process = Process.Start( info ) ?? throw new InvalidDataException( "Could not start the inventory tool." );
		var output = process.StandardOutput.ReadToEndAsync();
		var error = process.StandardError.ReadToEndAsync();
		process.WaitForExit();
		var stdout = output.GetAwaiter().GetResult();
		var stderr = error.GetAwaiter().GetResult();
		if ( process.ExitCode is not (0 or 1) )
			throw new InvalidDataException( $"Fresh corpus inventory failed (exit {process.ExitCode}): {stderr} {stdout}" );
		return System.Text.Encoding.UTF8.GetBytes( stdout );
	}

	private static string AssemblyHash( string assemblyPath )
	{
		if ( string.IsNullOrWhiteSpace( assemblyPath ) || !File.Exists( assemblyPath ) )
			throw new InvalidDataException( "Cannot establish loaded assembly provenance." );
		using var stream = File.OpenRead( assemblyPath );
		return Convert.ToHexStringLower( SHA256.HashData( stream ) );
	}

	private static string GitHead( string corpus )
	{
		var info = new ProcessStartInfo( "git" ) { RedirectStandardOutput = true, RedirectStandardError = true };
		foreach ( var argument in new[] { "-C", corpus, "rev-parse", "HEAD" } ) info.ArgumentList.Add( argument );
		using var process = Process.Start( info );
		var output = process.StandardOutput.ReadToEnd().Trim();
		process.WaitForExit();
		if ( process.ExitCode != 0 ) throw new InvalidDataException( "Cannot read corpus revision." );
		return output;
	}

	private static string ImplementationHash()
	{
		using var hash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		foreach ( var file in Directory.EnumerateFiles( "engine/Sandbox.Gltf", "*", SearchOption.TopDirectoryOnly ).Where( x => x.EndsWith( ".cs" ) || x.EndsWith( ".csproj" ) ).Order( StringComparer.Ordinal ) )
		{
			hash.AppendData( System.Text.Encoding.UTF8.GetBytes( Path.GetFileName( file ) + "\0" ) );
			hash.AppendData( File.ReadAllBytes( file ) );
		}
		return Convert.ToHexStringLower( hash.GetHashAndReset() );
	}
}
