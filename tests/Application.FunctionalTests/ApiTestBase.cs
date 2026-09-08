using Kompaz.Application.Authentication;
using Kompaz.Application.Authentication.Commands.RedeemLoginToken;
using Kompaz.Application.Authentication.Commands.RequestMagicLink;
using Kompaz.Application.Users;
using Kompaz.Application.Users.Commands.InviteUser;
using Kompaz.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Kompaz.Application.FunctionalTests;

/// <summary>
/// Shared plumbing for the end-to-end tests: a fresh application per test plus the sign-in helpers that every
/// authenticated scenario needs.
/// </summary>
internal abstract class ApiTestBase : IDisposable
{
	/// <summary>
	/// The platform administrator planted by the database seeder.
	/// </summary>
	protected const string SeededAdministratorEmail = "admin@kompaz.local";

	private readonly List<HttpClient> _clients = [];
	private CustomWebApplicationFactory _factory = null!;

	protected CapturingEmailSender Emails => _factory.Emails;

	/// <summary>
	/// The clock the application under test runs on. Advance it to reach behaviour that is days away.
	/// </summary>
	protected FakeTimeProvider Clock => _factory.Clock;

	[SetUp]
	public void SetUpApplication()
	{
		_factory = new CustomWebApplicationFactory();
	}

	[TearDown]
	public void TearDownApplication()
	{
		Dispose();
	}

	/// <summary>
	/// Tears down the application and every client it handed out. Safe to call more than once.
	/// </summary>
	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposing)
		{
			return;
		}

		foreach (var client in _clients)
		{
			client.Dispose();
		}

		_clients.Clear();
		_factory?.Dispose();
		_factory = null!;
	}

	/// <summary>
	/// Opens a scope on the application's own services, for a test about what the database does rather than about
	/// what an endpoint returns.
	/// </summary>
	protected IServiceScope CreateScope() => _factory.Services.CreateScope();

	/// <summary>
	/// Creates an anonymous client.
	/// </summary>
	protected HttpClient CreateClient()
	{
		var client = _factory.CreateClient();
		_clients.Add(client);

		return client;
	}

	/// <summary>
	/// Walks the full passwordless flow for an existing user and returns the session it opened, access token and
	/// refresh token included.
	/// </summary>
	protected async Task<AuthenticationResultDto> StartSessionAsync(string email)
	{
		var anonymous = CreateClient();

		var requested = await anonymous.PostAsJsonAsync("/api/auth/magic-link", new RequestMagicLinkCommand(email), JsonOptions.Web);
		requested.EnsureSuccessStatusCode();

		var redeemed = await anonymous.PostAsJsonAsync("/api/auth/tokens", new RedeemLoginTokenCommand(Emails.TokenFor(email)), JsonOptions.Web);
		redeemed.EnsureSuccessStatusCode();

		return await redeemed.Content.ReadFromJsonAsync<AuthenticationResultDto>(JsonOptions.Web)
			?? throw new InvalidOperationException("The API returned no authentication result.");
	}

	/// <summary>
	/// Signs a user in and returns a client carrying their access token.
	/// </summary>
	protected async Task<HttpClient> SignInAsync(string email)
	{
		var session = await StartSessionAsync(email);

		var client = CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(session.TokenType, session.AccessToken);

		return client;
	}

	/// <summary>
	/// Signs in as the seeded platform administrator.
	/// </summary>
	protected Task<HttpClient> SignInAsPlatformAdministratorAsync() => SignInAsync(SeededAdministratorEmail);

	/// <summary>
	/// Invites somebody and returns the created user without accepting the invitation.
	/// </summary>
	protected static async Task<UserDto> InviteAsync(HttpClient client, string email, string name, UserRole role = UserRole.Member, Guid? organizationId = null)
	{
		var response = await client.PostAsJsonAsync("/api/users/invitations", new InviteUserCommand(email, name, role, organizationId), JsonOptions.Web);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<UserDto>(JsonOptions.Web)
			?? throw new InvalidOperationException("The API returned no user.");
	}

	/// <summary>
	/// Invites somebody, accepts the invitation on their behalf, and returns a client carrying their access token.
	/// </summary>
	protected async Task<HttpClient> InviteAndSignInAsync(HttpClient inviter, string email, string name, UserRole role, Guid? organizationId = null)
	{
		await InviteAsync(inviter, email, name, role, organizationId);

		return await SignInAsync(email);
	}
}
