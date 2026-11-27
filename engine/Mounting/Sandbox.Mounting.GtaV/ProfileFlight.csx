var viewport = Editor.SceneViewWidget.Current?.LastSelectedViewportWidget;
if ( viewport?.Renderer.Camera.Scene.Source?.ResourcePath != "scenes/gta_world.scene" )
    throw new InvalidOperationException( "Select the gta_world scene viewport." );

const string ownerKey = "GtaV.ProfileFlight.Owner";
var domain = AppDomain.CurrentDomain;
if ( domain.GetData( ownerKey ) is not null )
    throw new InvalidOperationException( "A flight already controls this viewport." );

var state = viewport.State;
var originalPosition = state.CameraPosition;
var originalRotation = state.CameraRotation;
var expectedPosition = originalPosition;
var expectedRotation = originalRotation;

bool HasCamera() =>
    ReferenceEquals( Editor.SceneViewWidget.Current?.LastSelectedViewportWidget, viewport )
    && viewport.Renderer.Camera.IsValid()
    && state.CameraPosition.DistanceSquared( expectedPosition ) < 0.0001f
    && (state.CameraRotation.Forward.DistanceSquared( expectedRotation.Forward ) < 0.0000001f
        && state.CameraRotation.Up.DistanceSquared( expectedRotation.Up ) < 0.0000001f
        || !Editor.SceneEditorExtensions.IsControllingCamera && state.CameraRotation.Distance( expectedRotation ) < 0.5f);

var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var engine = typeof(Application).Assembly;
var settingsType = engine.GetType( "Sandbox.Engine.Settings.RenderSettings" );
var settings = settingsType.GetField( "Instance", flags ).GetValue( null );
var cap = settingsType.GetProperty( "MaxFrameRateInactive" );
var originalCap = cap.GetValue( settings );
var history = engine.GetType( "Sandbox.Diagnostics.DevTools" ).GetProperty( "Frames", flags ).GetValue( null );
var frameMs = history.GetType().GetMethod( "FrameMs" ).CreateDelegate<Func<int,float>>( history );
var gpuMs = history.GetType().GetMethod( "GpuMs" ).CreateDelegate<Func<int,float>>( history );
var gcMs = history.GetType().GetMethod( "GcMs" ).CreateDelegate<Func<int,float>>( history );
var gpuTotals = new double[3];
var gcTotals = new double[3];
var phases = new System.Collections.Generic.List<float>[] { new(), new(), new() };
var landmarks = new System.Collections.Generic.Dictionary<string,double>();
var destination = new Vector3( 8307.117f, -50581.848f, 2074.048f );
var height = lowFlight ? 1600 : 16000;
var start = destination + new Vector3( -80000, 0, height );
var descent = destination + new Vector3( -10000, 0, height );
var samples = new System.Collections.Generic.Dictionary<(string Region, int Lod, string Stage), System.Collections.Generic.List<double>>();
var mount = Editor.EditorUtility.Mounting.Get( "gtav" );
var mountFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var textureRequests = mount?.GetType().GetProperty( "Textures", mountFlags )?.GetValue( mount );
object TextureTotals() => textureRequests?.GetType().GetProperty( "Statistics", mountFlags )?.GetValue( textureRequests );
var textureStart = TextureTotals();
var memberReads = new System.Collections.Concurrent.ConcurrentDictionary<(uint File, uint Member), long>();
using var memberListener = new System.Diagnostics.Metrics.MeterListener();
memberListener.InstrumentPublished = (instrument, observer) =>
{
    if ( instrument.Meter.Name == "GtaV.TextureRequests" && instrument.Name == "gtav.texture.member.reads" )
        observer.EnableMeasurementEvents( instrument );
};
memberListener.SetMeasurementEventCallback<long>( (instrument, value, tags, _) =>
{
    uint file = 0, member = 0;
    foreach ( var tag in tags )
    {
        if ( tag.Key == "file" ) file = (uint)tag.Value;
        if ( tag.Key == "member" ) member = (uint)tag.Value;
    }
    memberReads.AddOrUpdate( (file, member), value, (_, previous) => previous + value );
} );
memberListener.Start();
var observation = System.Diagnostics.Stopwatch.StartNew();
var assetEvents = new System.Collections.Generic.List<object>();
using var assetListener = new System.Diagnostics.Metrics.MeterListener();
assetListener.InstrumentPublished = (instrument, observer) =>
{
    if ( instrument.Meter.Name == "GtaV.AssetStreaming" ) observer.EnableMeasurementEvents( instrument );
};
assetListener.SetMeasurementEventCallback<double>( (instrument, value, tags, _) =>
{
    string path = null, kind = null;
    long frame = 0;
    double wait = 0;
    foreach ( var tag in tags )
    {
        if ( tag.Key == "path" ) path = (string)tag.Value;
        if ( tag.Key == "kind" ) kind = (string)tag.Value;
        if ( tag.Key == "frame" ) frame = (long)tag.Value;
        if ( tag.Key == "waitMs" ) wait = (double)tag.Value;
    }
    assetEvents.Add( new { op = instrument.Name, frame, path, kind, ms = value, wait, sec = observation.Elapsed.TotalSeconds - 15 } );
} );
assetListener.Start();
using var listener = new System.Diagnostics.Metrics.MeterListener();
listener.InstrumentPublished = (instrument, observer) =>
{
    if ( instrument.Meter.Name == "GtaV.WorldStreaming" && instrument is System.Diagnostics.Metrics.Histogram<double> )
        observer.EnableMeasurementEvents( instrument );
};
listener.SetMeasurementEventCallback<double>( (instrument, value, tags, _) =>
{
    string region = "";
    string stage = instrument.Name;
    int lod = -1;
    foreach ( var tag in tags )
    {
        if ( tag.Key == "ymap" ) region = (string)tag.Value;
        if ( tag.Key == "lod" ) lod = (int)tag.Value;
        if ( tag.Key == "exit" ) stage = (string)tag.Value;
    }
    // Another capture can have observed the start of an interval. Count complete
    // selections during this flight separately from those already in progress.
    if ( instrument.Name == "gtav.render.dwell" && value > observation.Elapsed.TotalMilliseconds ) stage += "-from-before-capture";
    var key = (region, lod, stage);
    if ( !samples.TryGetValue( key, out var values ) ) samples[key] = values = new();
    values.Add( value );
} );
listener.Start();
var clock = System.Diagnostics.Stopwatch.StartNew();
domain.SetData( ownerKey, true );
try
{
    cap.SetValue( settings, 1000 );
    state.CameraPosition = expectedPosition = start;
    state.CameraRotation = expectedRotation = Rotation.LookAt( new Vector3( 1, 0, -0.24f ) );
    long lastFrame = (long)Application.FrameCount;
    double nextAudit = 0;
    object interruption = null;
    while ( true )
    {
        await GameTask.DelayRealtimeSeconds( 0.025f );
        cancellationToken.ThrowIfCancellationRequested();
        if ( !HasCamera() )
        {
            interruption = new { elapsedSeconds = clock.Elapsed.TotalSeconds, sameViewport = ReferenceEquals( Editor.SceneViewWidget.Current?.LastSelectedViewportWidget, viewport ), cameraValid = viewport.Renderer.Camera.IsValid(), expected = expectedPosition.ToString(), actual = state.CameraPosition.ToString(), rotationDegrees = expectedRotation.Distance( state.CameraRotation ) };
            break;
        }
        double t = clock.Elapsed.TotalSeconds - 15;
        long frame = (long)Application.FrameCount;
        if ( t >= 0 )
        {
            var phase = t < 10 ? 0 : t < 16 ? 1 : 2;
            for ( int i = 0; i < Math.Min( 512, frame - lastFrame ); i++ )
            {
                phases[phase].Add( frameMs( i ) );
                gpuTotals[phase] += gpuMs( i );
                gcTotals[phase] += gcMs( i );
            }
            if ( t >= 55 ) break;
            if ( t < 16 || expectedPosition != destination )
            {
                expectedPosition = t < 10 ? Vector3.Lerp( start, descent, (float)t / 10 )
                    : Vector3.Lerp( descent, destination, Math.Clamp( (float)(t - 10) / 6, 0, 1 ) );
                state.CameraPosition = expectedPosition;
            }
            if ( t >= nextAudit && landmarks.Count < 2 )
            {
                nextAudit = t + 0.5;
                foreach ( var renderer in viewport.Renderer.Camera.Scene.GetAllComponents<ModelRenderer>() )
                {
                    if ( !renderer.Active || renderer.Model is not { IsError: false, Name: { } name } ) continue;
                    if ( name.EndsWith( "/sc1_rd_r2_06.vmdl", StringComparison.OrdinalIgnoreCase ) ) landmarks.TryAdd( "sc1_rd_r2_06", t );
                    if ( name.EndsWith( "/sc1_04_bld_02.vmdl", StringComparison.OrdinalIgnoreCase ) ) landmarks.TryAdd( "sc1_04_bld_02", t );
                }
            }
        }
        lastFrame = frame;
    }

    object Stats( int phase )
    {
        var frames = phases[phase];
        if ( frames.Count == 0 ) return null;
        frames.Sort();
        return new { frames = frames.Count, fps = 1000 / frames.Average(), p99Ms = frames[(int)(frames.Count * 0.99)],
            gpuMeanMs = gpuTotals[phase] / frames.Count, gcTotalMs = gcTotals[phase], withinBudget = frames.Count( ms => ms <= 1000.0 / 120 ), maxMs = frames[^1], over33Ms = frames.Count( ms => ms > 33.33 ) };
    }
    var lodTimes = samples.Select( pair => {
        pair.Value.Sort();
        return new { pair.Key.Region, pair.Key.Lod, pair.Key.Stage, count = pair.Value.Count, totalMs = pair.Value.Sum(),
            medianMs = pair.Value[pair.Value.Count / 2], maxMs = pair.Value[^1] };
    } ).ToArray();
    return new { cancelled = interruption is not null, interruption, textureReads = new { start = textureStart, end = TextureTotals(), unique = memberReads.Count, repeats = memberReads.Values.Sum() - memberReads.Count }, assetEvents, lodTimes, landmarks, flight = Stats( 0 ), descent = Stats( 1 ), settle = Stats( 2 ) };
}
finally
{
    domain.SetData( ownerKey, null );
    cap.SetValue( settings, originalCap );
    if ( HasCamera() )
    {
        state.CameraPosition = originalPosition;
        state.CameraRotation = originalRotation;
    }
}
