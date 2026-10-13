using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Abstractions.Terminal;
using Mokaterm.Core.Extensions;
using Mokaterm.Core.Plugins;
using Mokaterm.UI.Common.Contributions;

namespace Mokaterm.Gallery.LoadCheck;

/// <summary>
/// Puts one package through the app's plugin code for every host it names: read and checked by the plugins manager,
/// loaded from an operator's folder (what the web host does), and on the desktop also installed and trusted through the
/// manager and loaded at the next start (what the Plugins page does). Built-in module ids and those of other listed
/// plugins are registered first, so a clash is refused the way the app refuses it.
/// </summary>
internal sealed class LoadCheckRun(LoadCheckArguments arguments)
{
	private const string Operator = "operator folder";
	private const string Install = "installed and trusted";

	private readonly List<string> _problems = [];
	private readonly List<string> _warnings = [];
	private readonly List<HostReport> _hosts = [];
	private string? _moduleId;

	public static string HostSdk { get; } = string.Create(
		CultureInfo.InvariantCulture,
		$"{PluginCompatibility.HostSdk.Major}.{PluginCompatibility.HostSdk.Minor}");

	public async Task<LoadCheckReport> RunAsync()
	{
		if (Directory.Exists(arguments.WorkDirectory))
		{
			Directory.Delete(arguments.WorkDirectory, recursive: true);
		}

		Directory.CreateDirectory(arguments.WorkDirectory);
		IReadOnlyList<string>? hosts = await ReadHostsAsync();
		if (hosts is not null)
		{
			bool desktop = hosts.Count == 0 || hosts.Contains(PluginHosts.Desktop);
			bool web = hosts.Count == 0 || hosts.Contains(PluginHosts.Web);
			if (web)
			{
				await LoadFromOperatorFolderAsync(HostKind.Web);
			}

			if (desktop)
			{
				await LoadFromOperatorFolderAsync(HostKind.Desktop);
				await InstallOnDesktopAsync();
			}
		}

		return new LoadCheckReport(HostSdk, _problems.Count == 0, _moduleId, _hosts, _problems, _warnings);
	}

	private async Task<IReadOnlyList<string>?> ReadHostsAsync()
	{
		(IPluginPackage? package, string? problem) = await ReadAsync(HostKind.Web, "first-read");
		if (package is null)
		{
			_problems.Add(problem!);
			return null;
		}

		using (package)
		{
			if (package.Id != arguments.Id)
			{
				_problems.Add($"The package is {package.Id}, but it is listed as {arguments.Id}.");
				return null;
			}

			return [.. package.Hosts];
		}
	}

	private async Task LoadFromOperatorFolderAsync(HostKind host)
	{
		string name = PluginHosts.For(host);
		(IPluginPackage? package, string? problem) = await ReadAsync(host, $"{name}-read");
		using (package)
		{
			problem ??= package!.Problem;
		}

		if (problem is not null)
		{
			Fail(name, Operator, problem);
			return;
		}

		// The manager accepted every path in the package, so extracting it as it is cannot leave the folder.
		string folder = Path.Combine(arguments.WorkDirectory, $"{name}-plugins");
		ZipFile.ExtractToDirectory(arguments.PackagePath, Path.Combine(folder, arguments.Id));
		PluginCatalog catalog = PluginCatalog.Open(new PluginCatalogOptions { Directory = folder, Host = host });
		catalog.ConfirmStarted();
		Register(name, Operator, catalog, folder);
	}

	private async Task InstallOnDesktopAsync()
	{
		string name = PluginHosts.Desktop;
		string folder = Path.Combine(arguments.WorkDirectory, "desktop-installed");
		PluginCatalogOptions options = new() { Directory = folder, Host = HostKind.Desktop, UserManaged = true };
		PluginCatalog first = PluginCatalog.Open(options);
		first.ConfirmStarted();
		ServiceCollection services = new();
		new MokatermBuilder(services).AddPlugins(first);
		await using (ServiceProvider provider = services.BuildServiceProvider())
		{
			IPluginManager manager = provider.GetRequiredService<IPluginManager>();
			IPluginPackage package;
			await using (FileStream file = File.OpenRead(arguments.PackagePath))
			{
				try
				{
					package = await manager.ReadPackageAsync(file, origin: null, CancellationToken.None);
				}
				catch (InvalidDataException ex)
				{
					Fail(name, Install, ex.Message);
					return;
				}
			}

			using (package)
			{
				try
				{
					await manager.InstallAsync(package, enable: true, CancellationToken.None);
				}
				catch (InvalidOperationException ex)
				{
					Fail(name, Install, ex.Message);
					return;
				}
			}
		}

		// The next start applies the pending install and checks the folder against the hash the install trusted.
		PluginCatalog next = PluginCatalog.Open(options);
		next.ConfirmStarted();
		Register(name, Install, next, folder);
	}

	private async Task<(IPluginPackage? Package, string? Problem)> ReadAsync(HostKind host, string folderName)
	{
		string folder = Path.Combine(arguments.WorkDirectory, folderName);
		Directory.CreateDirectory(folder);
		PluginCatalog catalog = PluginCatalog.Open(new PluginCatalogOptions { Directory = folder, Host = host });
		catalog.ConfirmStarted();
		ServiceCollection services = new();
		new MokatermBuilder(services).AddPlugins(catalog);
		await using ServiceProvider provider = services.BuildServiceProvider();
		IPluginManager manager = provider.GetRequiredService<IPluginManager>();
		await using FileStream file = File.OpenRead(arguments.PackagePath);
		try
		{
			return (await manager.ReadPackageAsync(file, origin: null, CancellationToken.None), null);
		}
		catch (InvalidDataException ex)
		{
			return (null, $"The app refuses the package: {ex.Message}");
		}
	}

	[SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Whatever the plugin's own module throws while registering is the finding, and the app would not survive it either.")]
	private void Register(string host, string mode, PluginCatalog catalog, string folder)
	{
		LoadedPlugin? loaded = catalog.Loaded.FirstOrDefault(plugin => plugin.Info.Id == arguments.Id);
		ServiceCollection services = new();
		MokatermBuilder builder = new(services);
		foreach (string taken in arguments.TakenModules)
		{
			builder.AddModule(new TakenModule(taken));
		}

		int before = services.Count;
		string? moduleId;
		try
		{
			moduleId = loaded?.Module.Info.Id;
			builder.AddPlugins(catalog);
		}
		catch (Exception ex)
		{
			Fail(host, mode, $"The plugin's module threw {ex.GetType().Name} while registering its services: {ex.Message}");
			return;
		}

		PluginRecord? record = catalog.Records.FirstOrDefault(plugin => plugin.Id == arguments.Id);
		if (record is null)
		{
			Fail(host, mode, "The app did not find the plugin in its folder.");
			return;
		}

		List<string> registrations = [.. services.Skip(before).Select(Describe).OfType<string>()];
		_hosts.Add(new HostReport(host, mode, record.Status.ToString(), record.Problem, registrations));
		if (record.Status != PluginStatus.Enabled)
		{
			_problems.Add($"{host}, {mode}: the app does not turn the plugin on ({record.Status}): {record.Problem ?? "no reason given"}");
			return;
		}

		_moduleId ??= moduleId;
		string pluginFolder = Path.Combine(folder, arguments.Id);
		CheckThemes(pluginFolder);
		if (loaded is not null)
		{
			CheckShadowedFiles(Path.Combine(pluginFolder, "lib"), loaded.Info.AssemblyName);
		}
	}

	// The app reads themes after start and drops one that does not convert without a word, so a broken one is caught here.
	private void CheckThemes(string pluginFolder)
	{
		string themes = Path.Combine(pluginFolder, "themes");
		if (!Directory.Exists(themes))
		{
			return;
		}

		ServiceCollection services = new();
		services.AddMokatermCore();
		using ServiceProvider provider = services.BuildServiceProvider();
		ITerminalThemeConverter converter = provider.GetRequiredService<ITerminalThemeConverter>();
		foreach (string file in Directory.EnumerateFiles(themes).Order(StringComparer.Ordinal))
		{
			TerminalThemeImport import = converter.Import(File.ReadAllText(file), Path.GetFileName(file));
			string problem = $"themes/{Path.GetFileName(file)} is not a terminal theme the app can read: {import.Error}";
			if (!import.Succeeded && !_problems.Contains(problem))
			{
				_problems.Add(problem);
			}
		}
	}

	// An assembly the host ships always comes from the host, whatever the plugin carries, so a copy in lib never runs.
	private void CheckShadowedFiles(string lib, string assemblyName)
	{
		if (!Directory.Exists(lib))
		{
			return;
		}

		HashSet<string> host = HostAssemblies();
		foreach (string file in Directory.EnumerateFiles(lib, "*.dll", SearchOption.AllDirectories))
		{
			string name = Path.GetFileNameWithoutExtension(file);
			if (name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase) || name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string warning = $"lib/{Path.GetRelativePath(lib, file).Replace('\\', '/')} is an assembly Mokaterm itself loads, so the plugin gets Mokaterm's copy, whatever version it carries.";
			if (host.Contains(name) && !_warnings.Contains(warning))
			{
				_warnings.Add(warning);
			}
		}
	}

	// The same list PluginLoadContext reads: every assembly the host was published with, plus whatever it already loaded.
	private static HashSet<string> HostAssemblies()
	{
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string list)
		{
			foreach (string path in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
			{
				names.Add(Path.GetFileNameWithoutExtension(path));
			}
		}

		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			if (System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly) == System.Runtime.Loader.AssemblyLoadContext.Default
				&& assembly.GetName().Name is { } name)
			{
				names.Add(name);
			}
		}

		return names;
	}

	private static string? Describe(ServiceDescriptor descriptor)
	{
		if (descriptor.IsKeyedService)
		{
			return null;
		}

		if (descriptor.ServiceType == typeof(IProtocolProvider))
		{
			return $"protocol provider {descriptor.ImplementationType?.FullName ?? "(factory)"}";
		}

		return descriptor.ImplementationInstance switch
		{
			ModuleInfo module => $"module {module.Id} {module.Version}",
			ProtocolUiDescriptor protocol => $"protocol ui {protocol.ProtocolId}",
			SessionToolDescriptor tool => $"session tool {tool.Key} for {tool.ProtocolId}",
			AppToolDescriptor tool => $"app tool {tool.Id}",
			SystemPageDescriptor page => $"system page {page.Id}",
			DockerPageDescriptor page => $"docker page {page.Id}",
			SettingsPageDescriptor page => $"settings page {page.Id} ({SettingsSearchIndex.For(page.Component).Count} searchable rows)",
			SettingsSectionType section => $"settings section {section.Key}",
			_ => null,
		};
	}

	private void Fail(string host, string mode, string problem)
	{
		_hosts.Add(new HostReport(host, mode, "Refused", problem, []));
		_problems.Add($"{host}, {mode}: {problem}");
	}
}
