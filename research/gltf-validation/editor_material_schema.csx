// Read the loaded stock shader's editor schema. Metadata is discovery evidence,
// not proof of glTF material equations or shader feature compatibility.
await MainThread.Wait();
if ( !Application.IsEditor ) throw new InvalidOperationException( "Run this discovery inside the s&box editor." );
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Could not locate the checkout from loaded engine: " + engineLocation );
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-material-schema" );
System.IO.Directory.CreateDirectory( output );
var stock = Material.Load( "materials/default/white.vmat" );
if ( stock is null || !stock.IsValid || stock.IsError ) throw new InvalidOperationException( "Stock white material unavailable." );
var shader = stock.Shader;
if ( shader is null || !shader.IsValid ) throw new InvalidOperationException( "Stock shader unavailable." );
var schema = shader.Schema;
if ( schema?.Variables is null || schema.Variables.Count == 0 || schema.Combos is null ) throw new InvalidOperationException( "Stock shader has no discoverable schema." );
var variables = schema.Variables.OrderBy( x => x.Name, StringComparer.Ordinal ).Select( x => new
{
	x.Name, x.Type, x.AttributeName, x.SourceType, x.UiType, x.UiGroup, x.UiStep,
	minimum = x.FloatMin.ToString(), maximum = x.FloatMax.ToString(), float_default = x.FloatDefault.ToString(), int_default = x.IntDefault.ToString(), x.DefaultInputTexture, x.TextureEnding
} ).ToArray();
var combos = schema.Combos.OrderBy( x => x.Name, StringComparer.Ordinal ).ToArray();
var receipt = new
{
	is_editor = Application.IsEditor, stock_material = stock.ResourcePath, shader_name = stock.ShaderName,
	scope = "loaded stock shader schema only; parameter names and defaults do not prove pixel equations, glTF mapping, UVs, sampling or color space",
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	frame = Application.FrameCount, variables, combos
};
var path = System.IO.Path.Combine( output, "receipt.json" );
System.IO.File.WriteAllText( path, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return new { receipt.is_editor, receipt.stock_material, receipt.shader_name, variable_count = variables.Length, combo_count = combos.Length, path };
