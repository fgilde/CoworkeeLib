using System.Globalization;
using Coworkee.Client.Blazor.Components.Editors;

namespace Coworkee.Client.Blazor.Tests;

public sealed class CronScheduleTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static string Describe(string expression) => CronSchedule.Parse(expression).Describe((key, args) => string.Format(English, key, args), English);

    [Theory]
    [InlineData("*/15 * * * *", CronFrequency.Minutes, "Every 15 minutes")]
    [InlineData("30 * * * *", CronFrequency.Hourly, "Every hour at minute 30")]
    [InlineData("0 6 * * *", CronFrequency.Daily, "Every day at 06:00 UTC")]
    [InlineData("5 18 * * 1-5", CronFrequency.Weekly, "Every Monday, Tuesday, Wednesday, Thursday, Friday at 18:05 UTC")]
    [InlineData("0 7 * * 0,6", CronFrequency.Weekly, "Every Sunday, Saturday at 07:00 UTC")]
    [InlineData("0 3 15 * *", CronFrequency.Monthly, "Every month on day 15 at 03:00 UTC")]
    [InlineData("0 6 * * MON", CronFrequency.Custom, "Custom schedule")]
    [InlineData("0 0 1 1 *", CronFrequency.Custom, "Custom schedule")]
    public void Recognizes_and_describes_the_common_patterns(string expression, CronFrequency frequency, string description)
    {
        CronSchedule.Parse(expression).Frequency.ShouldBe(frequency);
        Describe(expression).ShouldBe(description);
    }

    [Theory]
    [InlineData("*/15 * * * *")]
    [InlineData("30 * * * *")]
    [InlineData("0 6 * * *")]
    [InlineData("5 18 * * 1,2,3,4,5")]
    [InlineData("0 3 15 * *")]
    public void Builds_the_expression_it_parsed(string expression) => CronSchedule.Parse(expression).ToExpression().ShouldBe(expression);

    [Fact]
    public void Sunday_as_7_is_sunday_and_custom_keeps_no_expression()
    {
        CronSchedule.Parse("0 6 * * 7").Weekdays.ShouldBe([DayOfWeek.Sunday]);
        CronSchedule.Parse("0 0 1 1 *").ToExpression().ShouldBeNull();
        new CronSchedule(CronFrequency.Weekly, Minute: 30, Hour: 9, Days: [DayOfWeek.Friday, DayOfWeek.Monday]).ToExpression().ShouldBe("30 9 * * 1,5");
    }

    [Theory]
    [InlineData("0 6 * * *", true)]
    [InlineData("0 0 6 * * *", true)]
    [InlineData("*/5 9-17 * JAN-MAR MON-FRI", true)]
    [InlineData("0 12 ? * 5#3", true)]
    [InlineData("60 * * * *", false)]
    [InlineData("0 24 * * *", false)]
    [InlineData("0 6 0 * *", false)]
    [InlineData("0 6 * * * * *", false)]
    [InlineData("every day", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_expressions(string? expression, bool valid) => CronSchedule.IsValid(expression).ShouldBe(valid);

    [Theory]
    [InlineData("image/png", true)]
    [InlineData("image/*", true)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", true)]
    [InlineData("pdf", false)]
    [InlineData(".pdf", false)]
    [InlineData("*/*", false)]
    public void Content_types_look_like_mime_types(string value, bool valid) => ContentTypeCatalog.IsValid(value).ShouldBe(valid);

    [Fact]
    public void Unknown_content_types_keep_their_value_and_get_the_icon_of_their_kind()
    {
        ContentTypeCatalog.Describe("application/pdf").Name.ShouldBe("PDF");
        var icon = ContentTypeCatalog.Describe("image/x-icon");
        icon.Name.ShouldBe("image/x-icon");
        icon.Icon.ShouldBe(ContentTypeCatalog.Describe("image/*").Icon);
        ContentTypeCatalog.Groups.Single(g => g.Name == "Office documents").Values.ShouldContain("application/msword");
    }
}
