using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using KernSmith.Samples.BlazorWasm;

// Force the StbTrueType assembly to load so its [ModuleInitializer] fires.
// Without this, the trimmer strips the assembly since nothing directly references it.
RuntimeHelpers.RunClassConstructor(
    typeof(KernSmith.Rasterizers.StbTrueType.StbTrueTypeRasterizer).TypeHandle);

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

await builder.Build().RunAsync();
