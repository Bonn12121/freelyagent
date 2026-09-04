using System.Text.Json.Serialization;
using Windows.Security.Credentials;

namespace Freely.App.Services;

public enum ChatPermissionMode { AskForApproval, ApproveForMe, FullAccess }

public sealed record SavedModelProfile
{
    public SavedModelProfile() { }

    public SavedModelProfile(string id, string providerId, string endpoint, string modelId, bool protocolMode, bool hasApiKey)
    {
        Id = id;
        ProviderId = providerId;
        Endpoint = endpoint;
        ModelId = modelId;
        ProtocolMode = protocolMode;
        HasApiKey = hasApiKey;
    }

    public string Id { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public bool ProtocolMode { get; set; }
    public bool HasApiKey { get; set; }

    [JsonIgnore]
    public string DisplayName => ModelId;

    [JsonIgnore]
    public string Subtitle => $"{ProviderId} · {Endpoint}";
}

/// <summary>Stores provider secrets in the current Windows user's Credential Manager.</summary>
internal sealed class ApiKeyVault
{
    private const string UserName = "api-key";
    private readonly PasswordVault _vault = new();

    public string? Read(string profileId)
    {
        try
        {
            var credential = _vault.Retrieve(Resource(profileId), UserName);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            return null;
        }
    }

    public void Write(string profileId, string apiKey)
    {
        Delete(profileId);
        if (!string.IsNullOrWhiteSpace(apiKey))
            _vault.Add(new PasswordCredential(Resource(profileId), UserName, apiKey));
    }

    public void Delete(string profileId)
    {
        try
        {
            _vault.Remove(_vault.Retrieve(Resource(profileId), UserName));
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // The profile has no stored credential.
        }
    }

    private static string Resource(string profileId) => $"Freely/model/{profileId}";
}
