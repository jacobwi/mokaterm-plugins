using Microsoft.Extensions.DependencyInjection;
using Moka.DevTools;
using Moka.DevTools.Components;
using Moka.DevTools.Tool;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Contributions;

[assembly: MokatermPlugin(typeof(DevToolsModule))]

namespace Moka.DevTools;

/// <summary>What the Developer Utilities plugin adds: one app tool. It needs no session and no settings.</summary>
public sealed class DevToolsModule : IMokatermModule
{
	/// <summary>The app tool's id, which names its palette command and any shortcut bound to it.</summary>
	public const string ToolId = "devtools";

	public ModuleInfo Info { get; } = new(
		"moka.devtools",
		"Developer Utilities",
		"Decode and verify JWTs, encode and decode, hash text and files, make and read UUIDs and ULIDs, convert times, format and convert JSON and YAML, and test JavaScript regexes.",
		DisplayVersion.Of(typeof(DevToolsModule).Assembly));

	public void ConfigureServices(IServiceCollection services) =>
		services.AddAppTool(new AppToolDescriptor
		{
			Id = ToolId,
			Title = "Developer Utilities",
			Icon = DevToolIcons.Tool,
			Description = "JWT decoder, encoders, hashes, UUIDs and ULIDs, time conversion, JSON and YAML, regex tester",
			Component = typeof(DevToolsTool),
			Size = SessionToolSize.Wide,
		});
}
