using System.Globalization;
using System.Security.Cryptography;
using Moka.DevTools.Components;
using Moka.DevTools.Dates;
using Moka.DevTools.Hashing;
using Moka.DevTools.Jwt;
using Moka.DevTools.Text;
using Moka.Red.Core.Enums;
using Mokaterm.UI.Common.Components;

namespace Moka.DevTools.Tool;

/// <summary>
/// Reads a JWT: the header, the claims (hidden until shown), the times as dates, the signature, and a check of the
/// signature against a secret, a public key, a certificate or a JWK. The token and the secret are cleared when the tool
/// closes.
/// </summary>
public sealed partial class JwtSection : DevToolViewBase, IDisposable
{
	private static readonly IReadOnlyList<EnumOption<KeyFormat>> SecretFormats =
	[
		new(KeyFormat.Text, "Text"),
		new(KeyFormat.Base64, "Base64"),
		new(KeyFormat.Hex, "Hex"),
	];

	private string _token = "";
	private string _secret = "";
	private string _publicKey = "";
	private KeyFormat _secretFormat = KeyFormat.Text;
	private JwtToken? _jwt;
	private string? _error;
	private JwtVerification? _verification;
	private bool _verifying;
	private int _reads;

	private JwtFamily Family => JwtVerifier.FamilyOf(_jwt?.Algorithm);

	private string Summary => _jwt switch
	{
		null => _error is null ? "decode and verify" : "not read",
		{ } jwt => $"{jwt.Algorithm ?? "no alg"}, {Validity.Text}",
	};

	private (string Text, MokaColor Color) Validity
	{
		get
		{
			JwtTime? expires = _jwt?.Times.FirstOrDefault(time => time.Claim == "exp");
			JwtTime? notBefore = _jwt?.Times.FirstOrDefault(time => time.Claim == "nbf");
			DateTimeOffset now = Now;
			return (expires?.When, notBefore?.When) switch
			{
				({ } exp, _) when exp <= now => ("expired", MokaColor.Error),
				(_, { } nbf) when nbf > now => ("not yet valid", MokaColor.Warning),
				({ }, _) => ("valid now", MokaColor.Success),
				_ => ("no expiry", MokaColor.Warning),
			};
		}
	}

	private IReadOnlyList<Fact> TimeFacts
	{
		get
		{
			if (_jwt is not { } jwt)
			{
				return [];
			}

			DateTimeOffset now = Now;
			List<Fact> facts = [];
			foreach (JwtTime time in jwt.Times)
			{
				string label = time.Claim switch
				{
					"exp" => "exp, expires",
					"nbf" => "nbf, not before",
					"iat" => "iat, issued",
					_ => "auth_time, signed in",
				};

				facts.Add(time.When is { } when
					? new Fact(label, TimeFormats.IsoUtc(when), Note: $"{Relative(time.Claim, when, now)}. Local {TimeFormats.InZone(when, TimeZoneInfo.Local)}")
					: new Fact(label, time.Problem ?? "", Mono: false));
			}

			return facts;
		}
	}

	private IReadOnlyList<Fact> SignatureFacts => _jwt is not { } jwt
		? []
		:
		[
			new Fact("Algorithm", jwt.Algorithm ?? "none named", Mono: false),
			new Fact("Length", jwt.Signature.Length == 1 ? "1 byte" : string.Create(CultureInfo.CurrentCulture, $"{jwt.Signature.Length:N0} bytes"), Mono: false),
			new Fact("Base64url", jwt.SignatureText.Length > 0 ? jwt.SignatureText : "(empty)", Copyable: jwt.SignatureText.Length > 0),
			new Fact("Hex", jwt.Signature.Length > 0 ? HexText.Format(jwt.Signature) : "(empty)", Copyable: jwt.Signature.Length > 0),
		];

	private string NotCheckedText => Family switch
	{
		JwtFamily.None => "alg is none, so there is no signature to check.",
		JwtFamily.Hmac => "Type the secret and press Verify.",
		JwtFamily.PublicKey => "Paste the key and press Verify.",
		_ => _jwt?.Algorithm is { } algorithm ? $"{algorithm} signatures are not checked here." : "The header names no algorithm.",
	};

	public void Dispose()
	{
		// Strings cannot be wiped, but nothing should keep them reachable once the tool is closed.
		_token = "";
		_secret = "";
		_publicKey = "";
		_jwt = null;
		_verification = null;
	}

	private static string Relative(string claim, DateTimeOffset when, DateTimeOffset now)
	{
		string span = RelativeTime.Span(when - now);
		bool past = when <= now;
		return claim switch
		{
			"exp" => past ? $"Expired {span} ago" : $"Expires in {span}",
			"nbf" => past ? $"Valid since {span} ago" : $"Not valid for another {span}",
			"iat" => past ? $"Issued {span} ago" : $"Issued {span} in the future",
			_ => past ? $"Signed in {span} ago" : $"{span} in the future",
		};
	}

	private static string VerdictText(JwtVerdict verdict) => verdict switch
	{
		JwtVerdict.Verified => "verified",
		JwtVerdict.Failed => "failed",
		_ => "not checked",
	};

	private static ResultTone VerdictTone(JwtVerdict verdict) => verdict switch
	{
		JwtVerdict.Verified => ResultTone.Good,
		JwtVerdict.Failed => ResultTone.Bad,
		_ => ResultTone.Warning,
	};

	private async Task OnTokenChangedAsync(string? value)
	{
		_token = value ?? "";
		_verification = null;
		int read = ++_reads;
		string token = _token;
		if (token.Trim().Length == 0)
		{
			_jwt = null;
			_error = null;
			return;
		}

		(JwtToken? jwt, string? error) = token.Length >= TextLimits.BackgroundChars
			? await Task.Run(() => Decode(token))
			: Decode(token);
		if (read == _reads)
		{
			_jwt = jwt;
			_error = error;
		}
	}

	private static (JwtToken? Token, string? Error) Decode(string token)
	{
		try
		{
			return (JwtDecoder.Decode(token), null);
		}
		catch (JwtFormatException ex)
		{
			return (null, ex.Message);
		}
	}

	private Task ClearAsync() => OnTokenChangedAsync("");

	private void OnSecretChanged(string? value)
	{
		_secret = value ?? "";
		_verification = null;
	}

	private void OnSecretFormatChanged(KeyFormat format)
	{
		_secretFormat = format;
		_verification = null;
	}

	private void OnPublicKeyChanged(string? value)
	{
		_publicKey = value ?? "";
		_verification = null;
	}

	private async Task VerifyAsync()
	{
		if (_jwt is not { } jwt)
		{
			return;
		}

		string secret = _secret;
		KeyFormat format = _secretFormat;
		string publicKey = _publicKey;
		_verifying = true;
		try
		{
			// RSA and EC checks are quick, but a 16384-bit key or a long certificate chain is not, so none run on the renderer.
			_verification = await Task.Run(() => Family == JwtFamily.Hmac ? CheckSecret(jwt, secret, format) : CheckKey(jwt, publicKey));
		}
		finally
		{
			_verifying = false;
		}
	}

	private static JwtVerification CheckSecret(JwtToken jwt, string secret, KeyFormat format)
	{
		if (secret.Length == 0)
		{
			return JwtVerification.NotChecked("Type the secret to check with.");
		}

		byte[] key;
		try
		{
			key = SecretKey.Read(secret, format);
		}
		catch (FormatException ex)
		{
			return JwtVerification.NotChecked(ex.Message);
		}

		try
		{
			return JwtVerifier.VerifyHmac(jwt, key);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}

	private static JwtVerification CheckKey(JwtToken jwt, string text)
	{
		try
		{
			using JwtPublicKey key = JwtKeyReader.Read(text, jwt.KeyId);
			return JwtVerifier.VerifyPublicKey(jwt, key);
		}
		catch (JwtFormatException ex)
		{
			return JwtVerification.NotChecked(ex.Message);
		}
		catch (Exception ex) when (ex is CryptographicException or FormatException)
		{
			return JwtVerification.NotChecked("The key could not be read.");
		}
	}
}
