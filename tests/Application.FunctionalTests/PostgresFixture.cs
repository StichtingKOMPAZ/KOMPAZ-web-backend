using Npgsql;
using NUnit.Framework;
using System.Diagnostics.CodeAnalysis;
using Testcontainers.PostgreSql;

namespace Kompaz.Application.FunctionalTests;

/// <summary>
/// One PostgreSQL server, started once for the whole assembly and thrown away with it.
/// <para>
/// The application under test is a PostgreSQL application: <c>upper()</c> folds accents, a timestamp carries its
/// zone, and a duplicate arrives as SQLSTATE 23505. A stand-in database would agree with none of that, so these
/// tests run against the real server and take the few seconds it costs to start one.
/// </para>
/// <para>
/// Each <see cref="CustomWebApplicationFactory"/> gets a database of its own on that server, so tests stay isolated
/// from each other without paying for a container each.
/// </para>
/// </summary>
[SetUpFixture]
internal static class PostgresFixture
{
	private const string Image = "postgres:16-alpine";

	private static PostgreSqlContainer? _container;

	[OneTimeSetUp]
	public static async Task StartServerAsync()
	{
		_container = new PostgreSqlBuilder(Image)

			// A database per test means a connection pool per test, and the pools outlive the host that made them.
			// The stock limit of 100 is reached long before the suite is over.
			.WithCommand("-c", "max_connections=500")
			.WithCleanUp(true)
			.Build();

		await _container.StartAsync();
	}

	[OneTimeTearDown]
	public static async Task StopServerAsync()
	{
		if (_container is not null)
		{
			await _container.DisposeAsync();
			_container = null;
		}
	}

	/// <summary>
	/// Creates an empty database and returns how to reach it. The application migrates and seeds it on startup, the
	/// same way it would a real one, which is the point of not preparing it here.
	/// </summary>
	[SuppressMessage(
		"Security",
		"CA2100:Review SQL queries for security vulnerabilities",
		Justification = "An identifier cannot be parameterized, and the name is a generated GUID rather than input.")]
	public static string CreateDatabase()
	{
		string name = $"kompaz_{Guid.NewGuid():N}";

		using var connection = new NpgsqlConnection(ServerConnectionString);
		connection.Open();

		using var create = connection.CreateCommand();

		// The name is a generated GUID, so there is nothing to inject; identifiers cannot be parameterized anyway.
		create.CommandText = $"CREATE DATABASE \"{name}\"";
		create.ExecuteNonQuery();

		return ConnectionStringFor(name);
	}

	private static string ServerConnectionString =>
		_container?.GetConnectionString()
		?? throw new InvalidOperationException(
			"The PostgreSQL container is not running. Tests that need a database must live in "
			+ $"{typeof(PostgresFixture).Namespace} or below, so this fixture applies to them.");

	/// <summary>
	/// Reaches one database, with a pool small enough that a suite's worth of them fits on one server. Ten is more
	/// than any single test needs — the widest of them races six requests at once — and idle connections are pruned
	/// quickly so a finished test stops holding any.
	/// </summary>
	private static string ConnectionStringFor(string database) =>
		new NpgsqlConnectionStringBuilder(ServerConnectionString)
		{
			Database = database,
			MaxPoolSize = 10,
			ConnectionIdleLifetime = 2,
			ConnectionPruningInterval = 1,
		}.ConnectionString;
}
