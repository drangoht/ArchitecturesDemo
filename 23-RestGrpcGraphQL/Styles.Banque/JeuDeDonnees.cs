namespace Styles.Banque;

/// <summary>Les données de départ : Alice a trois comptes (de quoi voir le « 1 + N » de REST), Bob un seul.</summary>
internal static class JeuDeDonnees
{
    public const string Alice = "alice";
    public const string Bob = "bob";

    public static void Charger(ServiceBancaire banque)
    {
        banque.AjouterClient(new Client(Alice, "Alice Martin", "alice.martin@example.fr"));
        banque.AjouterClient(new Client(Bob, "Bob Durand", "bob.durand@example.fr"));

        banque.AjouterCompte(new Compte("10001", Alice, "Compte courant", TypeCompte.Courant,
            "FR76 3000 4000 0110 0010 0010 001", new DateOnly(2015, 3, 2), 500m, 0, 0));
        banque.AjouterCompte(new Compte("10002", Alice, "Livret A", TypeCompte.Epargne,
            "FR76 3000 4000 0110 0010 0020 002", new DateOnly(2016, 9, 15), 0m, 0, 0));
        banque.AjouterCompte(new Compte("10003", Alice, "Compte joint", TypeCompte.Courant,
            "FR76 3000 4000 0110 0010 0030 003", new DateOnly(2021, 6, 1), 200m, 0, 0));
        banque.AjouterCompte(new Compte("20001", Bob, "Compte courant", TypeCompte.Courant,
            "FR76 3000 4000 0220 0020 0010 004", new DateOnly(2019, 1, 10), 300m, 0, 0));

        // Septembre 2026, heure de Paris (UTC+2).
        var paris = TimeSpan.FromHours(2);
        DateTimeOffset Le(int jour, int heure) => new(2026, 9, jour, heure, 0, 0, paris);

        banque.AjouterOperation("10001", Le(1, 9), "Salaire", 2800.00m);
        banque.AjouterOperation("10001", Le(3, 12), "Loyer", -950.00m);
        banque.AjouterOperation("10001", Le(5, 18), "Courses", -86.40m);
        banque.AjouterOperation("10001", Le(9, 20), "Restaurant", -42.50m);
        banque.AjouterOperation("10001", Le(12, 10), "Électricité", -71.15m);
        banque.AjouterOperation("10001", Le(20, 17), "Librairie", -23.90m);

        banque.AjouterOperation("10002", Le(1, 8), "Report", 8000.00m);
        banque.AjouterOperation("10002", Le(2, 10), "Versement", 150.00m);

        banque.AjouterOperation("10003", Le(1, 8), "Apport Alice", 600.00m);
        banque.AjouterOperation("10003", Le(1, 9), "Apport Paul", 600.00m);
        banque.AjouterOperation("10003", Le(14, 11), "Assurance habitation", -38.20m);
        banque.AjouterOperation("10003", Le(28, 16), "Vacances", -412.00m);

        banque.AjouterOperation("20001", Le(1, 9), "Salaire", 1900.00m);
        banque.AjouterOperation("20001", Le(4, 12), "Loyer", -720.00m);
        banque.AjouterOperation("20001", Le(15, 19), "Téléphone", -19.99m);
    }
}
