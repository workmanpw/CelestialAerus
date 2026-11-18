using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.AerusBuffs;
using CelestialAerus.Configs;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Players
{
    // Brigitte's Charm logic (2026-11-15). The charm item sets charmEquipped
    // every tick while worn; everything here keys off that flag.
    //
    // WHAT IT DOES (Peyton's spec):
    //  - Every accessory effect (curated implementation — see ApplyAccessoryEffects).
    //    Sniper scope stats WITHOUT the zoom (player.scope is never set).
    //  - +600 defense lives on the item itself (Item.defense).
    //  - 600 HP/s and 600 mana/s: +10/tick direct, clamped — NOT Heal(), which
    //    would spam heal effects 60 times a second.
    //  - One of every vanilla minion and sentry, plus the mod's weapon
    //    minions, at flat 6,000 typeless damage. Sentries follow the
    //    player like minions instead of sitting on the floor.
    //  - A continuous radial projectile storm: a fresh ring of 10
    //    randomly picked projectiles every tick (every vanilla
    //    projectile that can fly and hurt enemies, minus hooks,
    //    destructive explosives, and player-held styles; spears/flails
    //    are converted to flying projectiles), plus an on-hit cascade.
    public class CharmPlayer : ModPlayer
    {
        public bool charmEquipped;

        // Flat damage for every charm summon and storm projectile. Typeless
        // (DamageClass.Generic) so no class bonus stacks on top of it.
        // Config-backed (CharmConfig.CharmDamage, default 6000) — read
        // live, so a config change applies to newly spawned minions
        // and storm rings.
        public static int CharmDamage => ModContent.GetInstance<CharmConfig>().CharmDamage;

        // Storm projectiles live at most 5 seconds (300 ticks) — long
        // enough for any of them to cross the whole combat radius, short
        // enough that long-lived drifters (clouds, boomerangs) recycle
        // their slots. The 60-active cap is the real performance guard.
        private const int StormProjectileLifetime = 300;

        // Local immunity frames per ally/projectile: 20 ticks = 3 hits/s each.
        public const int CharmLocalCooldown = 20;

        private int minionTimer;
        private int cascadeCooldown;
        private int stormTimer;
        private bool charmWasEquipped;

        // Dummy items for the accessory effects that 1.4.4 tracks as Item
        // references (the game checks them for null/IsAir, not for being
        // equipped). Created lazily on first use.
        private Item starVeilDummy;
        private Item honeyCombDummy;
        private Item brainDummy;

        // Known vanilla minions that are not marked as standard slot-based
        // minions, plus the dynamically discovered standard minions/sentries.
        // Stardust Dragon and Desert Tiger use only their base summon types.
        private static readonly int[] KnownVanillaMinionTypes = new int[]
        {
            ProjectileID.BabySlime,          // 266 Slime Staff
            ProjectileID.Hornet,             // 373 Hornet Staff (NOT BabyHornet 198 — unused)
            ProjectileID.FlyingImp,          // 375 Imp Staff (NOT BabyImp 821 — shot by nothing)
            ProjectileID.BabySpider,         // 379 Spider Staff
            ProjectileID.Retanimini,         // 387 Optic Staff
            ProjectileID.Spazmamini,         // 388 Optic Staff
            ProjectileID.OneEyedPirate,      // 393 Pirate Staff
            ProjectileID.SoulscourgePirate,  // 394 Pirate Staff
            ProjectileID.PirateCaptain,      // 395 Pirate Staff
            ProjectileID.Pygmy,              // 191 Pygmy Staff
            ProjectileID.Pygmy2,             // 192 Pygmy Staff
            ProjectileID.Pygmy3,             // 193 Pygmy Staff
            ProjectileID.Pygmy4,             // 194 Pygmy Staff
            ProjectileID.Raven,              // 317 Raven Staff
            ProjectileID.DeadlySphere,       // 533 Deadly Sphere Staff
            ProjectileID.Tempest,            // 407 Tempest Staff
            ProjectileID.UFOMinion,          // 423 Xeno Staff
            ProjectileID.StardustDragon1,    // 625 Stardust Dragon Staff (head only)
            ProjectileID.StardustCellMinion, // 613 Stardust Cell Staff
            ProjectileID.FairyQueenMagicItemShot, // 931 Terraprisma
            ProjectileID.StormTigerGem,      // 831 Desert Tiger Staff (base tier only)
            ProjectileID.BatOfLight,         // 755 Sanguine Staff
            ProjectileID.EmpressBlade,       // 946 Blade Staff
            ProjectileID.AbigailMinion,      // 963 Abigail's Flower
            ProjectileID.FlinxMinion,        // 951 Flinx Staff
            ProjectileID.VampireFrog,        // 758 Vampire Frog Staff
            ProjectileID.BabyBird,           // 759 Birdie Rattle
            ProjectileID.StardustGuardian,   // 623 Stardust armor set bonus
            ProjectileID.Smolstar,           // 864 Smolstar (item 4758)
        };

        private static int[] vanillaMinionTypes;
        private static int[] vanillaSentryTypes;

        private static int[] VanillaMinionTypes
        {
            get
            {
                EnsureVanillaSummonTypes();
                return vanillaMinionTypes;
            }
        }

        private static int[] VanillaSentryTypes
        {
            get
            {
                EnsureVanillaSummonTypes();
                return vanillaSentryTypes;
            }
        }

        private static void EnsureVanillaSummonTypes()
        {
            if (vanillaMinionTypes != null)
                return;

            HashSet<int> minions = new HashSet<int>(KnownVanillaMinionTypes);
            List<int> sentries = new List<int>();
            Projectile probe = new Projectile();
            for (int type = 1; type < ProjectileID.Count; type++)
            {
                try
                {
                    probe.SetDefaults(type);

                    if (probe.friendly &&
                        probe.minion &&
                        type < ProjectileID.Sets.MinionSacrificable.Length &&
                        ProjectileID.Sets.MinionSacrificable[type])
                    {
                        minions.Add(type);
                    }

                    if (probe.friendly && probe.sentry)
                        sentries.Add(type);
                }
                catch (Exception)
                {
                    // Skip projectile types that fail to initialize
                    continue;
                }
            }

            vanillaMinionTypes = new int[minions.Count];
            minions.CopyTo(vanillaMinionTypes);
            Array.Sort(vanillaMinionTypes);
            vanillaSentryTypes = sentries.ToArray();
        }

        // The charm's own weapon minions remain a separate, unchanged list.
        private static readonly int[] WeaponMinionTypes = new int[]
        {
            ModContent.ProjectileType<AncientHatchetMinion>(),
            ModContent.ProjectileType<AndromedasWrathMinion>(),
            ModContent.ProjectileType<ArkOfTheCelestialsMinion>(),
            ModContent.ProjectileType<CelestialAerusMinion>(),
            ModContent.ProjectileType<CelestialTempestMinion>(),
            ModContent.ProjectileType<CosmosisMinion>(),
            ModContent.ProjectileType<NebulaBlazeSwordMinion>(),
            ModContent.ProjectileType<SpaceLaserRemoteMinion>(),
            ModContent.ProjectileType<FirstlightMinion>(),
            ModContent.ProjectileType<ThreadOfTheEmpyreanMinion>(),
        };

        // The charm's boss minions (2026-11-18): the 21 vanilla bosses as
        // living minions, restored with sprites, animations, and attacks
        // fixed against the decompiled vanilla code. Their helper
        // projectiles (Destroyer probes, Prime bombs, Betsy breath, saucer
        // deathrays/missiles, Pumpking blades) are spawned by their
        // parents — NOT listed here.
        private static readonly int[] BossMinionTypes = new int[]
        {
            ModContent.ProjectileType<BetsyMinion>(),
            ModContent.ProjectileType<DarkMageMinion>(),
            ModContent.ProjectileType<DestroyerMinion>(),
            ModContent.ProjectileType<DukeFishronMinion>(),
            ModContent.ProjectileType<EmpressOfLightMinion>(),
            ModContent.ProjectileType<EverscreamMinion>(),
            ModContent.ProjectileType<EyeOfCthulhuMinion>(),
            ModContent.ProjectileType<GolemMinion>(),
            ModContent.ProjectileType<IceQueenMinion>(),
            ModContent.ProjectileType<KingSlimeMinion>(),
            ModContent.ProjectileType<MartianSaucerMinion>(),
            ModContent.ProjectileType<MoonLordMinion>(),
            ModContent.ProjectileType<MourningWoodMinion>(),
            ModContent.ProjectileType<OgreMinion>(),
            ModContent.ProjectileType<PlanteraMinion>(),
            ModContent.ProjectileType<PumpkingMinion>(),
            ModContent.ProjectileType<RetinazerMinion>(),
            ModContent.ProjectileType<SantaNK1Minion>(),
            ModContent.ProjectileType<SkeletronMinion>(),
            ModContent.ProjectileType<SkeletronPrimeMinion>(),
            ModContent.ProjectileType<SpazmatismMinion>(),
            ModContent.ProjectileType<TrueEyeOfCthulhuMinion>(),
        };

        // The storm pool (2026-11-18, Peyton's rule): EVERY vanilla
        // projectile that can fly at enemies and hurt them — EXCEPT:
        //   - grappling hooks (NEVER — Peyton's explicit rule),
        //   - explosives that break/remove tiles or place liquids/tiles,
        //   - minion/sentry bodies (they ARE summons, not shots — their
        //     SHOT projectiles pass the test on their own and stay in),
        //   - hostile-only projectiles and non-damaging ones (pets,
        //     visuals, town-NPC shots),
        //   - player-held, channeled, or aimed styles (yoyos, held
        //     projectiles, magic missiles, Medusa rays, whips, drills...)
        //     — they cannot simply fly on their own.
        // Spears and flails ARE included: at spawn time their aiStyle is
        // swapped to Arrow so they simply fly (see FireStormVolley).
        // The pool is a static hand-curated list (see StormPoolCandidates
        // below) — the Nov 18 dynamic probe found ZERO valid types
        // in-game and is never to be revived.
        // NOTE: vanilla CanCutTiles() is deliberately NOT a filter here.
        // It means cutting grass/plants/pots, which nearly every
        // projectile can do (verified in the decompiled vanilla source:
        // it returns true for everything except 5 cloud aiStyles and ~25
        // exception types). Copilot filtered on it and the pool collapsed
        // to a handful of gas clouds — that is why the storm looked
        // broken. Tile DESTRUCTION is what matters, and that is covered
        // by Sets.Explosive, the falling-block placers, and the liquid
        // placers below.
        private static int[] safeStormProjectileTypes;

        // The storm's live-projectile cap lives in CharmConfig
        // (StormMaxProjectiles, default 200) — high enough for the
        // rings to actually build (60 would saturate in a fraction of
        // a second and the storm would look frozen), low enough to
        // leave most of the 1000 projectile slots for minions, the
        // player, and enemies.

        // Storm pool: the Nov 15 static list (323 vanilla types, curated
        // from the game's own defaults), cleaned by name (31 removed:
        // tile-breaking explosives, the WaterStream placer, whip shots,
        // Magic Missile and the Medusa Head ray — plus, 2026-11-18 at
        // Peyton's order, all sand and biome-altering projectiles: the
        // four Sandgun balls — SandBallGun places sand, Ebonsand /
        // Crimsand / PearlSandBallGun place biome sand that SPREADS
        // Corruption, Crimson, and Hallow — and FinalFractal, removed
        // out of caution: its aiStyle shares a case block containing a
        // PlaceTile call in the decompiled source, and it could not be
        // proven clean for 1.4.4) and filtered once more at runtime
        // through the vanilla Sets (see IsStormSafe) so any explosive
        // the name pass missed is still caught. The Clentaminator
        // solutions, the three powders, and the three biome waters were
        // never in the list. SandnadoFriendly STAYS (Peyton, 2026-11-18:
        // "Keep the sandnado") — it is sand-themed but places nothing
        // and alters no biome.
        // TILE/LIQUID AUDIT (2026-11-18, Peyton's order — "make sure
        // anything that can place or break tiles or remove or place
        // liquids is excluded"): verified against the decompiled vanilla
        // source. Every tile-breaker in Kill_ExplodeTiles (36 types:
        // all bombs, dynamites, grenade II/IV, rocket II/IV, mines,
        // cluster/mininuke II, Celeb2 rockets, SuperBombs, TNTBarrel,
        // ScarabBomb) is NOT in the pool; the liquid bombs (Wet/Honey/
        // Lava/Dry — the only liquid placers/removers) are NOT in the
        // pool; the waters, WaterStream, WebSpit, sandgun balls, rope
        // coils, DirtBall, IceBlock, all boulders, and the acorn are
        // NOT in the pool. The lookalikes were checked individually:
        // DD2ApprenticeStorm's grass-planting is gated to the Acorn
        // Slingshot only, BookStaffShot is a plain arrow, SuperStarSlash
        // runs none of the tile-placement code, and HellfireArrow /
        // MolotovCocktail / CannonballFriendly / StarCannonStar /
        // BoulderStaffOfEarth / Bubble break no tiles. The blocklist in
        // IsStormSafe names the placers explicitly so a future edit
        // cannot re-add one.
        // WHY STATIC: the Nov 18 dynamic probe (new Projectile() +
        // SetDefaults per type) found ZERO valid types in-game — the
        // pool came up empty and the storm never fired (client.log
        // 2026-11-18 02:22:44, "The charm's storm pool is empty").
        // Never probe defaults with a bare projectile again.
        private static readonly int[] StormPoolCandidates = new int[]
        {
            ProjectileID.EnchantedBoomerang, ProjectileID.Starfury, ProjectileID.Flamarang, ProjectileID.ThornChakram, ProjectileID.WoodenBoomerang, ProjectileID.LightDisc, ProjectileID.IceBoomerang, ProjectileID.SwordBeam,
            ProjectileID.IceBolt, ProjectileID.FrostBoltSword, ProjectileID.Mushroom, ProjectileID.TerraBeam, ProjectileID.LightBeam, ProjectileID.NightBeam, ProjectileID.EnchantedBeam, ProjectileID.PossessedHatchet,
            ProjectileID.SporeCloud, ProjectileID.ChlorophyteOrb, ProjectileID.FlowerPowPetal, ProjectileID.IceSickle, ProjectileID.Bananarang, ProjectileID.DeathSickle, ProjectileID.PaladinsHammerFriendly, ProjectileID.VampireKnife,
            ProjectileID.EatersBite, ProjectileID.BloodyMachete, ProjectileID.FlamingJack, ProjectileID.FruitcakeChakram, ProjectileID.OrnamentFriendly, ProjectileID.NorthPoleSpear, ProjectileID.NorthPoleSnowflake, ProjectileID.FlaironBubble,
            ProjectileID.InfluxWaver, ProjectileID.SeedlerNut, ProjectileID.SeedlerThorn, ProjectileID.FlyingKnife, ProjectileID.ShadowFlameKnife, ProjectileID.Meowmere, ProjectileID.StarWrath, ProjectileID.Spark,
            ProjectileID.IchorSplash, ProjectileID.TerrarianBeam, ProjectileID.SolarFlareRay, ProjectileID.Daybreak, ProjectileID.Ale, ProjectileID.DD2SquireSonicBoom, ProjectileID.MonkStaffT1Explosion, ProjectileID.MonkStaffT2Ghast,
            ProjectileID.MonkStaffT3_AltShot, ProjectileID.ThunderSpearShot, ProjectileID.FirstFractal, ProjectileID.BouncingShield, ProjectileID.Shroomerang, ProjectileID.CombatWrench, ProjectileID.OrnamentStar, ProjectileID.DripplerFlailExtraBall,
            ProjectileID.TentacleSpike, ProjectileID.NightsEdge, ProjectileID.TrueNightsEdge, ProjectileID.LightsBane, ProjectileID.BloodButcherer, ProjectileID.Volcano, ProjectileID.Excalibur,
            ProjectileID.TrueExcalibur, ProjectileID.TerraBlade2, ProjectileID.TerraBlade2Shot, ProjectileID.TheHorsemansBlade, ProjectileID.Trimarang, ProjectileID.Waffle, ProjectileID.WoodenArrowFriendly, ProjectileID.FireArrow,
            ProjectileID.Shuriken, ProjectileID.UnholyArrow, ProjectileID.JestersArrow, ProjectileID.Bullet, ProjectileID.Bone, ProjectileID.SpikyBall, ProjectileID.MeteorShot, ProjectileID.HellfireArrow,
            ProjectileID.ThrowingKnife, ProjectileID.Seed, ProjectileID.PoisonedKnife, ProjectileID.Flames, ProjectileID.CrystalBullet,
            ProjectileID.CrystalShard, ProjectileID.HolyArrow, ProjectileID.HallowStar, ProjectileID.CursedArrow, ProjectileID.CursedBullet, ProjectileID.BoneArrow, ProjectileID.FrostArrow, ProjectileID.RocketI,
            ProjectileID.RocketIII, ProjectileID.CopperCoin, ProjectileID.SilverCoin, ProjectileID.GoldCoin, ProjectileID.PlatinumCoin, ProjectileID.SnowBallFriendly, ProjectileID.FrostburnArrow, ProjectileID.Beenade,
            ProjectileID.ChlorophyteBullet, ProjectileID.ChlorophyteArrow, ProjectileID.BulletHighVelocity, ProjectileID.Stynger, ProjectileID.StyngerShrapnel, ProjectileID.PoisonDartBlowgun, ProjectileID.IchorArrow, ProjectileID.IchorBullet,
            ProjectileID.VenomArrow, ProjectileID.VenomBullet, ProjectileID.PartyBullet, ProjectileID.NanoBullet, ProjectileID.GoldenBullet, ProjectileID.CandyCorn, ProjectileID.JackOLantern, ProjectileID.RottenEgg,
            ProjectileID.Stake, ProjectileID.StarAnise, ProjectileID.RocketSnowmanI, ProjectileID.RocketSnowmanIII, ProjectileID.PulseBolt, ProjectileID.MolotovCocktail, ProjectileID.ElectrosphereMissile,
            ProjectileID.Electrosphere, ProjectileID.Xenopopper, ProjectileID.BeeArrow, ProjectileID.BoneArrowFromMerchant, ProjectileID.CrystalDart, ProjectileID.CursedDart, ProjectileID.IchorDart, ProjectileID.CursedDartFlame,
            ProjectileID.Hellwing, ProjectileID.ShadowFlameArrow, ProjectileID.JavelinFriendly, ProjectileID.NailFriendly, ProjectileID.FrostDaggerfish, ProjectileID.ToxicBubble, ProjectileID.BoneGloveProj, ProjectileID.ClothiersCurse,
            ProjectileID.PainterPaintball, ProjectileID.BoneJavelin, ProjectileID.BoneDagger, ProjectileID.ScutlixLaser, ProjectileID.VortexBeaterRocket, ProjectileID.PhantasmArrow, ProjectileID.MoonlordBullet, ProjectileID.MoonlordArrow,
            ProjectileID.MoonlordArrowTrail, ProjectileID.BlackBolt, ProjectileID.DD2PhoenixBowShot, ProjectileID.DD2BetsyArrow, ProjectileID.Celeb2Rocket, ProjectileID.Celeb2RocketLarge, ProjectileID.SuperStar, ProjectileID.SuperStarSlash,
            ProjectileID.Geode, ProjectileID.ClusterRocketI, ProjectileID.ClusterFragmentsI, ProjectileID.ClusterSnowmanRocketI, ProjectileID.BloodArrow, ProjectileID.ClusterSnowmanFragmentsI, ProjectileID.SantankMountRocket, ProjectileID.FairyQueenRangedItemShot,
            ProjectileID.VolatileGelatinBall, ProjectileID.StarCannonStar, ProjectileID.PewMaticHornShot, ProjectileID.SilverBullet, ProjectileID.ShimmerArrow, ProjectileID.VilethornBase, ProjectileID.VilethornTip, ProjectileID.BallofFire,
            ProjectileID.GreenLaser, ProjectileID.WaterBolt, ProjectileID.Flamelash, ProjectileID.DemonScythe, ProjectileID.RainbowRodBullet, ProjectileID.PurpleLaser, ProjectileID.MagicDagger, ProjectileID.CrystalStorm,
            ProjectileID.CursedFlameFriendly, ProjectileID.UnholyTridentFriendly, ProjectileID.AmethystBolt, ProjectileID.TopazBolt, ProjectileID.SapphireBolt, ProjectileID.EmeraldBolt, ProjectileID.RubyBolt, ProjectileID.DiamondBolt,
            ProjectileID.NettleBurstRight, ProjectileID.NettleBurstLeft, ProjectileID.NettleBurstEnd, ProjectileID.Wasp, ProjectileID.Leaf, ProjectileID.RainFriendly, ProjectileID.BloodRain, ProjectileID.BallofFrost,
            ProjectileID.MagnetSphereBolt, ProjectileID.HeatRay, ProjectileID.BoulderStaffOfEarth, ProjectileID.PoisonFang, ProjectileID.GoldenShowerFriendly, ProjectileID.ShadowBeamFriendly, ProjectileID.InfernoFriendlyBolt, ProjectileID.InfernoFriendlyBlast,
            ProjectileID.LostSoulFriendly, ProjectileID.Bat, ProjectileID.PineNeedleFriendly, ProjectileID.Blizzard, ProjectileID.VenomFang, ProjectileID.SpectreWrath, ProjectileID.FrostBoltStaff, ProjectileID.Typhoon,
            ProjectileID.Bubble, ProjectileID.Meteor1, ProjectileID.Meteor2, ProjectileID.Meteor3, ProjectileID.LaserMachinegunLaser, ProjectileID.ChargedBlasterOrb, ProjectileID.SoulDrain, ProjectileID.CrystalVileShardHead,
            ProjectileID.CrystalVileShardShaft, ProjectileID.ShadowFlame, ProjectileID.ToxicFlask, ProjectileID.ToxicCloud, ProjectileID.ToxicCloud2, ProjectileID.ToxicCloud3, ProjectileID.CrystalPulse, ProjectileID.CrystalPulse2,
            ProjectileID.SporeGas, ProjectileID.SporeGas2, ProjectileID.SporeGas3, ProjectileID.MinecartMechLaser, ProjectileID.AmberBolt, ProjectileID.NebulaArcanum, ProjectileID.NebulaArcanumSubshot, ProjectileID.NebulaArcanumExplosionShot,
            ProjectileID.NebulaArcanumExplosionShotShard, ProjectileID.NebulaBlaze1, ProjectileID.NebulaBlaze2, ProjectileID.LunarFlare, ProjectileID.SandnadoFriendly, ProjectileID.SpiritFlame, ProjectileID.SkyFracture, ProjectileID.DD2ApprenticeStorm,
            ProjectileID.ApprenticeStaffT3Shot, ProjectileID.BookStaffShot, ProjectileID.ManaCloakStar, ProjectileID.BeeCloakStar, ProjectileID.StarVeilStar, ProjectileID.StarCloakStar, ProjectileID.ThunderStaffShot, ProjectileID.SharpTears,
            ProjectileID.BookOfSkullsSkull, ProjectileID.TitaniumStormShard, ProjectileID.FairyQueenMagicItemShot, ProjectileID.PrincessWeapon, ProjectileID.WandOfSparkingSpark, ProjectileID.InsanityShadowFriendly, ProjectileID.WeatherPainShot, ProjectileID.WandOfFrostingFrost,
            ProjectileID.CannonballFriendly, ProjectileID.NurseSyringeHurt, ProjectileID.TreeGlobe, ProjectileID.WorldGlobe, ProjectileID.GelBalloon, ProjectileID.MoonGlobe, ProjectileID.Bee,
            ProjectileID.FlowerPetal, ProjectileID.CrystalLeaf, ProjectileID.CrystalLeafShot, ProjectileID.ScutlixLaserFriendly, ProjectileID.GiantBee, ProjectileID.MechanicWrench, ProjectileID.SolarCounter, ProjectileID.SolarWhipSwordExplosion,
            ProjectileID.StardustGuardianExplosion, ProjectileID.DaybreakExplosion, ProjectileID.PygmySpear, ProjectileID.HornetStinger, ProjectileID.ImpFireball, ProjectileID.MiniRetinaLaser, ProjectileID.MiniSharkron, ProjectileID.UFOLaser,
            ProjectileID.StardustCellMinionShot, ProjectileID.FrostBlastFriendly, ProjectileID.SpiderEgg, ProjectileID.RainbowCrystalExplosion, ProjectileID.DD2FlameBurstTowerT1Shot, ProjectileID.DD2FlameBurstTowerT2Shot, ProjectileID.DD2FlameBurstTowerT3Shot, ProjectileID.DD2BallistraProj,
            ProjectileID.HoundiusShootiusFireball
        };

        private static int[] StormPool
        {
            get
            {
                if (safeStormProjectileTypes == null)
                {
                    List<int> pool = new List<int>();
                    foreach (int type in StormPoolCandidates)
                    {
                        if (IsStormSafe(type))
                            pool.Add(type);
                    }

                    if (pool.Count == 0)
                        throw new InvalidOperationException("The charm's storm pool is empty.");

                    safeStormProjectileTypes = pool.ToArray();
                }

                return safeStormProjectileTypes;
            }
        }

        // Runtime safety net over the static candidate list. Uses ONLY
        // the vanilla Sets (static data, no projectile probing) plus a
        // small name blocklist for the liquid placers, which no Set
        // covers. Hooks are never in the candidate list to begin with.
        private static bool IsStormSafe(int type)
        {
            if (type <= ProjectileID.None || type >= ProjectileID.Count)
                return false;

            // Tile-breaking explosives (grenades, cluster/mininuke
            // rockets, Beenade, trap explosions, ...). Sets.Explosive
            // is the vanilla marker — it catches every tile-breaker
            // including ones the name pass missed.
            if (type < ProjectileID.Sets.Explosive.Length && ProjectileID.Sets.Explosive[type])
                return false;
            if (type < ProjectileID.Sets.IsAnNPCAttachedExplosive.Length && ProjectileID.Sets.IsAnNPCAttachedExplosive[type])
                return false;
            // Anything that places a block when it lands.
            if (type < ProjectileID.Sets.FallingBlockTileItem.Length && ProjectileID.Sets.FallingBlockTileItem[type] != null)
                return false;
            // Grappling hooks: NEVER (belt and suspenders — none are
            // in the candidate list, but a future edit must not add one).
            if (type < ProjectileID.Sets.SingleGrappleHook.Length && ProjectileID.Sets.SingleGrappleHook[type])
                return false;

            // Liquid/tile placers and biome converters (no Set covers them).
            // None of these are in the candidate list — this is belt and
            // suspenders so a future edit cannot re-add one by accident.
            // Tile-BREAKERS are covered above by Sets.Explosive (the
            // game's own marker); the names below are the placers and
            // liquid movers, which no Set marks.
            if (type == ProjectileID.HolyWater ||
                type == ProjectileID.UnholyWater ||
                type == ProjectileID.BloodWater ||
                type == ProjectileID.WaterStream ||
                type == ProjectileID.WebSpit ||
                type == ProjectileID.SandBallGun ||
                type == ProjectileID.EbonsandBallGun ||
                type == ProjectileID.CrimsandBallGun ||
                type == ProjectileID.PearlSandBallGun ||
                type == ProjectileID.Football ||
                type == ProjectileID.WetBomb ||
                type == ProjectileID.HoneyBomb ||
                type == ProjectileID.LavaBomb ||
                type == ProjectileID.DryBomb ||
                type == ProjectileID.DirtBall ||
                type == ProjectileID.IceBlock ||
                type == ProjectileID.RopeCoil ||
                type == ProjectileID.VineRopeCoil ||
                type == ProjectileID.SilkRopeCoil ||
                type == ProjectileID.WebRopeCoil ||
                type == ProjectileID.SandBallFalling ||
                type == ProjectileID.FinalFractal)
                return false;

            return true;
        }

        // On-hit cascade: the old Eternity's on-hit triggers — lightning
        // (Sky Fracture's splitting bolt), spectral orbs, petals.
        private static readonly int[] CascadePool = new int[]
        {
            ProjectileID.SkyFracture,
            ProjectileID.SpectreWrath,
            ProjectileID.FlowerPetal,
        };

        public override void ResetEffects()
        {
            charmEquipped = false;
        }

        public override void UpdateEquips()
        {
            if (!charmEquipped)
                return;

            ApplyAccessoryEffects();
        }

        public override void PostUpdate()
        {
            if (!charmEquipped)
            {
                if (charmWasEquipped)
                {
                    DismissCharmSentries();
                    charmWasEquipped = false;
                }
                return;
            }

            charmWasEquipped = true;

            // Refresh continuously while equipped; the short duration lets
            // the buff expire promptly when the charm is removed.
            Player.AddBuff(ModContent.BuffType<CelestialEmpowerment>(), 2);

            // 600 HP/s + 600 mana/s: +10 per tick, clamped. Silent — no
            // Heal() effect spam.
            if (Player.statLife < Player.statLifeMax2)
                Player.statLife = Math.Min(Player.statLife + 10, Player.statLifeMax2);
            if (Player.statMana < Player.statManaMax2)
                Player.statMana = Math.Min(Player.statMana + 10, Player.statManaMax2);

            // Minion army: check once per second, spawn anything missing,
            // refresh the timeLeft of everything active.
            if (--minionTimer <= 0)
            {
                minionTimer = 60;
                MaintainMinionArmy();
            }
            // Sentries follow the player like minions (see FollowSentries).
            RefreshMinionTimeLeft();
            FollowSentries();

            // Storm: a fresh ring every few ticks (see FireStormVolley).
            // Rate, size, and cap come from CharmConfig (defaults: a
            // ring of 10 every 4 ticks, 200 max). Peyton reduced the
            // rate from every tick (2026-11-18).
            CharmConfig config = ModContent.GetInstance<CharmConfig>();
            if (config.StormEnabled)
            {
                if (++stormTimer >= config.StormFireRate)
                {
                    stormTimer = 0;
                    FireStormVolley();
                }
            }
            else
            {
                stormTimer = 0;
            }

            if (cascadeCooldown > 0)
                cascadeCooldown--;
        }

        // ------------------------------------------------------------------
        // Accessory effects: every accessory that matters, implemented by
        // hand (there is no "pretend equipped" API). Junk-drawer accessories
        // (Hand Warmer, Pocket Mirror tier) are excluded unless Peyton asks.
        // ------------------------------------------------------------------
        private void ApplyAccessoryEffects()
        {
            // --- Movement: wings, boots, jumps, climbing ---
            // Wings: Fishron Wings flight, infinite time (Soaring Insignia).
            // Only set wings if the player has none equipped.
            if (Player.wings == 0)
                Player.wings = ItemID.FishronWings;
            Player.wingTimeMax = 300;
            Player.empressBrooch = true;
            Player.wingTime = Player.wingTimeMax;

            Player.accRunSpeed = 12f;          // Terraspark Boots tier
            Player.moveSpeed += 0.5f;          // Aglet / boots / etc.
            Player.iceSkate = true;            // Ice Skates
            // Double jumps: tML 1.4.4 grants them via the ExtraJump API
            // (the old doubleJumpX bools and the 1.4.5 hasJumpOption_X
            // fields do not exist in tML 1.4.4).
            Player.GetJumpState(ExtraJump.CloudInABottle).Enable();
            Player.GetJumpState(ExtraJump.BlizzardInABottle).Enable();
            Player.GetJumpState(ExtraJump.SandstormInABottle).Enable();
            Player.GetJumpState(ExtraJump.FartInAJar).Enable();
            Player.GetJumpState(ExtraJump.TsunamiInABottle).Enable();
            Player.jumpBoost = true;           // Shiny Red Balloon
            Player.autoJump = true;            // Frog Leg
            Player.spikedBoots = 2;            // Shoe Spikes (wall climb)
            Player.dashType = 1;               // Shield of Cthulhu dash
            Player.gravControl = true;         // Gravity Globe (key-toggled)

            // --- Environment: lava, water, falls ---
            Player.lavaImmune = true;          // Lava Charm / Lava Waders
            Player.fireWalk = true;            // Obsidian Skull
            Player.waterWalk = true;           // Water Walking Boots
            Player.gills = true;               // Diving Gear
            Player.accFlipper = true;          // Flipper
            Player.ignoreWater = true;         // Neptune's Shell
            Player.noFallDmg = true;           // Lucky Horseshoe

            // --- Offense: damage, crit, speed, penetration ---
            Player.GetDamage(DamageClass.Generic) += 0.5f;   // Emblems + Avenger + Destroyer + Celestial Stone
            Player.GetDamage(DamageClass.Summon) += 0.3f;    // Papyrus Scarab + Necromantic Scroll + Hercules Beetle
            Player.GetDamage(DamageClass.Ranged) += 0.1f;    // Sniper Scope damage (NO zoom — see below)
            Player.GetCritChance(DamageClass.Melee) += 15;
            Player.GetCritChance(DamageClass.Ranged) += 25;  // 15 base + 10 Sniper Scope
            Player.GetCritChance(DamageClass.Magic) += 15;
            Player.GetCritChance(DamageClass.Summon) += 15;
            Player.GetAttackSpeed(DamageClass.Generic) += 0.2f;
            Player.GetAttackSpeed(DamageClass.Melee) += 0.12f; // Feral Claws
            Player.GetKnockback(DamageClass.Generic) += 0.5f;  // Titan Glove
            Player.GetArmorPenetration(DamageClass.Generic) += 10;       // Shark Tooth + Stinger Necklace

            // --- Defense: knockback immunity, endurance, debuff immunity ---
            Player.noKnockback = true;         // Cobalt Shield / Ankh Shield
            Player.endurance += 0.17f;         // Worm Scarf
            // Ankh Shield debuff immunity set:
            Player.buffImmune[BuffID.Poisoned] = true;
            Player.buffImmune[BuffID.Darkness] = true;
            Player.buffImmune[BuffID.Cursed] = true;
            Player.buffImmune[BuffID.OnFire] = true;
            Player.buffImmune[BuffID.Bleeding] = true;
            Player.buffImmune[BuffID.Slow] = true;
            Player.buffImmune[BuffID.Confused] = true;
            Player.buffImmune[BuffID.Weak] = true;
            Player.buffImmune[BuffID.Silenced] = true;
            Player.buffImmune[BuffID.BrokenArmor] = true;
            Player.buffImmune[BuffID.Burning] = true;
            Player.buffImmune[BuffID.Suffocation] = true;
            Player.buffImmune[BuffID.Chilled] = true;
            Player.buffImmune[BuffID.Frozen] = true;
            Player.buffImmune[BuffID.Stoned] = true;
            Player.buffImmune[BuffID.Webbed] = true;

            // --- Sustain: regen, mana, potion cooldown ---
            Player.lifeRegen += 5;             // Band of Regeneration / Charm of Myths
            Player.manaCost -= 0.5f;           // Nature's Gift / Mana Flower
            Player.manaRegen += 5;
            Player.manaMagnet = true;          // Celestial Magnet
            Player.magicCuffs = true;          // Magic Cuffs / Celestial Cuffs
            Player.manaFlower = true;          // Mana Flower
            Player.pStone = true;              // Philosopher's Stone (potion cooldown)
            Player.shinyStone = true;          // Shiny Stone

            // --- Minions and sentries ---
            // +6 helper projectiles (Destroyer probes, Prime bombs, Betsy
            // breath, Saucer deathrays/missiles, Pumpking blades) + 2 extra
            // True Eyes (three spawn, one is counted in BossMinionTypes).
            Player.maxMinions += VanillaMinionTypes.Length + WeaponMinionTypes.Length + BossMinionTypes.Length + 8;
            Player.maxTurrets += VanillaSentryTypes.Length + 10;

            // --- On-hit / reactive ---
            // 1.4.4 tracks these as Item references, not bools: the game
            // checks them for null/IsAir when the player is hit. Dummy items
            // of the right type give the effects without equipping anything.
            // Star Veil is the strongest cloak variant (stars + long
            // invincibility); the Bee Cloak and Mana Cloak are variants of
            // the SAME slot in vanilla, so Star Veil wins, as it would if
            // all three were equipped at once.
            Player.starCloakItem = GetDummy(ref starVeilDummy, ItemID.StarVeil);
            Player.starCloakItem_starVeilOverrideItem = starVeilDummy;
            Player.longInvince = true;         // Cross Necklace
            Player.honeyCombItem = GetDummy(ref honeyCombDummy, ItemID.HoneyComb); // Honey Comb / Sweetheart Necklace
            Player.panic = true;               // Panic Necklace
            Player.brainOfConfusionItem = GetDummy(ref brainDummy, ItemID.BrainOfConfusion); // Brain of Confusion
            Player.hasPaladinShield = true;    // Paladin's Shield
            Player.sporeSac = true;            // Spore Sac
            Player.volatileGelatin = true;     // Volatile Gelatin

            // --- Utility: yoyo, quiver, ammo, mining, info, pickup ---
            Player.yoyoGlove = true;           // Yoyo Glove
            Player.yoyoString = true;          // White String
            Player.counterWeight = 556 + Main.rand.Next(6); // Yoyo Bag counterweight
            Player.magicQuiver = true;         // Magic Quiver
            Player.ammoCost80 = true;          // Ammo Reservation / ammo saving
            Player.pickSpeed -= 0.25f;         // Ancient Chisel
            Player.treasureMagnet = true;      // Treasure Magnet
            Player.discountAvailable = true;   // Discount Card / Greedy Ring
            // Cell Phone info:
            Player.accWatch = 3;
            Player.accDepthMeter = 1;
            Player.accCompass = 1;
            Player.accFishFinder = true;
            Player.accDreamCatcher = true;
            Player.accOreFinder = true;
            Player.accStopwatch = true;
            Player.accCritterGuide = true;
            Player.accJarOfSouls = true;
            Player.accThirdEye = true;
            Player.accCalendar = true;
            Player.accWeatherRadio = true;

            // SNIPER SCOPE (Peyton's ruling, 2026-11-15): the damage and
            // crit are applied above. The ZOOM IS DELIBERATELY NOT APPLIED:
            // Player.scope is NEVER set here. Do not add it.
        }

        // Lazily creates (once) a dummy Item of the given vanilla type, for
        // the accessory effects that 1.4.4 tracks as Item references.
        private Item GetDummy(ref Item field, int type)
        {
            if (field == null || field.IsAir)
            {
                field = new Item();
                field.SetDefaults(type);
            }
            return field;
        }

        // ------------------------------------------------------------------
        // Minion army
        // ------------------------------------------------------------------
        private void MaintainMinionArmy()
        {
            // Owner-only: only the owning client spawns minions (they sync).
            if (Player.whoAmI != Main.myPlayer)
                return;

            // Each group is gated by its CharmConfig toggle (all default
            // true — the full army spawns unless the player turns a group
            // off). Disabled groups also stop getting their timeLeft
            // refreshed (see RefreshMinionTimeLeft), so they despawn.
            CharmConfig config = ModContent.GetInstance<CharmConfig>();

            if (config.VanillaMinionsEnabled)
            {
                foreach (int type in VanillaMinionTypes)
                {
                    if (!MinionActive(type))
                        SpawnMinion(type);
                }
                foreach (int type in VanillaSentryTypes)
                {
                    if (!MinionActive(type))
                        SpawnMinion(type);
                }
            }
            if (config.WeaponMinionsEnabled)
            {
                foreach (int type in WeaponMinionTypes)
                {
                    if (!MinionActive(type))
                        SpawnMinion(type);
                }
            }
            if (config.BossMinionsEnabled)
            {
                foreach (int type in BossMinionTypes)
                {
                    if (!MinionActive(type))
                        SpawnMinion(type);
                }

                // True Eyes of Cthulhu come in threes (vanilla spawns one per
                // destroyed Moon Lord part — Peyton, 2026-11-18). The boss
                // loop above guarantees the first; top up to three here.
                // The eyes stagger their attack cycles by index among the
                // active eyes (see TrueEyeOfCthulhuMinion), so they don't
                // attack in lockstep.
                int trueEyeType = ModContent.ProjectileType<TrueEyeOfCthulhuMinion>();
                int trueEyes = 0;
                foreach (Projectile proj in Main.projectile)
                {
                    if (proj.active && proj.owner == Player.whoAmI && proj.type == trueEyeType)
                        trueEyes++;
                }
                for (; trueEyes < 3; trueEyes++)
                    SpawnMinion(trueEyeType);
            }
        }

        private bool MinionActive(int type)
        {
            foreach (Projectile proj in Main.projectile)
            {
                if (proj.active && proj.owner == Player.whoAmI && proj.type == type)
                    return true;
            }
            return false;
        }

        private void SpawnMinion(int type)
        {
            // Sentries spawn at the player like everything else — the
            // follow logic in FollowSentries positions them from there.
            int index = Projectile.NewProjectile(
                Player.GetSource_FromThis(),
                Player.Center.X, Player.Center.Y,
                0f, 0f,
                type,
                CharmDamage, 0f,
                Player.whoAmI);
            if (index < 0 || index >= Main.maxProjectiles)
                return;

            Projectile proj = Main.projectile[index];

            // Typeless damage, local immunity frames: every ally hits each
            // enemy independently (20-tick cooldown per ally).
            proj.DamageType = DamageClass.Generic;
            proj.usesLocalNPCImmunity = true;
            proj.localNPCHitCooldown = CharmLocalCooldown;
            proj.timeLeft = proj.sentry ? Projectile.SentryLifeTime : 2;
            proj.netUpdate = true;
            if (proj.sentry)
                Player.UpdateMaxTurrets();
        }

        // Keep every charm summon alive while the charm is equipped.
        private void RefreshMinionTimeLeft()
        {
            CharmConfig config = ModContent.GetInstance<CharmConfig>();
            foreach (Projectile proj in Main.projectile)
            {
                if (!proj.active || proj.owner != Player.whoAmI)
                    continue;
                if (!IsCharmMinion(proj.type))
                    continue;
                // A disabled group's minions stop being refreshed, so
                // they despawn within a tick or two of the toggle flip.
                if (!IsGroupEnabled(proj.type, config))
                    continue;
                proj.timeLeft = proj.sentry ? Projectile.SentryLifeTime : 2;
            }
        }

        // Which CharmConfig toggle governs this minion type? Sentries
        // count as vanilla minions (they're vanilla summons).
        private bool IsGroupEnabled(int type, CharmConfig config)
        {
            foreach (int t in VanillaMinionTypes)
            {
                if (t == type)
                    return config.VanillaMinionsEnabled;
            }
            foreach (int t in VanillaSentryTypes)
            {
                if (t == type)
                    return config.VanillaMinionsEnabled;
            }
            foreach (int t in WeaponMinionTypes)
            {
                if (t == type)
                    return config.WeaponMinionsEnabled;
            }
            foreach (int t in BossMinionTypes)
            {
                if (t == type)
                    return config.BossMinionsEnabled;
            }
            return false;
        }

        // Sentries behave like minions and follow the player (Peyton,
        // 2026-11-18) instead of sitting on the floor where they were
        // summoned. Each one holds a slot in a loose ring around the
        // player, gliding toward it like a minion trailing its owner,
        // and snapping back if the player teleports or falls far away.
        // Sentry firing AI runs off position, not ground contact, so
        // they keep shooting while they follow; moving them by Center
        // (not velocity) means they glide through walls like minions.
        private void FollowSentries()
        {
            // Sentries are vanilla minions for config purposes — if the
            // group is off, nothing follows (the sentries despawn via
            // RefreshMinionTimeLeft anyway).
            if (!ModContent.GetInstance<CharmConfig>().VanillaMinionsEnabled)
                return;

            int sentryCount = 0;
            foreach (Projectile proj in Main.projectile)
            {
                if (proj.active && proj.owner == Player.whoAmI && proj.sentry && IsCharmMinion(proj.type))
                    sentryCount++;
            }
            if (sentryCount == 0)
                return;

            int slot = 0;
            foreach (Projectile proj in Main.projectile)
            {
                if (!proj.active || proj.owner != Player.whoAmI || !proj.sentry || !IsCharmMinion(proj.type))
                    continue;

                float angle = MathHelper.TwoPi * slot / sentryCount;
                Vector2 targetPos = Player.Center + Vector2.UnitX.RotatedBy(angle) * 120f;
                slot++;

                if (Vector2.DistanceSquared(proj.Center, targetPos) > 800f * 800f)
                {
                    proj.Center = targetPos;
                    proj.netUpdate = true;
                }
                else
                {
                    proj.Center = Vector2.Lerp(proj.Center, targetPos, 0.25f);
                }
                proj.velocity = Vector2.Zero;
            }
        }

        private void DismissCharmSentries()
        {
            foreach (Projectile proj in Main.projectile)
            {
                if (proj.active &&
                    proj.owner == Player.whoAmI &&
                    Array.IndexOf(VanillaSentryTypes, proj.type) >= 0)
                {
                    proj.timeLeft = 2;
                }
            }
        }

        private bool IsCharmMinion(int type)
        {
            foreach (int t in VanillaMinionTypes)
            {
                if (t == type)
                    return true;
            }
            foreach (int t in VanillaSentryTypes)
            {
                if (t == type)
                    return true;
            }
            foreach (int t in WeaponMinionTypes)
            {
                if (t == type)
                    return true;
            }
            foreach (int t in BossMinionTypes)
            {
                if (t == type)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // The storm: a fresh ring of 10 randomly picked projectiles EVERY
        // tick (2026-11-18, Peyton's order) — the pool cycles through
        // random groups of 10, fired in a full 360-degree radial ring
        // around the player. The ring is rotated by a random angle each
        // tick so the spray stays uniform instead of forming fixed spokes.
        // The active cap is the throttle: when the storm is at capacity,
        // the tick fires nothing and it refills as projectiles expire.
        // ------------------------------------------------------------------
        private void FireStormVolley()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            CharmConfig config = ModContent.GetInstance<CharmConfig>();
            int activeStormCount = CountActiveStormProjectiles();
            int remainingCapacity = Math.Max(0, config.StormMaxProjectiles - activeStormCount);
            if (remainingCapacity <= 0)
                return;

            int count = Math.Min(config.StormProjectilesPerRing, remainingCapacity);
            float ringOffset = Main.rand.NextFloat(MathHelper.TwoPi);
            for (int i = 0; i < count; i++)
            {
                int type = StormPool[Main.rand.Next(StormPool.Length)];

                Vector2 dir = Vector2.UnitX.RotatedBy(ringOffset + MathHelper.TwoPi * i / count + Main.rand.NextFloat(-0.15f, 0.15f));
                Vector2 velocity = dir * Main.rand.NextFloat(14f, 20f);

                int index = Projectile.NewProjectile(
                    Player.GetSource_FromThis(),
                    Player.Center, velocity,
                    type,
                    CharmDamage, 0f,
                    Player.whoAmI);
                if (index < 0 || index >= Main.maxProjectiles)
                    continue;

                Projectile proj = Main.projectile[index];
                proj.DamageType = DamageClass.Generic;
                proj.usesLocalNPCImmunity = true;
                proj.localNPCHitCooldown = CharmLocalCooldown;
                proj.timeLeft = Math.Min(proj.timeLeft, StormProjectileLifetime);

                // Spears and flails cannot fly on their own — their vanilla
                // AI (aiStyle Spear/Flail and family) holds them at the
                // player's hand and locks item use. Swap their aiStyle to
                // Arrow so they simply fly, and give them the gentle
                // homing so they curve toward enemies (Peyton, 2026-11-18).
                // Verified against the decompiled vanilla AI: spear/flail
                // behavior (hold, chain, extension hitbox, item-time lock)
                // is all dispatched on aiStyle, so the swap bypasses all
                // of it. Hooks are NEVER in the pool, so they never
                // reach this branch.
                if (proj.aiStyle == ProjAIStyleID.Spear ||
                    proj.aiStyle == ProjAIStyleID.NorthPoleSpear ||
                    proj.aiStyle == ProjAIStyleID.Flail ||
                    proj.aiStyle == ProjAIStyleID.Harpoon ||
                    proj.aiStyle == ProjAIStyleID.MechanicalPiranha ||
                    proj.aiStyle == ProjAIStyleID.Flairon)
                {
                    proj.aiStyle = ProjAIStyleID.Arrow;
                    proj.GetGlobalProjectile<CharmGlobalProjectile>().stormHoming = true;
                }

                proj.GetGlobalProjectile<CharmGlobalProjectile>().charmStormProjectile = true;
                proj.netUpdate = true;
            }
        }

        private int CountActiveStormProjectiles()
        {
            int count = 0;
            foreach (Projectile projectile in Main.projectile)
            {
                if (!projectile.active || projectile.owner != Player.whoAmI)
                    continue;

                if (projectile.GetGlobalProjectile<CharmGlobalProjectile>().charmStormProjectile)
                    count++;
            }
            return count;
        }

        // ------------------------------------------------------------------
        // On-hit cascade: attacks have a chance to trigger extra projectiles.
        // Minion hits do NOT trigger it (that would chain). A 10-tick global
        // cooldown bounds the rate even if a storm projectile triggers one.
        // ------------------------------------------------------------------
        public override void OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (!charmEquipped)
                return;
            TryCascade(target);
        }

        public override void OnHitNPCWithProj(Projectile proj, NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (!charmEquipped)
                return;
            if (proj.minion || proj.sentry)
                return;
            TryCascade(target);
        }

        private void TryCascade(NPC target)
        {
            if (cascadeCooldown > 0)
                return;
            if (!Main.rand.NextBool(4)) // 25% chance per hit
                return;
            if (Player.whoAmI != Main.myPlayer)
                return;

            cascadeCooldown = 10;
            int count = Main.rand.Next(1, 4); // 1–3 cascade projectiles
            for (int i = 0; i < count; i++)
            {
                int type = CascadePool[Main.rand.Next(CascadePool.Length)];
                Vector2 spawnPos = Player.Center + new Vector2(Main.rand.Next(-30, 31), Main.rand.Next(-30, 31));
                Vector2 dir = (target.Center - spawnPos).SafeNormalize(Vector2.UnitX);
                dir = dir.RotatedByRandom(MathHelper.ToRadians(15f));

                int index = Projectile.NewProjectile(
                    Player.GetSource_FromThis(),
                    spawnPos, dir * Main.rand.NextFloat(12f, 18f),
                    type,
                    CharmDamage, 0f,
                    Player.whoAmI);
                if (index < 0 || index >= Main.maxProjectiles)
                    continue;

                Projectile proj = Main.projectile[index];
                proj.DamageType = DamageClass.Generic;
                proj.usesLocalNPCImmunity = true;
                proj.localNPCHitCooldown = CharmLocalCooldown;
                proj.GetGlobalProjectile<CharmGlobalProjectile>().cascadeHoming = true;
                proj.netUpdate = true;
            }
        }
    }
}
