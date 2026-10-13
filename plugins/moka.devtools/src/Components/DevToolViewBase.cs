using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.DevTools.Text;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Platform;

namespace Moka.DevTools.Components;

/// <summary>
/// What every view of the plugin shares: the clock, the host, and the one way to copy (secrets through the host's
/// secret copy, which clears the clipboard again), save and open a file.
/// </summary>
public abstract class DevToolViewBase : ComponentBase
{
	[Inject]
	protected IClipboardService Clipboard { get; set; } = default!;

	[Inject]
	protected IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	protected ILocalFileAccess Files { get; set; } = default!;

	[Inject]
	protected ISettingsService SettingsService { get; set; } = default!;

	[Inject]
	protected TimeProvider Time { get; set; } = default!;

	[Inject]
	protected IAppEnvironment Environment { get; set; } = default!;

	protected DateTimeOffset Now => Time.GetUtcNow();

	/// <summary>On the web host files come from the browser and every box is sent to the server as it is typed.</summary>
	protected bool IsWeb => Environment.Kind == HostKind.Web;

	/// <summary>The most text a box holds before an opened file stays a file instead.</summary>
	protected int BoxChars => IsWeb ? TextLimits.WebBoxChars : TextLimits.MaxTextChars;

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

	/// <summary>Copies a token, a claim or a key through the host's secret copy, which clears the clipboard after the time set under Security.</summary>
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

	protected async Task SaveTextAsync(string fileName, string text)
	{
		byte[] bytes = StrictUtf8.Encoding.GetBytes(text);
		await SaveAsync(fileName, bytes);
	}

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

	/// <summary>Picks one file and reads it whole, up to <paramref name="maxBytes"/>. Null when the picker was closed or the file refused (which says why).</summary>
	protected async Task<(string Name, byte[] Bytes)?> OpenFileAsync(string what, int maxBytes)
	{
		PickedFile<byte[]> picked;
		try
		{
			picked = await LocalFilePick.ReadBytesAsync(Files, maxBytes, what);
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

	/// <summary>"12 bytes", "3.4 KB", "2.1 MB".</summary>
	protected static string Size(long bytes) => bytes switch
	{
		< 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes:N0} bytes"),
		< 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:0.#} KB"),
		< 1024L * 1024 * 1024 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024):0.#} MB"),
		_ => string.Create(CultureInfo.CurrentCulture, $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"),
	};
}
