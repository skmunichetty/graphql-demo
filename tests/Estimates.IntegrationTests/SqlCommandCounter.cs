using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Estimates.IntegrationTests;

/// <summary>
/// Records every SQL command the API executes, with its parameter values, so a test can count the
/// queries made for its own tenant even while other tests run in parallel.
/// </summary>
public sealed class SqlCommandCounter : DbCommandInterceptor
{
    private readonly ConcurrentQueue<(string Sql, string[] ParameterValues)> _commands = new();

    public int CountFor(string tableName, string parameterValue)
    {
        return _commands.Count(command =>
            command.Sql.Contains(tableName, StringComparison.Ordinal)
            && command.ParameterValues.Contains(parameterValue, StringComparer.Ordinal));
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string[] parameterValues = command.Parameters
            .Cast<DbParameter>()
            .Select(parameter => parameter.Value?.ToString() ?? string.Empty)
            .ToArray();
        _commands.Enqueue((command.CommandText, parameterValues));
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
