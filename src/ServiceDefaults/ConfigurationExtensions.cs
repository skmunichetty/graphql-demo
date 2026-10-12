using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;

namespace ServiceDefaults;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Binds and validates a settings section immediately. Used for settings that are needed while
    /// the application is being configured (before the DI container exists), so a bad value stops
    /// startup with a clear message instead of failing on the first request.
    /// </summary>
    public static T GetValidatedSettings<T>(this IConfiguration configuration, string sectionName)
        where T : class, new()
    {
        var settings = new T();
        configuration.GetSection(sectionName).Bind(settings);

        var results = new List<ValidationResult>();
        bool isValid = Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        if (!isValid)
        {
            string messages = string.Join("; ", results.Select(result => result.ErrorMessage));
            throw new InvalidOperationException($"Configuration section '{sectionName}' is invalid: {messages}");
        }

        return settings;
    }

    public static string GetRequiredConnectionString(this IConfiguration configuration, string name)
    {
        string? connectionString = configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{name}' is missing. Set ConnectionStrings__{name} as an environment variable or user secret.");
        }

        return connectionString;
    }
}
