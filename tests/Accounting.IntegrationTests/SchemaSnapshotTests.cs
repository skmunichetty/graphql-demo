using HotChocolate;
using HotChocolate.Execution;
using Path = System.IO.Path;

namespace Accounting.IntegrationTests;

/// <summary>
/// Compares the current schema with schema/accounting.graphql so every contract change is
/// reviewed. Run the tests with UPDATE_SCHEMA_SNAPSHOTS=1 to accept an intentional change.
/// </summary>
public sealed class SchemaSnapshotTests
{
    private readonly AccountingApiFactory _factory;

    public SchemaSnapshotTests(AccountingApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Schema_matches_checked_in_snapshot()
    {
        ISchemaDefinition schema = await _factory.Services.GetSchemaAsync(cancellationToken: TestContext.Current.CancellationToken);
        string actual = schema.ToString().ReplaceLineEndings("\n").Trim() + "\n";

        string snapshotPath = Path.Combine(FindRepositoryRoot(), "schema", "accounting.graphql");
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
