using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Moka.DevTools.Components;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.DevTools.Tool;

/// <summary>
/// The Developer Utilities app tool. It needs no session: everything runs on this machine (the server, on the web
/// host), except the regex tester, which runs in the page because terminal triggers do.
/// </summary>
public sealed partial class DevToolsTool : AppToolBase, IAsyncDisposable
{
	private const string JwtId = "jwt";
	private const string EncodeId = "encode";
	private const string HashId = "hash";
	private const string IdsId = "ids";
	private const string TimeId = "time";
	private const string DataId = "data";
	private const string RegexId = "regex";

	private static readonly IReadOnlyList<ToolNavEntry> Entries =
	[
		new(JwtId, "JWT", DevToolIcons.Jwt),
		new(EncodeId, "Encode", DevToolIcons.Encode),
		new(HashId, "Hashes", DevToolIcons.Hash),
		new(IdsId, "IDs", DevToolIcons.Ids),
		new(TimeId, "Time", DevToolIcons.Time),
		new(DataId, "JSON and YAML", DevToolIcons.Data),
		new(RegexId, "Regex", DevToolIcons.Regex),
	];

	private ElementReference _root;
	private DevToolsScript _script = default!;
	private string _current = JwtId;

	[Inject]
	private IJSRuntime JS { get; set; } = default!;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	[Inject]
	private IAppEnvironment Environment { get; set; } = default!;

	public ValueTask DisposeAsync() => _script.DisposeAsync();

	protected override void OnInitialized() => _script = new DevToolsScript(JS, Interaction);

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (firstRender)
		{
			await _script.AttachAsync(_root, Environment.Kind == HostKind.Web);
		}
	}

	private void Show(string id) => _current = id;
}
