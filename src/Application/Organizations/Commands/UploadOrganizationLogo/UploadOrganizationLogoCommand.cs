using Kompaz.Application.Common.Exceptions;
using Kompaz.Application.Common.Interfaces;
using Kompaz.Application.Common.Security;
using Kompaz.Domain.Entities;
using Kompaz.Domain.Enums;

namespace Kompaz.Application.Organizations.Commands.UploadOrganizationLogo;

/// <summary>
/// Sets or replaces the image an organization is shown with. Administrators may only do so for their own, which is
/// the same reach they have over its name.
/// <para>
/// The bytes arrive as a command rather than as a stream the handler pulls on, because they have to be looked at
/// twice — once to recognize the format and once to store it — and because a handler that read a request body
/// would be a use case that only works over HTTP.
/// </para>
/// </summary>
/// <param name="OrganizationId">The organization to give a logo.</param>
/// <param name="Content">The image, already read into memory and known to be within the size limit.</param>
[Authorize(MinimumRole = UserRole.Administrator)]
public record UploadOrganizationLogoCommand(Guid OrganizationId, byte[] Content) : IRequest<OrganizationDto>;

public class UploadOrganizationLogoCommandValidator : AbstractValidator<UploadOrganizationLogoCommand>
{
	public UploadOrganizationLogoCommandValidator()
	{
		RuleFor(command => command.OrganizationId)
			.NotEqual(Guid.Empty);

		RuleFor(command => command.Content)
			.NotEmpty()
			.WithMessage("Kies een logo om te uploaden.");

		// Both of the upload's own rejections are stated here, so the API answers them the same way whether the
		// caller sent one byte too many or a file that is not an image at all: a 400 naming the field.
		RuleFor(command => command.Content)
			.Must(content => !LogoImage.ExceedsMaximumSize(content.LongLength))
			.WithMessage($"Upload een kleiner bestand van maximaal {LogoImage.MaximumSize}.")
			.Must(content => LogoImage.DetectContentType(content) is not null)
			.WithMessage($"Upload een afbeelding van het type {LogoImage.AcceptedFormats}.")
			.When(command => command.Content is { Length: > 0 });
	}
}

public class UploadOrganizationLogoCommandHandler : IRequestHandler<UploadOrganizationLogoCommand, OrganizationDto>
{
	private readonly IApplicationDbContext _context;
	private readonly IFileStore _files;
	private readonly IUser _user;

	public UploadOrganizationLogoCommandHandler(IApplicationDbContext context, IFileStore files, IUser user)
	{
		_context = context;
		_files = files;
		_user = user;
	}

	public async Task<OrganizationDto> Handle(UploadOrganizationLogoCommand request, CancellationToken cancellationToken)
	{
		OrganizationAccess.EnsureCanManage(_user, request.OrganizationId);

		var organization = await _context.Organizations
			.Include(candidate => candidate.Logo)
			.SingleOrDefaultAsync(candidate => candidate.Id == request.OrganizationId, cancellationToken)
			?? throw new NotFoundException(nameof(Organization), request.OrganizationId);

		// Not null: the validator has already refused anything this cannot recognize. Read again rather than
		// passed along, because the format is a property of the bytes and not of the request that carried them.
		string contentType = LogoImage.DetectContentType(request.Content)!;
		string key = OrganizationLogo.MintKey(organization.Id, LogoImage.ExtensionFor(contentType));

		// Written before the row that names it, and to a key nothing points at yet, so the image being served
		// right now is untouched until the save below succeeds. The order matters: the other way round, a row
		// would name a file that does not exist for as long as the upload takes, and a reader in that window
		// would get the placeholder instead of the logo they had a moment ago.
		//
		// What this costs is a file nobody claims if the save then fails. That is the trade the whole design
		// makes — see OrganizationLogo — and it is why nothing here tries to be clever about undoing it.
		await _files.SaveAsync(key, request.Content, contentType, cancellationToken);

		if (organization.Logo is { } logo)
		{
			// Raises the discarding of the file it stops naming, which is cleaned up once this is committed.
			logo.Replace(key, contentType, request.Content.Length);
		}
		else
		{
			_context.OrganizationLogos.Add(
				OrganizationLogo.For(organization.Id, key, contentType, request.Content.Length));
		}

		await _context.SaveChangesAsync(cancellationToken);

		return await _context.Organizations
			.AsNoTracking()
			.Where(candidate => candidate.Id == organization.Id)
			.Select(OrganizationDto.Projection)
			.SingleAsync(cancellationToken);
	}
}
