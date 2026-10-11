using System.Diagnostics;

namespace Styles.Client;

/// <summary>Ce qu'a coûté un scénario sur le réseau : nombre de requêtes HTTP, octets envoyés et reçus (corps seulement).</summary>
public sealed class Mesure
{
    private int _requetes;
    private long _octetsEnvoyes, _octetsRecus;

    public int Requetes => _requetes;
    public long OctetsEnvoyes => Interlocked.Read(ref _octetsEnvoyes);
    public long OctetsRecus => Interlocked.Read(ref _octetsRecus);
    public TimeSpan Duree { get; internal set; }

    internal void Requete(long envoyes)
    {
        Interlocked.Increment(ref _requetes);
        Interlocked.Add(ref _octetsEnvoyes, envoyes);
    }

    internal void Recu(long octets) => Interlocked.Add(ref _octetsRecus, octets);

    public override string ToString() =>
        $"{Requetes} requête(s), {OctetsEnvoyes} o envoyés, {OctetsRecus} o reçus, {Duree.TotalMilliseconds:0} ms";
}

/// <summary>
/// Compte les requêtes et les octets des corps (les en-têtes ne sont pas comptés : HTTP/2 les compresse, HTTP/1.1 non),
/// et peut ajouter une latence par requête pour simuler un réseau mobile.
/// </summary>
public sealed class CompteurHttp : DelegatingHandler
{
    public Mesure Mesure { get; private set; } = new();

    public TimeSpan Latence { get; set; }

    public CompteurHttp() : base(new SocketsHttpHandler()) { }

    public CompteurHttp(HttpMessageHandler interieur) : base(interieur) { }

    /// <summary>Commence une nouvelle mesure et la rend.</summary>
    public Mesure Nouvelle() => Mesure = new Mesure();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage requete, CancellationToken annulation)
    {
        var mesure = Mesure;
        long envoyes = 0;
        if (requete.Content is not null)
        {
            await requete.Content.LoadIntoBufferAsync(annulation);
            envoyes = requete.Content.Headers.ContentLength ?? 0;
        }
        mesure.Requete(envoyes);
        if (Latence > TimeSpan.Zero)
            await Task.Delay(Latence, annulation);

        var reponse = await base.SendAsync(requete, annulation);
        var original = reponse.Content;
        var flux = await original.ReadAsStreamAsync(annulation);
        var compte = new StreamContent(new FluxCompteur(flux, mesure.Recu));
        foreach (var enTete in original.Headers)
            compte.Headers.TryAddWithoutValidation(enTete.Key, enTete.Value);
        reponse.Content = compte;   // les trailers restent sur la réponse
        return reponse;
    }

    private sealed class FluxCompteur(Stream interieur, Action<long> compter) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var lus = interieur.Read(buffer, offset, count);
            compter(lus);
            return lus;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken annulation = default)
        {
            var lus = await interieur.ReadAsync(buffer, annulation);
            compter(lus);
            return lus;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken annulation) =>
            ReadAsync(buffer.AsMemory(offset, count), annulation).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                interieur.Dispose();
            base.Dispose(disposing);
        }
    }
}

public static class Chrono
{
    /// <summary>Exécute un scénario avec une mesure neuve, et la rend avec son résultat.</summary>
    public static async Task<(T Resultat, Mesure Mesure)> Mesurer<T>(CompteurHttp compteur, Func<Task<T>> scenario)
    {
        var mesure = compteur.Nouvelle();
        var chrono = Stopwatch.StartNew();
        var resultat = await scenario();
        mesure.Duree = chrono.Elapsed;
        return (resultat, mesure);
    }
}
