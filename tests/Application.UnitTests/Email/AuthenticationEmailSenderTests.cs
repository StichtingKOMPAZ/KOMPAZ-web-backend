using FluentAssertions;
using Kompaz.Infrastructure.Authentication;
using Kompaz.Infrastructure.Email;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Kompaz.Application.UnitTests.Email;

/// <summary>
/// What the sign-in email actually says. The wording is an acceptance criterion, not an implementation detail, and
/// the functional tests cannot see it: they replace the whole sender with one that only keeps the secret.
/// </summary>
[TestFixture]
internal class AuthenticationEmailSenderTests
{
	[Test]
	public async Task TheSignInEmailUsesTheSubjectTheTicketSpecifies()
	{
		var (sender, dispatcher) = CreateSender();

		await sender.SendMagicLinkAsync("iemand@kompaz.local", "Iemand Anders", "the-secret");

		dispatcher.Sent!.Subject.Should().Be("Je login-link");
	}

	[Test]
	public async Task TheSignInEmailIsDutchAndNamesTheLinkAndItsLifetime()
	{
		var (sender, dispatcher) = CreateSender();

		await sender.SendMagicLinkAsync("iemand@kompaz.local", "Iemand Anders", "the-secret");

		string body = dispatcher.Sent!.Body;
		body.Should().StartWith("Hallo Iemand Anders,");
		body.Should().Contain("https://app.kompaz.test/auth/callback?token=the-secret");
		body.Should().Contain("30 minuten");
	}

	/// <summary>
	/// The lifetime in the wording is read from configuration, so the promise the email makes cannot drift away
	/// from the deadline the redemption endpoint enforces.
	/// </summary>
	[Test]
	public async Task TheStatedLifetimeFollowsTheConfiguredOne()
	{
		var (sender, dispatcher) = CreateSender(magicLinkLifetimeMinutes: 45);

		await sender.SendMagicLinkAsync("iemand@kompaz.local", "Iemand Anders", "the-secret");

		dispatcher.Sent!.Body.Should().Contain("45 minuten");
	}

	/// <summary>
	/// The point of the resource files: a second language is configuration, not a code change.
	/// </summary>
	[Test]
	public async Task AnotherConfiguredLanguageChangesTheWholeMessage()
	{
		var (sender, dispatcher) = CreateSender(language: "en");

		await sender.SendMagicLinkAsync("somebody@kompaz.local", "Some Body", "the-secret");

		dispatcher.Sent!.Subject.Should().Be("Your login link");
		dispatcher.Sent.Body.Should().StartWith("Hello Some Body,");
		dispatcher.Sent.Body.Should().Contain("30 minutes");
	}

	/// <summary>
	/// A culture with no resource file of its own falls back to the neutral one, which is Dutch on purpose.
	/// </summary>
	[Test]
	public async Task AnUntranslatedLanguageFallsBackToDutch()
	{
		var (sender, dispatcher) = CreateSender(language: "de");

		await sender.SendMagicLinkAsync("jemand@kompaz.local", "Jemand", "the-secret");

		dispatcher.Sent!.Subject.Should().Be("Je login-link");
	}

	[Test]
	public async Task TheInvitationEmailIsWrittenInTheSameLanguage()
	{
		var (sender, dispatcher) = CreateSender();

		await sender.SendInvitationAsync("iemand@kompaz.local", "Iemand Anders", "Zorggroep Noord", "the-secret");

		dispatcher.Sent!.Subject.Should().Be("Je bent uitgenodigd voor Zorggroep Noord op KOMPAZ");
		dispatcher.Sent.Body.Should().Contain("7 dagen");
		dispatcher.Sent.Body.Should().Contain("https://app.kompaz.test/invitations/accept?token=the-secret");
	}

	/// <summary>
	/// A secret is put into a URL, so anything that is not URL-safe has to survive the round trip.
	/// </summary>
	[Test]
	public async Task TheSecretIsEscapedIntoTheLink()
	{
		var (sender, dispatcher) = CreateSender();

		await sender.SendMagicLinkAsync("iemand@kompaz.local", "Iemand Anders", "a+b/c=");

		dispatcher.Sent!.Body.Should().Contain("token=a%2Bb%2Fc%3D");
	}

	[Test]
	public void AnUnknownLanguageIsRefusedAtStartup()
	{
		var settings = new EmailSettings
		{
			FromAddress = "noreply@kompaz.test",
			MagicLinkUrl = "https://app.kompaz.test/auth/callback?token={token}",
			InvitationUrl = "https://app.kompaz.test/invitations/accept?token={token}",
			DefaultLanguage = "not-a-language",
		};

		settings.Validate().Should().Contain("DefaultLanguage");
	}

	private static (AuthenticationEmailSender Sender, RecordingDispatcher Dispatcher) CreateSender(
		string language = "nl",
		int magicLinkLifetimeMinutes = 30)
	{
		var email = new EmailSettings
		{
			FromAddress = "noreply@kompaz.test",
			MagicLinkUrl = "https://app.kompaz.test/auth/callback?token={token}",
			InvitationUrl = "https://app.kompaz.test/invitations/accept?token={token}",
			DefaultLanguage = language,
		};

		var authentication = new AuthenticationSettings
		{
			MagicLinkLifetimeMinutes = magicLinkLifetimeMinutes,
			InvitationLifetimeDays = 7,
		};

		var dispatcher = new RecordingDispatcher();

		return (new AuthenticationEmailSender(Options.Create(email), Options.Create(authentication), dispatcher), dispatcher);
	}

	private sealed class RecordingDispatcher : IEmailDispatcher
	{
		public EmailMessage? Sent { get; private set; }

		public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
		{
			Sent = message;

			return Task.CompletedTask;
		}
	}
}
