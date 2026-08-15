using MudBlazor.Services;
using System.Text.Json.Serialization;
using Trumax.Examples.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    // جلوگیری از self referencing loop
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;

    // حساس نبودن به حروف بزرگ و کوچک
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = false;
});
builder.Services.AddRazorPages();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Mudblazor
builder.Services.AddMudServices();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseRouting();  //
app.UseAntiforgery();

app.MapRazorPages();
app.MapControllers(); 
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Trumax.Examples.Client._Imports).Assembly);

app.Run();
