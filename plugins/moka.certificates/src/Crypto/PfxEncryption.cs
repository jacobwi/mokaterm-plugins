namespace Moka.Certificates.Crypto;

/// <summary>
/// How a new PFX is encrypted: AES-256 with SHA-256, which Windows reads from Windows 10 1709 and Server 2019 on, or
/// 3DES with SHA-1, which older Windows and old Java need.
/// </summary>
public enum PfxEncryption
{
	Aes256,
	Legacy3Des,
}
