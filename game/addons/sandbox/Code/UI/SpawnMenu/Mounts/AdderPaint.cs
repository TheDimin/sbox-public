namespace Sandbox;

internal static class AdderPaint
{
	internal const string EntityPath = "vehicles/adder/adder.sent";
	internal const string BodyPath = "mount://gtav/models/fragments/adder/body.vmdl";

	internal static List<UIDropdown.Item> GetOptions( Model model, bool showPaint = true )
	{
		var options = new List<UIDropdown.Item>();
		if ( !model.IsValid() || model.IsError ) return options;

		for ( int i = 0; i < model.MaterialGroupCount; i++ )
		{
			var name = model.GetMaterialGroupName( i );
			int separator = showPaint ? name.LastIndexOf( '/' ) : -1;
			var label = separator >= 0 ? name[(separator + 1)..].Trim() : name;
			options.Add( new UIDropdown.Item( label, name )
			{
				Category = separator >= 0 ? name[..separator].Trim() : null,
				Swatch = showPaint ? PaintSwatch( model, i ) : null
			} );
		}

		return options;
	}

	static Color? PaintSwatch( Model model, int group )
	{
		var paint = model.GetMaterials( group ).FirstOrDefault( x =>
			x?.Name?.StartsWith( "gtav/paint/", StringComparison.OrdinalIgnoreCase ) == true );
		if ( paint is null ) return null;

		var linear = paint.GetColor( "g_vColorTint" );
		static float Srgb( float channel ) => channel <= 0.0031308f
			? 12.92f * channel
			: 1.055f * MathF.Pow( channel, 1f / 2.4f ) - 0.055f;

		return new Color( Srgb( linear.r ), Srgb( linear.g ), Srgb( linear.b ) );
	}

}
