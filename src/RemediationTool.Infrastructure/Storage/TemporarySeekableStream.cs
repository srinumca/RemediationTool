namespace RemediationTool.Infrastructure.Storage;

internal static class TemporarySeekableStream
{
    private const int BufferSize = 81920;

    public static FileStream Create()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"gfr-remediation-{Guid.NewGuid():N}.tmp");

        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous
            | FileOptions.SequentialScan
            | FileOptions.DeleteOnClose);
    }
}
