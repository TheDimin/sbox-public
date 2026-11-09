using System;
using System.Collections.Generic;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

internal static class IntegrationRegressionEntry
{
	public sealed record Result( string Name, bool Passed, string Error );
	public static List<Result> Run()
	{
		try { new EngineTests.GltfCompiledTextureTests().CompilesLinearPng16AndMetallicRoughnessWithoutQuantizingSource(); return [new( "permanent_engine_integration_png16_regression", true, null )]; }
		catch ( Exception e ) { return [new( "permanent_engine_integration_png16_regression", false, e.ToString() )]; }
	}
}
