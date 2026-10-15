using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Coworkee.OData.Transfer.Import;

namespace Coworkee.OData.Tests;

public sealed class ImportParsingTests
{
    static ImportParsingTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Csv_detects_semicolons_windows_1252_and_the_header()
    {
        var bytes = Encoding.GetEncoding(1252).GetBytes("Name;Preis;Datum\r\nMüller;1.234,50;15.01.2026\r\n\"Semi;colon\";2;16.01.2026\r\n");

        var table = ImportFileReader.Read(bytes, "rows.csv");

        table.Delimiter.ShouldBe(";");
        table.Encoding.ShouldBe("windows-1252");
        table.HasHeader.ShouldBeTrue();
        table.Headers.ShouldBe(["Name", "Preis", "Datum"]);
        table.Rows.Select(r => r[0]).ShouldBe(["Müller", "Semi;colon"]);
        table.RowNumber(0).ShouldBe(2);
    }

    [Fact]
    public void Csv_reads_utf8_with_bom_commas_and_quoted_line_breaks()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Name,Note\n\"Größe\",\"two\nlines, \"\"quoted\"\"\"\n")).ToArray();

        var table = ImportFileReader.Read(bytes, "rows.csv");

        table.Delimiter.ShouldBe(",");
        table.Headers.ShouldBe(["Name", "Note"]);
        table.Rows.Single().ShouldBe(["Größe", "two\nlines, \"quoted\""]);
    }

    [Fact]
    public void A_first_row_with_numbers_is_data_and_options_override_the_detection()
    {
        var bytes = Encoding.UTF8.GetBytes("Drill\t12\nSaw\t7\n");

        var detected = ImportFileReader.Read(bytes, "rows.tsv");
        var chosen = ImportFileReader.Read(bytes, "rows.tsv", new Contracts.Data.ImportParseOptions("\\t", "utf-8", HasHeader: true));

        detected.HasHeader.ShouldBeFalse();
        detected.Delimiter.ShouldBe("\\t");
        detected.Headers.ShouldBe(["Column 1", "Column 2"]);
        detected.Rows.Count.ShouldBe(2);
        detected.RowNumber(0).ShouldBe(1);
        chosen.Headers.ShouldBe(["Drill", "12"]);
        chosen.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public void Excel_cells_become_invariant_text()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Rows");
        sheet.Cell(1, 1).Value = "Name";
        sheet.Cell(1, 2).Value = "Price";
        sheet.Cell(1, 3).Value = "Made";
        sheet.Cell(1, 4).Value = "Active";
        sheet.Cell(2, 1).Value = "Drill";
        sheet.Cell(2, 2).Value = 1234.5;
        sheet.Cell(2, 3).Value = new DateTime(2026, 1, 15);
        sheet.Cell(2, 4).Value = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var table = ImportFileReader.Read(stream.ToArray(), "rows.xlsx");

        table.Typed.ShouldBeTrue();
        table.Rows.Single().ShouldBe(["Drill", "1234.5", "2026-01-15", "true"]);
    }

    [Fact]
    public void Json_arrays_take_the_keys_of_all_objects_as_columns()
    {
        var table = ImportFileReader.Read(Encoding.UTF8.GetBytes("""[{"Name":"Drill","Price":12.5},{"Name":"Saw","Active":true,"Price":null}]"""), "rows.json");

        table.Headers.ShouldBe(["Name", "Price", "Active"]);
        table.Rows[0].ShouldBe(["Drill", "12.5", null]);
        table.Rows[1].ShouldBe(["Saw", null, "true"]);
    }

    [Fact]
    public void Written_csv_reads_back_and_defuses_formulas()
    {
        var bytes = CsvFile.Write([["Name", "Note"], ["=SUM(A1)", "a;b"], ["-5", "x\"y"]]);

        var table = ImportFileReader.Read(bytes, "out.csv");

        table.Rows.Select(r => r[0]).ShouldBe(["'=SUM(A1)", "-5"]);
        table.Rows.Select(r => r[1]).ShouldBe(["a;b", "x\"y"]);
    }

    [Theory]
    [InlineData("1.234,5", "de-DE", false, 1234.5)]
    [InlineData("1,234.5", "en-US", false, 1234.5)]
    [InlineData("1234.5", "de-DE", true, 1234.5)]
    [InlineData("12,5", "de-DE", true, 12.5)]
    public void Numbers_follow_the_culture_of_text_files(string text, string culture, bool typed, double expected)
    {
        var (value, error) = new ImportValueConverter(CultureInfo.GetCultureInfo(culture), typed).Convert(text, typeof(decimal));

        error.ShouldBeNull();
        value.ShouldBe((decimal)expected);
    }

    [Fact]
    public void Integers_refuse_fractions_and_overflow()
    {
        var converter = new ImportValueConverter(CultureInfo.InvariantCulture, typed: true);

        converter.Convert("12.5", typeof(int)).Error.ShouldNotBeNull();
        converter.Convert("99999999999", typeof(int)).Error.ShouldNotBeNull();
        converter.Convert("42", typeof(int?)).Value.ShouldBe(42);
        converter.Convert(" ", typeof(int?)).ShouldBe((null, null));
    }

    [Fact]
    public void Dates_read_iso_first_then_the_culture()
    {
        var converter = new ImportValueConverter(CultureInfo.GetCultureInfo("de-DE"), typed: false);

        converter.Convert("2026-01-15", typeof(DateOnly)).Value.ShouldBe(new DateOnly(2026, 1, 15));
        converter.Convert("15.01.2026", typeof(DateTimeOffset)).Value.ShouldBe(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        converter.Convert("2026-01-15T10:30:00", typeof(DateTime)).Value.ShouldBe(new DateTime(2026, 1, 15, 10, 30, 0));
        converter.Convert("someday", typeof(DateTime)).Error.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Hammer", WidgetKind.Hammer)]
    [InlineData("powerdrill", WidgetKind.PowerDrill)]
    [InlineData("Claw hammer", WidgetKind.Hammer)]
    [InlineData("Hand saw", WidgetKind.Saw)]
    [InlineData("Power drill", WidgetKind.PowerDrill)]
    [InlineData("Klauenhammer", WidgetKind.Hammer)]
    [InlineData("1", WidgetKind.Wrench)]
    public void Enums_match_name_number_label_and_translated_label(string text, WidgetKind expected)
    {
        var converter = new ImportValueConverter(CultureInfo.GetCultureInfo("de-DE"), false, t => t == "Claw hammer" ? "Klauenhammer" : t);

        converter.Convert(text, typeof(WidgetKind)).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("X", true)]
    [InlineData("ja", true)]
    [InlineData("0", false)]
    [InlineData("Nein", false)]
    public void Booleans_know_yes_no_words_and_their_translation(string text, bool expected)
    {
        var converter = new ImportValueConverter(CultureInfo.GetCultureInfo("de-DE"), false, t => t switch { "Yes" => "Ja", "No" => "Nein", _ => t });

        converter.Convert(text, typeof(bool)).Value.ShouldBe(expected);
    }

    [Fact]
    public void Columns_match_fields_by_name_label_translation_and_reference_name()
    {
        var fields = new List<ImportField>
        {
            new("Name", "Name", typeof(string), true, null),
            new("MadeAt", "Made at", typeof(DateTimeOffset), false, null),
            new("Price", "Price", typeof(decimal), false, null),
            new("WidgetId", "Widget", typeof(Guid), false, null, new Lookup("Widgets", typeof(Widget), typeof(Widget).GetProperty(nameof(Widget.Name))!)),
        };

        var suggested = ImportColumnMatcher.Suggest(["NAME", "made_at", "Preis", "Widget", "name", "Other"], fields, t => t == "Price" ? "Preis" : t);

        suggested.ShouldBe(["Name", "MadeAt", "Price", "WidgetId", null, null]);
    }
}
