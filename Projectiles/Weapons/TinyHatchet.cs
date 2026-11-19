using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// TinyHatchet (Scourge + Shield model, 2026-11-15): spawned by the
    /// Ancient Hatchet's death (2-4, rarely 15). It homes like the
    /// Scourge's TinyEater (aiStyle 36: per-axis acceleration steering,
    /// line-of-sight target search, harmless until it locks on) and
    /// bounces like the Sergeant United Shield (penetrate 5, damage x0.8
    /// per hit, redirects to the next visible enemy at the same speed).
    /// No more sticking — it fights until its 5 hits are spent, then
    /// burns out.
    /// </summary>
    public class TinyHatchet : ModProjectile
    {
        // --- Eater homing (aiStyle 36, Terraria 1.4.4.9) ---
        private const int HomingDelay = 30;      // AI calls before target scanning starts
        private const float HomingSpeed = 13f;   // desired speed (eater: 13)
        private const float HomingAccel = 0.35f; // per-axis accel per AI call (eater: 0.35)
        private const float HomingRange = 800f;  // Manhattan-distance scan range (eater: 800)

        private const float RotationSpeed = 0.5f;

        // --- Shield bounce (BouncingShield, type 866) ---
        private const float DamageFalloff = 0.8f; // damage x0.8 per hit (shield: 0.8)

        private int homingTimer = 0;

        public override string Texture => "CelestialAerus/Assets/Textures/AncientHatchet";

        public override void SetDefaults()
        {
            // Hitbox matches the drawn sprite: 100x100 texture at 0.35 scale.
            Projectile.width = 35;
            Projectile.height = 35;
            Projectile.scale = 0.35f;

            Projectile.aiStyle = -1;
            // Eater rule: harmless until it locks on (friendly flips to
            // true the first time the homing scan finds a target).
            Projectile.friendly = false;
            Projectile.DamageType = DamageClass.Melee;
            // Shield rule: 5 hits total, then the game kills it.
            Projectile.penetrate = 5;

            // Eater lifespan: 10 seconds to find targets and bounce.
            Projectile.timeLeft = 600;
            Projectile.extraUpdates = 1;

            Projectile.tileCollide = false;
            Projectile.light = 0.6f;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 30;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            if (player == null || !player.active || player.dead)
            {
                Projectile.Kill();
                return;
            }

            Projectile.rotation += RotationSpeed;
            if (Projectile.rotation > MathHelper.TwoPi)
                Projectile.rotation -= MathHelper.TwoPi;

            // Small fire sparkle trail while flying.
            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(3))
            {
                int dust = Dust.NewDust(
                    Projectile.position - new Vector2(4, 4),
                    Projectile.width + 8,
                    Projectile.height + 8,
                    DustID.Torch,
                    0f, 0f, 0, default, 1f
                );
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity = Projectile.velocity * 0.15f + Main.rand.NextVector2Circular(1.5f, 1.5f);
                Main.dust[dust].color = Color.Orange;
                Main.dust[dust].scale = 0.8f + Main.rand.NextFloat(0.4f);
            }

            // --- Eater homing (aiStyle 36) ---
            homingTimer++;
            if (homingTimer <= HomingDelay)
                return; // warm-up: fly straight, no scanning yet

            // Target scan (eater rules): nearest enemy within 800f by
            // MANHATTAN distance (|dx| + |dy|), requires LINE OF SIGHT —
            // a TinyEater will not home through walls. (The eater also
            // ignores the wet-NPC rule that other aiStyle-36 projectiles
            // follow, so no wet check here.)
            NPC homingTarget = null;
            float minDist = HomingRange;
            foreach (NPC npc in Main.npc)
            {
                if (!npc.CanBeChasedBy(Projectile))
                    continue;
                float d = Math.Abs(Projectile.Center.X - npc.Center.X)
                        + Math.Abs(Projectile.Center.Y - npc.Center.Y);
                if (d < minDist && Collision.CanHit(Projectile.position, Projectile.width, Projectile.height,
                        npc.position, npc.width, npc.height))
                {
                    minDist = d;
                    homingTarget = npc;
                }
            }

            // Eater rule: harmless until it locks on.
            if (homingTarget != null && !Projectile.friendly)
            {
                Projectile.friendly = true;
                Projectile.netUpdate = true;
            }

            // Desired velocity: toward the target at HomingSpeed, or
            // straight ahead at HomingSpeed if there's no target (the
            // eater aims at position + velocity * 100 when targetless,
            // which is the same as holding course and speed).
            Vector2 desired;
            if (homingTarget != null)
            {
                Vector2 toTarget = homingTarget.Center - Projectile.Center;
                float dist = toTarget.Length();
                if (dist > 0.001f)
                    desired = toTarget / dist * HomingSpeed;
                else
                    desired = Projectile.velocity; // already on top of it
            }
            else
            {
                desired = Projectile.velocity.SafeNormalize(Vector2.Zero) * HomingSpeed;
            }

            // Per-axis acceleration steering (aiStyle 36, copied exactly):
            // each axis accelerates toward the desired velocity, and an
            // axis that must REVERSE sign gets 3x total acceleration
            // (1x + 2x) — fast turnaround, still smooth, never a snap.
            if (Projectile.velocity.X < desired.X)
            {
                Projectile.velocity.X += HomingAccel;
                if (Projectile.velocity.X < 0f && desired.X > 0f)
                    Projectile.velocity.X += HomingAccel * 2f;
            }
            else if (Projectile.velocity.X > desired.X)
            {
                Projectile.velocity.X -= HomingAccel;
                if (Projectile.velocity.X > 0f && desired.X < 0f)
                    Projectile.velocity.X -= HomingAccel * 2f;
            }
            if (Projectile.velocity.Y < desired.Y)
            {
                Projectile.velocity.Y += HomingAccel;
                if (Projectile.velocity.Y < 0f && desired.Y > 0f)
                    Projectile.velocity.Y += HomingAccel * 2f;
            }
            else if (Projectile.velocity.Y > desired.Y)
            {
                Projectile.velocity.Y -= HomingAccel;
                if (Projectile.velocity.Y > 0f && desired.Y < 0f)
                    Projectile.velocity.Y -= HomingAccel * 2f;
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);

            // Fire burst on every hit.
            for (int i = 0; i < 12; i++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.Torch, 0f, 0f, 0, default, 1.5f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity = Main.rand.NextVector2Circular(4f, 4f);
                Main.dust[dust].color = Color.Orange;
                Main.dust[dust].scale = 1f + Main.rand.NextFloat(0.5f);
            }
            for (int i = 0; i < 8; i++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.OrangeTorch, 0f, 0f, 100, default, 1.2f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity = Main.rand.NextVector2Circular(5f, 5f);
            }

            // Shield rule: each hit costs 20% of the remaining damage.
            Projectile.damage = (int)(Projectile.damage * DamageFalloff);

            // Shield bounce (owner-only): find the next visible enemy and
            // redirect toward it at the SAME speed. FindTargetWithLineOfSight
            // (vanilla) searches 800f, requires line of sight, and skips
            // enemies this projectile recently hit (local immunity) — so
            // it never bounces straight back to the enemy it just struck.
            // NOTE: this redirect is instant, exactly like the shield's —
            // it happens ON IMPACT (a collision event), not as steering.
            if (Projectile.owner == Main.myPlayer)
            {
                int next = Projectile.FindTargetWithLineOfSight();
                if (next != -1)
                {
                    Projectile.velocity = Projectile.velocity.Length()
                        * Projectile.DirectionTo(Main.npc[next].Center);
                }
            }

            Projectile.netUpdate = true;
        }

        public override void OnKill(int timeLeft)
        {
            // Burn out in a small fire poof (5 hits spent, or time ran out).
            if (Main.netMode != NetmodeID.Server)
            {
                for (int i = 0; i < 8; i++)
                {
                    int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                        DustID.Torch, 0f, 0f, 0, default, 1.2f);
                    Main.dust[dust].noGravity = true;
                    Main.dust[dust].velocity = Main.rand.NextVector2Circular(3f, 3f);
                    Main.dust[dust].color = Color.Orange;
                }
            }
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((short)homingTimer);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            homingTimer = reader.ReadInt16();
        }

        public override bool OnTileCollide(Vector2 oldVelocity) => false;

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Main.EntitySpriteDraw(texture, drawPos, null,
                Projectile.GetAlpha(lightColor),
                Projectile.rotation,
                texture.Size() / 2f,
                Projectile.scale,
                SpriteEffects.None, 0);
            return false;
        }
    }
}
