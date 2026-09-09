namespace Izigo.Application.Common.Models;

public record AdminPagedMeta(
    int Page,
    int PerPage,
    int Total,
    int LastPage,
    Dictionary<string, Dictionary<string, int>>? Facets = null,
    Dictionary<string, object>? Totals = null);

public static class AdminApiResponse
{
    public static object Ok<T>(IEnumerable<T> data, AdminPagedMeta meta)
        => new { success = true, data, meta };

    public static object Ok<T>(T data)
        => new { success = true, data };

    public static object Error(string code, string message)
        => new { success = false, error = new { code, message } };
}
