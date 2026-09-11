using FluentAssertions;
using Kompaz.Application.Organizations;
using Kompaz.Application.Organizations.Commands.CreateOrganization;
using Kompaz.Domain.Enums;
using NUnit.Framework;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Kompaz.Application.FunctionalTests.Organizations;

/// <summary>
/// The logo half of creating an organization: what may be uploaded, what comes back, and what an organization that
/// has not uploaded anything is shown with.
/// </summary>
[TestFixture]
internal sealed class OrganizationLogoTests : ApiTestBase
{
	/// <summary>
	/// A PNG's eight-byte signature. The API recognizes a format by these bytes rather than by what the upload
	/// claims, so a signature with some payload behind it is what a test needs to be treated as a PNG.
	/// </summary>
	private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

	[Test]
	public async Task AnOrganizationWithoutALogoIsServedThePlaceholder()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");

		var response = await client.GetAsync(organization.LogoUrl);

		organization.HasLogo.Should().BeFalse();
		organization.LogoUrl.Should().Be($"/api/organizations/{organization.Id}/logo");
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.Should().Be(LogoImage.SvgContentType);
		(await response.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
	}

	[Test]
	public async Task AnUploadedLogoIsServedBack()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		byte[] png = Png(2048);

		var uploaded = await UploadAsync(client, organization.LogoUrl, png, "logo.png");
		var updated = await uploaded.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web);
		var fetched = await client.GetAsync(organization.LogoUrl);

		uploaded.StatusCode.Should().Be(HttpStatusCode.OK);
		updated!.HasLogo.Should().BeTrue();
		fetched.Content.Headers.ContentType!.MediaType.Should().Be(LogoImage.PngContentType);
		(await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(png);
	}

	/// <summary>
	/// An SVG is a document, and this API's own origin is where any script inside one would run. The two headers
	/// are the whole mitigation, so their absence is worth a failing test rather than a code review.
	/// </summary>
	[Test]
	public async Task ALogoIsServedWithNothingItMayLoadAndNoSniffing()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		await UploadAsync(client, organization.LogoUrl, Svg(), "logo.svg");

		var response = await client.GetAsync(organization.LogoUrl);

		response.Content.Headers.ContentType!.MediaType.Should().Be(LogoImage.SvgContentType);
		response.Headers.GetValues("Content-Security-Policy").Should().Contain("default-src 'none'; sandbox");
		response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
	}

	[Test]
	public async Task UploadingASecondLogoReplacesTheFirst()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		byte[] replacement = Svg();

		await UploadAsync(client, organization.LogoUrl, Png(1024), "logo.png");
		await UploadAsync(client, organization.LogoUrl, replacement, "logo.svg");
		var fetched = await client.GetAsync(organization.LogoUrl);

		fetched.Content.Headers.ContentType!.MediaType.Should().Be(LogoImage.SvgContentType);
		(await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(replacement);
	}

	/// <summary>
	/// The size limit the ticket names. One byte over is over.
	/// </summary>
	[Test]
	public async Task AnOversizedLogoIsRejected()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");

		var response = await UploadAsync(client, organization.LogoUrl, Png(LogoImage.MaximumSizeInBytes + 1), "groot.png");

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await response.Content.ReadAsStringAsync())
			.Should().Contain($"Upload een kleiner bestand van maximaal {LogoImage.MaximumSize}.");
	}

	[Test]
	public async Task ALogoAtTheLimitIsAccepted()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");

		var response = await UploadAsync(client, organization.LogoUrl, Png(LogoImage.MaximumSizeInBytes), "precies.png");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// The format is read out of the bytes, so a file name and a content type that both say PNG do not make one.
	/// </summary>
	[Test]
	public async Task SomethingThatIsNotAnImageIsRejectedHoweverItIsLabelled()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		var response = await UploadAsync(
			client, organization.LogoUrl, Encoding.UTF8.GetBytes("MZ not an image"), "logo.png", LogoImage.PngContentType);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await response.Content.ReadAsStringAsync())
			.Should().Contain($"Upload een afbeelding van het type {LogoImage.AcceptedFormats}.");
	}

	[TestCase("logo.png", LogoImage.PngContentType)]
	[TestCase("logo.svg", LogoImage.SvgContentType)]
	[TestCase("logo.jpg", LogoImage.JpegContentType)]
	[TestCase("logo.webp", LogoImage.WebPContentType)]
	public async Task EveryAcceptedFormatIsStoredAsTheTypeItActuallyIs(string fileName, string expected)
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");

		// Uploaded as octet-stream throughout, so the type on the way back is the one the bytes were recognized
		// as rather than one the caller stated.
		await UploadAsync(client, organization.LogoUrl, Sample(expected), fileName);
		var fetched = await client.GetAsync(organization.LogoUrl);

		fetched.Content.Headers.ContentType!.MediaType.Should().Be(expected);
	}

	[Test]
	public async Task AdministratorsMayOnlySetTheirOwnOrganizationsLogo()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var other = await CreateAsync(platformAdministrator, "Klant B.V.");
		var administrator = await InviteAndSignInAsync(
			platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);
		var me = await administrator.GetFromJsonAsync<Kompaz.Application.Users.UserDto>("/api/auth/me", JsonOptions.Web);

		var own = await UploadAsync(administrator, $"/api/organizations/{me!.OrganizationId}/logo", Png(512), "logo.png");
		var theirs = await UploadAsync(administrator, $"/api/organizations/{other.Id}/logo", Png(512), "logo.png");

		own.StatusCode.Should().Be(HttpStatusCode.OK);
		theirs.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task MembersMayNotSetALogoButMaySeeOne()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAndSignInAsync(platformAdministrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);
		var me = await member.GetFromJsonAsync<Kompaz.Application.Users.UserDto>("/api/auth/me", JsonOptions.Web);

		var upload = await UploadAsync(member, $"/api/organizations/{me!.OrganizationId}/logo", Png(512), "logo.png");
		var read = await member.GetAsync($"/api/organizations/{me.OrganizationId}/logo");

		upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		read.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	/// <summary>
	/// The point of phase two: the row is a pointer and the bytes are in the store, keyed under the organization
	/// they belong to so a bucket can be reasoned about by looking at it.
	/// </summary>
	[Test]
	public async Task TheBytesGoToTheFileStoreAndTheRowOnlyPointsAtThem()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		byte[] png = Png(2048);

		await UploadAsync(client, organization.LogoUrl, png, "logo.png");

		Files.Count.Should().Be(1);
		string key = Files.Keys.Single();
		key.Should().StartWith($"organizations/{organization.Id:D}/logo-").And.EndWith(".png");
	}

	/// <summary>
	/// The orphan this design has to avoid. Replacing writes a new file and the old one is removed after the row
	/// stops naming it, so a bucket does not fill up with every logo anybody ever chose.
	/// </summary>
	[Test]
	public async Task ReplacingALogoRemovesTheFileItReplaced()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");

		await UploadAsync(client, organization.LogoUrl, Png(1024), "logo.png");
		string first = Files.Keys.Single();

		await UploadAsync(client, organization.LogoUrl, Svg(), "logo.svg");

		Files.Holds(first).Should().BeFalse();
		Files.Count.Should().Be(1);
		Files.Keys.Single().Should().EndWith(".svg");
	}

	[Test]
	public async Task RemovingALogoRemovesItsFile()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		await UploadAsync(client, organization.LogoUrl, Png(1024), "logo.png");

		await client.DeleteAsync(organization.LogoUrl);

		Files.Count.Should().Be(0);
	}

	/// <summary>
	/// The cascade takes the row, so nothing would be left to raise the removal of the file unless the deleting
	/// handler asks for the key first.
	/// </summary>
	[Test]
	public async Task DeletingAnOrganizationRemovesTheFileBehindItsLogo()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		await UploadAsync(client, organization.LogoUrl, Png(1024), "logo.png");

		await client.DeleteAsync($"/api/organizations/{organization.Id}");

		Files.Count.Should().Be(0);
	}

	/// <summary>
	/// The failure mode the split accepts. The file store is reached after the database commits, so a store that
	/// will not take the delete costs a file, never the deletion — and the caller is not told the request failed,
	/// because it did not.
	/// </summary>
	[Test]
	public async Task AStoreThatWillNotDeleteLeavesTheFileButNotTheRow()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		await UploadAsync(client, organization.LogoUrl, Png(1024), "logo.png");

		Files.DeletionFails = true;
		var removed = await client.DeleteAsync(organization.LogoUrl);
		var reread = await client.GetFromJsonAsync<OrganizationDto>(
			$"/api/organizations/{organization.Id}", JsonOptions.Web);

		removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
		reread!.HasLogo.Should().BeFalse();
		Files.Count.Should().Be(1);
	}

	/// <summary>
	/// The other half of that trade: a row naming a file the store has lost shows the placeholder rather than
	/// failing, so one orphaned pointer does not break the page it appears on.
	/// </summary>
	[Test]
	public async Task ARowPointingAtAMissingFileFallsBackToThePlaceholder()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		await UploadAsync(client, organization.LogoUrl, Png(1024), "logo.png");

		await Files.DeleteAsync(Files.Keys.Single());
		var response = await client.GetAsync(organization.LogoUrl);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.Should().Be(LogoImage.SvgContentType);
	}

	/// <summary>
	/// The delete action next to the logo in an expanded row. Removing it is not removing the picture from the
	/// screen — the placeholder takes over.
	/// </summary>
	[Test]
	public async Task ARemovedLogoFallsBackToThePlaceholder()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		await UploadAsync(client, organization.LogoUrl, Png(1024), "logo.png");

		var removed = await client.DeleteAsync(organization.LogoUrl);
		var fetched = await client.GetAsync(organization.LogoUrl);
		var reread = await client.GetFromJsonAsync<OrganizationDto>(
			$"/api/organizations/{organization.Id}", JsonOptions.Web);

		removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
		fetched.StatusCode.Should().Be(HttpStatusCode.OK);
		fetched.Content.Headers.ContentType!.MediaType.Should().Be(LogoImage.SvgContentType);
		reread!.HasLogo.Should().BeFalse();
	}

	/// <summary>
	/// Removing a logo that is not there is not a quiet success: the action is only offered for a logo that
	/// exists, so a caller reaching it twice is looking at a stale row.
	/// </summary>
	[Test]
	public async Task RemovingALogoThatWasNeverUploadedIsNotFound()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");

		var response = await client.DeleteAsync(organization.LogoUrl);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task MembersMayNotRemoveALogo()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var member = await InviteAndSignInAsync(platformAdministrator, "lid@kompaz.local", "Gewoon Lid", UserRole.Member);
		var me = await member.GetFromJsonAsync<Kompaz.Application.Users.UserDto>("/api/auth/me", JsonOptions.Web);

		await UploadAsync(platformAdministrator, $"/api/organizations/{me!.OrganizationId}/logo", Png(512), "logo.png");
		var response = await member.DeleteAsync($"/api/organizations/{me.OrganizationId}/logo");

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Test]
	public async Task AdministratorsMayOnlyRemoveTheirOwnOrganizationsLogo()
	{
		var platformAdministrator = await SignInAsPlatformAdministratorAsync();
		var other = await CreateAsync(platformAdministrator, "Klant B.V.");
		await UploadAsync(platformAdministrator, other.LogoUrl, Png(512), "logo.png");
		var administrator = await InviteAndSignInAsync(
			platformAdministrator, "beheer@kompaz.local", "Beheerder", UserRole.Administrator);

		var response = await administrator.DeleteAsync(other.LogoUrl);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	/// <summary>
	/// No logo and no organization are different answers: the first is the placeholder, the second is a 404.
	/// </summary>
	[Test]
	public async Task AnOrganizationThatDoesNotExistHasNoPlaceholderEither()
	{
		var client = await SignInAsPlatformAdministratorAsync();

		var response = await client.GetAsync($"/api/organizations/{Guid.NewGuid()}/logo");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Test]
	public async Task ReadingALogoRequiresABearerToken()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");

		var response = await CreateClient().GetAsync(organization.LogoUrl);

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Test]
	public async Task DeletingAnOrganizationTakesItsLogoWithIt()
	{
		var client = await SignInAsPlatformAdministratorAsync();
		var organization = await CreateAsync(client, "Klant B.V.");
		await UploadAsync(client, organization.LogoUrl, Png(1024), "logo.png");

		var deleted = await client.DeleteAsync($"/api/organizations/{organization.Id}");
		var readBack = await client.GetAsync(organization.LogoUrl);

		deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);
		readBack.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	private static async Task<OrganizationDto> CreateAsync(HttpClient client, string name)
	{
		var response = await client.PostAsJsonAsync("/api/organizations", new CreateOrganizationCommand(name), JsonOptions.Web);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<OrganizationDto>(JsonOptions.Web)
			?? throw new InvalidOperationException("The API returned no organization.");
	}

	/// <summary>
	/// Puts one file to the logo endpoint as a browser's form would, and owns the content while it does.
	/// <para>
	/// The declared content type defaults to <c>application/octet-stream</c>, which is deliberately unhelpful: the
	/// API is supposed to decide the format from the bytes, and a test that announced the right type would pass
	/// whether it does or not.
	/// </para>
	/// </summary>
	private static async Task<HttpResponseMessage> UploadAsync(
		HttpClient client,
		string url,
		byte[] content,
		string fileName,
		string contentType = "application/octet-stream")
	{
		using var form = new MultipartFormDataContent();
		using var file = new ByteArrayContent(content);

		file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
		form.Add(file, "logo", fileName);

		return await client.PutAsync(url, form);
	}

	private static byte[] Sample(string contentType) => contentType switch
	{
		LogoImage.PngContentType => Png(1024),
		LogoImage.JpegContentType => Filled(JpegSignature, 1024),
		LogoImage.WebPContentType => WebP(1024),
		LogoImage.SvgContentType => Svg(),
		_ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "No sample for that type."),
	};

	private static byte[] Png(int length) => Filled(PngSignature, length);

	private static byte[] Svg() =>
		Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\"></svg>");

	/// <summary>
	/// A RIFF container whose form type says WebP, which is how the format announces itself.
	/// </summary>
	private static byte[] WebP(int length)
	{
		byte[] content = Filled(Encoding.ASCII.GetBytes("RIFF"), length);
		Encoding.ASCII.GetBytes("WEBP").CopyTo(content, 8);

		return content;
	}

	/// <summary>
	/// A payload of the given length that opens with the given signature, so it is recognized as that format
	/// without a test having to carry a real image around.
	/// </summary>
	private static byte[] Filled(byte[] signature, int length)
	{
		byte[] content = new byte[length];
		signature.CopyTo(content, 0);

		return content;
	}
}
