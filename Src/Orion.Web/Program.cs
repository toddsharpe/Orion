using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

// The UI lives in wwwroot/index.html and js/explorer.js; this host only boots the .NET runtime so the [JSInvokable] interop statics are callable from JavaScript, and registers no root Blazor component on purpose.
WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
await builder.Build().RunAsync();
