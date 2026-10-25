using Sandbox.Utility;

namespace Sandbox;

internal static partial class Api
{
	internal static partial class Events
	{
		internal class EventRecord
		{
			public EventRecord( string name )
			{
				Name = name;
				Created = DateTime.UtcNow;
			}

			public string Name { get; set; }
			public DateTimeOffset Created { get; set; }
			public string Version { get; set; }
			public string Mode { get; set; }
			public Dictionary<string, object> Data { get; set; } = new();
			readonly HashSet<string> durationKeys = new();
			internal IReadOnlyCollection<string> DurationKeys => durationKeys;

			public void SetValue( string key, object value )
			{
				Data[key] = value;
				durationKeys.Remove( key );
			}

			internal void SetDuration( string key, int milliseconds )
			{
				SetValue( key, milliseconds );
				durationKeys.Add( key );
			}

			Dictionary<string, FastTimer> timers;

			public void StartTimer( string name )
			{
				timers ??= new Dictionary<string, FastTimer>();
				timers[name] = FastTimer.StartNew();
			}

			public void FinishTimer( string name )
			{
				if ( timers.TryGetValue( name, out var sw ) )
				{
					SetDuration( name, (int)sw.ElapsedMilliSeconds );
				}
			}

			public IDisposable ScopeTimer( string name )
			{
				var sw = FastTimer.StartNew();
				return new DisposeAction( () => SetDuration( name, (int)sw.ElapsedMilliSeconds ) );
			}

			public void Submit( bool andFlush = false )
			{
				Version = Application.Version;
				Mode = Application.IsEditor ? "editor" : "game";
				if ( Application.IsHeadless ) Mode = "app";
				if ( Application.IsDedicatedServer ) Mode = "server";

				Add( this );

				// Log.Info( Json.Serialize( Data ) );

				if ( andFlush )
					Flush();
			}

		}

	}
}
