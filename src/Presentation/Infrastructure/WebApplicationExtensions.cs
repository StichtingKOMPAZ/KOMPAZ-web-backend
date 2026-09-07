using System.Reflection;

namespace Kompaz.Presentation.Infrastructure;

internal static class WebApplicationExtensions
{
	public static RouteGroupBuilder MapEndpoints(this WebApplication app, string groupName) =>
		app.MapGroup($"/api/{groupName}")
			.WithTags(groupName);

	public static WebApplication MapEndpoints(this WebApplication app)
	{
		var endpointGroupType = typeof(IEndpointGroup);

		var endpointGroupTypes = Assembly.GetExecutingAssembly()
			.GetTypes()
			.Where(type => endpointGroupType.IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract);

		foreach (var type in endpointGroupTypes)
		{
			if (Activator.CreateInstance(type) is IEndpointGroup instance)
			{
				instance.Map(app);
			}
		}

		return app;
	}
}
