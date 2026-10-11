using Styles.Contrats;
using Styles.Outillage.Protobuf;

namespace Styles.Tests;

public class ProtobufTests
{
    private sealed class Entier
    {
        [Champ(1)] public int A { get; set; }
    }

    private sealed class Texte
    {
        [Champ(2)] public string B { get; set; } = "";
    }

    private sealed class Negatifs
    {
        [Champ(1)] public long Brut { get; set; }
        [Champ(2, ZigZag = true)] public long ZigZag { get; set; }
    }

    // Deux versions d'un même message : la v2 a gagné des champs (numéros 3 à 5).
    private sealed class CompteV1
    {
        [Champ(1)] public string Numero { get; set; } = "";
        [Champ(2)] public long SoldeCentimes { get; set; }
    }

    private sealed class CompteV2
    {
        [Champ(1)] public string Numero { get; set; } = "";
        [Champ(2)] public long SoldeCentimes { get; set; }
        [Champ(3)] public string Devise { get; set; } = "";
        [Champ(4)] public bool Bloque { get; set; }
        [Champ(5)] public List<Operation> Operations { get; set; } = [];
    }

    [Fact]
    public void Un_entier_s_encode_en_varint_comme_dans_la_documentation_Protobuf()
    {
        // L'exemple de la documentation officielle : champ 1, valeur 150 → 08 96 01.
        // 08 = numéro 1 et type filaire 0 (varint) ; 96 01 = 150 sur 7 bits par octet, poids faibles d'abord.
        Assert.Equal([0x08, 0x96, 0x01], Protobuf.Serialiser(new Entier { A = 150 }));
    }

    [Fact]
    public void Une_chaine_est_precedee_de_sa_longueur()
    {
        // Autre exemple de la documentation : champ 2, « testing » → 12 07 puis les 7 octets UTF-8. Aucun nom de champ.
        Assert.Equal([0x12, 0x07, .. "testing"u8.ToArray()], Protobuf.Serialiser(new Texte { B = "testing" }));
    }

    [Fact]
    public void Les_valeurs_par_defaut_ne_circulent_pas()
    {
        Assert.Empty(Protobuf.Serialiser(new CompteV2()));
        Assert.Empty(Protobuf.Serialiser(new Entier { A = 0 }));
    }

    [Fact]
    public void Le_zigzag_garde_les_petits_negatifs_courts()
    {
        // -1 en int64 « brut » : complément à deux sur 64 bits, donc 10 octets de varint. En sint64 (zigzag) : 1 octet.
        Assert.Equal(11, Protobuf.Serialiser(new Negatifs { Brut = -1 }).Length);
        Assert.Equal([0x10, 0x01], Protobuf.Serialiser(new Negatifs { ZigZag = -1 }));
        var relu = Protobuf.Deserialiser<Negatifs>(Protobuf.Serialiser(new Negatifs { Brut = -4250, ZigZag = -4250 }));
        Assert.Equal((-4250, -4250), (relu.Brut, relu.ZigZag));
    }

    [Fact]
    public void Un_message_imbrique_et_un_champ_repete_font_l_aller_retour()
    {
        var tableau = new TableauDeBord { ClientId = "alice", Nom = "Alice Martin" };
        tableau.Comptes.Add(new Compte { Numero = "10001", Type = TypeCompte.Courant, SoldeCentimes = 162_605 });
        tableau.Comptes.Add(new Compte { Numero = "10002", Type = TypeCompte.Epargne, SoldeCentimes = 815_000 });
        tableau.Comptes[0].DernieresOperations.Add(new Operation { Id = "OP1", Libelle = "Café", MontantCentimes = -250, DateUnixMs = 1_790_000_000_000 });

        var relu = Protobuf.Deserialiser<TableauDeBord>(Protobuf.Serialiser(tableau));

        Assert.Equal("Alice Martin", relu.Nom);
        Assert.Equal(["10001", "10002"], relu.Comptes.Select(c => c.Numero));
        Assert.Equal(TypeCompte.Epargne, relu.Comptes[1].Type);
        var operation = Assert.Single(relu.Comptes[0].DernieresOperations);
        Assert.Equal((-250L, 1_790_000_000_000L, "Café"), (operation.MontantCentimes, operation.DateUnixMs, operation.Libelle));
    }

    [Fact]
    public void Un_ancien_client_ignore_les_champs_ajoutes_depuis()
    {
        var v2 = new CompteV2 { Numero = "10001", SoldeCentimes = 12_345, Devise = "EUR", Bloque = true };
        v2.Operations.Add(new Operation { Id = "OP1", Libelle = "Café" });

        var luParV1 = Protobuf.Deserialiser<CompteV1>(Protobuf.Serialiser(v2));

        Assert.Equal(("10001", 12_345L), (luParV1.Numero, luParV1.SoldeCentimes));
    }

    [Fact]
    public void Un_nouveau_client_lit_un_ancien_message_avec_les_valeurs_par_defaut()
    {
        var luParV2 = Protobuf.Deserialiser<CompteV2>(Protobuf.Serialiser(new CompteV1 { Numero = "10001", SoldeCentimes = 5 }));

        Assert.Equal(("10001", 5L, "", false), (luParV2.Numero, luParV2.SoldeCentimes, luParV2.Devise, luParV2.Bloque));
        Assert.Empty(luParV2.Operations);
    }

    [Fact]
    public void Un_message_tronque_est_refuse()
    {
        var octets = Protobuf.Serialiser(new Texte { B = "testing" });
        Assert.Throws<FormatException>(() => Protobuf.Deserialiser<Texte>(octets.AsSpan(0, 5)));
    }
}
