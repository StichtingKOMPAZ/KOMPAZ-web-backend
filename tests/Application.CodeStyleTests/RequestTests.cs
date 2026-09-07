using FluentAssertions;
using Kompaz.Application.Common.Models;
using Kompaz.Application.Users.Commands.InviteUser;
using MediatR;
using NUnit.Framework;

namespace Kompaz.Application.CodeStyleTests;

[TestFixture]
internal class RequestTests
{
	private static readonly Type[] RequestTypes = typeof(InviteUserCommand).Assembly
		.GetTypes()
		.Where(type => type is { IsAbstract: false, IsInterface: false })
		.Where(type => type.GetInterfaces().Any(@interface =>
			@interface == typeof(IRequest) ||
			(@interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IRequest<>))))
		.ToArray();

	[Test]
	public void RequestsShouldEndWithCommandOrQuery()
	{
		RequestTypes.Should().OnlyContain(type =>
			type.Name.EndsWith("Command", StringComparison.Ordinal) ||
			type.Name.EndsWith("Query", StringComparison.Ordinal));
	}

	[Test]
	public void EveryListQueryShouldPage()
	{
		var listQueries = RequestTypes
			.Where(type => type.GetInterfaces().Any(@interface =>
				@interface.IsGenericType
				&& @interface.GetGenericTypeDefinition() == typeof(IRequest<>)
				&& @interface.GetGenericArguments()[0].IsGenericType
				&& @interface.GetGenericArguments()[0].GetGenericTypeDefinition() == typeof(PaginatedList<>)));

		listQueries.Should().NotBeEmpty();
		listQueries.Should().OnlyContain(type => typeof(PagedQuery).IsAssignableFrom(type));
	}
}
