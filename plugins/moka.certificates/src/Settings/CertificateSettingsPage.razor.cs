using Moka.Certificates.Crypto;
using Mokaterm.UI.Common.Components;

namespace Moka.Certificates.Settings;

/// <summary>The plugin's settings page, in the Plugins group, built from the shared settings fields.</summary>
public sealed partial class CertificateSettingsPage : SettingsSectionBase<CertificateSettings>
{
	private static readonly IReadOnlyList<EnumOption<KeyAlgorithm>> Algorithms = [new(KeyAlgorithm.Rsa, "RSA"), new(KeyAlgorithm.Ecdsa, "ECDSA")];

	private static readonly IReadOnlyList<EnumOption<RsaKeySize>> RsaSizes = [new(RsaKeySize.Bits2048, "2048"), new(RsaKeySize.Bits3072, "3072"), new(RsaKeySize.Bits4096, "4096")];

	private static readonly IReadOnlyList<EnumOption<EllipticCurve>> Curves = [new(EllipticCurve.P256, "P-256"), new(EllipticCurve.P384, "P-384"), new(EllipticCurve.P521, "P-521")];

	private static readonly IReadOnlyList<EnumOption<SignatureHash>> Hashes = [new(SignatureHash.Sha256, "SHA-256"), new(SignatureHash.Sha384, "SHA-384"), new(SignatureHash.Sha512, "SHA-512")];

	private static readonly IReadOnlyList<EnumOption<PfxEncryption>> PfxChoices = [new(PfxEncryption.Aes256, "AES-256"), new(PfxEncryption.Legacy3Des, "3DES (legacy)")];

	private Task SaveAsync(Func<CertificateSettings, CertificateSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
