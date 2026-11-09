// Read the loaded stock shader's editor schema. Metadata is discovery evidence,
// not proof of glTF material equations or shader feature compatibility.
await MainThread.Wait();
if ( !Application.IsEditor ) throw new InvalidOperationException( "Run this discovery inside the s&box editor." );
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Could not locate the checkout from loaded engine: " + engineLocation );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-material-schema", runId );
System.IO.Directory.CreateDirectory( output );
var stock = Material.Load( "materials/default/white.vmat" );
if ( stock is null || !stock.IsValid || stock.IsError ) throw new InvalidOperationException( "Stock white material unavailable." );
var shader = stock.Shader;
if ( shader is null || !shader.IsValid ) throw new InvalidOperationException( "Stock shader unavailable." );
var schema = shader.Schema;
if ( schema?.Variables is null || schema.Variables.Count == 0 || schema.Combos is null ) throw new InvalidOperationException( "Stock shader has no discoverable schema." );
// ShaderSchema models only Variables and Combos. Preserve the underlying JSON
// so extra metadata is not silently dropped by that typed projection. This
// does not create a sampler, select a shader permutation or render anything.
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var nativeField = typeof( Shader ).GetField( "native", flags ) ?? throw new InvalidOperationException( "Shader handle field is unavailable." );
var native = nativeField.GetValue( shader );
var getProperties = native.GetType().GetMethod( "GetPropertiesJson", flags ) ?? throw new InvalidOperationException( "Raw shader properties API is unavailable." );
var raw = (string)getProperties.Invoke( native, null );
if ( string.IsNullOrEmpty( raw ) || System.Text.Encoding.UTF8.GetByteCount( raw ) > 2 * 1024 * 1024 ) throw new InvalidOperationException( "Raw shader properties are missing or exceed the bounded metadata limit." );
using var rawDocument = System.Text.Json.JsonDocument.Parse( raw );
if ( rawDocument.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ) throw new InvalidOperationException( "Raw shader properties must be an object." );
var rawFields = rawDocument.RootElement.EnumerateObject().Select( property => property.Name ).OrderBy( name => name, StringComparer.Ordinal ).ToArray();
var additionalFields = rawFields.Where( name => !string.Equals( name, "Variables", StringComparison.OrdinalIgnoreCase ) && !string.Equals( name, "Combos", StringComparison.OrdinalIgnoreCase ) ).ToArray();
var rawPath = System.IO.Path.Combine( output, "raw-properties.json" );
var rawBytes = System.Text.Encoding.UTF8.GetBytes( raw );
System.IO.File.WriteAllBytes( rawPath, rawBytes );
var mountedFiles = new System.Collections.Generic.List<object>();
var mounted = Sandbox.FileSystem.Mounted;
foreach ( var filename in new[] { shader.ResourcePath, shader.ResourcePath + "_c" } )
{
	var exists = mounted is not null && mounted.FileExists( filename );
	mountedFiles.Add( new { filename, exists, physical_path = exists ? mounted.GetFullPath( filename ) : null } );
}
var variables = schema.Variables.OrderBy( x => x.Name, StringComparer.Ordinal ).Select( x => new
{
	x.Name, x.Type, x.AttributeName, x.SourceType, x.UiType, x.UiGroup, x.UiStep,
	minimum = x.FloatMin.ToString(), maximum = x.FloatMax.ToString(), float_default = x.FloatDefault.ToString(), int_default = x.IntDefault.ToString(), x.DefaultInputTexture, x.TextureEnding
} ).ToArray();
var combos = schema.Combos.OrderBy( x => x.Name, StringComparer.Ordinal ).ToArray();
var receipt = new
{
	is_editor = Application.IsEditor, stock_material = stock.ResourcePath, shader_name = stock.ShaderName,
	scope = "stock shader schema, raw properties metadata and mounted filename discovery after loading the stock material; no sampler creation, shader permutation selection or rendering; no proof of pixel equations, glTF mapping, UVs, sampling or color space",
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	frame = Application.FrameCount, variables, combos, mounted_files = mountedFiles,
	raw_properties_path = rawPath, raw_properties_sha256 = Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( rawBytes ) ),
	raw_top_level_fields = rawFields, raw_fields_outside_typed_schema = additionalFields,
	independent_texture_sampler_support = "unproven", output
};
var path = System.IO.Path.Combine( output, "receipt.json" );
System.IO.File.WriteAllText( path, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return new { receipt.is_editor, receipt.stock_material, receipt.shader_name, variable_count = variables.Length, combo_count = combos.Length, raw_top_level_fields = rawFields, raw_fields_outside_typed_schema = additionalFields, path };
