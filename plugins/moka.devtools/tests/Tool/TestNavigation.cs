using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Moka.DevTools.Tests.Tool;

// Moka.Red's tabs, which ViewTabs draws, inject a NavigationManager, and Mokaterm.Plugin.TestHost 0.3.0 registers none.
internal sealed class TestNavigation : NavigationManager
{
	public TestNavigation() => Initialize("http://localhost/", "http://localhost/");

	public static void Add(IServiceCollection services) => services.AddScoped<NavigationManager, TestNavigation>();
}
