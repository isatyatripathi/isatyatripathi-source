using System.Text.Json;

namespace DevSignalStudio.Infrastructure.Common;

public static class AtomicJsonFile
{
    public static async Task<T> ReadAsync<T>(
        string path,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        T? result = await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken);
        return result ?? throw new InvalidDataException($"'{path}' contained no JSON document.");
    }

    public static async Task WriteAsync<T>(
        string path,
        T value,
        JsonSerializerOptions options,
        int backupCount,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Cannot resolve the directory for '{path}'.");
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            RotateBackups(path, Math.Clamp(backupCount, 0, 20));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void RotateBackups(string path, int backupCount)
    {
        if (backupCount <= 0 || !File.Exists(path))
        {
            return;
        }

        for (int index = backupCount; index >= 2; index--)
        {
            string older = $"{path}.bak{index - 1}";
            string newer = $"{path}.bak{index}";
            if (File.Exists(older))
            {
                File.Move(older, newer, overwrite: true);
            }
        }

        File.Copy(path, $"{path}.bak1", overwrite: true);
    }
}
