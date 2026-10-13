using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Certificates.Components;
using Moka.Certificates.Crypto;
using Moka.Red.Core.Enums;

namespace Moka.Certificates.Tool;

/// <summary>
/// Reads whatever is pasted or opened and lists every object in it, with the details of the one picked: certificates,
/// signing requests and keys, whether a bundle is a chain in order, and which key goes with which certificate.
/// </summary>
public sealed partial class InspectSection : CertificateViewBase
{
	private InputContent _input = InputContent.Empty;
	private string _password = "";
	private Inspection _inspection = Inspection.Empty;
	private int? _selected;
	private bool _askPassword;
	private bool _reading;
	private int _reads;
	private int _handledRequest;

	/// <summary>Text another section sent here to be read.</summary>
	[Parameter]
	public InspectRequest? Request { get; set; }

	private InspectedItem? Selected => _inspection.Items.FirstOrDefault(item => item.Number == _selected);

	private string Summary
	{
		get
		{
			if (_inspection.Items.Count == 0)
			{
				return _input.IsEmpty ? "paste or open a file" : "nothing read";
			}

			List<string> parts = [];
			Count(parts, MaterialKind.Certificate, "certificate", "certificates");
			Count(parts, MaterialKind.CertificateRequest, "request", "requests");
			Count(parts, MaterialKind.PrivateKey, "private key", "private keys");
			Count(parts, MaterialKind.PublicKey, "public key", "public keys");
			return string.Join(", ", parts);
		}
	}

	protected override async Task OnParametersSetAsync()
	{
		if (Request is { } request && request.Sequence != _handledRequest)
		{
			_handledRequest = request.Sequence;
			_input = new InputContent { Text = request.Text };
			_password = "";
			await ReadAsync();
		}
	}

	private static MokaColor DotOf(InspectedItem item) => item switch
	{
		{ Certificate: { } certificate } => StatusChip.ColorOf(certificate.Expiry),
		{ Request.SignatureValid: false } => MokaColor.Error,
		{ Locked: true } => MokaColor.Warning,
		{ Kind: MaterialKind.PrivateKey } => MokaColor.Primary,
		_ => MokaColor.Surface,
	};

	private static bool GlowOf(InspectedItem item) =>
		item.Locked || item.Kind == MaterialKind.PrivateKey || item.Certificate?.Expiry is ExpiryState.Expired or ExpiryState.Expiring or ExpiryState.NotYetValid;

	private static string KindOf(InspectedItem item) => item switch
	{
		{ Locked: true } => "locked",
		{ Kind: MaterialKind.Certificate, Certificate.IsCertificateAuthority: true } => "CA cert",
		{ Kind: MaterialKind.Certificate } => "cert",
		{ Kind: MaterialKind.CertificateRequest } => "request",
		{ Kind: MaterialKind.PrivateKey } => "private key",
		_ => "public key",
	};

	private static string EndOf(InspectedItem item) => item switch
	{
		{ Certificate: { Expiry: ExpiryState.Expired } } => "expired",
		{ Certificate: { Expiry: ExpiryState.NotYetValid } } => "not yet valid",
		{ Certificate: { } certificate } => string.Create(CultureInfo.CurrentCulture, $"{certificate.DaysLeft:N0} d"),
		{ Key: { } key } => key.Description,
		{ Request: { } request } => request.Key.Description,
		_ => "",
	};

	private void Count(List<string> parts, MaterialKind kind, string one, string many)
	{
		int count = _inspection.Items.Count(item => item.Kind == kind);
		if (count > 0)
		{
			parts.Add(count == 1 ? $"1 {one}" : string.Create(CultureInfo.CurrentCulture, $"{count} {many}"));
		}
	}

	private async Task OnInputChangedAsync(InputContent input)
	{
		_input = input;
		if (input.IsEmpty)
		{
			_password = "";
		}

		await ReadAsync();
	}

	private void OnPasswordChanged(string? password) => _password = password ?? "";

	private async Task OnPasswordKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter")
		{
			await ReadAsync();
		}
	}

	private async Task ReadAsync()
	{
		int read = ++_reads;
		InputContent input = _input;
		string password = _password;
		int expiringDays = Settings.ExpiringDays;
		DateTimeOffset now = Now;
		_reading = true;

		// Opening a PFX or an encrypted key runs its password hashing, which can take a moment; keep it off the renderer.
		(Inspection inspection, bool needsPassword) = await Task.Run(() =>
		{
			using MaterialSet set = CertificateReader.Read(input.ToCryptoInput(), password);
			return (CertificateReader.Inspect(set, now, expiringDays), set.NeedsPassword);
		});

		if (read != _reads)
		{
			return;
		}

		_reading = false;
		_inspection = inspection;
		_askPassword = needsPassword || (password.Length > 0 && !input.IsEmpty);
		if (Selected is null)
		{
			_selected = _inspection.Items.Count > 0 ? _inspection.Items[0].Number : null;
		}
	}

	private void Select(InspectedItem item) => _selected = item.Number;

	private void CloseDetail() => _selected = null;
}
