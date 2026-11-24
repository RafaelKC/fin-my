using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FluentValidation;
using FluentValidation.Results;

namespace FinMy.Application.Onboarding;

/// <summary>Estado do guia (006 contracts/api.md): <c>autoStart</c> = não dispensado, Espaço sem carteiras e alguma etapa pendente.</summary>
public sealed record OnboardingState(IReadOnlyDictionary<string, string> Steps, bool Dismissed, bool AutoStart);

internal static class OnboardingSupport
{
    public static async Task<OnboardingState> ReadAsync(IOnboardingStore store, Tenant tenant, CancellationToken cancellationToken)
    {
        var dismissed = tenant.OnboardingDismissedAt is not null;
        var autoStart = !dismissed
            && OnboardingGuide.HasPendingStep(tenant.OnboardingSteps)
            && !await store.HasWalletsAsync(cancellationToken);
        return new OnboardingState(new Dictionary<string, string>(tenant.OnboardingSteps), dismissed, autoStart);
    }

    public static async Task<Tenant> TenantAsync(IOnboardingStore store, CancellationToken cancellationToken) =>
        await store.FindTenantAsync(cancellationToken) ?? throw new MissingTenantContextException();
}

/// <summary><c>GET /onboarding</c>.</summary>
public sealed class GetOnboardingHandler(IOnboardingStore store)
{
    public async Task<OnboardingState> HandleAsync(CancellationToken cancellationToken) =>
        await OnboardingSupport.ReadAsync(store, await OnboardingSupport.TenantAsync(store, cancellationToken), cancellationToken);
}

/// <summary><c>PUT /onboarding/steps/{step}</c>: a pessoa concluiu ou pulou a etapa; etapa ou estado inválido é 422.</summary>
public sealed class SetOnboardingStepHandler(IOnboardingStore store)
{
    public async Task<OnboardingState> HandleAsync(string? step, string? state, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        if (!OnboardingGuide.IsStep(step))
        {
            failures.Add(new ValidationFailure("step", "Etapa do guia inválida."));
        }

        if (!OnboardingGuide.IsState(state))
        {
            failures.Add(new ValidationFailure("state", "Informe done ou skipped."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        var tenant = await OnboardingSupport.TenantAsync(store, cancellationToken);
        tenant.SetOnboardingStep(step!, state!);
        await store.SaveAsync(cancellationToken);
        return await OnboardingSupport.ReadAsync(store, tenant, cancellationToken);
    }
}

/// <summary><c>POST /onboarding/dismiss</c>: dispensa o guia.</summary>
public sealed class DismissOnboardingHandler(IOnboardingStore store, IClock clock)
{
    public async Task<OnboardingState> HandleAsync(CancellationToken cancellationToken)
    {
        var tenant = await OnboardingSupport.TenantAsync(store, cancellationToken);
        tenant.DismissOnboarding(clock.UtcNow);
        await store.SaveAsync(cancellationToken);
        return await OnboardingSupport.ReadAsync(store, tenant, cancellationToken);
    }
}
