using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace Styles.Outillage.Protobuf;

/// <summary>
/// Le numéro d'un champ dans le message Protobuf : c'est LUI qui circule, jamais le nom. On peut renommer un champ sans
/// rien casser ; changer son numéro, ou le réutiliser, casse tous les clients déjà déployés.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ChampAttribute(int numero) : Attribute
{
    public int Numero => numero;

    /// <summary>Encodage « zigzag » (type <c>sint64</c>) : les petits nombres négatifs tiennent sur peu d'octets.</summary>
    public bool ZigZag { get; init; }
}

/// <summary>
/// Un encodeur Protobuf (proto3) réduit à ce dont la démo a besoin, par réflexion sur les attributs <see cref="ChampAttribute"/> :
/// entiers (varint, zigzag), booléens, énumérations, chaînes, messages imbriqués et champs répétés.
/// En vrai, <c>protoc</c> génère ce code à partir du fichier .proto (Google.Protobuf, Grpc.Tools).
/// </summary>
public static class Protobuf
{
    private const int Varint = 0, Fixe64 = 1, Delimite = 2, Fixe32 = 5;

    private sealed record Champ(PropertyInfo Propriete, int Numero, bool ZigZag, Type Type, bool Repete);

    private static readonly ConcurrentDictionary<Type, Champ[]> Champs = new();

    private static Champ[] ChampsDe(Type type) => Champs.GetOrAdd(type, t => t.GetProperties()
        .Select(p => (Propriete: p, Attribut: p.GetCustomAttribute<ChampAttribute>()))
        .Where(x => x.Attribut is not null)
        .Select(x =>
        {
            var repete = x.Propriete.PropertyType.IsGenericType && x.Propriete.PropertyType.GetGenericTypeDefinition() == typeof(List<>);
            var type = repete ? x.Propriete.PropertyType.GetGenericArguments()[0] : x.Propriete.PropertyType;
            return new Champ(x.Propriete, x.Attribut!.Numero, x.Attribut.ZigZag, type, repete);
        })
        .OrderBy(c => c.Numero)
        .ToArray());

    // ─── Écriture ───

    public static byte[] Serialiser(object message)
    {
        var tampon = new MemoryStream();
        Ecrire(tampon, message);
        return tampon.ToArray();
    }

    private static void Ecrire(MemoryStream sortie, object message)
    {
        foreach (var champ in ChampsDe(message.GetType()))
        {
            var valeur = champ.Propriete.GetValue(message);
            if (champ.Repete)
            {
                foreach (var element in (IList)valeur!)
                    EcrireValeur(sortie, champ, element!);
            }
            else if (!EstParDefaut(valeur))   // proto3 : une valeur par défaut ne circule pas (0 octet)
                EcrireValeur(sortie, champ, valeur!);
        }
    }

    private static bool EstParDefaut(object? valeur) => valeur switch
    {
        null => true,
        string s => s.Length == 0,
        long l => l == 0,
        int i => i == 0,
        bool b => !b,
        Enum e => Convert.ToInt32(e) == 0,
        _ => false
    };

    private static void EcrireValeur(MemoryStream sortie, Champ champ, object valeur)
    {
        switch (valeur)
        {
            case string texte:
                EcrireEnTete(sortie, champ.Numero, Delimite);
                var octets = Encoding.UTF8.GetBytes(texte);
                EcrireVarint(sortie, (ulong)octets.Length);
                sortie.Write(octets);
                break;
            case long entier:
                EcrireEnTete(sortie, champ.Numero, Varint);
                EcrireVarint(sortie, champ.ZigZag ? (ulong)((entier << 1) ^ (entier >> 63)) : (ulong)entier);
                break;
            case int entier:
                EcrireEnTete(sortie, champ.Numero, Varint);
                EcrireVarint(sortie, (ulong)(long)entier);   // un int32 négatif s'étend sur 64 bits : 10 octets
                break;
            case bool booleen:
                EcrireEnTete(sortie, champ.Numero, Varint);
                EcrireVarint(sortie, booleen ? 1UL : 0UL);
                break;
            case Enum enumeration:
                EcrireEnTete(sortie, champ.Numero, Varint);
                EcrireVarint(sortie, (ulong)(long)Convert.ToInt32(enumeration));
                break;
            default:   // un message imbriqué : sa longueur, puis son contenu
                EcrireEnTete(sortie, champ.Numero, Delimite);
                var interieur = Serialiser(valeur);
                EcrireVarint(sortie, (ulong)interieur.Length);
                sortie.Write(interieur);
                break;
        }
    }

    private static void EcrireEnTete(MemoryStream sortie, int numero, int typeFilaire) =>
        EcrireVarint(sortie, (ulong)((numero << 3) | typeFilaire));

    /// <summary>7 bits par octet, le bit de poids fort dit « il en reste » : 1 tient sur 1 octet, 300 sur 2.</summary>
    private static void EcrireVarint(MemoryStream sortie, ulong valeur)
    {
        while (valeur >= 0x80)
        {
            sortie.WriteByte((byte)(valeur | 0x80));
            valeur >>= 7;
        }
        sortie.WriteByte((byte)valeur);
    }

    // ─── Lecture ───

    public static T Deserialiser<T>(ReadOnlySpan<byte> octets) where T : new() => (T)Deserialiser(typeof(T), octets);

    private static object Deserialiser(Type type, ReadOnlySpan<byte> octets)
    {
        var message = Activator.CreateInstance(type)!;
        var champs = ChampsDe(type);
        var position = 0;
        while (position < octets.Length)
        {
            var enTete = LireVarint(octets, ref position);
            var numero = (int)(enTete >> 3);
            var typeFilaire = (int)(enTete & 7);
            var champ = Array.Find(champs, c => c.Numero == numero);
            if (champ is null)
            {
                // Un champ inconnu (ajouté par une version plus récente de l'autre côté) : on le saute, sans erreur.
                // C'est ce qui permet de faire évoluer un contrat sans redéployer tous les clients en même temps.
                Sauter(octets, ref position, typeFilaire);
                continue;
            }
            var valeur = LireValeur(champ, octets, ref position, typeFilaire);
            if (champ.Repete)
                ((IList)champ.Propriete.GetValue(message)!).Add(valeur);
            else
                champ.Propriete.SetValue(message, valeur);
        }
        return message;
    }

    private static object LireValeur(Champ champ, ReadOnlySpan<byte> octets, ref int position, int typeFilaire)
    {
        var attendu = champ.Type == typeof(string) || champ.Type.IsClass ? Delimite : Varint;
        if (typeFilaire != attendu)
            throw new FormatException($"Champ {champ.Numero} : type filaire {typeFilaire} reçu, {attendu} attendu.");

        if (attendu == Delimite)
        {
            var longueur = checked((int)LireVarint(octets, ref position));
            if (position + longueur > octets.Length)
                throw new FormatException($"Champ {champ.Numero} : message tronqué.");
            var contenu = octets.Slice(position, longueur);
            position += longueur;
            return champ.Type == typeof(string) ? Encoding.UTF8.GetString(contenu) : Deserialiser(champ.Type, contenu);
        }

        var brut = LireVarint(octets, ref position);
        if (champ.Type == typeof(long))
            return champ.ZigZag ? (long)(brut >> 1) ^ -(long)(brut & 1) : (long)brut;
        if (champ.Type == typeof(int))
            return (int)(long)brut;
        if (champ.Type == typeof(bool))
            return brut != 0;
        if (champ.Type.IsEnum)
            return Enum.ToObject(champ.Type, (int)(long)brut);   // une valeur inconnue est gardée telle quelle (proto3)
        throw new NotSupportedException($"Type {champ.Type.Name} non pris en charge.");
    }

    private static ulong LireVarint(ReadOnlySpan<byte> octets, ref int position)
    {
        ulong valeur = 0;
        for (var decalage = 0; decalage < 64; decalage += 7)
        {
            if (position >= octets.Length)
                throw new FormatException("Varint tronqué.");
            var octet = octets[position++];
            valeur |= (ulong)(octet & 0x7F) << decalage;
            if (octet < 0x80)
                return valeur;
        }
        throw new FormatException("Varint trop long.");
    }

    private static void Sauter(ReadOnlySpan<byte> octets, ref int position, int typeFilaire)
    {
        switch (typeFilaire)
        {
            case Varint: LireVarint(octets, ref position); break;
            case Fixe64: position += 8; break;
            case Delimite:
                // Pas « position += LireVarint(ref position) » : l'ancienne valeur de position serait lue AVANT l'appel.
                var longueur = checked((int)LireVarint(octets, ref position));
                position += longueur;
                break;
            case Fixe32: position += 4; break;
            default: throw new FormatException($"Type filaire {typeFilaire} inconnu.");
        }
        if (position > octets.Length)
            throw new FormatException("Message tronqué.");
    }
}
