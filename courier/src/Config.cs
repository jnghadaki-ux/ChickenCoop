using CounterStrikeSharp.API.Core;

namespace ChickenCourier;

/// <summary>configs/plugins/ChickenCourier/ChickenCourier.json, created with these defaults on first load.</summary>
public sealed class CourierConfig : BasePluginConfig
{
    /// <summary>The mode is on (css_courier flips this live).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maps (or prefixes ending in *) where the courier replaces the normal bomb carrier.</summary>
    public List<string> Maps { get; set; } = new() { "de_*" };

    /// <summary>Fewer players than this on teams: normal bomb rules for the round.</summary>
    public int MinPlayers { get; set; } = 2;

    public string ChatPrefix { get; set; } = "{orange}[Courier]{default}";

    /// <summary>Fallback message language; players get their own CS2 language when a file exists.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Flags for css_courier (toggle) and css_courier_spawn.</summary>
    public List<string> AdminPermissions { get; set; } = new() { "@css/generic" };

    /// <summary>Chicken model: models/chicken/chicken.vmdl, chicken_polish.vmdl or chicken_silkie.vmdl.</summary>
    public string Model { get; set; } = "models/chicken/chicken.vmdl";
    public float Scale { get; set; } = 1.15f;

    /// <summary>Courage: CT damage drains it; at zero the chicken panics. It never dies, so the bomb is never lost.</summary>
    public double Courage { get; set; } = 120;
    /// <summary>Courage lost per point of CT damage.</summary>
    public double DamageToCourage { get; set; } = 1.0;
    /// <summary>Seconds the chicken runs around in a panic (no handler can lead it).</summary>
    public double PanicSeconds { get; set; } = 6;
    /// <summary>Courage the chicken comes back with after a panic.</summary>
    public double CourageAfterPanic { get; set; } = 70;
    /// <summary>Courage regained per second after 3 seconds without being hit.</summary>
    public double CourageRegen { get; set; } = 4;

    /// <summary>A Terrorist presses E this close to the chicken to become its handler (it follows them).</summary>
    public float HandlerRange { get; set; } = 130f;
    /// <summary>A handler further than this from the chicken lets go of it.</summary>
    public float LeashRange { get; set; } = 900f;
    /// <summary>With no handler for this long, the nearest Terrorist (within AutoRange) takes over. 0 = off (bots always auto-handle).</summary>
    public double AutoHandlerSeconds { get; set; } = 10;
    public float AutoRange { get; set; } = 700f;

    /// <summary>Seconds inside a bombsite (with the handler close) for the chicken to lay the bomb.</summary>
    public double LaySeconds { get; set; } = 4;
    /// <summary>The handler must stay this close while the chicken lays.</summary>
    public float LayHandlerRange { get; set; } = 400f;
    /// <summary>Bombsite radius around the site centre when a map's bombsite bounds can't be read.</summary>
    public float SiteRadius { get; set; } = 280f;

    /// <summary>Counter-Terrorists press E this close to scare the chicken (short panic, some courage lost).</summary>
    public bool CtCanScare { get; set; } = true;
    public float ScareRange { get; set; } = 150f;
    public double ScareCourage { get; set; } = 30;
    public double ScarePanicSeconds { get; set; } = 2.5;
    public double ScareCooldownSeconds { get; set; } = 8;

    /// <summary>When the chicken panics, it drops the bomb where it stands (Terrorists can pick it up and plant normally).</summary>
    public bool DropBombOnPanic { get; set; } = false;

    /// <summary>Money: handler when the bomb is laid, the CT who breaks its nerve, a CT who scares it.</summary>
    public int LayReward { get; set; } = 300;
    public int PanicReward { get; set; } = 150;
    public int ScareReward { get; set; } = 50;

    /// <summary>Floating "BOMB COURIER" label with a courage bar, seen by everyone.</summary>
    public bool Marker { get; set; } = true;
    /// <summary>Show laying progress in the centre of the handler's screen.</summary>
    public bool CentreHud { get; set; } = true;
    /// <summary>Clucks now and then so everyone can hear where it is (seconds, 0 = off).</summary>
    public double ClucksEverySeconds { get; set; } = 6;
}
