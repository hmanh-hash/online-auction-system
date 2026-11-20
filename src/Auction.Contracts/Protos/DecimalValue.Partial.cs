namespace Auction.Contracts.Protos;

/// <summary>
/// Chuyển đổi ngầm định giữa System.Decimal và message DecimalValue của protobuf.
/// </summary>
public partial class DecimalValue
{
    private const decimal NanoFactor = 1_000_000_000m;

    public DecimalValue(long units, int nanos)
    {
        Units = units;
        Nanos = nanos;
    }

    public static implicit operator decimal(DecimalValue? value)
        => value is null ? 0m : value.Units + value.Nanos / NanoFactor;

    public static implicit operator DecimalValue(decimal value)
    {
        var units = decimal.ToInt64(decimal.Truncate(value));
        var nanos = decimal.ToInt32((value - units) * NanoFactor);
        return new DecimalValue(units, nanos);
    }
}
