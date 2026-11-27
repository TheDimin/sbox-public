using System;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace GtaV.ManagedReader;

/// <summary>Reads one archive entry without changing the shared file position.</summary>
internal sealed class RpfRegionStream : Stream
{
	SafeFileHandle file;
	readonly long start, length;
	long read;

	internal RpfRegionStream( SafeFileHandle file, long start, long length )
	{
		// Some dictionary readers remain cached after the archive read lock is released.
		bool acquired = false;
		file.DangerousAddRef( ref acquired );
		this.file = file;
		this.start = start;
		this.length = length;
	}

	~RpfRegionStream() => Dispose( false );

	public override bool CanRead => file is not null;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => length;
	public override long Position { get => read; set => throw new NotSupportedException(); }

	public override int Read( byte[] buffer, int offset, int count ) => Read( buffer.AsSpan( offset, count ) );

	public override int Read( Span<byte> buffer )
	{
		ObjectDisposedException.ThrowIf( file is null, this );
		int count = (int)Math.Min( Length - read, buffer.Length );
		if ( count == 0 ) return 0;
		int actual = RandomAccess.Read( file, buffer[..count], start + read );
		if ( actual == 0 ) throw new EndOfStreamException( "Archive entry was truncated." );
		read += actual;
		return actual;
	}

	public override void Flush() { }
	public override long Seek( long offset, SeekOrigin origin ) => throw new NotSupportedException();
	public override void SetLength( long value ) => throw new NotSupportedException();
	public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

	protected override void Dispose( bool disposing )
	{
		var handle = file;
		file = null;
		handle?.DangerousRelease();
		base.Dispose( disposing );
	}
}
