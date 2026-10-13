using System.Globalization;
using Moka.DevTools.Codecs;
using Moka.DevTools.Components;
using Moka.DevTools.Text;
using Mokaterm.UI.Common.Components;

namespace Moka.DevTools.Tool;

/// <summary>
/// Base64, Base64url, URL, hex and HTML, both ways, as the input changes. Decoded bytes are shown as text only when they
/// are UTF-8; otherwise as hex or a dump, never with replacement characters standing in for the real bytes.
/// </summary>
public sealed partial class EncodeSection : DevToolViewBase, IDisposable
{
	private static readonly IReadOnlyList<EnumOption<CodecKind>> Kinds =
	[
		new(CodecKind.Base64, "Base64"),
		new(CodecKind.Base64Url, "Base64url"),
		new(CodecKind.Url, "URL"),
		new(CodecKind.Hex, "Hex"),
		new(CodecKind.Html, "HTML"),
	];

	private static readonly IReadOnlyList<EnumOption<CodecDirection>> Directions = [new(CodecDirection.Encode, "Encode"), new(CodecDirection.Decode, "Decode")];

	private static readonly IReadOnlyList<EnumOption<UrlMode>> UrlModes = [new(UrlMode.Component, "Component"), new(UrlMode.WholeUrl, "Whole URL")];

	private static readonly IReadOnlyList<EnumOption<HexSeparator>> Separators =
	[
		new(HexSeparator.None, "No gaps"),
		new(HexSeparator.Space, "Spaces"),
		new(HexSeparator.Colon, "Colons"),
	];

	private static readonly IReadOnlyList<EnumOption<OutputView>> AllViews = [new(OutputView.Text, "Text"), new(OutputView.Hex, "Hex"), new(OutputView.Dump, "Dump")];

	private static readonly IReadOnlyList<EnumOption<OutputView>> ByteViews = [new(OutputView.Hex, "Hex"), new(OutputView.Dump, "Dump")];

	private CodecKind _kind;
	private CodecDirection _direction;
	private TextValue _input = TextValue.Empty;
	private bool _padding = true;
	private UrlMode _urlMode;
	private bool _plusIsSpace;
	private HexSeparator _separator;
	private bool _upper;
	private bool _nonAscii;
	private CodecOutput? _output;
	private string? _outputUtf8;
	private OutputView _view;
	private string? _error;
	private int _runs;
	private CancellationTokenSource? _running;

	private bool TakesBytes => _direction == CodecDirection.Encode && _kind is CodecKind.Base64 or CodecKind.Base64Url or CodecKind.Hex;

	private string Summary => $"{Kinds.First(kind => kind.Value == _kind).Text}, {(_direction == CodecDirection.Encode ? "encode" : "decode")}";

	private string InputLabel => _direction == CodecDirection.Encode ? (TakesBytes ? "Text or a file" : "Text") : $"{Kinds.First(kind => kind.Value == _kind).Text} to decode";

	private string InputPlaceholder => (_direction, _kind) switch
	{
		(CodecDirection.Encode, _) => "Type or paste text. It is encoded as UTF-8.",
		(_, CodecKind.Base64) => "SGVsbG8sIHdvcmxkIQ==",
		(_, CodecKind.Base64Url) => "SGVsbG8sIHdvcmxkIQ",
		(_, CodecKind.Url) => "Hello%2C%20world%21",
		(_, CodecKind.Hex) => "48 65 6c 6c 6f",
		_ => "&lt;b&gt;Hello&lt;/b&gt; &amp; goodbye",
	};

	/// <summary>The output as the view shows it: text, hex or a dump.</summary>
	private string? OutputText => _output switch
	{
		null => null,
		{ Text: { } text } => text,
		{ Bytes: { } bytes } => _view switch
		{
			OutputView.Text when _outputUtf8 is not null => _outputUtf8,
			OutputView.Dump => HexText.Dump(bytes),
			_ => HexText.Format(bytes, separator: " "),
		},
		_ => null,
	};

	private string? OutputDetail => _output switch
	{
		{ Text: { } text } => string.Create(CultureInfo.CurrentCulture, $"{text.Length:N0} chars"),
		{ Bytes: { } bytes } => _outputUtf8 is null
			? $"{Size(bytes.Length)}, not UTF-8 text"
			: string.Create(CultureInfo.CurrentCulture, $"{Size(bytes.Length)}, UTF-8"),
		_ => null,
	};

	private string? OutputFileName => _output switch
	{
		{ Text: not null } => _direction == CodecDirection.Encode ? "encoded.txt" : "decoded.txt",
		{ Bytes: not null } when _view == OutputView.Text && _outputUtf8 is not null => "decoded.txt",
		_ => null,
	};

	/// <summary>What "Use as input" puts in the box: the output when it is text.</summary>
	private string? SwapText => _output switch
	{
		{ Text: { } text } => text,
		{ Bytes: not null } => _outputUtf8,
		_ => null,
	};

	public void Dispose()
	{
		Cancel();
		_input = TextValue.Empty;
		_output = null;
		_outputUtf8 = null;
	}

	private IReadOnlyList<EnumOption<OutputView>> ViewsFor(CodecOutput output) => _outputUtf8 is null || output.Bytes is null ? ByteViews : AllViews;

	private Task OnKindChangedAsync(CodecKind kind) => SetAsync(() => _kind = kind);

	private Task OnDirectionChangedAsync(CodecDirection direction) => SetAsync(() =>
	{
		_direction = direction;
		if (!TakesBytes && _input.Bytes is not null)
		{
			_input = TextValue.Empty;
		}
	});

	private Task OnInputChangedAsync(TextValue value) => SetAsync(() => _input = value);

	private Task SetAsync(Action change)
	{
		change();
		return RunAsync();
	}

	private async Task SwapAsync()
	{
		if (SwapText is not { } text)
		{
			return;
		}

		_input = new TextValue { Text = text };
		_direction = _direction == CodecDirection.Encode ? CodecDirection.Decode : CodecDirection.Encode;
		await RunAsync();
	}

	private Task SaveBytesAsync() => _output?.Bytes is { } bytes ? SaveAsync("decoded.bin", bytes) : Task.CompletedTask;

	private async Task RunAsync()
	{
		Cancel();
		int run = ++_runs;
		if (_input.IsEmpty)
		{
			_output = null;
			_outputUtf8 = null;
			_error = null;
			return;
		}

		CodecRequest request = new()
		{
			Kind = _kind,
			Direction = _direction,
			Text = _input.Text,
			FileBytes = _input.Bytes,
			Padding = _padding,
			UrlMode = _urlMode,
			PlusIsSpace = _plusIsSpace,
			UpperHex = _upper,
			Separator = _separator,
			EncodeNonAscii = _nonAscii,
		};

		CancellationTokenSource running = new();
		_running = running;
		(CodecOutput? output, string? utf8, string? error) result;
		try
		{
			bool large = request.Text.Length >= TextLimits.BackgroundChars || request.FileBytes is not null;
			result = large ? await Task.Run(() => Run(request), running.Token) : Run(request);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (run != _runs)
		{
			return;
		}

		(_output, _outputUtf8, _error) = result;
		_view = _output?.Bytes is not null && _outputUtf8 is null ? OutputView.Hex : OutputView.Text;
	}

	private static (CodecOutput? Output, string? Utf8, string? Error) Run(CodecRequest request)
	{
		try
		{
			CodecOutput output = CodecRunner.Run(request);
			string? utf8 = output.Bytes is { } bytes && StrictUtf8.TryDecode(bytes, out string? text) ? text : null;
			return (output, utf8, null);
		}
		catch (CodecException ex)
		{
			return (null, null, ex.Message);
		}
	}

	private void Cancel()
	{
		if (Interlocked.Exchange(ref _running, null) is { } previous)
		{
			previous.Cancel();
			previous.Dispose();
		}
	}
}
