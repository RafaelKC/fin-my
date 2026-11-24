namespace FinMy.Domain.Identity;

/// <summary>Etapas e estados do guia de primeiros passos (006 FR-007, research R3).</summary>
public static class OnboardingGuide
{
    public const string Done = "done";
    public const string Skipped = "skipped";

    /// <summary>Etapas na ordem do guia.</summary>
    public static readonly IReadOnlyList<string> Steps = ["wallets", "cards", "settlement_tags", "installments", "recurrences"];

    public static bool IsStep(string? step) => step is not null && Steps.Contains(step);

    public static bool IsState(string? state) => state is Done or Skipped;

    /// <summary>Há etapa sem estado (nem concluída nem pulada).</summary>
    public static bool HasPendingStep(IReadOnlyDictionary<string, string> steps) => Steps.Any(s => !steps.ContainsKey(s));
}
