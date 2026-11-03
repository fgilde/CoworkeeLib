namespace Coworkee.Features;

/// <summary>Modules describe the features an edition or a tenant can switch on, like settings.</summary>
public interface IFeatureDefinitionContributor
{
    void Define(FeatureDefinitionContext context);
}
