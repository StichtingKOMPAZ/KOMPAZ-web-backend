using Kompaz.Application;
using Kompaz.Infrastructure;
using Kompaz.Presentation;
using Kompaz.Presentation.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddPresentationServices(builder.Configuration, builder.Environment);

var app = builder.Build();

await app.InitialiseAndSeedDatabaseAsync();

app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
	app.UseMigrationsEndPoint();
}
else
{
	app.UseHsts();
}

if (app.Configuration.GetValue("Swagger", false))
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapHealthChecks("/health");
app.MapEndpoints();

await app.RunAsync();

namespace Kompaz.Presentation
{
	internal partial class Program
	{
	}
}
