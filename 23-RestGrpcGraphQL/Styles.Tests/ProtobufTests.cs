using Styles.Contracts;
using Styles.Tooling.Protobuf;

namespace Styles.Tests;

public class ProtobufTests
{
    private sealed class IntegerMessage
    {
        [Field(1)] public int A { get; set; }
    }

    private sealed class TextMessage
    {
        [Field(2)] public string B { get; set; } = "";
    }

    private sealed class Negatives
    {
        [Field(1)] public long Raw { get; set; }
        [Field(2, ZigZag = true)] public long ZigZag { get; set; }
    }

    // Two versions of the same message: v2 gained fields (numbers 3 to 5).
    private sealed class AccountV1
    {
        [Field(1)] public string Number { get; set; } = "";
        [Field(2)] public long BalanceCents { get; set; }
    }

    private sealed class AccountV2
    {
        [Field(1)] public string Number { get; set; } = "";
        [Field(2)] public long BalanceCents { get; set; }
        [Field(3)] public string Currency { get; set; } = "";
        [Field(4)] public bool Blocked { get; set; }
        [Field(5)] public List<Operation> Operations { get; set; } = [];
    }

    [Fact]
    public void An_integer_is_encoded_as_a_varint_as_in_the_Protobuf_documentation()
    {
        // The example of the official documentation: field 1, value 150 → 08 96 01.
        // 08 = number 1 and wire type 0 (varint); 96 01 = 150 on 7 bits per byte, least significant first.
        Assert.Equal([0x08, 0x96, 0x01], Protobuf.Serialize(new IntegerMessage { A = 150 }));
    }

    [Fact]
    public void A_string_is_preceded_by_its_length()
    {
        // Another example of the documentation: field 2, "testing" → 12 07 then the 7 UTF-8 bytes. No field name.
        Assert.Equal([0x12, 0x07, .. "testing"u8.ToArray()], Protobuf.Serialize(new TextMessage { B = "testing" }));
    }

    [Fact]
    public void Default_values_do_not_travel()
    {
        Assert.Empty(Protobuf.Serialize(new AccountV2()));
        Assert.Empty(Protobuf.Serialize(new IntegerMessage { A = 0 }));
    }

    [Fact]
    public void Zigzag_keeps_small_negatives_short()
    {
        // -1 as a "raw" int64: two's complement on 64 bits, hence 10 bytes of varint. As sint64 (zigzag): 1 byte.
        Assert.Equal(11, Protobuf.Serialize(new Negatives { Raw = -1 }).Length);
        Assert.Equal([0x10, 0x01], Protobuf.Serialize(new Negatives { ZigZag = -1 }));
        var readBack = Protobuf.Deserialize<Negatives>(Protobuf.Serialize(new Negatives { Raw = -4250, ZigZag = -4250 }));
        Assert.Equal((-4250, -4250), (readBack.Raw, readBack.ZigZag));
    }

    [Fact]
    public void A_nested_message_and_a_repeated_field_make_the_round_trip()
    {
        var dashboard = new Dashboard { CustomerId = "alice", Name = "Alice Martin" };
        dashboard.Accounts.Add(new Account { Number = "10001", Type = AccountType.Current, BalanceCents = 162_605 });
        dashboard.Accounts.Add(new Account { Number = "10002", Type = AccountType.Savings, BalanceCents = 815_000 });
        dashboard.Accounts[0].LatestOperations.Add(new Operation { Id = "OP1", Label = "Coffee", AmountCents = -250, DateUnixMs = 1_790_000_000_000 });

        var readBack = Protobuf.Deserialize<Dashboard>(Protobuf.Serialize(dashboard));

        Assert.Equal("Alice Martin", readBack.Name);
        Assert.Equal(["10001", "10002"], readBack.Accounts.Select(c => c.Number));
        Assert.Equal(AccountType.Savings, readBack.Accounts[1].Type);
        var operation = Assert.Single(readBack.Accounts[0].LatestOperations);
        Assert.Equal((-250L, 1_790_000_000_000L, "Coffee"), (operation.AmountCents, operation.DateUnixMs, operation.Label));
    }

    [Fact]
    public void An_old_client_ignores_the_fields_added_since()
    {
        var v2 = new AccountV2 { Number = "10001", BalanceCents = 12_345, Currency = "EUR", Blocked = true };
        v2.Operations.Add(new Operation { Id = "OP1", Label = "Coffee" });

        var readByV1 = Protobuf.Deserialize<AccountV1>(Protobuf.Serialize(v2));

        Assert.Equal(("10001", 12_345L), (readByV1.Number, readByV1.BalanceCents));
    }

    [Fact]
    public void A_new_client_reads_an_old_message_with_the_default_values()
    {
        var readByV2 = Protobuf.Deserialize<AccountV2>(Protobuf.Serialize(new AccountV1 { Number = "10001", BalanceCents = 5 }));

        Assert.Equal(("10001", 5L, "", false), (readByV2.Number, readByV2.BalanceCents, readByV2.Currency, readByV2.Blocked));
        Assert.Empty(readByV2.Operations);
    }

    [Fact]
    public void A_truncated_message_is_refused()
    {
        var bytes = Protobuf.Serialize(new TextMessage { B = "testing" });
        Assert.Throws<FormatException>(() => Protobuf.Deserialize<TextMessage>(bytes.AsSpan(0, 5)));
    }
}
