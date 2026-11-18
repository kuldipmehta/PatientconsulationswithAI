using System.Collections.Concurrent;

namespace PatientManagement.Infrastructure.Services.Ai;

/// <summary>
/// Loads versioned system prompt files from the prompts/ directory (e.g. prompts/summary.v1.txt).
/// Rejects path-traversal segments and caches file contents by version key.
/// </summary>
public static class PromptTemplateLoader
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static (string PromptText, string Version) Load(string promptVersion, string fallbackPrompt)
    {
        if (string.IsNullOrWhiteSpace(promptVersion))
        {
            return (fallbackPrompt, "inline-default");
        }

        var sanitizedVersion = promptVersion.Trim();
        if (sanitizedVersion.Contains("..", StringComparison.Ordinal) ||
            sanitizedVersion.Contains('/') ||
            sanitizedVersion.Contains('\\'))
        {
            throw new ArgumentException($"Invalid prompt version identifier: '{promptVersion}'.");
        }

        var fileName = sanitizedVersion.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? sanitizedVersion
            : $"{sanitizedVersion}.txt";

        var normalizedVersion = fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;

        if (Cache.TryGetValue(normalizedVersion, out var cached))
        {
            return (cached, normalizedVersion);
        }

        var resolvedPath = ResolvePromptFilePath(fileName);
        if (resolvedPath == null)
        {
            return (fallbackPrompt, normalizedVersion);
        }

        var text = File.ReadAllText(resolvedPath).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return (fallbackPrompt, normalizedVersion);
        }

        Cache[normalizedVersion] = text;
        return (text, normalizedVersion);
    }

    private static string? ResolvePromptFilePath(string fileName)
    {
        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in searchRoots)
        {
            var dir = new DirectoryInfo(root);
            for (int depth = 0; depth < 6 && dir != null; depth++)
            {
                var candidate = Path.Combine(dir.FullName, "prompts", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
        }

        return null;
    }
}
