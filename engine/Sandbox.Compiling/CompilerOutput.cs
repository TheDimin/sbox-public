using Microsoft.CodeAnalysis;

namespace Sandbox;

public class CompilerOutput
{
	public CompilerOutput( Compiler compiler )
	{
		Compiler = compiler;
	}

	/// <summary>
	/// True if the build succeeded
	/// </summary>
	public bool Successful { get; internal set; }

	/// <summary>
	/// The compiler that has produced this build
	/// </summary>
	public Compiler Compiler { get; }

	/// <summary>
	/// The version of the assembly
	/// </summary>
	public Version Version { get; set; }

	/// <summary>
	/// The [assembly].dll contents for this build
	/// </summary>
	public byte[] AssemblyData { get; internal set; }

	/// <summary>
	/// A code archive created during the compile
	/// </summary>
	public CodeArchive Archive { get; internal set; }

	/// <summary>
	/// The [assembly].xml contents for this build
	/// </summary>
	public string XmlDocumentation { get; internal set; }

	/// <summary>
	/// A list of diagnostics caused by the previous build
	/// </summary>
	public List<Microsoft.CodeAnalysis.Diagnostic> Diagnostics { get; } = new();

	/// <summary>
	/// If an exception happened during the build, it'll be available here
	/// </summary>
	public Exception Exception { get; internal set; }

	/// <summary>
	/// For referencing this assembly from another compiler.
	/// </summary>
	internal PortableExecutableReference MetadataReference { get; set; }

	/// <summary>
	/// Serialize completed archives on workers, then publish each batch in input order on
	/// the caller's context. Only two archives and 16 MiB of source text can be prepared
	/// together; a larger archive is processed alone. No serialized bytes are retained here.
	/// </summary>
	internal static async Task PublishArchivesAsync( IReadOnlyList<CompilerOutput> outputs, Action<CompilerOutput, byte[]> publish )
	{
		const long sourceByteBudget = 16 * 1024 * 1024;

		for ( var index = 0; index < outputs.Count; )
		{
			var count = 1;
			if ( index + 1 < outputs.Count
				&& GetArchiveSourceBytes( outputs[index].Archive ) + GetArchiveSourceBytes( outputs[index + 1].Archive ) <= sourceByteBudget )
			{
				count = 2;
			}

			var tasks = new Task<byte[]>[count];
			for ( var offset = 0; offset < count; offset++ )
			{
				var archive = outputs[index + offset].Archive;
				tasks[offset] = Task.Run( archive.Serialize );
			}

			var bytes = await Task.WhenAll( tasks );
			for ( var offset = 0; offset < count; offset++ )
			{
				publish( outputs[index + offset], bytes[offset] );
			}

			// Do not keep the previous batch's results alive while preparing the next one.
			Array.Clear( bytes );
			Array.Clear( tasks );
			index += count;
		}
	}

	private static long GetArchiveSourceBytes( CodeArchive archive )
	{
		// UTF-16 source size is a work budget, not a bound on JSON/compression allocations.
		return sizeof( char ) * (archive.SyntaxTrees.Sum( x => (long)x.Length )
			+ archive.AdditionalFiles.Sum( x => (long)(x.Text?.Length ?? 0) ));
	}
}
