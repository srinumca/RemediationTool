using RemediationTool.Application.Interfaces;

public class LocalStorageService : IStorageService
{
    private readonly string _basePath = "storage/files";

    public async Task UploadAsync(string key, Stream data)
    {
        var fullPath = Path.Combine(_basePath, key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
        await data.CopyToAsync(fileStream);
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        var fullPath = Path.Combine(_basePath, key);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException(fullPath);

        var memory = new MemoryStream();
        using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        await fileStream.CopyToAsync(memory);

        memory.Position = 0;
        return memory;
    }

    public async Task MoveAsync(string sourceKey, string destinationKey)
    {
        var sourcePath = Path.Combine(_basePath, sourceKey);
        var destPath = Path.Combine(_basePath, destinationKey);

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        File.Copy(sourcePath, destPath, true);
        File.Delete(sourcePath); // ✅ only delete source

        await Task.CompletedTask;
    }

    public async Task DeleteAsync(string key)
    {
        var fullPath = Path.Combine(_basePath, key);

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        await Task.CompletedTask;
    }
}