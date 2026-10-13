using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Moka.Certificates.Settings;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;

namespace Moka.Certificates.Components;

/// <summary>
/// What every view of the plugin shares: the settings, the clock, and the one way to copy (secrets through the host's
/// secret copy, which clears the clipboard again), save and open a file.
/// </summary>
public abstract class CertificateViewBase : ComponentBase
{
	/// <summary>The largest file opened: far more than any certificate, key, bundle or PFX.</summary>
	public const int MaxOpenBytes = 8 * 1024 * 1024;

	[Inject]
	protected ISettingsService SettingsService { get; set; } = default!;

	[Inject]
	protected IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	protected ILocalFileAccess Files { get; set; } = default!;

	[Inject]
	protected IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	protected TimeProvider Time { get; set; } = default!;

	protected CertificateSettings Settings => SettingsService.Get<CertificateSettings>().Clamped();

	protected DateTimeOffset Now => Time.GetUtcNow();

	protected async Task CopyAsync(string text, string what)
	{
		try
		{
			await Clipboard.WriteTextAsync(text);
			Interaction.Notify(NoticeSeverity.Success, $"{what} copied.");
		}
		catch (Exception ex) when (IsPageFailure(ex))
		{
			Interaction.Notify(NoticeSeverity.Error, $"{what} could not be copied.");
		}
	}

	/// <summary>Copies a private key or a password through the host's secret copy, which clears the clipboard after the time set under Security.</summary>
	protected async Task CopySecretAsync(string secret, string what)
	{
		try
		{
			await Clipboard.WriteSecretAsync(secret);
		}
		catch (Exception ex) when (IsPageFailure(ex))
		{
			Interaction.Notify(NoticeSeverity.Error, $"{what} could not be copied.");
			return;
		}

		int clearSeconds = SettingsService.Get<SecuritySettings>().ClipboardClearSeconds;
		Interaction.Notify(
			NoticeSeverity.Success,
			clearSeconds > 0
				? string.Create(CultureInfo.CurrentCulture, $"{what} copied. The clipboard clears in {clearSeconds} s.")
				: $"{what} copied.");
	}

	protected Task SaveTextAsync(string fileName, string text) => SaveAsync(fileName, new UTF8Encoding(false).GetBytes(text));

	protected async Task SaveAsync(string fileName, byte[] bytes)
	{
		ArgumentNullException.ThrowIfNull(bytes);
		try
		{
			await Files.SaveFileAsync(fileName, bytes.Length, (stream, token) => stream.WriteAsync(bytes, token).AsTask());
		}
		catch (Exception ex) when (IsPageFailure(ex) || ex is UnauthorizedAccessException)
		{
			Interaction.Notify(NoticeSeverity.Error, $"{fileName} could not be saved.");
		}
	}

	/// <summary>Picks one file and reads it whole, up to <see cref="MaxOpenBytes"/>. Null when the picker was closed or the file refused (which says why).</summary>
	protected async Task<(string Name, byte[] Bytes)?> OpenFileAsync(string what)
	{
		PickedFile<byte[]> picked;
		try
		{
			picked = await LocalFilePick.ReadBytesAsync(Files, MaxOpenBytes, what);
		}
		catch (Exception ex) when (IsPageFailure(ex) || ex is UnauthorizedAccessException)
		{
			Interaction.Notify(NoticeSeverity.Error, "The file could not be read.");
			return null;
		}

		if (picked.Error is { } error)
		{
			Interaction.Notify(NoticeSeverity.Error, error);
			return null;
		}

		return picked.WasRead ? (picked.Name, picked.Content) : null;
	}

	protected void Notify(NoticeSeverity severity, string message) => Interaction.Notify(severity, message);

	/// <summary>A failure of the page or the file system rather than of this code: the page went away, the browser refused, the disk did.</summary>
	protected static bool IsPageFailure(Exception exception) =>
		exception is IOException or InvalidOperationException or Microsoft.JSInterop.JSException or Microsoft.JSInterop.JSDisconnectedException;

	/// <summary>A file name made from a certificate's title: letters, digits, dots and dashes.</summary>
	protected static string FileNameFrom(string title, string extension)
	{
		StringBuilder name = new();
		foreach (char c in title.Trim())
		{
			name.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-');
		}

		string stem = name.ToString().Trim('-', '.');
		if (stem.Length == 0)
		{
			stem = "certificate";
		}

		return (stem.Length > 60 ? stem[..60] : stem) + extension;
	}
}
