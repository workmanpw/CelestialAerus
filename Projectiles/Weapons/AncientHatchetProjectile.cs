using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Ancient Hatchet (Scourge model, 2026-11-15): the parent hatchet keeps
    /// its turn-rate homing (lock-on + prediction, smooth curves only), but
    /// it no longer bounces, sticks, or returns. On its FIRST hit it
    /// explodes into fire and dies — and like the Scourge of the
    /// Corruptor's EatersBite, its death spawns TinyHatchets (2-4, with a
    /// 1% chance of 15) at 75% damage / 35% knockback. The explosion is
    /// visual only; the TinyHatchets do the follow-up damage.
    /// </summary>
    public class AncientHatchetProjectile : ModProjectile
    {
        // --- Homing (kept from the 2026-11-15 homing pass) ---
        private const int HomingDelay = 15;
        private const float HomingSpeed = 22f;
        private const float HomingRange = 1000f;
        private const float RotationSpeed = 0.4f;

        // --- Scourge spawn (copied from EatersBite's Kill, Terraria 1.4.4.9) ---
        private const int MinTinyHatchets = 2;   // Main.rand.Next(2, 5) -> 2-4
        private const int MaxTinyHatchets = 5;   // exclusive upper bound
        private const int JackpotChance = 100;   // 1 in 100 -> 15 instead
        private const int JackpotCount = 15;
        private const float SpawnDamageMult = 0.75f;    // 75% of the hatchet's damage
        private const float SpawnKnockbackMult = 0.35f; // 35% of the hatchet's knockback

        // --- State ---
        private int homingTimer = 0;
        private float originalDamage = 0f;
        private bool initialized = false;

        // Homing lock-on state (2026-11-15): the hatchet remembers its
        // target and only re-scans when the lock breaks (target dead, out
        // of range, or no longer chasable). Kills target flicker and most
        // scan cost.
        private NPC lockedTarget = null;
        private int rescanTimer = 0;

        private readonly TrailRecorder trail = new TrailRecorder(MaxTrail);
        private const int MaxTrail = 24;

        public override string Texture => "CelestialAerus/Assets/Textures/AncientHatchet";

        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.PossessedHatchet);
            // Hitbox matches the drawn sprite: 100x100 texture at 0.7 scale.
            Projectile.width = 70;
            Projectile.height = 70;
            Projectile.scale = 0.7f;

            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            // Scourge model: one hit, then it dies (OnKill spawns the TinyHatchets).
            Projectile.penetrate = 1;

            Projectile.timeLeft = 300;
            Projectile.extraUpdates = 1;

            Projectile.tileCollide = false;
            Projectile.light = 1f;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            if (player == null || !player.active || player.dead)
            {
                Projectile.Kill();
                return;
            }

            if (!initialized)
            {
                originalDamage = Projectile.damage;
                initialized = true;
            }

            Projectile.rotation += RotationSpeed;
            if (Projectile.rotation > MathHelper.TwoPi)
                Projectile.rotation -= MathHelper.TwoPi;

            // --- Shader ribbon trail ---
            trail.Record(Projectile.Center);

            // --- Dust Trail ---
            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(2))
            {
                int dust = Dust.NewDust(
                    Projectile.position - new Vector2(10, 10),
                    Projectile.width + 20,
                    Projectile.height + 20,
                    DustID.Torch,
                    0f, 0f, 0, default, 1.5f
                );
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity = Projectile.velocity * 0.2f + Main.rand.NextVector2Circular(2f, 2f);
                Main.dust[dust].fadeIn = 0f;
                Main.dust[dust].color = Color.Orange;
                Main.dust[dust].noLight = false;
                Main.dust[dust].alpha = 100;
                Main.dust[dust].scale = 1.8f + Main.rand.NextFloat(0.5f);

                if (Main.rand.NextBool(2))
                {
                    int spark = Dust.NewDust(
                        Projectile.position - new Vector2(8, 8),
                        Projectile.width + 16,
                        Projectile.height + 16,
                        DustID.Torch,
                        0f, 0f, 0, default, 1.2f
                    );
                    Main.dust[spark].noGravity = true;
                    Main.dust[spark].velocity = Projectile.velocity * 0.3f + Main.rand.NextVector2Circular(3f, 3f);
                    Main.dust[spark].color = Color.OrangeRed;
                    Main.dust[spark].alpha = 150;
                    Main.dust[spark].fadeIn = 0f;
                }
            }

            // --- Homing (unchanged from the 2026-11-15 homing pass) ---
            // Lock on, predict, steer with a capped turn rate. No target:
            // the hatchet just keeps flying straight until its time runs
            // out (then OnKill explodes it anyway, Scourge-style).
            homingTimer++;
            if (homingTimer >= HomingDelay)
            {
                if (!HomingHelper.IsTargetValid(Projectile, lockedTarget, Projectile.Center, HomingRange))
                {
                    // Throttled re-scan.
                    rescanTimer++;
                    if (rescanTimer >= 5)
                    {
                        rescanTimer = 0;
                        lockedTarget = HomingHelper.FindNearestTarget(Projectile, Projectile.Center, HomingRange);
                    }
                }

                if (lockedTarget != null)
                {
                    Vector2 aimPoint = HomingHelper.PredictPosition(lockedTarget, Projectile.Center, HomingSpeed);
                    // Anti-orbit (2026-11-15): the capped turn rate gives
                    // the hatchet a minimum turn circle of ~73px, so at
                    // close range it circled enemies instead of hitting
                    // them. The turn rate now eases up smoothly as the
                    // hatchet closes in — gentle at range (0.15, the same
                    // smooth arc as before), tight up close (0.45) so it
                    // converges instead of orbiting. The lerp keeps the
                    // change gradual: no sudden sharp turns, ever.
                    float dist = Vector2.Distance(Projectile.Center, lockedTarget.Center);
                    float turnRate = MathHelper.Lerp(0.45f, 0.15f,
                        MathHelper.Clamp((dist - 100f) / 300f, 0f, 1f));
                    Projectile.velocity = HomingHelper.SteerToward(
                        Projectile.velocity, Projectile.Center, aimPoint, HomingSpeed, turnRate);
                }
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Player player = Main.player[Projectile.owner];

            // Lifesteal — the Celestial buffs (Empowerment/Obliteration)
            // trigger via AerusPlayer on hit, not from this projectile.
            if (player != null && player.active)
            {
                int lifeStealAmount = 50;
                if (player.statLife + lifeStealAmount > player.statLifeMax2)
                    lifeStealAmount = player.statLifeMax2 - player.statLife;
                player.Heal(lifeStealAmount);
                player.HealEffect(lifeStealAmount);
            }

            // The hit burns: Daybreak for 5 seconds.
            target.AddBuff(BuffID.Daybreak, 300);

            // Scourge model: the hatchet dies on its first hit. OnKill
            // handles the fire explosion and the TinyHatchet spawn.
            Projectile.Kill();
        }

        public override void OnKill(int timeLeft)
        {
            // Don't explode/spawn if the player is dead — the AI kills the
            // projectile in that case, and orphaned TinyHatchets would be
            // pointless (and the explosion would look wrong).
            Player player = Main.player[Projectile.owner];
            if (player == null || !player.active || player.dead)
                return;

            // --- Fire explosion (visual only, like the Scourge's burst) ---
            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
            for (int i = 0; i < 40; i++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.OrangeTorch, 0f, 0f, 100, default, 2f);
                Main.dust[dust].velocity *= 3f;
                if (Main.rand.NextBool())
                {
                    Main.dust[dust].scale = 0.5f;
                    Main.dust[dust].fadeIn = 0f;
                }
            }
            for (int j = 0; j < 70; j++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.OrangeTorch, 0f, 0f, 100, default, 3f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity *= 5f;
                dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.OrangeTorch, 0f, 0f, 100, default, 2f);
                Main.dust[dust].velocity *= 2f;
            }
            for (int k = 0; k < 20; k++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.Flare, 0f, 0f, 150, default, 1.5f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity *= 4f;
                Main.dust[dust].color = new Color(255, 150, 0, 100);
            }

            // --- Spawn TinyHatchets (Scourge numbers, owner-only) ---
            if (Projectile.owner != Main.myPlayer)
                return;

            int count = Main.rand.Next(MinTinyHatchets, MaxTinyHatchets);
            if (Main.rand.Next(1, JackpotChance + 1) == JackpotChance)
                count = JackpotCount;

            for (int i = 0; i < count; i++)
            {
                // Scourge scatter: random velocity, +-7 on each axis.
                float vx = Main.rand.Next(-35, 36) * 0.02f * 10f;
                float vy = Main.rand.Next(-35, 36) * 0.02f * 10f;

                Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    Projectile.Center,
                    new Vector2(vx, vy),
                    ModContent.ProjectileType<TinyHatchet>(),
                    (int)(originalDamage * SpawnDamageMult),
                    Projectile.knockBack * SpawnKnockbackMult,
                    Projectile.owner);
            }
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((short)homingTimer);
            writer.Write(originalDamage);
            writer.Write(initialized);
            writer.Write((short)(lockedTarget != null ? lockedTarget.whoAmI : -1));
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            homingTimer    = reader.ReadInt16();
            originalDamage = reader.ReadSingle();
            initialized    = reader.ReadBoolean();
            short lockedWho = reader.ReadInt16();
            lockedTarget = (lockedWho >= 0 && lockedWho < Main.maxNPCs) ? Main.npc[lockedWho] : null;
        }

        public override bool OnTileCollide(Vector2 oldVelocity) => false;

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            // Fixed trail width, decoupled from the hitbox (2026-11-14):
            // the hitbox now matches the drawn sprite (70x70), but deriving
            // the ribbon widths from it blew all three ribbons up 2.5x.
            // 9.8 restores the pre-fix-pass ribbons (flame/core derive
            // from it).
            float outerHalfWidth = 9.8f;
            float flameHalfWidth = outerHalfWidth * 0.62f;
            float coreHalfWidth = outerHalfWidth * 0.3f;
            float fireTime = Main.GlobalTimeWrappedHourly;

            float Flicker(float progress, float phase, float amount)
            {
                float envelope = (float)Math.Sin(MathHelper.Clamp(progress, 0f, 1f) * MathHelper.Pi);
                float wave = (float)Math.Sin(fireTime * 12f - progress * 28f + phase);
                float lick = (float)Math.Sin(fireTime * 7f + progress * 17f + phase * 1.7f);
                return 1f + envelope * amount * (wave * 0.7f + lick * 0.3f);
            }

            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    Color.Lerp(new Color(150, 24, 0), new Color(255, 112, 12), progress),
                    (float)Math.Pow(progress, 0.85f) * 0.55f * Flicker(progress, 0f, 0.16f)),
                progress => outerHalfWidth * (float)Math.Pow(progress, 1.1f) * Flicker(progress, 0f, 0.32f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 9f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.32f);
                },
                trailStartOffset);

            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    Color.Lerp(new Color(255, 90, 0), new Color(255, 190, 80), progress),
                    (float)Math.Pow(progress, 0.9f) * 0.9f * Flicker(progress, 2.1f, 0.1f)),
                progress => flameHalfWidth * (float)Math.Pow(progress, 1.2f) * Flicker(progress, 2.1f, 0.22f),
                "CelestialFireTrail",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 9f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.3f);
                    SafeShaderDraw.Set(effect, "TrailFades", 0f);
                    SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlowColor", new Color(255, 153, 51));
                    SafeShaderDraw.Set(effect, "FireIntensity", 1.35f);
                    SafeShaderDraw.Set(effect, "FireColor", new Color(255, 115, 13));
                    SafeShaderDraw.Set(effect, "FireDecay", 0f);
                    SafeShaderDraw.Set(effect, "FireEmbers", 1f);
                },
                trailStartOffset);

            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    Color.Lerp(new Color(255, 180, 60), new Color(255, 248, 200), progress),
                    (float)Math.Pow(progress, 0.95f) * 0.95f * Flicker(progress, 4.2f, 0.08f)),
                progress => coreHalfWidth * (float)Math.Pow(progress, 1.3f) * Flicker(progress, 4.2f, 0.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 13f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.35f);
                },
                trailStartOffset);

            return SafeShaderDraw.DrawWithShader("AncientHatchetFlame",
                () =>
                {
                Vector2 drawPos = Projectile.Center - Main.screenPosition;
                Main.EntitySpriteDraw(texture, drawPos, null,
                    Projectile.GetAlpha(lightColorCopy),
                    Projectile.rotation,
                    texture.Size() / 2f,
                    Projectile.scale,
                    SpriteEffects.None, 0);
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "FlameIntensity", 1.55f);
                    SafeShaderDraw.Set(effect, "FlameDistortion", 1.45f);
                    SafeShaderDraw.Set(effect, "FlameCoreColor", new Color(255, 218, 92));
                    SafeShaderDraw.Set(effect, "FlameEdgeColor", new Color(224, 48, 0));
                    SafeShaderDraw.Set(effect, "AxeSpinSpeed", 5f);
                });
        }
    }
}
