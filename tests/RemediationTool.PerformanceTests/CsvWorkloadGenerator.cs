using System.Globalization;
using System.Text;

namespace RemediationTool.PerformanceTests;

internal static class CsvWorkloadGenerator
{
    private const int StreamBufferSize = 65_536;

    private static readonly string[] Header =
    {
        "ID",
        "Inbound_File_Name",
        "Finding_File_Name",
        "Finding File Format",
        "Finding_File_Size",
        "Current_File_Location",
        "Finding_Type",
        "Originating_Data_System",
        "Originating_Vendor_Tool",
        "Last_Modified_Date",
        "Risk_Level"
    };

    public static async Task<long> WriteAsync(
        string filePath,
        int recordCount,
        int invalidRowPercentage,
        int jobNumber,
        CancellationToken cancellationToken)
    {
        var parentDirectory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
            Directory.CreateDirectory(parentDirectory);

        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            StreamBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StreamBufferSize,
            leaveOpen: false);

        await writer.WriteLineAsync(
            string.Join(',', Header).AsMemory(),
            cancellationToken);

        var lastModifiedDate = DateTime.UtcNow.AddYears(-12)
            .ToString("O", CultureInfo.InvariantCulture);

        for (var index = 1; index <= recordCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isInvalid = invalidRowPercentage > 0
                && index % 100 < invalidRowPercentage;
            var findingType = isInvalid ? "UnsupportedType" : "Obsolete";
            var findingFileName = $"LoadTest-{jobNumber:D2}-{index:D8}.txt";
            var path = $@"\\performance-server\share\Batch-{jobNumber:D2}\{findingFileName}";

            var values = new[]
            {
                $"PERF-{jobNumber:D2}-{index:D8}",
                Path.GetFileName(filePath),
                findingFileName,
                "txt",
                (1024 + index % 4096).ToString(CultureInfo.InvariantCulture),
                path,
                findingType,
                "SMB",
                "PerformanceTestRunner",
                lastModifiedDate,
                "Low"
            };

            await writer.WriteLineAsync(
                string.Join(",", values.Select(Escape)).AsMemory(),
                cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
        return stream.Length;
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',')
            && !value.Contains('"')
            && !value.Contains('\n')
            && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
