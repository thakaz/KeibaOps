using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using KeibaOps;
using KeibaOps.Services;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddMudServices();
builder.Services.AddSingleton<レース運用サービス>();
builder.Services.AddSingleton<オッズ市場サービス>();
builder.Services.AddSingleton<財布サービス>();
builder.Services.AddSingleton<馬市場サービス>();
builder.Services.AddSingleton<繁殖サービス>();
builder.Services.AddSingleton<馬名生成サービス>();
builder.Services.AddSingleton<馬個体管理サービス>();
builder.Services.AddSingleton<実況サービス>();

await builder.Build().RunAsync();
