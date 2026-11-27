using Coworkee.Contracts.Reporting;
using Coworkee.Reporting.Subscriptions;

namespace Coworkee.Reporting.Tests;

public sealed class MailChipTests
{
    [Fact]
    public void Mail_chips_count_like_the_widget_rows_without_a_key_stay_out_and_an_empty_value_has_its_chip()
    {
        ReportQueryRow[] rows =
        [
            new(null, null, [50]),
            new(new ReportKey("South", "South", "Region eq 'South'"), null, [25]),
            new(new ReportKey(null, string.Empty, "Region eq null"), null, [25]),
        ];

        var shares = ReportMailWidget.ChipShares(rows, [new ValueIcon("South", "<svg/>"), new ValueIcon(string.Empty, "<svg/>")]);

        shares.Select(s => (s.Key.Filter, s.Share)).ShouldBe([("Region eq 'South'", 50d), ("Region eq null", 50d)]);
    }
}
