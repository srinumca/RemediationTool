using System.Globalization;

namespace RemediationTool.Application.Services;

/// <summary>
/// Defensive wrapper around CsvHelper.CsvReader used by IngestionService.
///
/// IngestionService catches row-level CSV exceptions and then continues its read loop.
/// If CsvHelper throws while reading a malformed row and does not advance the parser,
/// the next loop iteration can keep retrying the same row forever.
///
/// This wrapper preserves the existing IngestionService behavior for normal rows, but
/// after a CsvHelper read failure it makes the next Read() return false. That lets the
/// ingestion loop terminate instead of spinning forever on the same malformed row.
/// </summary>
internal sealed class CsvReader : IDisposable
{
    private readonly CsvHelper.CsvReader _inner;
    private bool _stopAfterReadFailure;

    public CsvReader(TextReader reader, CultureInfo cultureInfo)
    {
        _inner = new CsvHelper.CsvReader(reader, cultureInfo);
        Context = new CsvReaderContext();
    }

    public string[]? HeaderRecord => _inner.HeaderRecord;

    public CsvReaderContext Context { get; }

    public bool Read()
    {
        if (_stopAfterReadFailure)
        {
            return false;
        }

        try
        {
            var hasRecord = _inner.Read();
            SyncRowNumber();
            return hasRecord;
        }
        catch
        {
            SyncRowNumber();
            _stopAfterReadFailure = true;
            throw;
        }
    }

    public void ReadHeader()
    {
        _inner.ReadHeader();
        SyncRowNumber();
    }

    public string? GetField(string name)
    {
        return _inner.GetField(name);
    }

    public void Dispose()
    {
        _inner.Dispose();
    }

    private void SyncRowNumber()
    {
        Context.Parser.Row = _inner.Context?.Parser?.Row ?? Context.Parser.Row;
    }
}

internal sealed class CsvReaderContext
{
    public CsvParserContext Parser { get; } = new();
}

internal sealed class CsvParserContext
{
    public int Row { get; internal set; }
}
