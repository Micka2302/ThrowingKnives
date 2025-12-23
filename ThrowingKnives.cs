using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CSTimer = CounterStrikeSharp.API.Modules.Timers.Timer;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Drawing;
using System.Collections.Concurrent;
using KitsuneMenu;
using KitsuneMenu.Core;
using KitsuneMenu.Core.Interfaces;

namespace ThrowingKnives;

public class PluginConfig : BasePluginConfig
{
    [JsonPropertyName("KnifeAmount")]
    public int KnifeAmount { get; set; } = -1;

    [JsonPropertyName("KnifeVelocity")]
    public float KnifeVelocity { get; set; } = 2250.0f;

    [JsonPropertyName("KnifeDamage")]
    public float KnifeDamage { get; set; } = 45.0f;

    [JsonPropertyName("KnifeHeadshotDamage")]
    public float KnifeHeadshotDamage { get; set; } = 130.0f;

    [JsonPropertyName("KnifeGravity")]
    public float KnifeGravity { get; set; } = 1.0f;

    [JsonPropertyName("KnifeAmountsByFlag")]
    [JsonConverter(typeof(StringIntDictionaryConverter))]
    public Dictionary<string, int> KnifeAmountsByFlag { get; set; } = new();

    [JsonPropertyName("DebugHits")]
    public bool DebugHits { get; set; } = false;

    [JsonPropertyName("KnifeElasticity")]
    public float KnifeElasticity { get; set; } = 0.2f;

    [JsonPropertyName("KnifeLifetime")]
    public float KnifeLifetime { get; set; } = 5.0f;

    [JsonPropertyName("KnifeTrailTime")]
    public float KnifeTrailTime { get; set; } = 3.0f;

    [JsonPropertyName("KnifeCooldown")]
    public float KnifeCooldown { get; set; } = 3.0f;

    [JsonPropertyName("KnifeFlags")]
    public List<string> KnifeFlags { get; set; } = [];

    [JsonPropertyName("ConfigVersion")]
    public override int Version { get; set; } = 6;
}

[MinimumApiVersion(352)]
public class Plugin : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "Throwing Knives";
    public override string ModuleDescription => "Throwing Knives plugin for CS2";
    public override string ModuleAuthor => "Cruze";
    public override string ModuleVersion => "1.0.6";

    public required PluginConfig Config { get; set; } = new();

    // Used for saving player knife cooldown, timers & hud
    private static TimeSpan _playerCooldownDuration = TimeSpan.FromSeconds(3);
    private readonly ConcurrentDictionary<int, DateTime> _playerCooldowns = new();
    private CSTimer?[] _playerCooldownTimers = new CSTimer[65];

    // Used for tracking player permission for throwing knives
    private Dictionary<int, bool> _playerHasPerms = new();

    // Used for tracking attacker active weapon when knife was thrown
    private Dictionary<string, uint?> _knivesThrown = new();

    // Used for tracking thrown knife amount
    private Dictionary<int, int> _knivesAvailable = new();

    // Used for trails
    private Dictionary<uint, Vector3> _knivesOldPos = new();
    private readonly Dictionary<int, Color> _playerTrailColors = new();
    private static readonly (string Name, Color Color)[] TrailPalette =
    {
        ("Bleu", Color.CornflowerBlue),
        ("Rouge", Color.IndianRed),
        ("Vert", Color.MediumSeaGreen),
        ("Violet", Color.MediumOrchid),
        ("Jaune", Color.Gold),
        ("Blanc", Color.White)
    };

    // Used for thrown knife model
    private static Dictionary<ushort, string> KnifePaths { get; } = new()
    {
        { 42, "weapons/models/knife/knife_default_ct/weapon_knife_default_ct.vmdl" },
        { 59, "weapons/models/knife/knife_default_t/weapon_knife_default_t.vmdl" },
        { 500, "weapons/models/knife/knife_bayonet/weapon_knife_bayonet.vmdl" },
        { 503, "weapons/models/knife/knife_css/weapon_knife_css.vmdl" },
        { 505, "weapons/models/knife/knife_flip/weapon_knife_flip.vmdl" },
        { 506, "weapons/models/knife/knife_gut/weapon_knife_gut.vmdl" },
        { 507, "weapons/models/knife/knife_karambit/weapon_knife_karambit.vmdl" },
        { 508, "weapons/models/knife/knife_m9/weapon_knife_m9.vmdl" },
        { 509, "weapons/models/knife/knife_tactical/weapon_knife_tactical.vmdl" },
        { 512, "weapons/models/knife/knife_falchion/weapon_knife_falchion.vmdl" },
        { 514, "weapons/models/knife/knife_bowie/weapon_knife_bowie.vmdl" },
        { 515, "weapons/models/knife/knife_butterfly/weapon_knife_butterfly.vmdl" },
        { 516, "weapons/models/knife/knife_push/weapon_knife_push.vmdl" },
        { 517, "weapons/models/knife/knife_cord/weapon_knife_cord.vmdl" },
        { 518, "weapons/models/knife/knife_canis/weapon_knife_canis.vmdl" },
        { 519, "weapons/models/knife/knife_ursus/weapon_knife_ursus.vmdl" },
        { 520, "weapons/models/knife/knife_navaja/weapon_knife_navaja.vmdl" },
        { 521, "weapons/models/knife/knife_outdoor/weapon_knife_outdoor.vmdl" },
        { 522, "weapons/models/knife/knife_stiletto/weapon_knife_stiletto.vmdl" },
        { 523, "weapons/models/knife/knife_talon/weapon_knife_talon.vmdl" },
        { 525, "weapons/models/knife/knife_skeleton/weapon_knife_skeleton.vmdl" },
        { 526, "weapons/models/knife/knife_kukri/weapon_knife_kukri.vmdl" }
    };

    public void OnConfigParsed(PluginConfig config)
    {
        Config = config;
        Config.KnifeAmountsByFlag ??= new();
        if (config.Version != Config.Version)
        {
            Logger.LogWarning("Configuration version mismatch (Expected: {0} | Current: {1})", Config.Version, config.Version);
        }

        if (Config.KnifeTrailTime > 0)
        {
            RegisterListener<Listeners.OnTick>(OnTick);
        }
        else
        {
            RemoveListener<Listeners.OnTick>(OnTick);
        }

        _playerCooldownDuration = TimeSpan.FromSeconds(Config.KnifeCooldown);
    }

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);
        EnsureKitsuneMenuConfigFiles();
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnEntityTakeDamage);
        KitsuneMenu.KitsuneMenu.Init();
        AddCommand("css_tk", "Ouvre le menu couleur de trail du couteau", (player, info) =>
        {
            if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
                return;

            KitsuneMenu.KitsuneMenu.ShowMenu(player, BuildTrailMenu(player));
        });

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers().Where(p => !p.IsBot && !p.IsHLTV))
            {
                _knivesAvailable[player.Slot] = GetKnifeAmountForPlayer(player);
                _playerHasPerms[player.Slot] = PlayerHasPerm(player, Config.KnifeFlags);
                EnsureTrailColor(player);
            }
        }
    }

    public override void Unload(bool hotReload)
    {
        base.Unload(hotReload);
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        RemoveListener<Listeners.OnEntityTakeDamagePre>(OnEntityTakeDamage);
        KitsuneMenu.KitsuneMenu.Cleanup();
    }

    private HookResult OnEntityTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (entity == null || !entity.IsValid || !entity.DesignerName.Equals("player"))
            return HookResult.Continue;

        var pawn = entity.As<CCSPlayerPawn>();
        if (pawn == null || !pawn.IsValid)
            return HookResult.Continue;

        var player = pawn.OriginalController.Get();
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        var thrownKnife = damageInfo.Inflictor.Value?.As<CPhysicsPropOverride>();

        if (thrownKnife == null || !thrownKnife.IsValid || !thrownKnife.DesignerName.Equals("prop_physics_override"))
            return HookResult.Continue;

        if (thrownKnife.Entity == null || !thrownKnife.Entity.Name.StartsWith("tknife_") ||
            !_knivesThrown.TryGetValue(thrownKnife.Entity.Name, out var activeWeapon)
                || activeWeapon == null)
            return HookResult.Continue;

        var hActiveWeapon = (CHandle<CBasePlayerWeapon>?)Activator.CreateInstance(typeof(CHandle<CBasePlayerWeapon>), activeWeapon);

        if (hActiveWeapon == null || !hActiveWeapon.IsValid)
            return HookResult.Continue;

        var attacker = thrownKnife.OwnerEntity.Value?.As<CCSPlayerPawn>();

        if (attacker == null || !attacker.IsValid)
            return HookResult.Continue;

        if (attacker.TeamNum == pawn.TeamNum)
        {
            thrownKnife.AcceptInput("Kill");
            return HookResult.Stop;
        }

        damageInfo.Inflictor.Raw = attacker.EntityHandle;
        damageInfo.Attacker.Raw = attacker.EntityHandle;
        damageInfo.Ability.Raw = (uint)activeWeapon;
        damageInfo.BitsDamageType = DamageTypes_t.DMG_SLASH;
        var hitGroup = damageInfo.HitGroupId;
        if (hitGroup == HitGroup_t.HITGROUP_INVALID)
        {
            var infoHitGroup = damageInfo.GetHitGroup();
            if (infoHitGroup != HitGroup_t.HITGROUP_INVALID)
            {
                hitGroup = infoHitGroup;
            }
        }
        if (hitGroup == HitGroup_t.HITGROUP_INVALID && pawn.LastHitGroup != HitGroup_t.HITGROUP_INVALID)
        {
            hitGroup = pawn.LastHitGroup;
        }

        // Heuristic head detection using impact point relative to player bbox
        var impactPoint = GetImpactPoint(damageInfo, thrownKnife, pawn);
        var collision = pawn.Collision;
        var mins = collision?.Mins ?? new CounterStrikeSharp.API.Modules.Utils.Vector(0, 0, 0);
        var maxs = collision?.Maxs ?? new CounterStrikeSharp.API.Modules.Utils.Vector(0, 0, 72);
        var origin = pawn.AbsOrigin ?? impactPoint;

        float height = maxs.Z - mins.Z;
        if (height < 1.0f) height = 72.0f;

        var localZ = impactPoint.Z - origin.Z;
        float fracZ = (localZ - mins.Z) / height;
        if (float.IsNaN(fracZ) || float.IsInfinity(fracZ))
            fracZ = 0.0f;

        bool heurHead = fracZ >= 0.8f;

        if (hitGroup == HitGroup_t.HITGROUP_INVALID)
        {
            hitGroup = heurHead ? HitGroup_t.HITGROUP_HEAD : HitGroup_t.HITGROUP_CHEST;
        }
        else if (hitGroup != HitGroup_t.HITGROUP_HEAD && heurHead)
        {
            hitGroup = HitGroup_t.HITGROUP_HEAD;
        }

        var isHead = hitGroup == HitGroup_t.HITGROUP_HEAD;
        damageInfo.HitGroupId = hitGroup;
        if (isHead)
        {
            damageInfo.BitsDamageType |= DamageTypes_t.DMG_HEADSHOT;
        }

        damageInfo.Damage = isHead
            ? Config.KnifeHeadshotDamage
            : Config.KnifeDamage;

        if (Config.DebugHits && attacker.IsValid)
        {
            var attackerController = attacker.OriginalController?.Get();
            if (attackerController != null && attackerController.IsValid)
            {
                var hitText = hitGroup == HitGroup_t.HITGROUP_HEAD ? "tête" : "corps";
                attackerController.PrintToChat($"[ThrowingKnives] Hit: {hitText} (dmg {damageInfo.Damage:F1})");
            }
        }

        thrownKnife.AcceptInput("Kill");

        return HookResult.Changed;
    }

    public void OnMapStart(string map) { }

    public void OnTick()
    {
        var knives = Utilities.FindAllEntitiesByDesignerName<CPhysicsPropOverride>("prop_physics_override");

        foreach (var knife in knives)
        {
            if (knife == null || !knife.IsValid || knife.AbsOrigin == null || knife.Entity == null || !knife.Entity.Name.StartsWith("tknife_") || !_knivesOldPos.TryGetValue(knife.Index, out var oldpos))
                continue;

            var knifePos = (Vector3)knife.AbsOrigin;

            if (!ShouldUpdateTrail(knifePos, oldpos)) continue;

            var owner = knife.OwnerEntity.Value?.As<CCSPlayerPawn>();
            var ownerController = owner?.OriginalController?.Get();

            if (owner == null || !owner.IsValid)
                continue;

            var trailColor = GetTrailColor(ownerController, owner);
            CreateTrail(knifePos, oldpos, trailColor, lifetime: Config.KnifeTrailTime);
            _knivesOldPos[knife.Index] = knifePos;
        }
    }

    [ListenerHandler<Listeners.OnPlayerButtonsChanged>]
    public void OnPlayerButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        CBasePlayerWeapon? activeWeapon;
        if (pressed.HasFlag(PlayerButtons.Attack) &&
            _playerHasPerms.ContainsKey(player.Slot) && _playerHasPerms[player.Slot] &&
                (activeWeapon = player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value) != null && activeWeapon.IsValid &&
                    (activeWeapon.DesignerName.Contains("knife") || activeWeapon.DesignerName.Contains("bayonet")))
        {
            ThrowKnife(player, activeWeapon);
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerConnect(EventPlayerConnectFull @event, GameEventInfo @info)
    {
        var player = @event.Userid;

        if (player == null || !player.IsValid || player.IsBot || player.IsHLTV) return HookResult.Continue;

        _playerHasPerms[player.Slot] = PlayerHasPerm(player, Config.KnifeFlags);
        _knivesAvailable[player.Slot] = GetKnifeAmountForPlayer(player);
        EnsureTrailColor(player);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo @info)
    {
        _knivesOldPos.Clear();
        _knivesThrown.Clear();

        for (int i = 0; i < 65; i++)
        {
            _playerCooldownTimers[i]?.Kill();

            var player = Utilities.GetPlayerFromSlot(i);
            if (player == null || !player.IsValid ||
                player.Connected != PlayerConnectedState.PlayerConnected ||
                player.IsBot || player.IsHLTV) continue;

            _knivesAvailable[player.Slot] = GetKnifeAmountForPlayer(player);
        }
        return HookResult.Continue;
    }

    private void ThrowKnife(CCSPlayerController player, CBasePlayerWeapon? activeWeapon)
    {
        var pawn = player.PlayerPawn.Value;

        if (pawn == null) return;
        var playerKnifeLimit = GetKnifeAmountForPlayer(player);

        if (playerKnifeLimit != -1)
        {
            if (!_knivesAvailable.TryGetValue(player.Slot, out var knivesLeft))
            {
                knivesLeft = playerKnifeLimit;
                _knivesAvailable[player.Slot] = knivesLeft;
            }

            if (knivesLeft == 0)
            {
                return;
            }
        }

        if (_playerCooldowns.TryGetValue(player.Slot, out var lastTime))
        {
            if (DateTime.UtcNow - lastTime < _playerCooldownDuration)
                return;
        }

        ushort index;

        if (activeWeapon != null && activeWeapon.IsValid)
            index = activeWeapon.AttributeManager.Item.ItemDefinitionIndex;
        else
            index = (ushort)(player.TeamNum == 3 ? 42 : 59);

        if (!KnifePaths.TryGetValue(index, out var modelPath))
        {
            return;
        }

        var entity = Utilities.CreateEntityByName<CPhysicsPropOverride>("prop_physics_override")!;

        string entName = $"tknife_{Server.TickCount}";

        entity.Entity!.Name = entName;

        entity.CBodyComponent!.SceneNode!.Owner!.Entity!.Flags &= ~(uint)(1 << 2);

        entity.SetModel(modelPath);

        entity.DispatchSpawn();

        entity.Elasticity = Config.KnifeElasticity;
        entity.GravityScale = Config.KnifeGravity;
        entity.OwnerEntity.Raw = player.PlayerPawn.Raw;

        entity.Collision.CollisionGroup = (byte)CollisionGroup.COLLISION_GROUP_DEFAULT;

        float angleYaw = pawn.EyeAngles.Y * (float)Math.PI / 180f;
        float anglePitch = pawn.EyeAngles.X * (float)Math.PI / 180f;

        Vector3 rotation = (Vector3)pawn.AbsRotation!;

        Vector3 forward = new Vector3(
            (float)(Math.Cos(anglePitch) * Math.Cos(angleYaw)),
            (float)(Math.Cos(anglePitch) * Math.Sin(angleYaw)),
            (float)-Math.Sin(anglePitch)
        );

        float spawnDistance = 64.0f;
        Vector3 spawnPosition = new Vector3(
            pawn.AbsOrigin!.X + forward.X * spawnDistance,
            pawn.AbsOrigin.Y + forward.Y * spawnDistance + 5,
            pawn.AbsOrigin.Z + forward.Z * spawnDistance + 50.0f
        );

        float throwStrength = Config.KnifeVelocity;
        Vector3 velocity = new Vector3(
            forward.X * throwStrength,
            forward.Y * throwStrength,
            forward.Z * throwStrength + 300.0f
        );

        entity.Teleport(spawnPosition, rotation, velocity);

        _knivesOldPos[entity.Index] = spawnPosition;
        _knivesThrown[entName] = activeWeapon?.EntityHandle.Raw ?? null;

        entity.AddEntityIOEvent("Kill", entity, delay: Config.KnifeLifetime);

        int slot = player.Slot;

        _playerCooldowns[slot] = DateTime.UtcNow;
        _playerCooldowns.TryGetValue(slot, out lastTime);

        _playerCooldownTimers[slot] = AddTimer(0.2f, () =>
        {
            if (player == null || !player.IsValid || player.Connected != PlayerConnectedState.PlayerConnected)
            {
                _playerCooldownTimers[slot]?.Kill();
                return;
            }

            float cdLeft = Config.KnifeCooldown - (float)(DateTime.UtcNow - lastTime).TotalSeconds;
            int playerLimit = GetKnifeAmountForPlayer(player);
            int knivesLeft = playerLimit == -1 ? -1 : (_knivesAvailable.TryGetValue(slot, out var value) ? value : playerLimit);
            string knivesText = playerLimit == -1 ? "inf" : knivesLeft.ToString();

            if (cdLeft <= 0)
            {
                player.PrintToCenterAlert($"Couteau pret | Restants: {knivesText}");
                _playerCooldownTimers[slot]?.Kill();
                return;
            }

            player.PrintToCenterAlert($"Recharge: {cdLeft:F1}s | Restants: {knivesText}");
        }, TimerFlags.REPEAT);

        if (playerKnifeLimit == -1) return;

        _knivesAvailable[player.Slot] -= 1;
    }

    public void CreateTrail(Vector3 position, Vector3 endposition, Color color, float width = 1.0f, float lifetime = 3.0f)
    {
        var beam = Utilities.CreateEntityByName<CEnvBeam>("env_beam");
        if (beam == null)
            return;

        beam.Width = width;
        beam.Render = color;
        beam.Teleport(position);
        beam.DispatchSpawn();

        beam.EndPos.X = endposition.X;
        beam.EndPos.Y = endposition.Y;
        beam.EndPos.Z = endposition.Z;
        Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");

        beam.AddEntityIOEvent("Kill", beam, delay: lifetime);
    }

    public bool ShouldUpdateTrail(Vector3 position, Vector3 endposition, float minDistance = 5.0f)
    {
        return Distance(position, endposition) > minDistance;
    }

    public float Distance(Vector3 vector1, Vector3 vector2)
    {
        float dx = vector2.X - vector1.X;
        float dy = vector2.Y - vector1.Y;
        float dz = vector2.Z - vector1.Z;

        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private void EnsureKitsuneMenuConfigFiles()
    {
        try
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            if (string.IsNullOrEmpty(assemblyDir))
                return;

            var sharedDir = Path.Combine(Server.GameDirectory, "csgo", "addons", "counterstrikesharp", "shared", "KitsuneMenu");
            var mappings = new (string Source, string Target)[]
            {
                ("menu_config.jsonc", "kitsune_menu_config.jsonc"),
                ("kitsune_menu_config.jsonc", "kitsune_menu_config.jsonc"),
                ("menu_translations.jsonc", "kitsune_menu_translations.jsonc"),
                ("kitsune_menu_translations.jsonc", "kitsune_menu_translations.jsonc"),
            };

            foreach (var (sourceName, targetName) in mappings)
            {
                var sourcePath = Path.Combine(sharedDir, sourceName);
                var targetPath = Path.Combine(assemblyDir, targetName);

                if (File.Exists(sourcePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    File.Copy(sourcePath, targetPath, overwrite: true);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"KitsuneMenu config copy failed: {ex.Message}");
        }
    }

    private static bool IsValidVector(CounterStrikeSharp.API.Modules.Utils.Vector v)
    {
        return !(float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) ||
                 float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z)) &&
               (Math.Abs(v.X) > 0.01f || Math.Abs(v.Y) > 0.01f || Math.Abs(v.Z) > 0.01f);
    }

    private CounterStrikeSharp.API.Modules.Utils.Vector GetImpactPoint(CTakeDamageInfo info, CPhysicsPropOverride? knife, CCSPlayerPawn pawn)
    {
        if (IsValidVector(info.DamagePosition))
            return info.DamagePosition;

        if (knife != null && knife.IsValid && knife.AbsOrigin != null)
            return (CounterStrikeSharp.API.Modules.Utils.Vector)knife.AbsOrigin;

        if (pawn.AbsOrigin != null)
            return pawn.AbsOrigin;

        return new CounterStrikeSharp.API.Modules.Utils.Vector(0, 0, 0);
    }

    private void EnsureTrailColor(CCSPlayerController player)
    {
        if (_playerTrailColors.ContainsKey(player.Slot))
            return;

        _playerTrailColors[player.Slot] = GetDefaultTrailColor(player.TeamNum);
    }

    private Color GetTrailColor(CCSPlayerController? player, CCSPlayerPawn? pawn)
    {
        if (player != null && _playerTrailColors.TryGetValue(player.Slot, out var color))
            return color;

        var teamNum = pawn?.TeamNum ?? player?.TeamNum ?? 2;
        var fallback = GetDefaultTrailColor(teamNum);

        if (player != null)
            _playerTrailColors[player.Slot] = fallback;

        return fallback;
    }

    private Color GetDefaultTrailColor(byte teamNum)
    {
        return teamNum == 3 ? Color.Blue : Color.Red;
    }

    private IMenu BuildTrailMenu(CCSPlayerController player)
    {
        var defaultName = TrailPalette.First().Name;
        if (_playerTrailColors.TryGetValue(player.Slot, out var current))
        {
            var found = TrailPalette.FirstOrDefault(p => p.Color.ToArgb() == current.ToArgb());
            if (!string.IsNullOrEmpty(found.Name))
                defaultName = found.Name;
        }
        else
        {
            EnsureTrailColor(player);
        }

        var menu = KitsuneMenu.KitsuneMenu.Create("Couleur du trail")
            .AddChoice("Couleur", TrailPalette.Select(p => p.Name).ToArray(), defaultName, (p, choice) =>
            {
                var selected = TrailPalette.FirstOrDefault(c => c.Name.Equals(choice, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(selected.Name))
                {
                    _playerTrailColors[p.Slot] = selected.Color;
                    p.PrintToChat($"[ThrowingKnives] Trail: {selected.Name}");
                }
            })
            .Build();

        return menu;
    }

    private int GetKnifeAmountForPlayer(CCSPlayerController player)
    {
        var matches = Config.KnifeAmountsByFlag
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) &&
                          (AdminManager.PlayerHasPermissions(player, kvp.Key) || AdminManager.PlayerInGroup(player, kvp.Key)))
            .Select(kvp => kvp.Value);

        if (matches.Any())
            return matches.Max();

        return Config.KnifeAmount;
    }

    public static bool PlayerHasPerm(CCSPlayerController player, List<string> flags)
    {
        bool access = false;

        if (flags.Count() == 0)
        {
            access = true;
        }
        else
        {
            foreach (var flag in flags)
            {
                if (string.IsNullOrWhiteSpace(flag) || AdminManager.PlayerHasPermissions(player, flag) || AdminManager.PlayerInGroup(player, flag))
                {
                    access = true;
                    break;
                }
            }
        }
        return access;
    }
}

// Allows reading integers or numeric strings (and "inf"/"-1") in KnifeAmountsByFlag without breaking config parsing.
public class StringIntDictionaryConverter : JsonConverter<Dictionary<string, int>>
{
    public override Dictionary<string, int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            var key = reader.GetString() ?? string.Empty;
            if (!reader.Read())
                throw new JsonException();

            int value;
            switch (reader.TokenType)
            {
                case JsonTokenType.Number when reader.TryGetInt32(out value):
                    dict[key] = value;
                    break;
                case JsonTokenType.String:
                    var strVal = reader.GetString();
                    if (string.Equals(strVal, "inf", StringComparison.OrdinalIgnoreCase))
                    {
                        dict[key] = -1;
                    }
                    else if (int.TryParse(strVal, out value))
                    {
                        dict[key] = value;
                    }
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return dict;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, int> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var kvp in value)
        {
            writer.WriteNumber(kvp.Key, kvp.Value);
        }
        writer.WriteEndObject();
    }
}
