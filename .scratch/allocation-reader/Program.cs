using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

var converted = TraceLog.CreateFromEventPipeDataFile( args[0] );
using var log = new TraceLog( converted );
var stacks = new Dictionary<string, int>();
foreach ( var e in log.Events.OfType<GCAllocationTickTraceData>() )
{
	var frames = new List<string>();
	for ( var stack = e.CallStack(); stack is not null; stack = stack.Caller )
		frames.Add( stack.CodeAddress.FullMethodName );
	if ( !frames.Any( x => x.Contains( "RenderToTexture" ) || x.Contains( "RenderSystem" ) ) )
		continue;

	var key = $"{e.TypeName}\n{string.Join( "\n", frames.Take( 24 ) )}";
	stacks[key] = stacks.GetValueOrDefault( key ) + 1;
}

foreach ( var (key, count) in stacks.OrderByDescending( x => x.Value ).Take( 32 ) )
	Console.WriteLine( $"{count} samples: {key}\n" );
