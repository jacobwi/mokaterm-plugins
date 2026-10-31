using Moka.Network.Components;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.Network.Session;

/// <summary>
/// The Network Toolkit as a session tool for SSH: resolve, listening ports, reach and capture, each run on the
/// connected server through the session's command feature and shown from that side of the link. It needs the session
/// connected, so the shell hides the button until it is.
/// </summary>
public sealed partial class NetworkSessionTool : SessionViewBase
{
	private const string ResolveId = "resolve";

	private const string PortsId = "ports";

	private const string ReachId = "reach";

	private const string CaptureId = "capture";

	private static readonly IReadOnlyList<ToolNavEntry> Entries =
	[
		new(ResolveId, "Resolve", NetworkIcons.Dns),
		new(PortsId, "Listening ports", NetworkIcons.Port),
		new(ReachId, "Reach", NetworkIcons.Trace),
		new(CaptureId, "Capture", NetworkIcons.Capture),
	];

	private string _current = ResolveId;

	private void Show(string id) => _current = id;
}
