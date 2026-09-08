using FluentAssertions;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kompaz.Application.FunctionalTests.Common;

/// <summary>
/// Every failure this API returns should describe itself the same way, whoever produced it: the framework, the
/// exception handler, or a validator.
/// </summary>
[TestFixture]
internal sealed class ProblemDetailsTests : ApiTestBase
{
	[Test]
	public async Task AProblemNamesItsStatusFromOneVocabulary()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var response = await administrator.GetAsync($"/api/users/{Guid.NewGuid()}");
		var problem = await ReadProblemAsync(response);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
		problem.GetProperty("type").GetString().Should().Be("https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.5");
		problem.GetProperty("title").GetString().Should().Be("Not Found");
	}

	[Test]
	public async Task AProblemPointsAtTheRequestWithAUriReference()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();
		string route = $"/api/users/{Guid.NewGuid()}";

		var problem = await ReadProblemAsync(await administrator.GetAsync(route));

		problem.GetProperty("instance").GetString().Should().Be(route);
		problem.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
	}

	[Test]
	public async Task AValidationProblemKeepsItsOwnTitle()
	{
		var administrator = await SignInAsPlatformAdministratorAsync();

		var response = await administrator.PostAsJsonAsync(
			"/api/users/invitations", new { email = "geen-adres", name = "Iemand" }, JsonOptions.Web);
		var problem = await ReadProblemAsync(response);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		problem.GetProperty("type").GetString().Should().Be("https://datatracker.ietf.org/doc/html/rfc9110#section-15.5.1");

		// The status name would be a worse title than the one the validation problem came with.
		problem.GetProperty("title").GetString().Should().NotBe("Bad Request");
		problem.GetProperty("errors").EnumerateObject().Should().NotBeEmpty();
	}

	private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response) =>
		await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions.Web);
}
