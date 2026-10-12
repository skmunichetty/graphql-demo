using HotChocolate;
using HotChocolate.Execution;
using Path = System.IO.Path;

namespace Estimates.IntegrationTests;

/// <summary>
/// Guards the public contract. The current schema is compared with the checked-in file
/// schema/estimates.graphql. Any schema change fails this test until the file is updated, so
/// every change is visible in code review, where it can be checked for breaking changes
/// (see docs/schema-evolution.md).
///
/// To accept an intentional change, run the tests with UPDATE_SCHEMA_SNAPSHOTS=1.
/// </summary>
public sealed class SchemaSnapshotTests
{
    private readonly EstimatesApiFactory _factory;

    public SchemaSnapshotTests(EstimatesApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Schema_matches_checked_in_snapshot()
    {
        ISchemaDefinition schema = await _factory.Services.GetSchemaAsync(cancellationToken: TestContext.Current.CancellationToken);
        string actual = schema.ToString().ReplaceLineEndings("\n").Trim() + "\n";

        string snapshotPath = Path.Combine(FindRepositoryRoot(), "schema", "estimates.graphql");
        if (Environment.GetEnvironmentVariable("UPDATE_SCHEMA_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(snapshotPath, actual, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(snapshotPath), $"Snapshot not found at {snapshotPath}. Run the tests with UPDATE_SCHEMA_SNAPSHOTS=1 to create it.");
        string expected = (await File.ReadAllTextAsync(snapshotPath, TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");
        Assert.Equal(expected, actual);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConstructionEstimation.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Could not find the repository root (ConstructionEstimation.slnx).");
        }

        return directory.FullName;
    }
}
