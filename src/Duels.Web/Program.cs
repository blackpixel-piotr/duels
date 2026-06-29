using Duels.Infrastructure.DI;
using Duels.Web;
using Duels.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddDuelsInfrastructure();
builder.Services.AddSingleton<GameService>();

await builder.Build().RunAsync();
