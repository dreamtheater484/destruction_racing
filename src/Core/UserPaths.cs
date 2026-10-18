namespace DestructionRacing.Core;

/// <summary>
/// Where the player's files live: settings, the championship saves and the profile under <c>user://</c>, or under
/// <c>user://dev/</c> with <c>--devuser</c>, so test runs never touch the player's own saves.
/// </summary>
public static class UserPaths
{
    public static string Root => DevTools.Instance?.Arg("devuser") != null ? "user://dev/" : "user://";

    public static string Of(string relative)
    {
        string root = Root;
        if (root != "user://") Godot.DirAccess.MakeDirRecursiveAbsolute(Godot.ProjectSettings.GlobalizePath(root));
        return root + relative;
    }
}
