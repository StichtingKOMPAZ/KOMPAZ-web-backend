using Kompaz.Application.Common.Models;
using Kompaz.Domain.Entities;

namespace Kompaz.Application.Common.Interfaces;

/// <summary>
/// Mints the bearer token that a client sends on subsequent requests.
/// </summary>
public interface IAccessTokenIssuer
{
	AccessToken Issue(User user);
}
