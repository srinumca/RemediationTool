namespace RemediationTool.Application.Models;

/// <summary>
/// Standard response envelope for new APIs.
/// Existing endpoints keep their current response shape for backward compatibility.
/// </summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public T? Data { get; init; }

    public string? CorrelationId { get; init; }

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data, string message = "Request completed successfully.", string? correlationId = null)
        => new()
        {
            Success = true,
            Message = message,
            Data = data,
            CorrelationId = correlationId
        };

    public static ApiResponse<T> Fail(string message, string? correlationId = null)
        => new()
        {
            Success = false,
            Message = message,
            CorrelationId = correlationId
        };
}
