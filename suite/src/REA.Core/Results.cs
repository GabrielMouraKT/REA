namespace REA.Core;

public record OperationResult(bool Success, string? Error)
{
    public static OperationResult Ok() => new(true, null);
    public static OperationResult Fail(string error) => new(false, error);
}

public sealed record OperationResult<T>(bool Success, string? Error, T? Value) : OperationResult(Success, Error)
{
    public static OperationResult<T> Ok(T value) => new(true, null, value);
    public static new OperationResult<T> Fail(string error) => new(false, error, default);
}

/// <summary>Paginação feita no servidor/banco. Tamanhos permitidos: 50, 100, 200, 500.</summary>
public sealed record PageRequest
{
    public static readonly IReadOnlyList<int> AllowedSizes = [50, 100, 200, 500];
    public const int DefaultSize = 50;

    public int Page { get; }
    public int Size { get; }

    public PageRequest(int page = 1, int size = DefaultSize)
    {
        Page = page < 1 ? 1 : page;
        Size = AllowedSizes.Contains(size) ? size : DefaultSize;
    }

    public int Skip => (Page - 1) * Size;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, PageRequest Page)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)Page.Size);
}
