using Microsoft.AspNetCore.Components;
using Moka.Certificates.Components;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.Certificates.Tool;

/// <summary>
/// The Certificates app tool. It needs no session: everything runs here, except the server check, which connects from
/// this machine and so is offered on the desktop only.
/// </summary>
public sealed partial class CertificatesTool : AppToolBase
{
	private const string InspectId = "inspect";
	private const string GenerateId = "generate";
	private const string ConvertId = "convert";
	private const string SignId = "sign";
	private const string EncryptId = "encrypt";
	private const string CheckId = "check";

	private string _current = InspectId;
	private InspectRequest? _inspectRequest;
	private int _requests;

	[Inject]
	private IAppEnvironment Environment { get; set; } = default!;

	private IReadOnlyList<ToolNavEntry> Entries =>
	[
		new(InspectId, "Inspect", CertificateIcons.Inspect),
		new(GenerateId, "Generate", CertificateIcons.Generate),
		new(ConvertId, "Convert", CertificateIcons.Convert),
		new(SignId, "Sign and verify", CertificateIcons.Sign),
		new(EncryptId, "Encrypt", CertificateIcons.Encrypt),
		new(CheckId, "Check a server", CertificateIcons.Server, ServerCheckSection.Unavailable(Environment)),
	];

	private void Show(string id) => _current = id;

	// Another section hands its result over to be read in full.
	private Task InspectAsync(string text)
	{
		_inspectRequest = new InspectRequest(text, ++_requests);
		_current = InspectId;
		return Task.CompletedTask;
	}
}
