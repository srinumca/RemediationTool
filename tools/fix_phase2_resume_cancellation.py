from pathlib import Path

path = Path("src/RemediationTool.Application/Services/IngestionService.cs")
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one match, found {count}: {old[:160]!r}")
    text = text.replace(old, new, 1)


replace_once(
    '''                recordsToResume = await LoadRecordsForResumeAsync(
                    jobId,
                    checkpoint,
                    response,
                    jobAudit,
                    cancellationToken);
            }
            catch (Exception ex)
            {
''',
    '''                recordsToResume = await LoadRecordsForResumeAsync(
                    jobId,
                    checkpoint,
                    response,
                    jobAudit,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
''')

replace_once(
    '''            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[PARQUET_RESUME_FAILED_FALLBACK] JobId:{JobId}, Path:{Path}",
''',
    '''            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[PARQUET_RESUME_FAILED_FALLBACK] JobId:{JobId}, Path:{Path}",
''')

path.write_text(text, encoding="utf-8")
print("Phase 2 resume cancellation safety fix applied.")
