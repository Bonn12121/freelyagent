namespace Freely.Security.Permissions;

public static class NativeAppControlPermissions
{
    public static IReadOnlyList<string> ToolNames { get; } =
    [
        "app.launch",
        "app.focus",
        "app.click_element",
        "app.type_element",
        "computer.mouse_move",
        "computer.mouse_click",
        "computer.mouse_scroll",
        "computer.keyboard_type",
        "computer.keyboard_press"
    ];

    public static void Apply(PermissionPolicy policy, bool alwaysAllow)
    {
        ArgumentNullException.ThrowIfNull(policy);
        foreach (var toolName in ToolNames)
        {
            if (alwaysAllow) policy.SetOverride(toolName, PermissionLevel.Allow);
            else policy.ClearOverride(toolName);
        }
    }
}
