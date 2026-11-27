using Sandbox.ShaderCompiler;

var compiler = new SlangCompiler( args[0], [args[1]] );
var result = await compiler.CompileOfflineAsync( args[2], args[3], Console.WriteLine );
Console.WriteLine( $"Result: {result}" );
return result == OfflineResult.Compiled || result == OfflineResult.UpToDate ? 0 : 1;
