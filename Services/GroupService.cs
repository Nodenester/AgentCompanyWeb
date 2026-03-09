using Microsoft.EntityFrameworkCore;
using AgentCompanyWeb.Data;
using AgentCompanyWeb.Data.Models;

namespace AgentCompanyWeb.Services;

/// <summary>
/// Service for managing Groups (Companies).
/// </summary>
public class GroupService
{
    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ILogger<GroupService> _logger;

    public GroupService(AppDbContext db, EncryptionService encryption, ILogger<GroupService> logger)
    {
        _db = db;
        _encryption = encryption;
        _logger = logger;
    }

    /// <summary>
    /// Get all groups with agent counts.
    /// </summary>
    public async Task<List<GroupWithStats>> GetAllGroupsAsync()
    {
        return await _db.Groups
            .Include(g => g.Agents)
            .Select(g => new GroupWithStats
            {
                Group = g,
                AgentCount = g.Agents.Count,
                OnlineCount = g.Agents.Count(a => a.Status == AgentStatus.Online || a.Status == AgentStatus.Working),
                WorkingCount = g.Agents.Count(a => a.Status == AgentStatus.Working)
            })
            .OrderBy(g => g.Group.Name)
            .ToListAsync();
    }

    /// <summary>
    /// Get a group by ID.
    /// </summary>
    public async Task<Group?> GetGroupAsync(int id)
    {
        return await _db.Groups
            .Include(g => g.Agents)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    /// <summary>
    /// Create a new group.
    /// </summary>
    public async Task<Group> CreateGroupAsync(GroupCreateDto dto)
    {
        var group = new Group
        {
            Name = dto.Name,
            Description = dto.Description,
            Color = dto.Color ?? "blue",
            GitHubTokenEncrypted = _encryption.Encrypt(dto.GitHubToken),
            AnthropicKeyEncrypted = _encryption.Encrypt(dto.AnthropicKey),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Groups.Add(group);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created group {Name} (ID: {Id})", group.Name, group.Id);
        return group;
    }

    /// <summary>
    /// Update a group.
    /// </summary>
    public async Task<Group?> UpdateGroupAsync(int id, GroupUpdateDto dto)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return null;

        group.Name = dto.Name ?? group.Name;
        group.Description = dto.Description;
        group.Color = dto.Color ?? group.Color;
        group.UpdatedAt = DateTime.UtcNow;

        // Only update credentials if provided (not empty)
        if (!string.IsNullOrEmpty(dto.GitHubToken))
            group.GitHubTokenEncrypted = _encryption.Encrypt(dto.GitHubToken);

        if (!string.IsNullOrEmpty(dto.AnthropicKey))
            group.AnthropicKeyEncrypted = _encryption.Encrypt(dto.AnthropicKey);

        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated group {Name} (ID: {Id})", group.Name, group.Id);
        return group;
    }

    /// <summary>
    /// Delete a group.
    /// </summary>
    public async Task<bool> DeleteGroupAsync(int id)
    {
        var group = await _db.Groups.FindAsync(id);
        if (group == null)
            return false;

        _db.Groups.Remove(group);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted group {Name} (ID: {Id})", group.Name, group.Id);
        return true;
    }

    /// <summary>
    /// Get decrypted GitHub token for a group.
    /// </summary>
    public async Task<string?> GetGitHubTokenAsync(int groupId)
    {
        var group = await _db.Groups.FindAsync(groupId);
        return group != null ? _encryption.Decrypt(group.GitHubTokenEncrypted) : null;
    }

    /// <summary>
    /// Get decrypted Anthropic key for a group.
    /// </summary>
    public async Task<string?> GetAnthropicKeyAsync(int groupId)
    {
        var group = await _db.Groups.FindAsync(groupId);
        return group != null ? _encryption.Decrypt(group.AnthropicKeyEncrypted) : null;
    }

    /// <summary>
    /// Check if a group has credentials configured (legacy + new system).
    /// </summary>
    public bool HasCredentials(Group group)
    {
        return !string.IsNullOrEmpty(group.GitHubTokenEncrypted) ||
               !string.IsNullOrEmpty(group.AnthropicKeyEncrypted);
    }

    /// <summary>
    /// Check if a group has any credentials (async, checks new credential table).
    /// </summary>
    public async Task<bool> HasAnyCredentialsAsync(int groupId)
    {
        var group = await _db.Groups.FindAsync(groupId);
        if (group == null) return false;

        // Check legacy credentials
        if (!string.IsNullOrEmpty(group.GitHubTokenEncrypted) ||
            !string.IsNullOrEmpty(group.AnthropicKeyEncrypted))
            return true;

        // Check new credentials table
        return await _db.GroupCredentials.AnyAsync(gc => gc.GroupId == groupId);
    }

    #region Flexible Credential System

    /// <summary>
    /// Set or update a credential for a group.
    /// </summary>
    public async Task SetCredentialAsync(int groupId, string credentialType, string value)
    {
        var existing = await _db.GroupCredentials
            .FirstOrDefaultAsync(gc => gc.GroupId == groupId && gc.CredentialType == credentialType);

        if (existing != null)
        {
            existing.EncryptedValue = _encryption.Encrypt(value) ?? "";
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var credential = new GroupCredential
            {
                GroupId = groupId,
                CredentialType = credentialType,
                EncryptedValue = _encryption.Encrypt(value) ?? "",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.GroupCredentials.Add(credential);
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Set credential {Type} for group {GroupId}", credentialType, groupId);
    }

    /// <summary>
    /// Get a decrypted credential value for a group.
    /// Falls back to legacy fields for github/anthropic if not in new table.
    /// </summary>
    public async Task<string?> GetCredentialAsync(int groupId, string credentialType)
    {
        // First check new credentials table
        var credential = await _db.GroupCredentials
            .FirstOrDefaultAsync(gc => gc.GroupId == groupId && gc.CredentialType == credentialType);

        if (credential != null)
        {
            return _encryption.Decrypt(credential.EncryptedValue);
        }

        // Fall back to legacy fields for backward compatibility
        var group = await _db.Groups.FindAsync(groupId);
        if (group == null) return null;

        return credentialType switch
        {
            CredentialTypes.GitHub => _encryption.Decrypt(group.GitHubTokenEncrypted),
            CredentialTypes.Anthropic => _encryption.Decrypt(group.AnthropicKeyEncrypted),
            _ => null
        };
    }

    /// <summary>
    /// Get all configured credential types for a group (not the values).
    /// Includes both legacy and new credentials.
    /// </summary>
    public async Task<List<string>> GetAllCredentialTypesAsync(int groupId)
    {
        var types = new HashSet<string>();

        // Check legacy fields
        var group = await _db.Groups.FindAsync(groupId);
        if (group != null)
        {
            if (!string.IsNullOrEmpty(group.GitHubTokenEncrypted))
                types.Add(CredentialTypes.GitHub);
            if (!string.IsNullOrEmpty(group.AnthropicKeyEncrypted))
                types.Add(CredentialTypes.Anthropic);
        }

        // Add types from new credentials table
        var newTypes = await _db.GroupCredentials
            .Where(gc => gc.GroupId == groupId)
            .Select(gc => gc.CredentialType)
            .ToListAsync();

        foreach (var type in newTypes)
        {
            types.Add(type);
        }

        return types.ToList();
    }

    /// <summary>
    /// Get all credentials for a group as a dictionary of type -> decrypted value.
    /// Used for passing to agent containers.
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllCredentialsAsync(int groupId)
    {
        var credentials = new Dictionary<string, string>();

        // Get legacy credentials
        var group = await _db.Groups.FindAsync(groupId);
        if (group != null)
        {
            var githubToken = _encryption.Decrypt(group.GitHubTokenEncrypted);
            if (!string.IsNullOrEmpty(githubToken))
                credentials[CredentialTypes.GitHub] = githubToken;

            var anthropicKey = _encryption.Decrypt(group.AnthropicKeyEncrypted);
            if (!string.IsNullOrEmpty(anthropicKey))
                credentials[CredentialTypes.Anthropic] = anthropicKey;
        }

        // Get new credentials (will override legacy if both exist)
        var newCredentials = await _db.GroupCredentials
            .Where(gc => gc.GroupId == groupId)
            .ToListAsync();

        foreach (var cred in newCredentials)
        {
            var value = _encryption.Decrypt(cred.EncryptedValue);
            if (!string.IsNullOrEmpty(value))
            {
                credentials[cred.CredentialType] = value;
            }
        }

        return credentials;
    }

    /// <summary>
    /// Delete a credential for a group.
    /// </summary>
    public async Task<bool> DeleteCredentialAsync(int groupId, string credentialType)
    {
        // Handle legacy fields specially
        if (credentialType == CredentialTypes.GitHub || credentialType == CredentialTypes.Anthropic)
        {
            var group = await _db.Groups.FindAsync(groupId);
            if (group != null)
            {
                if (credentialType == CredentialTypes.GitHub)
                    group.GitHubTokenEncrypted = null;
                else if (credentialType == CredentialTypes.Anthropic)
                    group.AnthropicKeyEncrypted = null;

                group.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Also delete from new table if exists
        var credential = await _db.GroupCredentials
            .FirstOrDefaultAsync(gc => gc.GroupId == groupId && gc.CredentialType == credentialType);

        if (credential != null)
        {
            _db.GroupCredentials.Remove(credential);
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Deleted credential {Type} for group {GroupId}", credentialType, groupId);
        return true;
    }

    /// <summary>
    /// Check if a specific credential type is configured for a group.
    /// </summary>
    public async Task<bool> HasCredentialAsync(int groupId, string credentialType)
    {
        // Check new table first
        var hasNew = await _db.GroupCredentials
            .AnyAsync(gc => gc.GroupId == groupId && gc.CredentialType == credentialType);
        if (hasNew) return true;

        // Fall back to legacy fields
        var group = await _db.Groups.FindAsync(groupId);
        if (group == null) return false;

        return credentialType switch
        {
            CredentialTypes.GitHub => !string.IsNullOrEmpty(group.GitHubTokenEncrypted),
            CredentialTypes.Anthropic => !string.IsNullOrEmpty(group.AnthropicKeyEncrypted),
            _ => false
        };
    }

    #endregion
}

/// <summary>
/// Group with computed statistics.
/// </summary>
public class GroupWithStats
{
    public required Group Group { get; set; }
    public int AgentCount { get; set; }
    public int OnlineCount { get; set; }
    public int WorkingCount { get; set; }
}

/// <summary>
/// DTO for creating a group.
/// </summary>
public class GroupCreateDto
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? GitHubToken { get; set; }
    public string? AnthropicKey { get; set; }
}

/// <summary>
/// DTO for updating a group.
/// </summary>
public class GroupUpdateDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? GitHubToken { get; set; }
    public string? AnthropicKey { get; set; }
}
