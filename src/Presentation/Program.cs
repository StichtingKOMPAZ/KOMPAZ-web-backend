using Kompaz.Application;
using Kompaz.Infrastructure;
using Kompaz.Presentation;
using Kompaz.Presentation.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration, builder.Environment);
builder.Services.AddPresentationServices(builder.Configuration, builder.Environment);

var app = builder.Build();

await app.InitialiseAndSeedDatabaseAsync();

// First: everything below reads the scheme or the caller's address, and behind a proxy both are wrong until this
// has run.
app.UseConfiguredForwardedHeaders();

// Early, so it also covers the middleware beneath it rather than only the endpoints.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
	app.UseMigrationsEndPoint();
}
else
{
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();

// Before the rate limiter, so a 429 still carries the headers a browser needs to read it. The other way round, a
// throttled cross-origin caller sees an opaque network error instead of the status.
app.UseCors();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

if (app.Configuration.GetValue("Swagger", false))
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

// Exempt from the rate limiter on purpose: an orchestrator polls this, and an instance under load answering its own
// probe with 429 would be restarted for being busy.
app.MapHealthChecks("/health").DisableRateLimiting();

app.MapEndpoints();

await app.RunAsync();

namespace Kompaz.Presentation
{
	internal partial class Program
	{
	}
}
