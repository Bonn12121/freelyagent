using Freely.Agent.Runtime;

namespace Freely.Security.Permissions;

public enum PermissionLevel { Allow, Ask, Deny }

public sealed class PermissionPolicy
{
    private readonly Dictionary<string, PermissionLevel> _toolOverrides = new(StringComparer.OrdinalIgnoreCase);

    public PermissionLevel ReadOnly { get; set; } = PermissionLevel.Allow;
    public PermissionLevel Write { get; set; } = PermissionLevel.Ask;
    public PermissionLevel SystemChanging { get; set; } = PermissionLevel.Ask;
    public PermissionLevel Administrator { get; set; } = PermissionLevel.Ask;
    public PermissionLevel Destructive { get; set; } = PermissionLevel.Ask;
    public PermissionLevel Unknown { get; set; } = PermissionLevel.Ask;

    public PermissionLevel GetLevel(string toolName, ToolRisk risk)
    {
        if (_toolOverrides.TryGetValue(toolName, out var level))
        {
            return level;
        }

        return risk switch
        {
            ToolRisk.ReadOnly => ReadOnly,
            ToolRisk.Write => Write,
            ToolRisk.SystemChanging => SystemChanging,
            ToolRisk.Administrator => Administrator,
            ToolRisk.Destructive => Destructive,
            _ => Unknown
        };
    }

    public void SetOverride(string toolName, PermissionLevel level) => _toolOverrides[toolName] = level;
    public void ClearOverride(string toolName) => _toolOverrides.Remove(toolName);
}
