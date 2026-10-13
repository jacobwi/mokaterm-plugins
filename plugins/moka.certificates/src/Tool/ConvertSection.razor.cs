using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Components;
using Moka.Certificates.Components;
using Moka.Certificates.Crypto;
using Mokaterm.UI.Common.Components;

namespace Moka.Certificates.Tool;

/// <summary>
/// PEM and DER both ways, PFX to PEM and back, a private key between PKCS#8 and its algorithm's own form, and a key
/// encrypted or decrypted with a password. Every input goes through the reader, so what can be inspected can be
/// converted.
/// </summary>
public sealed partial class ConvertSection : CertificateViewBase
{
	private static readonly IReadOnlyList<ConvertKind> KindList = Enum.GetValues<ConvertKind>();

	private static readonly IReadOnlyList<EnumOption<PrivateKeyFormat>> Formats =
	[
		new(PrivateKeyFormat.Pkcs8, "PKCS#8"),
		new(PrivateKeyFormat.Traditional, "PKCS#1 or SEC1"),
	];

	private static readonly IReadOnlyList<EnumOption<PfxEncryption>> PfxChoices = [new(PfxEncryption.Aes256, "AES-256"), new(PfxEncryption.Legacy3Des, "3DES (older Windows)")];

	private ConvertKind _kind = ConvertKind.PemToDer;
	private InputContent _input = InputContent.Empty;
	private string _password = "";
	private string _newPassword = "";
	private string _newPasswordAgain = "";
	private string _friendlyName = "";
	private PrivateKeyFormat _format = PrivateKeyFormat.Pkcs8;
	private PfxEncryption _pfxEncryption;
	private bool _busy;
	private string? _error;
	private IReadOnlyList<ConvertedOutput> _outputs = [];
	private IReadOnlyList<string> _notes = [];

	[Parameter]
	public EventCallback<string> OnInspect { get; set; }

	private bool TakesPassword => _kind is ConvertKind.PfxToPem or ConvertKind.PemToPfx or ConvertKind.KeyFormat or ConvertKind.EncryptKey or ConvertKind.DecryptKey;

	private string? Mismatch => _newPasswordAgain.Length > 0 && _newPasswordAgain != _newPassword ? "The two passwords differ." : null;

	private string InputLabel => _kind switch
	{
		ConvertKind.DerToPem => "DER file",
		ConvertKind.PfxToPem => "PFX or P12 file",
		ConvertKind.PemToPfx => "Certificate, private key and chain",
		ConvertKind.KeyFormat or ConvertKind.EncryptKey => "Private key",
		ConvertKind.DecryptKey => "Encrypted private key",
		_ => "PEM",
	};

	private string InputPlaceholder => _kind switch
	{
		ConvertKind.DerToPem => "Open a .der, .cer, .csr or .key file, or paste its base64",
		ConvertKind.PfxToPem => "Open a .pfx or .p12 file",
		ConvertKind.PemToPfx => "Paste the certificate, its private key and the chain as PEM, in any order",
		ConvertKind.DecryptKey => "Paste an ENCRYPTED PRIVATE KEY block, or open the file",
		ConvertKind.KeyFormat or ConvertKind.EncryptKey => "Paste a private key in any PEM form, or open the file",
		_ => "Paste one or more PEM blocks",
	};

	private string PasswordPlaceholder => _kind switch
	{
		ConvertKind.PfxToPem => "The PFX's password",
		ConvertKind.DecryptKey => "The key's password",
		_ => "The key's password, if it is encrypted",
	};

	private string KindHelp => _kind switch
	{
		ConvertKind.PemToDer => "Each PEM block becomes its own DER file.",
		ConvertKind.DerToPem => "A DER certificate, request or key as PEM, under the label its type is written with.",
		ConvertKind.PfxToPem => "The certificate, its private key and the rest of the chain, as separate PEM files and as one full chain.",
		ConvertKind.PemToPfx => "One PFX with the certificate, its key and the chain, encrypted under a password.",
		ConvertKind.KeyFormat => "PKCS#8 (BEGIN PRIVATE KEY) or the algorithm's own form (BEGIN RSA PRIVATE KEY, BEGIN EC PRIVATE KEY).",
		ConvertKind.EncryptKey => "The key as encrypted PKCS#8 (BEGIN ENCRYPTED PRIVATE KEY).",
		_ => "An encrypted key as plain PKCS#8. Keep the result somewhere only you can read.",
	};

	protected override void OnInitialized() => _pfxEncryption = Settings.PfxEncryption;

	private static string KindLabel(ConvertKind kind) => kind switch
	{
		ConvertKind.PemToDer => "PEM to DER",
		ConvertKind.DerToPem => "DER to PEM",
		ConvertKind.PfxToPem => "PFX to PEM",
		ConvertKind.PemToPfx => "PEM to PFX",
		ConvertKind.KeyFormat => "Private key format",
		ConvertKind.EncryptKey => "Encrypt a private key",
		_ => "Decrypt a private key",
	};

	private void OnKindChanged(ConvertKind kind)
	{
		_kind = kind;
		_error = null;
		_outputs = [];
		_notes = [];
	}

	private void OnInputChanged(InputContent input)
	{
		_input = input;
		_error = null;
	}

	private async Task ConvertAsync()
	{
		_error = null;
		ConvertKind kind = _kind;
		InputContent input = _input;
		string password = _password;
		string newPassword = _newPassword;
		if (kind == ConvertKind.EncryptKey && (newPassword.Length == 0 || newPassword != _newPasswordAgain))
		{
			_error = newPassword.Length == 0 ? "Type the new password twice." : "The two passwords differ.";
			return;
		}

		PfxOptions pfx = new() { Password = newPassword, FriendlyName = _friendlyName, Encryption = _pfxEncryption };
		PrivateKeyFormat format = _format;
		string stem = FileNameFrom(input.FileName is { } file ? Path.GetFileNameWithoutExtension(file) : "converted", "");
		_busy = true;
		StateHasChanged();
		try
		{
			// PFX files and encrypted keys run their password hashing; that stays off the renderer.
			(IReadOnlyList<ConvertedOutput> outputs, IReadOnlyList<string> notes) = await Task.Run(() => Run(kind, input, password, newPassword, format, pfx, stem));
			_outputs = outputs;
			_notes = notes;
		}
		catch (CertificateException ex)
		{
			_error = ex.Message;
			_outputs = [];
			_notes = [];
		}
		catch (CryptographicException)
		{
			_error = "The conversion failed: the input is damaged or uses something this system does not support.";
			_outputs = [];
			_notes = [];
		}
		finally
		{
			_busy = false;
		}
	}

	private static (IReadOnlyList<ConvertedOutput>, IReadOnlyList<string>) Run(
		ConvertKind kind,
		InputContent input,
		string password,
		string newPassword,
		PrivateKeyFormat format,
		PfxOptions pfx,
		string stem)
	{
		switch (kind)
		{
			case ConvertKind.PemToDer:
			{
				if (input.Bytes is not null)
				{
					throw new CertificateException("That file is not PEM. Pick DER to PEM to go the other way.");
				}

				IReadOnlyList<DerBlock> blocks = CertificateConverter.PemToDer(input.Text);
				return ([.. blocks.Select((block, index) => new ConvertedOutput(
					block.Label.ToUpperInvariant(),
					Convert.ToBase64String(block.Der),
					null,
					block.Der,
					string.Create(CultureInfo.InvariantCulture, $"{stem}{(blocks.Count > 1 ? $"-{index + 1}" : "")}{block.FileExtension}"),
					block.IsSecret,
					string.Create(CultureInfo.CurrentCulture, $"{block.Der.Length:N0} bytes, base64 below")))], []);
			}

			case ConvertKind.DerToPem:
			{
				byte[] der = input.ToStructure() ?? throw new CertificateException("Open a DER file, or paste its base64.");
				string pem = CertificateConverter.DerToPem(der);
				bool secret = pem.Contains("PRIVATE KEY", StringComparison.Ordinal);
				return ([new ConvertedOutput("PEM", pem, stem + (secret ? ".key" : ".pem"), null, null, secret)], []);
			}

			case ConvertKind.PfxToPem:
			{
				using MaterialSet set = CertificateReader.Read(input.ToCryptoInput(), password);
				if (!set.Items.Any() && set.Notes.Count > 0)
				{
					throw new CertificateException(set.Notes[0].Message);
				}

				PemBundle bundle = CertificateConverter.PfxToPem(set, format, newPassword.Length > 0 ? newPassword : null);
				List<ConvertedOutput> outputs = [];
				if (bundle.Certificate is { } certificate)
				{
					outputs.Add(new ConvertedOutput("Certificate", certificate, stem + ".crt", null, null, Secret: false));
				}

				if (bundle.Chain is { } chain)
				{
					outputs.Add(new ConvertedOutput("Chain", chain, stem + "-chain.pem", null, null, Secret: false));
					outputs.Add(new ConvertedOutput("Full chain", bundle.Certificate + chain, stem + "-fullchain.pem", null, null, Secret: false, "certificate, then the chain, as nginx wants it"));
				}

				if (bundle.PrivateKey is { } key)
				{
					outputs.Add(new ConvertedOutput("Private key", key, stem + ".key", null, null, Secret: true));
				}

				return (outputs, bundle.Notes);
			}

			case ConvertKind.PemToPfx:
			{
				if (pfx.Password.Length == 0)
				{
					throw new CertificateException("Type a password for the PFX: without one, anyone who has the file has the key.");
				}

				using MaterialSet set = CertificateReader.Read(input.ToCryptoInput(), password);
				byte[] bytes = CertificateConverter.ToPfx(set, pfx);
				string detail = string.Create(CultureInfo.CurrentCulture, $"{bytes.Length:N0} bytes, {CryptoChoices.PfxEncryptionName(pfx.Encryption)}");
				return ([new ConvertedOutput("PFX", null, null, bytes, stem + ".pfx", Secret: false, detail)], []);
			}

			default:
			{
				using MaterialSet set = CertificateReader.Read(input.ToCryptoInput(), password);
				PrivateKeyMaterial key = set.RequireOnePrivateKey();
				string pem = kind switch
				{
					ConvertKind.EncryptKey => CertificateConverter.EncryptedPrivateKeyPem(key.Key, newPassword),
					ConvertKind.DecryptKey => CertificateConverter.PrivateKeyPem(key.Key, PrivateKeyFormat.Pkcs8),
					_ => CertificateConverter.PrivateKeyPem(key.Key, format),
				};

				string description = CertificateReader.DescribeKey(key.PublicKey).Description;
				return ([new ConvertedOutput("Private key", pem, stem + ".key", null, null, Secret: true, description)], []);
			}
		}
	}
}
