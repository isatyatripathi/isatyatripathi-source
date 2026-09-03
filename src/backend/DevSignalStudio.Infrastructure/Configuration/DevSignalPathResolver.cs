namespace DevSignalStudio.Infrastructure.Configuration;

public static class DevSignalPathResolver
{
    public static string ResolveRoot(string? configuredRoot, string contentRootPath)
    {
        string? environmentRoot = Environment.GetEnvironmentVariable("DEVSIGNAL_ROOT");
        if (!string.IsNullOrWhiteSpace(environmentRoot))
        {
            return Validate(Path.GetFullPath(environmentRoot));
        }

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string resolved = Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(contentRootPath, configuredRoot);
            return Validate(Path.GetFullPath(resolved));
        }

        foreach (string start in new[] { contentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? current = new(Path.GetFullPath(start));
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "config", "topics.json")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the DevSignal root. Set DevSignal:RootPath or DEVSIGNAL_ROOT.");
    }

    private static string Validate(string root)
    {
        string config = Path.Combine(root, "config");
        if (!Directory.Exists(config))
        {
            throw new DirectoryNotFoundException($"The DevSignal config directory was not found at '{config}'.");
        }
        return root;
    }
}
