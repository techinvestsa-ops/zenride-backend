namespace Izigo.Application.Common.Models;

// Matches the contract envelope: { success, data, meta } or { success, error }
public class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public PagedMeta? Meta { get; init; }
    public ApiError? Error { get; init; }

    public static ApiResponse<T> Ok(T data, PagedMeta? meta = null) =>
        new() { Success = true, Data = data, Meta = meta };

    public static ApiResponse<T> Fail(string code, string message, Dictionary<string, string[]>? fields = null) =>
        new() { Success = false, Error = new ApiError(code, message, fields) };
}

public class ApiResponse
{
    public bool Success { get; init; }
    public object? Data { get; init; }
    public PagedMeta? Meta { get; init; }
    public ApiError? Error { get; init; }

    public static ApiResponse Ok(object? data = null, PagedMeta? meta = null) =>
        new() { Success = true, Data = data, Meta = meta };

    public static ApiResponse Fail(string code, string message, Dictionary<string, string[]>? fields = null) =>
        new() { Success = false, Error = new ApiError(code, message, fields) };
}

public record ApiError(string Code, string Message, Dictionary<string, string[]>? Fields = null);

public class PagedMeta
{
    public int Page { get; init; }
    public int PerPage { get; init; }
    public int Total { get; init; }
    public int LastPage => PerPage > 0 ? (int)Math.Ceiling((double)Total / PerPage) : 1;
    public Dictionary<string, Dictionary<string, int>>? Facets { get; init; }
    public Dictionary<string, long>? Totals { get; init; }

    public static PagedMeta From(int page, int perPage, int total,
        Dictionary<string, Dictionary<string, int>>? facets = null,
        Dictionary<string, long>? totals = null) =>
        new() { Page = page, PerPage = perPage, Total = total, Facets = facets, Totals = totals };
}

public class PaginationRequest
{
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 20;
    public string? Q { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? Format { get; set; } // csv
}
