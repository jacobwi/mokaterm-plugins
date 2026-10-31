using System.Text;
using Mokaterm.Abstractions.Commands;

namespace Moka.Network.Server;

/// <summary>One packet line as it arrived, with what the parser made of it.</summary>
public sealed record CaptureEntry(string Raw, TcpDumpLine Parsed);

/// <summary>
/// The packet lines of a capture, gathered as tcpdump writes them: the bytes are heard on the stream-reading loop,
/// decoded as UTF-8 (a character split across two pieces arrives whole), cut at line breaks, parsed, and kept in a
/// ring of at most <see cref="Capacity"/> lines with the oldest dropped first and counted. Nothing is queued for the
/// view: it takes a <see cref="Snapshot"/> when <see cref="Version"/> moved, and what it has not looked at by then is
/// simply gone from the top, which the drop count says out loud.
/// </summary>
public sealed class CaptureLines
{
	/// <summary>How many lines the ring holds; the capture tab keeps this fixed rather than a setting.</summary>
	public const int Capacity = 10_000;

	/// <summary>The longest line kept as one line, so one runaway packet summary cannot eat the ring.</summary>
	public const int MaxLineCharacters = 1024;

	private readonly Lock _lock = new();
	private readonly Queue<CaptureEntry> _lines = new();
	private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetDecoder();
	private readonly StringBuilder _line = new();
	private bool _afterReturn;
	private long _dropped;
	private long _total;
	private int _version;

	/// <summary>Bumped by every change, so a view can tell whether to take a new snapshot.</summary>
	public int Version
	{
		get
		{
			lock (_lock)
			{
				return _version;
			}
		}
	}

	/// <summary>How many of the oldest lines were dropped to keep the rest.</summary>
	public long Dropped
	{
		get
		{
			lock (_lock)
			{
				return _dropped;
			}
		}
	}

	/// <summary>Every line the capture has produced, dropped ones counted, which is the honest size of a capture.</summary>
	public long Total
	{
		get
		{
			lock (_lock)
			{
				return _total;
			}
		}
	}

	/// <summary>How many lines the ring holds right now.</summary>
	public int Count
	{
		get
		{
			lock (_lock)
			{
				return _lines.Count;
			}
		}
	}

	/// <summary>
	/// The handler a capture's request is given. stdout is where tcpdump writes its packet lines and stderr its
	/// complaints, which the run's own outcome carries, so only stdout is gathered here.
	/// </summary>
	public void Write(CommandStream stream, ReadOnlySpan<byte> bytes)
	{
		if (stream != CommandStream.Output || bytes.IsEmpty)
		{
			return;
		}

		char[] chars = new char[bytes.Length];
		int decoded;
		lock (_lock)
		{
			// The decoder is not thread safe, so both of its calls sit under the lock with everything else.
			decoded = _decoder.GetChars(bytes, chars, flush: false);
			Take(chars.AsSpan(0, decoded));
			_version++;
		}
	}

	/// <summary>Ends the stream: a last line without its line break still becomes a line.</summary>
	public void Complete()
	{
		Span<char> rest = stackalloc char[4];
		lock (_lock)
		{
			int decoded = _decoder.GetChars([], rest, flush: true);
			Take(rest[..decoded]);
			if (_line.Length > 0)
			{
				End();
			}

			_version++;
		}
	}

	/// <summary>The kept lines oldest first, with the line still being written at the end.</summary>
	public IReadOnlyList<CaptureEntry> Snapshot()
	{
		lock (_lock)
		{
			List<CaptureEntry> lines = [.. _lines];
			if (_line.Length > 0)
			{
				lines.Add(Made(_line.ToString()));
			}

			return lines;
		}
	}

	/// <summary>Every kept line as it arrived, oldest first, for a copy of the capture.</summary>
	public string AllText()
	{
		lock (_lock)
		{
			StringBuilder builder = new();
			foreach (CaptureEntry entry in _lines)
			{
				builder.AppendLine(entry.Raw);
			}

			return builder.ToString();
		}
	}

	private void Take(ReadOnlySpan<char> chars)
	{
		foreach (char character in chars)
		{
			if (character == '\n' && _afterReturn)
			{
				_afterReturn = false;
				continue;
			}

			_afterReturn = character == '\r';
			if (character is '\n' or '\r')
			{
				End();
				continue;
			}

			_line.Append(character);
			if (_line.Length >= MaxLineCharacters)
			{
				End();
			}
		}
	}

	private void End()
	{
		_lines.Enqueue(Made(_line.ToString()));
		_line.Clear();
		_total++;
		while (_lines.Count > Capacity)
		{
			_ = _lines.Dequeue();
			_dropped++;
		}
	}

	private static CaptureEntry Made(string raw) => new(raw, TcpDumpLine.Parse(raw));
}
