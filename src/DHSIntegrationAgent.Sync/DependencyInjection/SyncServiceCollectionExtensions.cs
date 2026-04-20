using DHSIntegrationAgent.Sync.Pipeline;
using DHSIntegrationAgent.Sync.Rules;
using DHSIntegrationAgent.Sync.Sql;
using DHSIntegrationAgent.Sync.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace DHSIntegrationAgent.Sync.DependencyInjection;

public static class SyncServiceCollectionExtensions
{
    public static IServiceCollection AddDhsSync(this IServiceCollection services)
    {
        // SQL layer
        services.AddSingleton<ISqlDialectFactory, SqlDialectFactory>();
        services.AddSingleton<DynamicSqlBuilder>();

        // Mapper
        services.AddSingleton<Mapper.TypeCoercionMap>();

        // Descriptor cache + validation
        services.AddSingleton<DescriptorResolver>();
        services.AddSingleton<SchemaValidator>();

        // Post-load rules (registered in declaration order from the plan)
        services.AddSingleton<IPostLoadRule, DeleteServicesByCompanyCodeRule>();
        services.AddSingleton<IPostLoadRule, NullIfColumnEqualsRule>();
        services.AddSingleton<IPostLoadRule, RecomputeHeaderTotalsRule>();
        services.AddSingleton<IPostLoadRule, ClampHeaderFieldRule>();
        services.AddSingleton<IPostLoadRule, SetInvestigationResultRule>();
        services.AddSingleton<IPostLoadRule, BackfillEmrFieldsRule>();
        services.AddSingleton<IPostLoadRule, ForceServiceFieldRule>();
        services.AddSingleton<IPostLoadRule, BackfillDiagnosisCodeFromSourceRule>();
        services.AddSingleton<IPostLoadRule, ForceNullRule>();
        services.AddSingleton<IPostLoadRule, BackfillMemberIdRule>();
        services.AddSingleton<PostLoadRuleEngine>();

        services.AddSingleton<IClaimExtractionPipeline, ClaimExtractionPipeline>();

        return services;
    }
}
