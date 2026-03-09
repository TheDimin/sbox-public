using Sandbox;
using Sandbox.Diagnostics;
using Sims4.Dbpf;
using Sims4.Dbpf.Resources;
using Sims4.Dbpf.Structures;
using Sims4.Sbox;
using System;
using static Sandbox.Services.Inventory;

namespace Mounting.Sims4;

public class ModelLoader( DbpfPackage package, DbpfEntry description, int index ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-ModelLoader" );

	protected override object Load()
	{
		GeomResource geom = null;
		Model model = null;
		try
		{
			geom = package.ReadGeom( description );

			model = GeomModelBuilder.Build( geom );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load model {description.Key}:: INDEX:{index}" );

			//Dump all model info


			Log.Info( e );
			throw;
		}

		return model;
	}
}
