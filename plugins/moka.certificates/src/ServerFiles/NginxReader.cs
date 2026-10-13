using System.Text;

namespace Moka.Certificates.ServerFiles;

/// <summary>One tokenizer per dump; its block stack starts over with every file.</summary>
internal sealed class NginxReader
{
	private readonly List<ConfiguredCertificate> _found = [];
	private readonly List<string> _skipped = [];
	private readonly List<(string Path, string File)> _topLevel = [];
	private readonly Stack<NginxBlock> _blocks = new();
	private readonly List<string> _words = [];
	private readonly StringBuilder _word = new();
	private string _file = "";
	private string? _prefix;
	private char _quote;
	private bool _inWord;

	public void StartFile(string path)
	{
		CloseOpenBlocks();
		_words.Clear();
		_word.Clear();
		_inWord = false;
		_quote = '\0';
		_file = path;

		// nginx resolves relative paths against the folder of its main file, which -T prints first.
		_prefix ??= path.LastIndexOf('/') is > 0 and int slash ? path[..(slash + 1)] : "/etc/nginx/";
	}

	public void Read(string line)
	{
		bool escaped = false;
		foreach (char c in line)
		{
			if (_quote != '\0')
			{
				if (escaped)
				{
					_word.Append(c);
					escaped = false;
				}
				else if (c == '\\')
				{
					escaped = true;
				}
				else if (c == _quote)
				{
					_quote = '\0';
				}
				else
				{
					_word.Append(c);
				}

				continue;
			}

			switch (c)
			{
				case '#':
					EndWord();
					return;
				case '"' or '\'':
					_quote = c;
					_inWord = true;
					break;
				case ';':
					EndWord();
					Directive();
					break;
				case '{':
					EndWord();
					_blocks.Push(new NginxBlock(_words.Count > 0 ? _words[0] : ""));
					_words.Clear();
					break;
				case '}':
					EndWord();
					_words.Clear();
					if (_blocks.Count > 0)
					{
						Close(_blocks.Pop());
					}

					break;
				default:
					if (char.IsWhiteSpace(c))
					{
						EndWord();
					}
					else
					{
						_word.Append(c);
						_inWord = true;
					}

					break;
			}
		}

		// A quoted value may run across lines.
		if (_quote != '\0')
		{
			_word.Append('\n');
		}
		else
		{
			EndWord();
		}
	}

	public (IReadOnlyList<ConfiguredCertificate>, IReadOnlyList<string>) Finish()
	{
		CloseOpenBlocks();
		foreach ((string path, string file) in _topLevel)
		{
			_found.Add(new ConfiguredCertificate(path, "nginx", file, [], "ssl_certificate"));
		}

		return (_found, _skipped);
	}

	private void EndWord()
	{
		if (_inWord)
		{
			_words.Add(_word.ToString());
			_word.Clear();
			_inWord = false;
		}
	}

	private void Directive()
	{
		if (_words.Count == 0)
		{
			return;
		}

		string name = _words[0];
		NginxBlock? server = _blocks.FirstOrDefault(block => block.Name == "server");
		if (name == "server_name" && server is not null)
		{
			server.Names.AddRange(_words.Skip(1).Where(word => word.Length > 0 && word != "_"));
		}
		else if (name == "ssl_certificate" && _words.Count > 1)
		{
			string value = _words[1];
			if (value.Contains('$', StringComparison.Ordinal) || value.StartsWith("data:", StringComparison.Ordinal))
			{
				_skipped.Add($"{_file}: ssl_certificate {value} is chosen per request, so it cannot be read from here.");
			}
			else
			{
				string path = value.StartsWith('/') ? value : (_prefix ?? "/etc/nginx/") + value;
				if (server is not null)
				{
					server.Certificates.Add(path);
				}
				else
				{
					_topLevel.Add((path, _file));
				}
			}
		}

		_words.Clear();
	}

	private void Close(NginxBlock block)
	{
		if (block.Name != "server")
		{
			return;
		}

		// A stream server sits in a stream block; nginx -T prints it with the stream block around it only when both are in one file.
		bool stream = _blocks.Any(outer => outer.Name == "stream");
		foreach (string path in block.Certificates)
		{
			_found.Add(new ConfiguredCertificate(path, stream ? "nginx stream" : "nginx", _file, [.. block.Names], "ssl_certificate"));
		}
	}

	private void CloseOpenBlocks()
	{
		while (_blocks.Count > 0)
		{
			Close(_blocks.Pop());
		}
	}
}
