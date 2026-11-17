using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.AerusBuffs;
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
    //  - One of every vanilla minion (29 types — the Dead Cells minion is
    //    1.4.5-only and doesn't exist in tML 1.4.4) + one of every boss
    //    minion (12 types), all at flat 6,000 typeless damage with local
    //    immunity frames (20 ticks) so every ally damages the same enemy
    //    independently.
    //  - The old Soul of Eternity storm: a passive barrage every second, plus
    //    an on-hit cascade.
    public class CharmPlayer : ModPlayer
    {
        public bool charmEquipped;

        // Flat damage for every charm summon and storm projectile. Typeless
        // (DamageClass.Generic) so no class bonus stacks on top of it.
        public const int CharmDamage = 6000;

        // Local immunity frames per ally/projectile: 20 ticks = 3 hits/s each.
        public const int CharmLocalCooldown = 20;

        private int stormTimer;
        private int minionTimer;
        private int cascadeCooldown;

        // Dummy items for the accessory effects that 1.4.4 tracks as Item
        // references (the game checks them for null/IsAir, not for being
        // equipped). Created lazily on first use.
        private Item starVeilDummy;
        private Item honeyCombDummy;
        private Item brainDummy;

        // Every vanilla minion type, verified against the decompiled source
        // (2026-11-15). Stardust Dragon: HEAD ONLY (625) — it grows its own
        // segments. Desert Tiger: base tier only (831) — it evolves itself.
        // Sentries are NOT in v1 (stationary, separate category).
        private static readonly int[] VanillaMinionTypes = new int[]
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
            // NOTE: DeadCellsMushroomBoiMinion (1022) is NOT in tML 1.4.4's
            // ProjectileID — the Dead Cells crossover minion is 1.4.5-only.
            // 29 vanilla minions, not 30. Do not re-add it.
        };

        // Boss minions (ModProjectiles in this mod). All 12.
        // Aerus weapon minions (2026-11-15, Peyton's redesign: the charm
        // summons the mod's own weapons as living minions instead of bosses).
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
            ModContent.ProjectileType<TestWeaponMinion>(),
            ModContent.ProjectileType<ThreadOfTheEmpyreanMinion>(),
        };

        // The storm pool: ~34 iconic vanilla projectiles, all verified IDs
        // (2026-11-15).
        // Storm pool (2026-11-15, Peyton's rule): EVERY vanilla player
        // projectile that can deal damage to enemies — 340 types, verified
        // against the decompiled ProjectileID enum. Minions and sentries
        // themselves are NEVER spawned; their SHOT projectiles are in the
        // pool instead. Tile-destroying explosives (26 types) are excluded
        // by Peyton's rule — nothing in the storm may destroy tiles.
        // Liquid explosives (wet/lava/honey/dry) are also excluded —
        // Peyton considers them destructive.
        // One exclusion from the old pool: RainbowCrystal
        // (643) is a sentry, replaced by its shot RainbowCrystalExplosion.
        private static readonly int[] StormPool = new int[]
        {
            // Melee (85)
            ProjectileID.EnchantedBoomerang, // 6
            ProjectileID.Starfury, // 9
            ProjectileID.Flamarang, // 19
            ProjectileID.ThornChakram, // 33
            ProjectileID.WoodenBoomerang, // 52
            ProjectileID.LightDisc, // 106
            ProjectileID.IceBoomerang, // 113
            ProjectileID.SwordBeam, // 116
            ProjectileID.IceBolt, // 118
            ProjectileID.FrostBoltSword, // 119
            ProjectileID.Mushroom, // 131
            ProjectileID.TerraBeam, // 132
            ProjectileID.LightBeam, // 156
            ProjectileID.NightBeam, // 157
            ProjectileID.EnchantedBeam, // 173
            ProjectileID.PossessedHatchet, // 182
            ProjectileID.SporeCloud, // 228
            ProjectileID.ChlorophyteOrb, // 229
            ProjectileID.FlowerPowPetal, // 248
            ProjectileID.IceSickle, // 263
            ProjectileID.Bananarang, // 272
            ProjectileID.DeathSickle, // 274
            ProjectileID.PaladinsHammerFriendly, // 301
            ProjectileID.VampireKnife, // 304
            ProjectileID.EatersBite, // 306
            ProjectileID.BloodyMachete, // 320
            ProjectileID.FlamingJack, // 321
            ProjectileID.FruitcakeChakram, // 333
            ProjectileID.OrnamentFriendly, // 335
            ProjectileID.NorthPoleSpear, // 343
            ProjectileID.NorthPoleSnowflake, // 344
            ProjectileID.FlaironBubble, // 405
            ProjectileID.InfluxWaver, // 451
            ProjectileID.SeedlerNut, // 483
            ProjectileID.SeedlerThorn, // 484
            ProjectileID.FlyingKnife, // 491
            ProjectileID.ShadowFlameKnife, // 497
            ProjectileID.Meowmere, // 502
            ProjectileID.StarWrath, // 503
            ProjectileID.Spark, // 504
            ProjectileID.IchorSplash, // 524
            ProjectileID.TerrarianBeam, // 604
            ProjectileID.SolarFlareRay, // 607
            ProjectileID.Daybreak, // 636
            ProjectileID.Ale, // 669
            ProjectileID.DD2SquireSonicBoom, // 684
            ProjectileID.MonkStaffT1Explosion, // 698
            ProjectileID.MonkStaffT2Ghast, // 700
            ProjectileID.MonkStaffT3_AltShot, // 709
            ProjectileID.ThunderSpearShot, // 732
            ProjectileID.FirstFractal, // 857
            ProjectileID.BouncingShield, // 866
            ProjectileID.Shroomerang, // 867
            ProjectileID.CombatWrench, // 902
            ProjectileID.OrnamentStar, // 907
            ProjectileID.DripplerFlailExtraBall, // 928
            ProjectileID.FinalFractal, // 933
            ProjectileID.TentacleSpike, // 971
            ProjectileID.NightsEdge, // 972
            ProjectileID.TrueNightsEdge, // 973
            ProjectileID.LightsBane, // 974
            ProjectileID.BloodButcherer, // 975
            ProjectileID.Volcano, // 978
            ProjectileID.Excalibur, // 982
            ProjectileID.TrueExcalibur, // 983
            ProjectileID.TerraBlade2, // 984
            ProjectileID.TerraBlade2Shot, // 985
            ProjectileID.TheHorsemansBlade, // 997
            ProjectileID.Trimarang, // 1000
            ProjectileID.Waffle, // 1012
            // Ranged (148)
            ProjectileID.ClusterGrenadeI, // 777
            ProjectileID.MiniNukeGrenadeI, // 794
            ProjectileID.WoodenArrowFriendly, // 1
            ProjectileID.FireArrow, // 2
            ProjectileID.Shuriken, // 3
            ProjectileID.UnholyArrow, // 4
            ProjectileID.JestersArrow, // 5
            ProjectileID.Bullet, // 14
            ProjectileID.Bone, // 21
            ProjectileID.SpikyBall, // 24
            ProjectileID.Grenade, // 30
            ProjectileID.MeteorShot, // 36
            ProjectileID.HellfireArrow, // 41
            ProjectileID.SandBallGun, // 42
            ProjectileID.ThrowingKnife, // 48
            ProjectileID.Seed, // 51
            ProjectileID.PoisonedKnife, // 54
            ProjectileID.EbonsandBallGun, // 65
            ProjectileID.PearlSandBallGun, // 68
            ProjectileID.Flames, // 85
            ProjectileID.CrystalBullet, // 89
            ProjectileID.CrystalShard, // 90
            ProjectileID.HolyArrow, // 91
            ProjectileID.HallowStar, // 92
            ProjectileID.CursedArrow, // 103
            ProjectileID.CursedBullet, // 104
            ProjectileID.BoneArrow, // 117
            ProjectileID.FrostArrow, // 120
            ProjectileID.GrenadeI, // 133
            ProjectileID.RocketI, // 134
            ProjectileID.ProximityMineI, // 135
            ProjectileID.GrenadeIII, // 139
            ProjectileID.RocketIII, // 140
            ProjectileID.ProximityMineIII, // 141
            ProjectileID.CopperCoin, // 158
            ProjectileID.SilverCoin, // 159
            ProjectileID.GoldCoin, // 160
            ProjectileID.PlatinumCoin, // 161
            ProjectileID.SnowBallFriendly, // 166
            ProjectileID.FrostburnArrow, // 172
            ProjectileID.Beenade, // 183
            ProjectileID.ChlorophyteBullet, // 207
            ProjectileID.ChlorophyteArrow, // 225
            ProjectileID.BulletHighVelocity, // 242
            ProjectileID.Stynger, // 246
            ProjectileID.StyngerShrapnel, // 249
            ProjectileID.PoisonDartBlowgun, // 267
            ProjectileID.IchorArrow, // 278
            ProjectileID.IchorBullet, // 279
            ProjectileID.VenomArrow, // 282
            ProjectileID.VenomBullet, // 283
            ProjectileID.PartyBullet, // 284
            ProjectileID.NanoBullet, // 285
            ProjectileID.ExplosiveBullet, // 286
            ProjectileID.GoldenBullet, // 287
            ProjectileID.CandyCorn, // 311
            ProjectileID.JackOLantern, // 312
            ProjectileID.RottenEgg, // 318
            ProjectileID.Stake, // 323
            ProjectileID.StarAnise, // 330
            ProjectileID.RocketSnowmanI, // 338
            ProjectileID.RocketSnowmanIII, // 340
            ProjectileID.CrimsandBallGun, // 354
            ProjectileID.PulseBolt, // 357
            ProjectileID.StickyGrenade, // 397
            ProjectileID.MolotovCocktail, // 399
            ProjectileID.ElectrosphereMissile, // 442
            ProjectileID.Electrosphere, // 443
            ProjectileID.Xenopopper, // 444
            ProjectileID.BeeArrow, // 469
            ProjectileID.BoneArrowFromMerchant, // 474
            ProjectileID.CrystalDart, // 477
            ProjectileID.CursedDart, // 478
            ProjectileID.IchorDart, // 479
            ProjectileID.CursedDartFlame, // 480
            ProjectileID.Hellwing, // 485
            ProjectileID.ShadowFlameArrow, // 495
            ProjectileID.JavelinFriendly, // 507
            ProjectileID.NailFriendly, // 514
            ProjectileID.BouncyGrenade, // 517
            ProjectileID.FrostDaggerfish, // 520
            ProjectileID.ToxicBubble, // 523
            ProjectileID.BoneGloveProj, // 532
            ProjectileID.ClothiersCurse, // 585
            ProjectileID.PainterPaintball, // 587
            ProjectileID.PartyGirlGrenade, // 588
            ProjectileID.BoneJavelin, // 598
            ProjectileID.BoneDagger, // 599
            ProjectileID.ScutlixLaser, // 606
            ProjectileID.VortexBeaterRocket, // 616
            ProjectileID.PhantasmArrow, // 631
            ProjectileID.MoonlordBullet, // 638
            ProjectileID.MoonlordArrow, // 639
            ProjectileID.MoonlordArrowTrail, // 640
            ProjectileID.BlackBolt, // 661
            ProjectileID.DD2PhoenixBowShot, // 706
            ProjectileID.DD2BetsyArrow, // 710
            ProjectileID.Celeb2Rocket, // 715
            ProjectileID.Celeb2RocketLarge, // 717
            ProjectileID.SuperStar, // 728
            ProjectileID.SuperStarSlash, // 729
            ProjectileID.Geode, // 772
            ProjectileID.ClusterRocketI, // 776
            ProjectileID.ClusterMineI, // 778
            ProjectileID.ClusterFragmentsI, // 779
            ProjectileID.MiniNukeRocketI, // 793
            ProjectileID.MiniNukeMineI, // 795
            ProjectileID.ClusterSnowmanRocketI, // 803
            ProjectileID.MiniNukeSnowmanRocketI, // 808
            ProjectileID.BloodArrow, // 819
            ProjectileID.ClusterSnowmanFragmentsI, // 862
            ProjectileID.SantankMountRocket, // 930
            ProjectileID.FairyQueenRangedItemShot, // 932
            ProjectileID.VolatileGelatinBall, // 937
            ProjectileID.StarCannonStar, // 955
            ProjectileID.PewMaticHornShot, // 968
            ProjectileID.SilverBullet, // 981
            ProjectileID.ShimmerArrow, // 1006
            // Magic (98)
            ProjectileID.VilethornBase, // 7
            ProjectileID.VilethornTip, // 8
            ProjectileID.BallofFire, // 15
            ProjectileID.MagicMissile, // 16
            ProjectileID.GreenLaser, // 20
            ProjectileID.WaterStream, // 22
            ProjectileID.WaterBolt, // 27
            ProjectileID.Flamelash, // 34
            ProjectileID.DemonScythe, // 45
            ProjectileID.RainbowRodBullet, // 79
            ProjectileID.PurpleLaser, // 88
            ProjectileID.MagicDagger, // 93
            ProjectileID.CrystalStorm, // 94
            ProjectileID.CursedFlameFriendly, // 95
            ProjectileID.UnholyTridentFriendly, // 114
            ProjectileID.AmethystBolt, // 121
            ProjectileID.TopazBolt, // 122
            ProjectileID.SapphireBolt, // 123
            ProjectileID.EmeraldBolt, // 124
            ProjectileID.RubyBolt, // 125
            ProjectileID.DiamondBolt, // 126
            ProjectileID.NettleBurstRight, // 150
            ProjectileID.NettleBurstLeft, // 151
            ProjectileID.NettleBurstEnd, // 152
            ProjectileID.Wasp, // 189
            ProjectileID.Leaf, // 206
            ProjectileID.RainFriendly, // 239
            ProjectileID.BloodRain, // 245
            ProjectileID.BallofFrost, // 253
            ProjectileID.MagnetSphereBolt, // 255
            ProjectileID.HeatRay, // 260
            ProjectileID.BoulderStaffOfEarth, // 261
            ProjectileID.PoisonFang, // 265
            ProjectileID.GoldenShowerFriendly, // 280
            ProjectileID.ShadowBeamFriendly, // 294
            ProjectileID.InfernoFriendlyBolt, // 295
            ProjectileID.InfernoFriendlyBlast, // 296
            ProjectileID.LostSoulFriendly, // 297
            ProjectileID.Bat, // 316
            ProjectileID.PineNeedleFriendly, // 336
            ProjectileID.Blizzard, // 337
            ProjectileID.VenomFang, // 355
            ProjectileID.SpectreWrath, // 356
            ProjectileID.FrostBoltStaff, // 359
            ProjectileID.Typhoon, // 409
            ProjectileID.Bubble, // 410
            ProjectileID.Meteor1, // 424
            ProjectileID.Meteor2, // 425
            ProjectileID.Meteor3, // 426
            ProjectileID.LaserMachinegunLaser, // 440
            ProjectileID.ChargedBlasterOrb, // 459
            ProjectileID.SoulDrain, // 476
            ProjectileID.CrystalVileShardHead, // 493
            ProjectileID.CrystalVileShardShaft, // 494
            ProjectileID.ShadowFlame, // 496
            ProjectileID.ToxicFlask, // 510
            ProjectileID.ToxicCloud, // 511
            ProjectileID.ToxicCloud2, // 512
            ProjectileID.ToxicCloud3, // 513
            ProjectileID.CrystalPulse, // 521
            ProjectileID.CrystalPulse2, // 522
            ProjectileID.MedusaHeadRay, // 536
            ProjectileID.SporeGas, // 569
            ProjectileID.SporeGas2, // 570
            ProjectileID.SporeGas3, // 571
            ProjectileID.MinecartMechLaser, // 591
            ProjectileID.AmberBolt, // 597
            ProjectileID.NebulaArcanum, // 617
            ProjectileID.NebulaArcanumSubshot, // 618
            ProjectileID.NebulaArcanumExplosionShot, // 619
            ProjectileID.NebulaArcanumExplosionShotShard, // 620
            ProjectileID.NebulaBlaze1, // 634
            ProjectileID.NebulaBlaze2, // 635
            ProjectileID.LunarFlare, // 645
            ProjectileID.SandnadoFriendly, // 656
            ProjectileID.SpiritFlame, // 659
            ProjectileID.SkyFracture, // 660
            ProjectileID.DD2ApprenticeStorm, // 704
            ProjectileID.ApprenticeStaffT3Shot, // 711
            ProjectileID.BookStaffShot, // 712
            ProjectileID.ManaCloakStar, // 723
            ProjectileID.BeeCloakStar, // 724
            ProjectileID.StarVeilStar, // 725
            ProjectileID.StarCloakStar, // 726
            ProjectileID.ThunderStaffShot, // 731
            ProjectileID.SharpTears, // 756
            ProjectileID.BookOfSkullsSkull, // 837
            ProjectileID.TitaniumStormShard, // 908
            ProjectileID.ScytheWhipProj, // 916
            ProjectileID.CoolWhipProj, // 917
            ProjectileID.FireWhipProj, // 918
            ProjectileID.FairyQueenMagicItemShot, // 931
            ProjectileID.PrincessWeapon, // 950
            ProjectileID.WandOfSparkingSpark, // 954
            ProjectileID.InsanityShadowFriendly, // 964
            ProjectileID.WeatherPainShot, // 969
            ProjectileID.WandOfFrostingFrost, // 979
            // Thrown (20)
            ProjectileID.CannonballFriendly, // 162
            ProjectileID.NurseSyringeHurt, // 583
            ProjectileID.SantaBombs, // 589
            ProjectileID.Football, // 861
            ProjectileID.TreeGlobe, // 868
            ProjectileID.WorldGlobe, // 869
            ProjectileID.GelBalloon, // 936
            ProjectileID.MoonGlobe, // 996
            // Other (12)
            ProjectileID.Bee, // 181
            ProjectileID.FlowerPetal, // 221
            ProjectileID.CrystalLeaf, // 226
            ProjectileID.CrystalLeafShot, // 227
            ProjectileID.ExplosiveBunny, // 281
            ProjectileID.ScutlixLaserFriendly, // 434
            ProjectileID.GiantBee, // 566
            ProjectileID.MechanicWrench, // 582
            ProjectileID.SolarCounter, // 608
            ProjectileID.SolarWhipSwordExplosion, // 612
            ProjectileID.StardustGuardianExplosion, // 624
            ProjectileID.DaybreakExplosion, // 953
            // Minion shots (7)
            ProjectileID.PygmySpear, // 195
            ProjectileID.HornetStinger, // 374
            ProjectileID.ImpFireball, // 376
            ProjectileID.MiniRetinaLaser, // 389
            ProjectileID.MiniSharkron, // 408
            ProjectileID.UFOLaser, // 433
            ProjectileID.StardustCellMinionShot, // 614
            // Sentry shots (11)
            ProjectileID.FrostBlastFriendly, // 309
            ProjectileID.SpiderEgg, // 378
            ProjectileID.RainbowCrystalExplosion, // 644
            ProjectileID.DD2FlameBurstTowerT1Shot, // 664
            ProjectileID.DD2FlameBurstTowerT2Shot, // 666
            ProjectileID.DD2FlameBurstTowerT3Shot, // 668
            ProjectileID.DD2BallistraProj, // 680
            ProjectileID.DD2ExplosiveTrapT1Explosion, // 694
            ProjectileID.DD2ExplosiveTrapT2Explosion, // 695
            ProjectileID.DD2ExplosiveTrapT3Explosion, // 696
            ProjectileID.HoundiusShootiusFireball, // 967
        };

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
                return;

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
            RefreshMinionTimeLeft();

            // The storm: a volley every half second in combat, every second
            // when no enemies are around (2026-11-15, Peyton's order).
            if (--stormTimer <= 0)
            {
                bool foundTargets = FireStormVolley();
                stormTimer = foundTargets ? 30 : 60;
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

            // --- Minions ---
            Player.maxMinions += 45;           // 29 vanilla + 10 Aerus + 6 spare
            Player.maxTurrets += 10;

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

            foreach (int type in VanillaMinionTypes)
            {
                if (!MinionActive(type))
                    SpawnMinion(type);
            }
            foreach (int type in WeaponMinionTypes)
            {
                if (!MinionActive(type))
                    SpawnMinion(type);
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
            proj.timeLeft = 2;
            proj.netUpdate = true;
        }

        // Keep every charm minion alive while the charm is equipped. When it
        // is unequipped, the refresh stops and the army dismisses itself
        // within 2 ticks (their timeLeft is 2).
        private void RefreshMinionTimeLeft()
        {
            foreach (Projectile proj in Main.projectile)
            {
                if (!proj.active || proj.owner != Player.whoAmI)
                    continue;
                if (!IsCharmMinion(proj.type))
                    continue;
                proj.timeLeft = 2;
            }
        }

        private bool IsCharmMinion(int type)
        {
            foreach (int t in VanillaMinionTypes)
            {
                if (t == type)
                    return true;
            }
            foreach (int t in WeaponMinionTypes)
            {
                if (t == type)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // The storm: one volley per second at nearby enemies.
        // ------------------------------------------------------------------
        // Returns true if any targets were found (drives the volley timer:
        // half a second in combat, one second when the area is clear).
        private bool FireStormVolley()
        {
            if (Player.whoAmI != Main.myPlayer)
                return false;

            // Up to 5 nearby enemies to split the volley across. The check is
            // inlined (active, hostile, damageable) instead of calling
            // NPC.CanBeChasedBy() — the parameterless overload's behavior in
            // tML 1.4.4 is not the same as the projectile overload the boss
            // minions use, and the storm found no targets with it.
            List<NPC> targets = new List<NPC>();
            foreach (NPC npc in Main.npc)
            {
                if (!npc.active || npc.friendly || npc.lifeMax <= 5 || npc.dontTakeDamage || npc.type == NPCID.TargetDummy)
                    continue;
                if (Vector2.DistanceSquared(Player.Center, npc.Center) > 2000f * 2000f)
                    continue;
                targets.Add(npc);
                if (targets.Count >= 5)
                    break;
            }

            int count = Main.rand.Next(8, 11); // 8–10 projectiles per volley
            for (int i = 0; i < count; i++)
            {
                int type = StormPool[Main.rand.Next(StormPool.Length)];

                // Evenly space the volley around the player. Keep storm
                // projectiles on their radial paths instead of homing toward
                // one target and making the volley appear one-sided.
                Vector2 dir = Vector2.UnitX.RotatedBy(MathHelper.TwoPi * i / count);
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
            }

            return targets.Count > 0;
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
            }
        }
    }
}
