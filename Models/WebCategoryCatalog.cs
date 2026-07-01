namespace EduGuardAgent.Models;

/// <summary>
/// One filterable web-content category: a curated set of representative domains
/// (blocked at DNS/hosts level, works in every browser) plus high-signal hostname
/// keyword tokens used by the extension to auto-categorise sites that aren't in the
/// curated list. These are safety categories — always hard-enforced (a matching site
/// simply doesn't load), regardless of supervision mode.
/// </summary>
internal sealed record WebCategory(
    string Key,
    string DisplayName,
    string Description,
    string Glyph,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> Keywords)
{
    /// <summary>
    /// Page-content vocabulary for the extension's on-device text scoring (word-boundary
    /// matched). Strong terms are near-unambiguous for the category; weak terms only add
    /// supporting weight. Empty lists mean the category is not content-scanned.
    /// </summary>
    public IReadOnlyList<string> StrongTerms { get; init; } = [];
    public IReadOnlyList<string> WeakTerms { get; init; } = [];
}

internal static class WebCategoryCatalog
{
    /// <summary>All categories, in display order.</summary>
    public static readonly IReadOnlyList<WebCategory> All =
    [
        new WebCategory(
            Key: "adult",
            DisplayName: "Adult & pornography",
            Description: "Explicit sexual content, pornography and cam sites.",
            Glyph: "",
            Domains:
            [
                "pornhub.com", "xvideos.com", "xnxx.com", "xhamster.com", "redtube.com",
                "youporn.com", "spankbang.com", "youjizz.com", "tube8.com", "porn.com",
                "brazzers.com", "onlyfans.com", "chaturbate.com", "livejasmin.com", "cam4.com",
                "bongacams.com", "stripchat.com", "myfreecams.com", "adultfriendfinder.com",
                "hentaihaven.xxx", "nhentai.net", "e-hentai.org", "rule34.xxx", "motherless.com",
                "eporner.com", "txxx.com", "hqporner.com", "porntrex.com", "fapello.com",
                "erome.com", "ashemaletube.com", "beeg.com", "tnaflix.com", "empflix.com",
                "porn300.com", "3movs.com", "porndig.com", "vporn.com", "drtuber.com",
                "sunporno.com", "porngo.com", "gotporn.com", "voyeurhit.com", "nuvid.com",
                "keezmovies.com", "extremetube.com", "slutload.com", "thumbzilla.com",
                "pornone.com", "xvideos2.com", "camwhores.tv", "imlive.com", "camsoda.com",
                "flirt4free.com", "streamate.com", "18comix.com", "hanime.tv", "hclips.com",
                "xmoviesforyou.tv", "definebabe.com", "dirtyship.com", "xtits.com",
                "tukif.com", "voir-porno.com", "jacquieetmicheltv.net",
                "lovehoney.com", "lovehoney.fr", "adamandeve.com", "dorcelstore.com",
                "dorcel.com", "espaceplaisir.fr", "passagedudesir.fr", "lelo.com",
                "satisfyer.com", "womanizer.com", "we-vibe.com", "fleshlight.com",
                "kiiroo.com", "lovense.com", "tracyssdog.com", "sextoys.fr",
            ],
            Keywords:
            [
                "porn", "xxx", "xnxx", "xvideos", "xhamster", "redtube", "youjizz",
                "hentai", "camgirl", "camsex", "escort", "nsfw", "onlyfans", "brazzers",
                "fapp", "fapello", "milf", "bdsm", "rule34", "camwhore", "nude", "boobs",
                "spankbang", "chaturbate", "livejasmin", "stripchat", "hqporner", "porntrex",
                "erome", "voirporno", "jacquieetmichel",
                "sextoy", "sexshop", "sex-shop", "adulttoy", "lovehoney", "dorcel",
                "fleshlight", "lovense",
            ])
        {
            StrongTerms =
            [
                "porn", "porno", "pornographie", "pornography", "sextoy", "sextoys",
                "sex toy", "sex toys", "sexshop", "sex shop", "godemichet", "gode",
                "godes", "dildo", "dildos", "vibromasseur", "vibromasseurs", "vibrator",
                "vibrators", "plug anal", "butt plug", "masturbateur", "masturbator",
                "fleshlight", "bondage", "bdsm", "fellation", "blowjob", "cunnilingus",
                "gangbang", "hentai", "camgirl", "escort girl", "xxx",
            ],
            WeakTerms =
            [
                "érotique", "erotique", "erotic", "sexy", "lingerie", "lubrifiant",
                "lubricant", "nudes", "topless", "hardcore", "nsfw",
            ],
        },

        new WebCategory(
            Key: "gambling",
            DisplayName: "Gambling & betting",
            Description: "Casinos, sports betting, poker and lottery sites.",
            Glyph: "",
            Domains:
            [
                "bet365.com", "williamhill.com", "pokerstars.com", "888casino.com", "888poker.com",
                "bwin.com", "betfair.com", "unibet.com", "draftkings.com", "fanduel.com",
                "stake.com", "roobet.com", "betway.com", "ladbrokes.com", "paddypower.com",
                "casino.com", "partypoker.com", "pokerstars.net", "winamax.fr", "betclic.com",
                "coral.co.uk", "skybet.com", "ggpoker.com", "1xbet.com", "parionssport.fdj.fr",
                "casumo.com", "leovegas.com", "betsson.com", "mrgreen.com", "pmu.fr",
                "zeturf.fr", "vbet.fr", "bwin.fr", "unibet.fr", "joacasino.com",
                "circusbet.fr", "genybet.fr", "netbet.fr", "betclic.fr",
            ],
            Keywords:
            [
                "casino", "poker", "betting", "sportsbet", "gambling", "roulette", "slots",
                "blackjack", "bookmaker", "1xbet", "sportsbook", "wager", "parionssport",
                "paris-sportifs", "machinesasous",
            ])
        {
            StrongTerms =
            [
                "casino en ligne", "online casino", "machine à sous", "machines à sous",
                "slot machine", "slot machines", "paris sportifs", "poker en ligne",
                "online poker", "blackjack", "roulette en ligne", "bookmaker", "sportsbook",
                "free spins", "tours gratuits", "bonus de bienvenue", "welcome bonus",
                "betting odds", "mise minimale", "dépôt minimum",
            ],
            WeakTerms =
            [
                "casino", "poker", "jackpot", "pari", "paris", "cotes", "gambling",
                "jeux d'argent", "mise", "croupier",
            ],
        },

        new WebCategory(
            Key: "violence",
            DisplayName: "Violence & gore",
            Description: "Graphic violence, gore and shock imagery.",
            Glyph: "",
            Domains:
            [
                "bestgore.com", "theync.com", "documentingreality.com", "kaotic.com",
                "goregrish.com", "watchpeopledie.tv", "seegore.com", "hoodsite.com",
                "liveleak.com", "deathaddict.com", "crazyshit.com",
            ],
            Keywords:
            [
                "bestgore", "gore", "watchpeopledie", "kaotic", "deathvideo", "beheading",
            ]),

        new WebCategory(
            Key: "drugs",
            DisplayName: "Drugs",
            Description: "Recreational drug sales, promotion and how-to content.",
            Glyph: "",
            Domains:
            [
                "leafly.com", "weedmaps.com", "erowid.org", "dancesafe.org", "silkroaddrugs.org",
                "buymyweedonline.com", "cannabis.net", "growweedeasy.com", "hightimes.com",
            ],
            Keywords:
            [
                "buyweed", "buydrugs", "cocaine", "psychedelic", "darknetmarket", "cannabisstore",
            ]),

        new WebCategory(
            Key: "weapons",
            DisplayName: "Weapons",
            Description: "Firearms, ammunition and weapon sales.",
            Glyph: "",
            Domains:
            [
                "gunbroker.com", "budsgunshop.com", "palmettostatearmory.com", "brownells.com",
                "cheaperthandirt.com", "armslist.com", "gunsamerica.com", "ammoseek.com",
                "sportsmansguide.com", "opticsplanet.com", "midwayusa.com", "impactguns.com",
                "primaryarms.com", "aimsurplus.com", "gunsinternational.com",
            ],
            Keywords:
            [
                "gunshop", "buyguns", "firearmsale", "ammoseek", "gunbroker", "armslist",
            ]),

        new WebCategory(
            Key: "hate",
            DisplayName: "Hate & extremism",
            Description: "Hate speech, extremist and radicalisation content.",
            Glyph: "",
            Domains:
            [
                "stormfront.org", "dailystormer.in", "kiwifarms.net", "gab.com", "4chan.org",
                "8kun.top", "bitchute.com", "vk.com",
            ],
            Keywords:
            [
                "stormfront", "dailystormer", "kiwifarms",
            ]),

        new WebCategory(
            Key: "dating",
            DisplayName: "Dating & hookups",
            Description: "Dating apps and hookup sites.",
            Glyph: "",
            Domains:
            [
                "tinder.com", "bumble.com", "match.com", "okcupid.com", "pof.com",
                "badoo.com", "grindr.com", "hinge.co", "ashleymadison.com", "meetic.fr",
                "adultfriendfinder.com", "fling.com", "seeking.com", "adopteunmec.com",
                "happn.com", "zoosk.com", "eharmony.com", "attractiveworld.com",
            ],
            Keywords:
            [
                "hookup", "ashleymadison", "adultdating",
            ]),

        new WebCategory(
            Key: "piracy",
            DisplayName: "Piracy & warez",
            Description: "Torrent trackers and illegal streaming/download sites.",
            Glyph: "",
            Domains:
            [
                "thepiratebay.org", "1337x.to", "rarbg.to", "yts.mx", "nyaa.si",
                "kickasstorrents.to", "torrentgalaxy.to", "limetorrents.lol", "eztv.re",
                "fmovies.to", "123movies.net", "putlocker.vip", "sflix.to", "soap2day.to",
                "torrentz2.eu", "zooqle.com", "torrentdownloads.me", "isohunt.to",
                "yourbittorrent.com", "1337x.st", "movierulz.com", "gomovies.sx",
                "yesmovies.ag", "vumoo.to", "cuevana3.io",
                "cpasbien.tel", "zone-telechargement.ws", "wawacity.tv", "voirfilms.co",
                "french-stream.cool", "papadustream.cx", "coflix.tv", "vostfree.cx",
                "yggtorrent.wf", "torrent9.pm",
            ],
            Keywords:
            [
                "piratebay", "1337x", "torrent", "putlocker", "123movies", "fmovies",
                "soap2day", "warez", "crackedsoftware", "cpasbien", "zone-telechargement",
                "wawacity", "yggtorrent", "streaming-gratuit", "filmstreamvf",
            ]),
    ];

    private static readonly Dictionary<string, WebCategory> ByKey =
        All.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string key, out WebCategory category) =>
        ByKey.TryGetValue(key, out category!);

    public static bool IsKnown(string key) => ByKey.ContainsKey(key);

    /// <summary>Curated domains for the given enabled category keys, de-duplicated.</summary>
    public static IReadOnlyList<string> DomainsFor(IEnumerable<string> enabledKeys)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in enabledKeys)
        {
            if (ByKey.TryGetValue(key, out var cat))
                foreach (var d in cat.Domains)
                    set.Add(d);
        }
        return set.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Weighted page-content vocabularies for the enabled categories, shaped for the
    /// extension's shield-state payload: { categoryKey: { strong: [...], weak: [...] } }.
    /// Categories without content terms are omitted.
    /// </summary>
    public static IReadOnlyDictionary<string, object> ContentTermsFor(IEnumerable<string> enabledKeys)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in enabledKeys)
        {
            if (ByKey.TryGetValue(key, out var cat) && cat.StrongTerms.Count > 0)
            {
                result[cat.Key] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["strong"] = cat.StrongTerms,
                    ["weak"] = cat.WeakTerms,
                };
            }
        }

        return result;
    }

    /// <summary>Hostname keyword tokens for the given enabled category keys, de-duplicated.</summary>
    public static IReadOnlyList<string> KeywordsFor(IEnumerable<string> enabledKeys)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in enabledKeys)
        {
            if (ByKey.TryGetValue(key, out var cat))
                foreach (var k in cat.Keywords)
                    set.Add(k);
        }
        return set.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
