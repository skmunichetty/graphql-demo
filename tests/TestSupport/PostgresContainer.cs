using Testcontainers.PostgreSql;

namespace TestSupport;

/// <summary>
/// Starts a throwaway PostgreSQL container for integration tests (requires Docker).
/// Tests run against the same database engine as production, so constraints, numeric precision
/// and concurrency behave exactly as they will for real.
/// </summary>
public static class PostgresContainer
{
    public const string Image = "postgres:17-alpine";

    public static PostgreSqlContainer Create(string databaseName)
    {
        return new PostgreSqlBuilder(Image)
            .WithDatabase(databaseName)
            .Build();
    }
}
