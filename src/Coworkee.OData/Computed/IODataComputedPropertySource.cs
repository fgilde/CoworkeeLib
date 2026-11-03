namespace Coworkee.OData.Computed;

/// <summary>Adds computed properties to registered entity sets; registered as a singleton, asked once when the EDM model is built.</summary>
public interface IODataComputedPropertySource
{
    IEnumerable<ODataComputedProperty> Properties(ODataEntityRegistry registry);
}
