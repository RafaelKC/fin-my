namespace FinMy.Domain.Monetary;

/// <summary>Divisão de um valor em partes (RT-02): cada parte recebe o piso e a sobra vai para a primeira.</summary>
public static class MoneySplitter
{
    /// <summary>
    /// Divide <paramref name="totalCents"/> em <paramref name="parts"/> partes. Cada parte recebe
    /// <c>piso(total / partes)</c>; a diferença é somada à primeira. Ex.: 10000 em 3 → [3334, 3333, 3333].
    /// </summary>
    public static long[] Split(long totalCents, int parts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCents);
        ArgumentOutOfRangeException.ThrowIfLessThan(parts, 1);

        var share = totalCents / parts;
        var result = new long[parts];
        Array.Fill(result, share);
        result[0] += totalCents - (share * parts);
        return result;
    }
}
