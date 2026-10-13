using Microsoft.Extensions.DependencyInjection;
using Mokaterm.Abstractions.Modules;

namespace Mokaterm.Gallery.LoadCheck;

/// <summary>Holds a module id the way a built-in module or another plugin would, so a plugin that takes it is refused here too.</summary>
internal sealed class TakenModule(string id) : IMokatermModule
{
	public ModuleInfo Info { get; } = new(id, id, "Already registered by Mokaterm or another listed plugin.", "0.0.0");

	public void ConfigureServices(IServiceCollection services)
	{
	}
}
