namespace Izigo.Application.Features.Dispatch;

/// <summary>
/// Assigns each rider to at most one driver so the total road time is as
/// small as possible. A rider who has two good drivers can be given the
/// slightly farther one when the other rider would otherwise be left with
/// a very long wait.
/// </summary>
public static class MinCostMatcher
{
    public const long Forbidden = 1_000_000_000L;

    /// <summary>
    /// Returns the chosen column for each row, or -1 when that row is left
    /// unmatched. A cost of <see cref="Forbidden"/> is never chosen while a
    /// real pairing exists.
    /// </summary>
    public static int[] Solve(long[,] cost)
    {
        var rows = cost.GetLength(0);
        var cols = cost.GetLength(1);
        if (rows == 0 || cols == 0)
            return new int[rows];

        var n = Math.Max(rows, cols);
        var a = new long[n + 1, n + 1];
        for (var i = 1; i <= n; i++)
        for (var j = 1; j <= n; j++)
            a[i, j] = Forbidden;

        for (var i = 0; i < rows; i++)
        for (var j = 0; j < cols; j++)
            a[i + 1, j + 1] = cost[i, j];

        var u = new long[n + 1];
        var v = new long[n + 1];
        var p = new int[n + 1];
        var way = new int[n + 1];

        for (var i = 1; i <= n; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = new long[n + 1];
            var used = new bool[n + 1];
            Array.Fill(minv, long.MaxValue / 4);

            do
            {
                used[j0] = true;
                var i0 = p[j0];
                var delta = long.MaxValue / 4;
                var j1 = 0;
                for (var j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    var cur = a[i0, j] - u[i0] - v[j];
                    if (cur < minv[j])
                    {
                        minv[j] = cur;
                        way[j] = j0;
                    }
                    if (minv[j] < delta)
                    {
                        delta = minv[j];
                        j1 = j;
                    }
                }

                for (var j = 0; j <= n; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else
                    {
                        minv[j] -= delta;
                    }
                }

                j0 = j1;
            }
            while (p[j0] != 0);

            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            }
            while (j0 != 0);
        }

        var result = new int[rows];
        Array.Fill(result, -1);
        for (var j = 1; j <= cols; j++)
        {
            var row = p[j];
            if (row == 0 || row > rows) continue;
            if (cost[row - 1, j - 1] >= Forbidden) continue;
            result[row - 1] = j - 1;
        }
        return result;
    }
}
