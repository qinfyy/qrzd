using Sv.Configuration;

namespace Sv.Utility;

public static class Util
{
    public static byte[]? ReadServerDataBytes(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string configured = Config.GameData.ServerDataPath;
        var ServerDataRoot = string.IsNullOrWhiteSpace(configured) ? Path.Combine(AppContext.BaseDirectory, "ServerData") : configured;
        string fullPath = Path.GetFullPath(Path.Combine(ServerDataRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(fullPath))
        {
            return null;
        }

        byte[] content = File.ReadAllBytes(fullPath);
        return content.Length == 0 ? null : content;
    }

    public static byte[]? ResolveWindseedScript(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Path.IsPathRooted(path))
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        byte[]? content = ReadServerDataBytes(Path.Combine("windseed", path)) ?? ReadServerDataBytes(path);
        if (content is not null)
        {
            return content;
        }

        foreach (string root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            string candidate = Path.GetFullPath(path, root);
            if (File.Exists(candidate))
            {
                return File.ReadAllBytes(candidate);
            }
        }

        return null;
    }
}
