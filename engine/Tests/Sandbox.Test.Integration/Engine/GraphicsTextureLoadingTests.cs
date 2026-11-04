namespace EngineTests;

[TestClass]
public class GraphicsTextureLoadingTests
{
	[TestMethod]
	public void HeadlessUnitTestsContinueToSkipTextureIo()
	{
		if ( Graphics.IsAvailable ) Assert.Inconclusive( "Requires the empty renderer." );
		Assert.IsNull( Texture.TryToLoad( null, "textures/dev/blue_noise_256.vtex", false ) );
	}

	[TestMethod]
	public void GraphicsUnitTestsCanLoadStockPipelineLookups()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires the graphics-enabled test host." );
		foreach ( var path in new[]
		{
			"textures/dev/blue_noise_256.vtex", "textures/dev/ggx_integrate_brdf_lut_schlick.vtex",
			"textures/dev/ltc_amp.vtex", "textures/dev/ltc_mat.vtex"
		} )
		{
			var texture = Texture.Load( path, false );
			Assert.IsNotNull( texture, $"The graphics test host skipped {path}." );
			Assert.IsTrue( texture.IsValid && !texture.IsError, $"The stock pipeline lookup failed to load: {path}." );
		}
	}
}
