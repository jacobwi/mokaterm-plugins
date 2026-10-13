using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Passwords.Components;
using Moka.Passwords.Generation;
using Moka.Passwords.Settings;
using Moka.Passwords.Strength;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Interop;
using Mokaterm.UI.Common.Platform;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.Passwords.Tool;

/// <summary>
/// The Password Generator app tool. It needs no session and touches nothing outside the page: values are made here,
/// never logged or saved, copied only through the host's secret copy, and dropped when the tool closes.
/// </summary>
public sealed partial class PasswordGeneratorTool : AppToolBase, IDisposable
{
	private readonly List<string> _values = [];
	private GenerationMode _mode;
	private PasswordOptions _password = new();
	private PassphraseOptions _passphrase = new();
	private int _pinDigits = PinGenerator.DefaultDigits;
	private int _count = 1;
	private bool _hidden;
	private string? _problem;
	private StrengthReport? _strength;
	private int _copied = -1;

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	[Inject]
	private IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	private string Summary => _problem is not null
		? "nothing to make"
		: _mode switch
		{
			GenerationMode.Password => string.Create(CultureInfo.CurrentCulture, $"{_password.Length} characters from {PasswordAlphabet.For(_password).Size}"),
			GenerationMode.Passphrase => string.Create(CultureInfo.CurrentCulture, $"{_passphrase.Words} words from {WordList.Count}"),
			_ => string.Create(CultureInfo.CurrentCulture, $"{_pinDigits} digits"),
		};

	private IReadOnlyList<string> Notes => _mode switch
	{
		GenerationMode.Passphrase when _passphrase.BoundariesAmbiguous =>
			["Without a separator or capitals, a few passphrases can be spelled from two sets of words, so this is an upper bound."],
		GenerationMode.Pin => ["A PIN only holds up where the device locks after a few wrong tries."],
		_ => [],
	};

	public void Dispose() => Forget();

	protected override void OnInitialized()
	{
		PasswordSettings settings = SettingsService.Get<PasswordSettings>().Clamped();
		_mode = settings.Mode;
		_password = settings.ToPasswordOptions();
		_passphrase = settings.ToPassphraseOptions();
		_pinDigits = settings.PinDigits;
		_count = settings.Count;
		_hidden = settings.StartHidden;
		Regenerate();
	}

	private void OnModeChanged(string id)
	{
		if (Enum.TryParse(id, out GenerationMode mode) && Enum.IsDefined(mode))
		{
			_mode = mode;
			Regenerate();
		}
	}

	// One symbol set serves both: passwords draw from it, and passphrases when they add a symbol.
	private void OnPasswordChanged(PasswordOptions options)
	{
		_password = options;
		_passphrase = _passphrase with { SymbolSet = options.SymbolSet };
		Regenerate();
	}

	private void OnPassphraseChanged(PassphraseOptions options)
	{
		_passphrase = options;
		_password = _password with { SymbolSet = options.SymbolSet };
		Regenerate();
	}

	private void OnPinDigitsChanged(int digits)
	{
		_pinDigits = digits;
		Regenerate();
	}

	private void OnCountChanged(int count)
	{
		_count = count;
		Regenerate();
	}

	private void ToggleHidden() => _hidden = !_hidden;

	private void Regenerate()
	{
		Forget();
		_problem = _mode switch
		{
			GenerationMode.Password => PasswordAlphabet.ProblemWith(_password),
			GenerationMode.Passphrase => _passphrase.Problem(),
			_ => PinGenerator.ProblemWith(_pinDigits),
		};

		if (_problem is not null)
		{
			_strength = null;
			return;
		}

		_strength = StrengthReport.For(_mode switch
		{
			GenerationMode.Password => Entropy.OfPassword(_password),
			GenerationMode.Passphrase => Entropy.OfPassphrase(_passphrase),
			_ => Entropy.OfPin(_pinDigits),
		});

		for (int index = 0; index < _count; index++)
		{
			_values.Add(_mode switch
			{
				GenerationMode.Password => PasswordGenerator.Generate(_password),
				GenerationMode.Passphrase => PassphraseGenerator.Generate(_passphrase),
				_ => PinGenerator.Generate(_pinDigits),
			});
		}
	}

	private async Task CopyAsync(int index)
	{
		if (index >= _values.Count)
		{
			return;
		}

		string noun = ModeOptions.Name(_mode);
		string value = _values[index];
		try
		{
			await Clipboard.WriteSecretAsync(value);
		}
		catch (Exception ex) when (JsModule.IsExpected(ex) || ex is InvalidOperationException)
		{
			Interaction.Notify(NoticeSeverity.Error, $"{noun} could not be copied.");
			return;
		}

		// A regenerate while the copy ran replaced the row, which then shows no mark.
		_copied = index < _values.Count && ReferenceEquals(_values[index], value) ? index : -1;
		int clearSeconds = SettingsService.Get<SecuritySettings>().ClipboardClearSeconds;
		Interaction.Notify(
			NoticeSeverity.Success,
			clearSeconds > 0
				? string.Create(CultureInfo.CurrentCulture, $"{noun} copied. The clipboard clears in {clearSeconds} s.")
				: $"{noun} copied.");
	}

	// .NET strings cannot be wiped: dropping every reference is as far as clearing goes, and the collector reclaims the
	// memory when it runs.
	private void Forget()
	{
		_values.Clear();
		_copied = -1;
	}
}
