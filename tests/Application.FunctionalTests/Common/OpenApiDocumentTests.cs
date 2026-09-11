using FluentAssertions;
using NUnit.Framework;
using System.Net.Http.Json;
using System.Text.Json;

namespace Kompaz.Application.FunctionalTests.Common;

/// <summary>
/// The published contract has to describe the API that actually runs. Nothing else here would notice if it stopped:
/// the document is generated from a second set of serializer options and from metadata a handler's return type says
/// nothing about, so it can drift silently and only a generated client finds out.
/// </summary>
[TestFixture]
internal sealed class OpenApiDocumentTests : IDisposable
{
	private CustomWebApplicationFactory _factory = null!;
	private JsonElement _document;

	[OneTimeSetUp]
	public async Task FetchDocumentAsync()
	{
		_factory = new CustomWebApplicationFactory(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["Swagger"] = "true",
		});

		using var client = _factory.CreateClient();

		_document = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json", JsonOptions.Web);
	}

	[OneTimeTearDown]
	public void Dispose()
	{
		_factory?.Dispose();
		_factory = null!;
	}

	/// <summary>
	/// The runtime serializes enums by name. Swashbuckle reads a different options object, so if the two are not
	/// configured together this reverts to <c>integer</c> and no generated client can read a user.
	/// </summary>
	[TestCase("UserRole", "Member", "Administrator", "PlatformAdministrator")]
	[TestCase("UserStatus", "Invited", "Active")]
	public void EnumsArePublishedByName(string schemaName, params string[] names)
	{
		var schema = Schema(schemaName);

		schema.GetProperty("type").GetString().Should().Be("string");
		schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString())
			.Should().BeEquivalentTo(names);
	}

	/// <summary>
	/// A non-nullable string is required, and says so. This is what breaks when
	/// <c>SupportNonNullableReferenceTypes()</c> is missing: everything reads as optional and nullable.
	/// </summary>
	[TestCase("RequestMagicLinkCommand", "email")]
	[TestCase("RedeemLoginTokenCommand", "token")]
	[TestCase("UpdateOwnProfileCommand", "name")]
	public void ANonNullableStringIsRequiredAndNotNullable(string schemaName, string property)
	{
		var schema = Schema(schemaName);

		schema.GetProperty("required").EnumerateArray().Select(value => value.GetString())
			.Should().Contain(property);
		schema.GetProperty("properties").GetProperty(property)
			.TryGetProperty("nullable", out _).Should().BeFalse();
	}

	[Test]
	public void ANullablePropertyStaysOptional()
	{
		var invitedUtc = Schema("UserDto").GetProperty("properties").GetProperty("invitedUtc");

		invitedUtc.GetProperty("nullable").GetBoolean().Should().BeTrue();
	}

	/// <summary>
	/// The sign-in endpoints are reached precisely because the caller has no token, so a padlock on them is wrong.
	/// </summary>
	[TestCase("/api/auth/magic-link", "post")]
	[TestCase("/api/auth/tokens", "post")]
	[TestCase("/api/auth/tokens/refresh", "post")]
	[TestCase("/api/auth/tokens/revoke", "post")]
	public void TheAuthenticationEntryPointsAskForNoToken(string path, string method)
	{
		Operation(path, method).TryGetProperty("security", out _).Should().BeFalse();
	}

	[TestCase("/api/auth/me", "get")]
	[TestCase("/api/users", "get")]
	[TestCase("/api/organizations", "post")]
	public void EverythingElseAsksForTheBearerToken(string path, string method)
	{
		var schemes = Operation(path, method).GetProperty("security")
			.EnumerateArray()
			.SelectMany(requirement => requirement.EnumerateObject().Select(scheme => scheme.Name));

		schemes.Should().Contain("Bearer");
	}

	/// <summary>
	/// A handler's return type describes success only; every failure arrives as an exception, so without the filter
	/// the document claims these can only succeed.
	/// </summary>
	[TestCase("/api/users/invitations", "post", "400", "401", "403", "409", "429")]
	[TestCase("/api/users/{id}", "delete", "401", "403", "404", "409", "429")]
	[TestCase("/api/users/{id}", "get", "401", "403", "404", "429")]
	[TestCase("/api/auth/tokens", "post", "400", "401", "429")]
	public void FailuresAreDocumented(string path, string method, params string[] expected)
	{
		var responses = Operation(path, method).GetProperty("responses")
			.EnumerateObject()
			.Select(response => response.Name);

		responses.Should().Contain(expected);
	}

	/// <summary>
	/// The logo upload is the one endpoint that takes a file rather than JSON. A generated client that was told
	/// otherwise would send a JSON body to a handler that reads a form and get a 400 it cannot explain.
	/// </summary>
	[Test]
	public void TheLogoUploadIsDescribedAsAFileUpload()
	{
		var body = Operation("/api/organizations/{id}/logo", "put")
			.GetProperty("requestBody").GetProperty("content");

		var schema = body.GetProperty("multipart/form-data").GetProperty("schema");

		schema.GetProperty("properties").GetProperty("logo").GetProperty("format").GetString().Should().Be("binary");
	}

	[Test]
	public void AFailureIsDescribedAsProblemDetails()
	{
		var conflict = Operation("/api/users/invitations", "post")
			.GetProperty("responses").GetProperty("409")
			.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");

		conflict.GetProperty("$ref").GetString().Should().EndWith("/ProblemDetails");
	}

	private JsonElement Schema(string name) =>
		_document.GetProperty("components").GetProperty("schemas").GetProperty(name);

	private JsonElement Operation(string path, string method) =>
		_document.GetProperty("paths").GetProperty(path).GetProperty(method);
}
