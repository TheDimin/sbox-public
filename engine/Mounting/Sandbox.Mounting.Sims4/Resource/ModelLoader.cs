using Sandbox.Mounting.Sims4;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mounting.Sims4;

public class ModelLoader( DbpfRecord record ) : ResourceLoader<SimsMount>
{
	protected override object Load()
	{
		var id = record.InstanceId;
		return base.Load();
	}
}
