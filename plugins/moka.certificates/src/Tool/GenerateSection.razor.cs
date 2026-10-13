using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Components;
using Moka.Certificates.Components;
using Moka.Certificates.Crypto;
using Moka.Certificates.Settings;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Common.Components;

namespace Moka.Certificates.Tool;

/// <summary>
/// Makes key pairs, signing requests, self-signed certificates, CAs, and certificates a loaded CA signs. The new key
/// stays in memory for the PFX export until the next run or until the tool closes.
/// </summary>
public sealed partial class GenerateSection : CertificateViewBase, IDisposable
{
	private static readonly IReadOnlyList<EnumOption<GenerateKind>> Kinds =
	[
		new(GenerateKind.KeyPair, "Key pair"),
		new(GenerateKind.Request, "Signing request"),
		new(GenerateKind.SelfSigned, "Self-signed"),
		new(GenerateKind.Authority, "CA"),
		new(GenerateKind.SignedByCa, "Signed by a CA"),
	];

	private static readonly IReadOnlyList<EnumOption<KeyAlgorithm>> Algorithms = [new(KeyAlgorithm.Rsa, "RSA"), new(KeyAlgorithm.Ecdsa, "ECDSA")];

	private static readonly IReadOnlyList<EnumOption<RsaKeySize>> RsaSizes = [new(RsaKeySize.Bits2048, "2048"), new(RsaKeySize.Bits3072, "3072"), new(RsaKeySize.Bits4096, "4096")];

	private static readonly IReadOnlyList<EnumOption<EllipticCurve>> Curves = [new(EllipticCurve.P256, "P-256"), new(EllipticCurve.P384, "P-384"), new(EllipticCurve.P521, "P-521")];

	private static readonly IReadOnlyList<EnumOption<SignatureHash>> Hashes = [new(SignatureHash.Sha256, "SHA-256"), new(SignatureHash.Sha384, "SHA-384"), new(SignatureHash.Sha512, "SHA-512")];

	private static readonly IReadOnlyList<EnumOption<PfxEncryption>> PfxChoices = [new(PfxEncryption.Aes256, "AES-256"), new(PfxEncryption.Legacy3Des, "3DES (older Windows)")];

	private GenerateKind _kind = GenerateKind.SelfSigned;
	private KeyAlgorithm _algorithm;
	private RsaKeySize _rsaSize;
	private EllipticCurve _curve;
	private SignatureHash _hash;
	private int _validityDays;
	private PfxEncryption _pfxEncryption;
	private SubjectSpec _subject = new();
	private string _namesText = "";
	private IReadOnlyList<GeneralNameInfo> _names = [];
	private IReadOnlyList<string> _nameProblems = [];
	private UsagePresets _usages = UsagePresets.TlsServer;
	private string _pathLength = "";
	private string _keyPassword = "";
	private InputContent _ca = InputContent.Empty;
	private string _caPassword = "";
	private InputContent _request = InputContent.Empty;
	private string _pfxPassword = "";
	private string _friendlyName = "";
	private bool _busy;
	private string? _error;
	private GeneratedOutput? _output;
	private AsymmetricAlgorithm? _key;
	private X509Certificate2? _certificate;
	private List<X509Certificate2> _chain = [];

	/// <summary>Opens a result in the Inspect section.</summary>
	[Parameter]
	public EventCallback<string> OnInspect { get; set; }

	private bool NeedsNewKey => _kind != GenerateKind.SignedByCa || _request.IsEmpty;

	private string Summary => _busy ? "working" : _output is null ? CryptoChoices.Describe(new KeySpec(_algorithm, _rsaSize, _curve)) : $"made {_output.Title}";

	private string KindHelp => _kind switch
	{
		GenerateKind.KeyPair => "A private key and its public key, nothing else.",
		GenerateKind.Request => "A new key and a signing request (CSR) to send to a CA.",
		GenerateKind.SelfSigned => "A new key and a certificate signed by itself, for testing and internal services.",
		GenerateKind.Authority => "A new key and a self-signed CA certificate that can sign others.",
		_ => "A certificate signed by a CA you load, for a new key or for the key in a signing request.",
	};

	private string NamesHelp => _nameProblems.Count > 0
		? _nameProblems[0]
		: _names.Count == 0 && _usages.HasFlag(UsagePresets.TlsServer) && _kind != GenerateKind.Authority
			? "Browsers ignore the common name: put every host name here."
			: string.Create(CultureInfo.CurrentCulture, $"{_names.Count} names");

	public void Dispose() => DropKey();

	protected override void OnInitialized()
	{
		CertificateSettings settings = Settings;
		_algorithm = settings.KeyAlgorithm;
		_rsaSize = settings.RsaKeySize;
		_curve = settings.Curve;
		_hash = settings.SignatureHash;
		_validityDays = settings.ValidityDays;
		_pfxEncryption = settings.PfxEncryption;
	}

	private bool Has(UsagePresets usage) => _usages.HasFlag(usage);

	private void SetUsage(UsagePresets usage, bool on) => _usages = on ? _usages | usage : _usages & ~usage;

	private void SetSubject(SubjectSpec subject) => _subject = subject;

	private void OnKindChanged(GenerateKind kind)
	{
		_kind = kind;
		_error = null;
	}

	private void OnNamesChanged(string? text)
	{
		_namesText = text ?? "";
		(_names, _nameProblems) = SubjectNames.Parse(_namesText);
	}

	private void OnCaChanged(InputContent content) => _ca = content;

	private void OnRequestChanged(InputContent content) => _request = content;

	private async Task GenerateAsync()
	{
		_error = null;
		if (_nameProblems.Count > 0)
		{
			_error = _nameProblems[0];
			return;
		}

		int? pathLength = null;
		if (_kind == GenerateKind.Authority && _pathLength.Trim().Length > 0)
		{
			if (!int.TryParse(_pathLength.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int length))
			{
				_error = "The path length is a number from 0 to 8, or empty for no limit.";
				return;
			}

			pathLength = length;
		}

		CertificateSpec spec = new()
		{
			Subject = _subject,
			Names = _names,
			Usages = _kind == GenerateKind.Authority ? UsagePresets.CertificateAuthority : _usages,
			ValidityDays = _validityDays,
			Hash = _hash,
			PathLength = pathLength,
		};

		KeySpec keySpec = new(_algorithm, _rsaSize, _curve);
		GenerateKind kind = _kind;
		string keyPassword = _keyPassword;
		InputContent ca = _ca;
		string caPassword = _caPassword;
		InputContent request = _request;
		DateTimeOffset now = Now;
		int expiringDays = Settings.ExpiringDays;
		_busy = true;
		StateHasChanged();
		try
		{
			// RSA 4096 takes a few seconds to generate; it runs off the renderer so the page stays alive meanwhile.
			Generated generated = await Task.Run(() => Run(kind, keySpec, spec, keyPassword, ca, caPassword, request, now, expiringDays));
			DropKey();
			_key = generated.Key;
			_certificate = generated.Certificate;
			_chain = generated.Chain;
			_output = generated.Output;
			_pfxPassword = "";
		}
		catch (CertificateException ex)
		{
			_error = ex.Message;
		}
		catch (CryptographicException)
		{
			_error = "The key or certificate could not be made with these settings.";
		}
		finally
		{
			_busy = false;
		}
	}

	private static Generated Run(
		GenerateKind kind,
		KeySpec keySpec,
		CertificateSpec spec,
		string keyPassword,
		InputContent ca,
		string caPassword,
		InputContent request,
		DateTimeOffset now,
		int expiringDays)
	{
		if (kind == GenerateKind.SignedByCa)
		{
			return SignedByCa(keySpec, spec, keyPassword, ca, caPassword, request, now, expiringDays);
		}

		AsymmetricAlgorithm key = CertificateGenerator.CreateKey(keySpec);
		try
		{
			string title = spec.Subject.CommonName.Trim() is { Length: > 0 } commonName
				? commonName
				: spec.Names.Count > 0 ? spec.Names[0].Value : CryptoChoices.Describe(keySpec).Replace(' ', '-');
			GeneratedOutput output = KeyOutput(key, keyPassword, keySpec, title);
			X509Certificate2? certificate = null;
			switch (kind)
			{
				case GenerateKind.Request:
				{
					byte[] der = CertificateGenerator.CreateSigningRequest(key, spec);
					output = output with { RequestPem = CertificateConverter.RequestPem(der), RequestDer = der };
					break;
				}

				case GenerateKind.SelfSigned or GenerateKind.Authority:
					certificate = CertificateGenerator.CreateSelfSigned(key, spec, now);
					output = WithCertificate(output, certificate, now, expiringDays);
					break;
			}

			return new Generated(output, key, certificate, []);
		}
		catch
		{
			key.Dispose();
			throw;
		}
	}

	private static Generated SignedByCa(
		KeySpec keySpec,
		CertificateSpec spec,
		string keyPassword,
		InputContent ca,
		string caPassword,
		InputContent request,
		DateTimeOffset now,
		int expiringDays)
	{
		if (ca.IsEmpty)
		{
			throw new CertificateException("Load the CA first: its certificate and private key, or its PFX.");
		}

		using MaterialSet caSet = CertificateReader.Read(ca.ToCryptoInput(), caPassword);
		PrivateKeyMaterial caKey = caSet.RequireOnePrivateKey();
		X509Certificate2 issuer = caSet.CertificateFor(caKey) ?? throw new CertificateException("The CA's certificate is missing: paste it with its key.");
		List<X509Certificate2> chain = [X509CertificateLoader.LoadCertificate(issuer.RawData)];

		if (!request.IsEmpty)
		{
			using MaterialSet requestSet = CertificateReader.Read(request.ToCryptoInput());
			RequestMaterial csr = requestSet.Requests.Count == 1
				? requestSet.Requests[0]
				: throw new CertificateException("The request box holds no signing request, or more than one.");
			IssuedCertificate issued = CertificateGenerator.SignRequest(issuer, caKey.Key, csr, spec, now);
			CertificateInfo info = CertificateReader.Describe(issued.Certificate, now, expiringDays);
			GeneratedOutput output = new()
			{
				Title = info.Title,
				Certificate = info,
				CertificatePem = CertificateConverter.CertificatePem(issued.Certificate),
				ChainPem = CertificateConverter.CertificatePem(issuer),
				Notes = issued.Notes,
			};

			return new Generated(output, null, issued.Certificate, chain);
		}

		AsymmetricAlgorithm key = CertificateGenerator.CreateKey(keySpec);
		try
		{
			using IssuedCertificate issued = CertificateGenerator.CreateSigned(issuer, caKey.Key, new PublicKey(key), spec, now);
			X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(issued.Certificate.RawData);
			GeneratedOutput output = WithCertificate(KeyOutput(key, keyPassword, keySpec, "certificate"), certificate, now, expiringDays) with
			{
				ChainPem = CertificateConverter.CertificatePem(issuer),
				Notes = issued.Notes,
			};

			return new Generated(output, key, certificate, chain);
		}
		catch
		{
			key.Dispose();
			throw;
		}
	}

	private static GeneratedOutput KeyOutput(AsymmetricAlgorithm key, string password, KeySpec spec, string title) => new()
	{
		Title = title,
		KeyDescription = CryptoChoices.Describe(spec),
		PrivateKeyPem = password.Length > 0
			? CertificateConverter.EncryptedPrivateKeyPem(key, password)
			: CertificateConverter.PrivateKeyPem(key, PrivateKeyFormat.Pkcs8),
		PrivateKeyEncrypted = password.Length > 0,
		PublicKeyPem = CertificateConverter.PublicKeyPem(new PublicKey(key)),
	};

	private static GeneratedOutput WithCertificate(GeneratedOutput output, X509Certificate2 certificate, DateTimeOffset now, int expiringDays)
	{
		CertificateInfo info = CertificateReader.Describe(certificate, now, expiringDays);
		return output with
		{
			Title = info.Title,
			Certificate = info,
			CertificatePem = CertificateConverter.CertificatePem(certificate),
		};
	}

	private async Task SavePfxAsync()
	{
		if (_certificate is null || _key is null)
		{
			return;
		}

		if (_pfxPassword.Length == 0)
		{
			Notify(NoticeSeverity.Warning, "Type a password for the PFX: a PFX without one leaves the key readable by anyone who has the file.");
			return;
		}

		byte[] pfx;
		try
		{
			pfx = CertificateConverter.ToPfx(_certificate, _key, _chain, new PfxOptions { Password = _pfxPassword, FriendlyName = _friendlyName, Encryption = _pfxEncryption });
		}
		catch (CertificateException ex)
		{
			Notify(NoticeSeverity.Error, ex.Message);
			return;
		}

		await SaveAsync(FileNameFrom(_output?.Title ?? "certificate", ".pfx"), pfx);
	}

	private void DropKey()
	{
		_key?.Dispose();
		_key = null;
		_certificate?.Dispose();
		_certificate = null;
		foreach (X509Certificate2 certificate in _chain)
		{
			certificate.Dispose();
		}

		_chain = [];
	}
}
