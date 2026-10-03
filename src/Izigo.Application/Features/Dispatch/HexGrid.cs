namespace Izigo.Application.Features.Dispatch;

/// <summary>
/// Indexes the map as pointy-top hexagons with a 500 m edge.
/// A search starts in the rider's cell plus the six cells that touch it,
/// then adds the next ring only when that neighbourhood is too thin.
/// A hexagon stays closer to a circle than a square, so a driver sitting
/// on a corner is not treated as if they were as near as one on a flat side.
/// </summary>
public static class HexGrid
{
    public const double EdgeM = 500;

    // Côte d'Ivoire sits near this latitude, so one metre scale is enough
    // for every city we dispatch in today.
    private const double RefLatRad = 5.3 * Math.PI / 180.0;
    private const double LngMeters = 111_320.0;
    private const double LatMeters = 110_540.0;

    public readonly record struct Cell(int Q, int R);

    public static Cell FromLatLng(double lat, double lng)
    {
        var x = lng * LngMeters * Math.Cos(RefLatRad);
        var y = lat * LatMeters;
        var q = (Math.Sqrt(3) / 3.0 * x - y / 3.0) / EdgeM;
        var r = (2.0 / 3.0 * y) / EdgeM;
        return Round(q, r);
    }

    /// <summary>
    /// How far, in metres, the k-th ring still covers. Ring 1 is the home
    /// cell plus its six neighbours.
    /// </summary>
    public static int RingReachM(int k) =>
        (int)Math.Ceiling(k * 1.5 * EdgeM + EdgeM);

    public static int RingsForRadius(int radiusM)
    {
        var k = 1;
        while (RingReachM(k) < radiusM && k < 8)
            k++;
        return k;
    }

    public static HashSet<Cell> Disk(Cell center, int k)
    {
        var cells = new HashSet<Cell>();
        for (var q = -k; q <= k; q++)
        {
            var rMin = Math.Max(-k, -q - k);
            var rMax = Math.Min(k, -q + k);
            for (var r = rMin; r <= rMax; r++)
                cells.Add(new Cell(center.Q + q, center.R + r));
        }
        return cells;
    }

    private static Cell Round(double q, double r)
    {
        var s  = -q - r;
        var rq = (int)Math.Round(q);
        var rr = (int)Math.Round(r);
        var rs = (int)Math.Round(s);
        var dq = Math.Abs(rq - q);
        var dr = Math.Abs(rr - r);
        var ds = Math.Abs(rs - s);
        if (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds) rr = -rq - rs;
        return new Cell(rq, rr);
    }
}
