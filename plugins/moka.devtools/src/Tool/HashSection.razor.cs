using System.Globalization;
using System.Security.Cryptography;
using Moka.DevTools.Components;
using Moka.DevTools.Hashing;
using Moka.DevTools.Text;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.Components;

namespace Moka.DevTools.Tool;

/// <summary>
/// Every hash of a text or a file at once, plain or as an HMAC, and a check against a hash pasted to compare. A file
/// is streamed in one pass with its progress shown and can be cancelled; the key is cleared after each use.
/// </summary>
public sealed partial class HashSection : DevToolViewBase, IDisposable
{
	private static readonly TimeSpan ProgressEvery = TimeSpan.FromMilliseconds(150);

	private static readonly IReadOnlyList<EnumOption<HashSource>> Sources = [new(HashSource.Text, "Text"), new(HashSource.File, "File")];

	private static readonly IReadOnlyList<EnumOption<HashFormat>> Formats =
	[
		new(HashFormat.Hex, "hex"),
		new(HashFormat.UpperHex, "HEX"),
		new(HashFormat.Base64, "Base64"),
	];

	private static readonly IReadOnlyList<EnumOption<KeyFormat>> KeyFormats =
	[
		new(KeyFormat.Text, "Text"),
		new(KeyFormat.Hex, "Hex"),
		new(KeyFormat.Base64, "Base64"),
	];

	private readonly ProgressCounter _progress = new();
	private HashSource _source;
	private HashFormat _format;
	private TextValue _text = TextValue.Empty;
	private bool _hmac;
	private string _key = "";
	private KeyFormat _keyFormat;
	private string _expected = "";
	private IReadOnlyList<HashValue>? _values;
	private bool _valuesAreHmac;
	private string? _error;
	private string? _fileName;
	private long? _fileSize;
	private bool _hashing;
	private int _runs;
	private CancellationTokenSource? _running;

	private string Summary => (_source, _hmac) switch
	{
		(HashSource.Text, false) => "text, plain",
		(HashSource.Text, true) => "text, HMAC",
		(_, false) => _fileName is null ? "a file" : "file, plain",
		_ => _fileName is null ? "a file, HMAC" : "file, HMAC",
	};

	private double Percent => _fileSize is > 0 and { } size ? Math.Min(100, 100.0 * _progress.Value / size) : 0;

	private string ProgressText => _fileSize is { } size
		? $"{Size(_progress.Value)} of {Size(size)}"
		: $"{Size(_progress.Value)} read";

	private string FileNote => IsWeb
		? "On the web version the browser sends the file to the server, which hashes it as it arrives. Nothing is kept."
		: string.Create(CultureInfo.CurrentCulture, $"Read from disk in one pass, up to {TextLimits.MaxHashFileBytes / (1024 * 1024 * 1024)} GB.");

	private (string Chip, ResultTone Tone, string Text)? Comparison
	{
		get
		{
			if (_expected.Trim().Length == 0)
			{
				return null;
			}

			byte[] expected;
			string form;
			try
			{
				(expected, form) = ExpectedHash.Parse(_expected);
			}
			catch (FormatException ex)
			{
				return ("not read", ResultTone.Warning, ex.Message);
			}

			if (_values is null)
			{
				return ("waiting", ResultTone.Plain, $"Read as {form}, {expected.Length * 8} bits. Hash something to compare.");
			}

			List<string> names = [.. _values.Where(value => value.Bytes is { } bytes && Same(bytes, expected)).Select(value => value.Algorithm.Name)];
			return names.Count > 0
				? ("matches", ResultTone.Good, $"The {form} hash is the {string.Join(" and ", names)}{(_valuesAreHmac ? " HMAC" : "")}.")
				: ("no match", ResultTone.Bad, string.Create(CultureInfo.CurrentCulture, $"No algorithm gives this {form} hash ({expected.Length * 8} bits)."));
		}
	}

	public void Dispose()
	{
		Cancel();
		_key = "";
		_text = TextValue.Empty;
		_values = null;
	}

	private static bool Same(byte[] left, byte[] right) => left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

	private bool Matches(HashValue value)
	{
		if (value.Bytes is not { } bytes || _expected.Trim().Length == 0)
		{
			return false;
		}

		try
		{
			return Same(bytes, ExpectedHash.Parse(_expected).Bytes);
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private string Format(byte[] bytes) => _format switch
	{
		HashFormat.UpperHex => Convert.ToHexString(bytes),
		HashFormat.Base64 => Convert.ToBase64String(bytes),
		_ => Convert.ToHexStringLower(bytes),
	};

	private void OnExpectedChanged(string? value) => _expected = value ?? "";

	private Task OnSourceChangedAsync(HashSource source)
	{
		_source = source;
		_values = null;
		_error = null;
		return source == HashSource.Text ? HashTextAsync() : Task.CompletedTask;
	}

	private Task OnHmacChangedAsync(bool hmac)
	{
		_hmac = hmac;
		return Refresh();
	}

	private Task OnKeyChangedAsync(string? key)
	{
		_key = key ?? "";
		return Refresh();
	}

	private Task OnKeyFormatChangedAsync(KeyFormat format)
	{
		_keyFormat = format;
		return Refresh();
	}

	private Task OnTextChangedAsync(TextValue value)
	{
		_text = value;
		return HashTextAsync();
	}

	// A file is not read again behind the user's back: a change of key or mode clears its results instead.
	private Task Refresh()
	{
		if (_source == HashSource.Text)
		{
			return HashTextAsync();
		}

		_values = null;
		return Task.CompletedTask;
	}

	private async Task HashTextAsync()
	{
		Cancel();
		int run = ++_runs;
		_error = null;
		if (_text.IsEmpty && !_hmac)
		{
			_values = null;
			return;
		}

		byte[] data;
		byte[] key;
		try
		{
			data = StrictUtf8.Encode(_text.Text);
			key = _hmac ? SecretKey.Read(_key, _keyFormat) : [];
		}
		catch (FormatException ex)
		{
			_values = null;
			_error = ex.Message;
			return;
		}

		bool hmac = _hmac;
		try
		{
			IReadOnlyList<HashValue> values = data.Length >= TextLimits.BackgroundChars
				? await Task.Run(() => HashComputer.Compute(data, key, hmac))
				: HashComputer.Compute(data, key, hmac);
			if (run == _runs)
			{
				_values = values;
				_valuesAreHmac = hmac;
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}

	private async Task PickFileAsync()
	{
		Cancel();
		_error = null;
		IReadOnlyList<LocalFileItem> picked;
		try
		{
			picked = await Files.PickFilesAsync(multiple: false);
		}
		catch (Exception ex) when (IsPageFailure(ex))
		{
			_error = "The file picker could not be opened.";
			return;
		}

		try
		{
			if (picked.Count == 0 || picked[0] is not { OpenReadAsync: { } open } file)
			{
				return;
			}

			if (file.Length > TextLimits.MaxHashFileBytes)
			{
				_error = string.Create(CultureInfo.CurrentCulture, $"{file.Name} is larger than {TextLimits.MaxHashFileBytes / (1024 * 1024 * 1024)} GB, the most this hashes.");
				return;
			}

			await HashFileAsync(file.Name, file.Length, open);
		}
		finally
		{
			// The web host keeps a picked file alive in the page until it is let go.
			try
			{
				await Files.ReleaseAsync(picked);
			}
			catch (Exception ex) when (IsPageFailure(ex))
			{
				// The page is gone, and the file with it.
			}
		}
	}

	private async Task HashFileAsync(string name, long? length, Func<CancellationToken, ValueTask<Stream>> open)
	{
		byte[] key;
		try
		{
			key = _hmac ? SecretKey.Read(_key, _keyFormat) : [];
		}
		catch (FormatException ex)
		{
			_error = ex.Message;
			return;
		}

		CancellationTokenSource running = new();
		_running = running;
		CancellationToken token = running.Token;
		bool hmac = _hmac;
		_fileName = name;
		_fileSize = length;
		_values = null;
		_hashing = true;
		_progress.Reset();
		StateHasChanged();
		try
		{
			await using Stream stream = await open(token);
			Task<IReadOnlyList<HashValue>> work = Task.Run(() => HashComputer.ComputeAsync(stream, key, hmac, _progress, token), token);
			while (!work.IsCompleted)
			{
				await Task.WhenAny(work, Task.Delay(ProgressEvery, Time, CancellationToken.None));
				StateHasChanged();
			}

			_values = await work;
			_valuesAreHmac = hmac;
		}
		catch (OperationCanceledException)
		{
			_error = $"Hashing {name} was cancelled.";
		}
		catch (Exception ex) when (IsPageFailure(ex) || ex is UnauthorizedAccessException)
		{
			_error = ex is IOException { Message: { } message } && message.StartsWith("The file is larger", StringComparison.Ordinal)
				? message
				: $"{name} could not be read to the end.";
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
			_hashing = false;
			if (ReferenceEquals(_running, running))
			{
				_running = null;
			}

			running.Dispose();
		}
	}

	private void CancelFile() => _running?.Cancel();

	private void Cancel()
	{
		if (Interlocked.Exchange(ref _running, null) is { } previous)
		{
			previous.Cancel();
		}
	}

	/// <summary>Bytes read so far, written by the hashing thread and read by the renderer.</summary>
	private sealed class ProgressCounter : IProgress<long>
	{
		private long _value;

		public long Value => Interlocked.Read(ref _value);

		public void Report(long value) => Interlocked.Exchange(ref _value, value);

		public void Reset() => Interlocked.Exchange(ref _value, 0);
	}
}
