namespace AgentCompanyWeb.Data.Models;

/// <summary>
/// A flexible credential storage for group-level API keys and tokens.
/// Supports multiple credential types per group.
/// </summary>
public class GroupCredential
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public Group Group { get; set; } = null!;

    /// <summary>
    /// The type of credential (e.g., "github", "anthropic", "runway").
    /// </summary>
    public string CredentialType { get; set; } = string.Empty;

    /// <summary>
    /// The encrypted credential value.
    /// </summary>
    public string EncryptedValue { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Known credential types with their environment variable mappings.
/// </summary>
public static class CredentialTypes
{
    public const string GitHub = "github";
    public const string Anthropic = "anthropic";
    public const string Runway = "runway";
    public const string Replicate = "replicate";
    public const string Unsplash = "unsplash";
    public const string Pexels = "pexels";
    public const string OpenAI = "openai";
    public const string Fal = "fal";
    public const string PiApi = "piapi";
    public const string Gemini = "gemini";

    /// <summary>
    /// Maps credential types to their environment variable names.
    /// </summary>
    public static readonly Dictionary<string, string> EnvVarMappings = new()
    {
        { GitHub, "GITHUB_TOKEN" },
        { Anthropic, "ANTHROPIC_API_KEY" },
        { Runway, "RUNWAY_API_KEY" },
        { Replicate, "REPLICATE_API_TOKEN" },
        { Unsplash, "UNSPLASH_ACCESS_KEY" },
        { Pexels, "PEXELS_API_KEY" },
        { OpenAI, "OPENAI_API_KEY" },
        { Fal, "FAL_KEY" },
        { PiApi, "PIAPI_API_KEY" },
        { Gemini, "GEMINI_API_KEY" }
    };

    /// <summary>
    /// Get the environment variable name for a credential type.
    /// </summary>
    public static string? GetEnvVarName(string credentialType)
    {
        return EnvVarMappings.TryGetValue(credentialType, out var envVar) ? envVar : null;
    }
}
