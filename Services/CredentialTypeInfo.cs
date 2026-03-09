namespace AgentCompanyWeb.Services;

/// <summary>
/// Metadata about a credential type for display in the UI.
/// </summary>
public record CredentialTypeInfo(
    string Type,
    string DisplayName,
    string Description,
    string GetKeyUrl,
    string EnvVarName,
    bool SupportsOAuth,
    string? OAuthUrl,
    string IconClass
);

/// <summary>
/// Registry of all supported credential types with their metadata.
/// </summary>
public static class CredentialTypeInfos
{
    public static readonly CredentialTypeInfo[] All =
    [
        new CredentialTypeInfo(
            Type: "github",
            DisplayName: "GitHub",
            Description: "Git operations & GitHub API access",
            GetKeyUrl: "https://github.com/settings/tokens",
            EnvVarName: "GITHUB_TOKEN",
            SupportsOAuth: true,
            OAuthUrl: "/auth/github",
            IconClass: "fab fa-github"
        ),
        new CredentialTypeInfo(
            Type: "anthropic",
            DisplayName: "Anthropic",
            Description: "Claude API for AI agents",
            GetKeyUrl: "https://console.anthropic.com/settings/keys",
            EnvVarName: "ANTHROPIC_API_KEY",
            SupportsOAuth: false,
            OAuthUrl: null,
            IconClass: "fas fa-robot"
        ),
        new CredentialTypeInfo(
            Type: "runway",
            DisplayName: "Runway",
            Description: "AI video generation (Gen-3 Alpha)",
            GetKeyUrl: "https://app.runwayml.com/api-keys",
            EnvVarName: "RUNWAY_API_KEY",
            SupportsOAuth: false,
            OAuthUrl: null,
            IconClass: "fas fa-video"
        ),
        new CredentialTypeInfo(
            Type: "replicate",
            DisplayName: "Replicate",
            Description: "Access to 100s of open-source AI models",
            GetKeyUrl: "https://replicate.com/account/api-tokens",
            EnvVarName: "REPLICATE_API_TOKEN",
            SupportsOAuth: false,
            OAuthUrl: null,
            IconClass: "fas fa-cloud"
        ),
        new CredentialTypeInfo(
            Type: "unsplash",
            DisplayName: "Unsplash",
            Description: "Free high-quality stock photos",
            GetKeyUrl: "https://unsplash.com/developers",
            EnvVarName: "UNSPLASH_ACCESS_KEY",
            SupportsOAuth: false,
            OAuthUrl: null,
            IconClass: "fas fa-image"
        ),
        new CredentialTypeInfo(
            Type: "pexels",
            DisplayName: "Pexels",
            Description: "Free stock photos & videos",
            GetKeyUrl: "https://www.pexels.com/api/",
            EnvVarName: "PEXELS_API_KEY",
            SupportsOAuth: false,
            OAuthUrl: null,
            IconClass: "fas fa-images"
        ),
        new CredentialTypeInfo(
            Type: "openai",
            DisplayName: "OpenAI",
            Description: "DALL-E, Sora, GPT models",
            GetKeyUrl: "https://platform.openai.com/api-keys",
            EnvVarName: "OPENAI_API_KEY",
            SupportsOAuth: false,
            OAuthUrl: null,
            IconClass: "fas fa-brain"
        ),
        new CredentialTypeInfo(
            Type: "fal",
            DisplayName: "Fal.ai",
            Description: "Fast AI video & image generation",
            GetKeyUrl: "https://fal.ai/dashboard/keys",
            EnvVarName: "FAL_KEY",
            SupportsOAuth: false,
            OAuthUrl: null,
            IconClass: "fas fa-bolt"
        ),
        new CredentialTypeInfo(
            Type: "piapi",
            DisplayName: "PiAPI",
            Description: "Midjourney, Flux, Kling access",
            GetKeyUrl: "https://piapi.ai/dashboard",
            EnvVarName: "PIAPI_API_KEY",
            SupportsOAuth: false,
            OAuthUrl: null,
            IconClass: "fas fa-palette"
        ),
        new CredentialTypeInfo(
            Type: "gemini",
            DisplayName: "Google Gemini",
            Description: "Gemini Pro, Imagen, Veo video generation",
            GetKeyUrl: "https://aistudio.google.com/apikey",
            EnvVarName: "GEMINI_API_KEY",
            SupportsOAuth: false,
            OAuthUrl: null,
            IconClass: "fab fa-google"
        )
    ];

    /// <summary>
    /// Get credential type info by type name.
    /// </summary>
    public static CredentialTypeInfo? GetByType(string type)
    {
        return All.FirstOrDefault(c => c.Type == type);
    }

    /// <summary>
    /// Get all credential types as a dictionary.
    /// </summary>
    public static Dictionary<string, CredentialTypeInfo> ToDictionary()
    {
        return All.ToDictionary(c => c.Type);
    }
}
