using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using KernSmith;
using KernSmith.Rasterizer;
using KernSmith.Rasterizers.Native;
using KernSmith.Rasterizers.StbTrueType;
using KernSmith.Samples.BlazorWasm;

// Register the backends explicitly: a published (trimmed) Blazor app drops any backend assembly
// nothing references, so the factory's reflection auto-discovery finds nothing there.
RasterizerFactory.Register(RasterizerBackend.StbTrueType, () => new StbTrueTypeRasterizer());
RasterizerFactory.Register(RasterizerBackend.Native, () => new NativeRasterizer());

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().RunAsync();
