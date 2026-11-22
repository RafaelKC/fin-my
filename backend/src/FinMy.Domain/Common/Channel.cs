namespace FinMy.Domain.Common;

/// <summary>Canal pelo qual uma alteração foi feita (gravado em <c>created_via</c>/<c>updated_via</c>).</summary>
public enum Channel
{
    App,
    Claude,
    System,
}
