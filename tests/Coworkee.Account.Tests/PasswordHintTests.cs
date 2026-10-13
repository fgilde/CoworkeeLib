using Coworkee.Contracts.Configuration;

namespace Coworkee.Account.Tests;

public sealed class PasswordHintTests
{
    private static string English(string text, object?[] arguments) => string.Format(text, arguments);

    [Fact]
    public void Without_values_the_rules_are_the_identity_defaults_with_length_8() =>
        PasswordRules.From(new Dictionary<string, string?>()).ShouldBe(new PasswordRules(8, 1, true, true, true, true));

    [Fact]
    public void Values_are_read_from_the_client_settings()
    {
        var rules = PasswordRules.From(new Dictionary<string, string?>
        {
            [PasswordSettings.RequiredLength] = "12",
            [PasswordSettings.RequiredUniqueChars] = "4",
            [PasswordSettings.RequireDigit] = "false",
            [PasswordSettings.RequireLowercase] = "true",
            [PasswordSettings.RequireUppercase] = "false",
            [PasswordSettings.RequireNonAlphanumeric] = "false",
        });

        rules.ShouldBe(new PasswordRules(12, 4, false, true, false, false));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void The_length_is_at_least_1(string stored) =>
        PasswordRules.From(new Dictionary<string, string?> { [PasswordSettings.RequiredLength] = stored }).RequiredLength.ShouldBe(1);

    [Fact]
    public void A_value_that_is_not_a_number_or_flag_keeps_the_default() =>
        PasswordRules.From(new Dictionary<string, string?> { [PasswordSettings.RequiredLength] = "many", [PasswordSettings.RequireDigit] = "maybe" })
            .ShouldBe(new PasswordRules(8, 1, true, true, true, true));

    [Fact]
    public void The_hint_lists_the_rules_that_apply() =>
        new PasswordRules(12, 0, true, false, true, false).Describe(English).ShouldBe("At least 12 characters, a digit, an uppercase letter.");

    [Fact]
    public void The_hint_lists_every_rule_in_order() =>
        new PasswordRules(10, 5, true, true, true, true).Describe(English)
            .ShouldBe("At least 10 characters, 5 different characters, a digit, a lowercase letter, an uppercase letter, a special character.");
}
