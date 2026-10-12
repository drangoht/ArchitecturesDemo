using Fig.Modern;

namespace Fig.Tests;

/// <summary>The translation between the two models, without any server.</summary>
public class AntiCorruptionLayerTests
{
    private static LegacyLogRow Row(string type = "OPERATION", string date = "12/10/2026 10:00:00", long cents = -12_345,
        string origin = "LEGACY") =>
        new(7, type, "10001", null, "L-000007", date, "ACCOUNT MAINTENANCE FEE", cents, origin);

    [Fact]
    public void A_log_row_becomes_an_operation_in_euros_with_its_time_zone()
    {
        var fact = Assert.IsType<OperationRecorded>(LegacyTranslator.Translate(Row()));

        Assert.Equal("10001", fact.Number);
        Assert.Equal(-123.45m, fact.Operation.Amount);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 8, 0, 0, TimeSpan.Zero), fact.Operation.Date);   // 10 am in Paris = 8 am UTC
        Assert.Equal(TimeSpan.FromHours(2), fact.Operation.Date.Offset);
        Assert.Equal(Origin.Legacy, fact.Operation.Origin);
    }

    [Fact]
    public void Paris_time_does_not_have_the_same_offset_in_winter()
    {
        var fact = Assert.IsType<OperationRecorded>(LegacyTranslator.Translate(Row(date: "15/01/2026 10:00:00")));

        Assert.Equal(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero), fact.Operation.Date);
    }

    [Fact]
    public void An_account_opening_becomes_an_account()
    {
        var opening = new LegacyLogRow(1, "OPENING", "10001", "ALICE MARTIN", null, null, null, 0, "LEGACY");

        Assert.Equal(new AccountOpened("10001", "ALICE MARTIN"), LegacyTranslator.Translate(opening));
    }

    [Fact]
    public void An_unknown_type_stops_the_translation_instead_of_being_ignored()
    {
        Assert.Throws<FormatException>(() => LegacyTranslator.Translate(Row(type: "OVERDRAFT")));
    }

    [Fact]
    public void An_operation_of_the_modern_service_makes_the_round_trip_without_loss()
    {
        var operation = new Operation("M-1", new DateTimeOffset(2026, 10, 12, 8, 30, 15, TimeSpan.Zero), "TRF TO 10002", -50.5m, Origin.Modern);

        var toLegacy = LegacyTranslator.ToLegacy(new OperationToForward("10001", operation));
        Assert.Equal("12/10/2026 10:30:15", toLegacy.Date);
        Assert.Equal(-5_050, toLegacy.AmountCents);

        var back = new LegacyLogRow(9, "OPERATION", toLegacy.Number, null, toLegacy.OperationId, toLegacy.Date,
            toLegacy.Label, toLegacy.AmountCents, "MODERN");
        var fact = Assert.IsType<OperationRecorded>(LegacyTranslator.Translate(back));
        Assert.Equal(operation, fact.Operation);
    }

    [Fact]
    public void An_amount_beyond_the_cent_cannot_leave_for_the_legacy_system()
    {
        Assert.Throws<ArgumentException>(() => LegacyTranslator.Cents(1.005m));
    }
}
