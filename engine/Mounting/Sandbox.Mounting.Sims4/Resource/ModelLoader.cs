using Sandbox.Mounting.Sims4;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mounting.Sims4;

public class ModelLoader( DbpfPackage instance, DbpfRecord record ) : ResourceLoader<SimsMount>
{
	protected override object Load()
	{
		var dataRecord = instance.ReadData( record );

		// Debug: Print first 50 bytes as hex
		var hexString = Convert.ToHexString( dataRecord);
		Log.Info( $"Data Record : {hexString}" );

		var ModelRecord = new GEOMResource( dataRecord );

		Log.Info( $"Found {ModelRecord.VertexElements.Length} vertexElements" );

		var id = record.InstanceId;
		//record.
		return base.Load();
	}
}
