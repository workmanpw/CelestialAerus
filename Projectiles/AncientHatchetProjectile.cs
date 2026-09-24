using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;

namespace CelestialAerus.Projectiles
{
    public class AncientHatchetProjectile : ModProjectile
    {
        // --- Constants ---
        private const int HomingDelay = 15;
        private const float HomingSpeed = 22f;
        private const int MaxBounces = 3;
        private const float ReturnSpeed = 44f;
        private const int MaxStickTime = 300;
        private const int DotInterval = 30;
        private const float RotationSpeed = 0.4f;

        private const float ReturnKillDistance = ReturnSpeed * 2f;
        private const float MaxReturnDistance  = 3000f;

        // --- Fallback return triggers ---
        private const int NoTargetTimeout = 120;         // ~1 s with no target
        private const int LowTimeReturnThreshold = 60;   // start returning with this much timeLeft

        // --- State ---
        private int homingTimer = 0;
        private int noTargetTimer = 0;
        private int bounceCount = 0;
        private bool isReturning = false;
        private bool isStuck = false;
        private int stuckNPCWho = -1;
        private int stickTimer = 0;
        private int dotCounter = 0;
        private float originalDamage = 0f;
        private bool initialized = false;
        private NPC lastHitNPC = null;

        public override string Texture => "CelestialAerus/Textures/AncientHatchet";
        public override LocalizedText DisplayName => Language.GetOrRegister("Ancient Hatchet");

        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.PossessedHatchet);
            Projectile.width = 28;
            Projectile.height = 28;
            Projectile.scale = 0.7f;

            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;

            Projectile.timeLeft = 300;
            Projectile.extraUpdates = 1;

            Projectile.tileCollide = false;
            Projectile.light = 1f;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 60;
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

            // --- Dust Trail ---
            if (Main.rand.NextBool(2))
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
                Main.dust[dust].fadeIn = 1f;
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
                    Main.dust[spark].fadeIn = 0.5f;
                }
            }

            // --- Stuck state ---
            if (isStuck)
            {
                if (stuckNPCWho < 0 || stuckNPCWho >= Main.maxNPCs)
                {
                    StartReturning();
                    return;
                }

                NPC target = Main.npc[stuckNPCWho];
                if (!target.active || target.life <= 0)
                {
                    StartReturning();
                    return;
                }

                Projectile.Center = target.Center;

                dotCounter++;
                if (dotCounter % DotInterval == 0)
                {
                    if (Projectile.owner == Main.myPlayer)
                    {
                        target.SimpleStrikeNPC((int)originalDamage, 0, false, 0f, DamageClass.Melee);
                    }

                    if (Main.netMode != NetmodeID.Server)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            Dust.NewDust(target.position, target.width, target.height,
                                DustID.Torch, 0f, 0f, 0, default, 1.2f);
                        }
                    }
                }

                stickTimer++;
                if (stickTimer >= MaxStickTime)
                {
                    StartReturning();
                }
                return;
            }

            // --- Returning state ---
            if (isReturning)
            {
                Vector2 toPlayer = player.Center - Projectile.Center;
                float dist = toPlayer.Length();

                if (dist <= ReturnKillDistance)
                {
                    Projectile.Kill();
                    return;
                }

                if (dist > MaxReturnDistance)
                {
                    Projectile.Kill();
                    return;
                }

                Projectile.velocity = toPlayer.SafeNormalize(Vector2.Zero) * ReturnSpeed;
                return;
            }

            // --- Homing state ---

            // Fallback #1: not enough timeLeft to home safely -> return.
            if (Projectile.timeLeft <= LowTimeReturnThreshold)
            {
                StartReturning();
                return;
            }

            homingTimer++;
            if (homingTimer >= HomingDelay)
            {
                NPC target = null;
                float minDist = 1000f;
                foreach (NPC npc in Main.npc)
                {
                    if (npc.CanBeChasedBy(Projectile) && npc != lastHitNPC)
                    {
                        float d = Vector2.Distance(Projectile.Center, npc.Center);
                        if (d < minDist)
                        {
                            minDist = d;
                            target = npc;
                        }
                    }
                }

                if (target != null)
                {
                    noTargetTimer = 0;
                    Vector2 dir = target.Center - Projectile.Center;
                    Projectile.velocity = dir.SafeNormalize(Vector2.Zero) * HomingSpeed;
                }
                else
                {
                    // Fallback #2: no valid target for too long -> return.
                    noTargetTimer++;
                    if (noTargetTimer >= NoTargetTimeout)
                    {
                        StartReturning();
                    }
                }
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (isStuck)
                return;

            Player player = Main.player[Projectile.owner];

            if (!isReturning)
            {
                int lifeStealAmount = 50;
                if (player.statLife + lifeStealAmount > player.statLifeMax2)
                    lifeStealAmount = player.statLifeMax2 - player.statLife;
                player.Heal(lifeStealAmount);
                player.HealEffect(lifeStealAmount);
            }

            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
            for (int i = 0; i < 40; i++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.OrangeTorch, 0f, 0f, 100, default, 2f);
                Main.dust[dust].velocity *= 3f;
                if (Main.rand.NextBool())
                {
                    Main.dust[dust].scale = 0.5f;
                    Main.dust[dust].fadeIn = 1f + Main.rand.Next(10) * 0.1f;
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

            if (!isReturning)
            {
                if (bounceCount >= MaxBounces - 1)
                {
                    isStuck = true;
                    stuckNPCWho = target.whoAmI;
                    stickTimer = 0;
                    dotCounter = 0;
                    Projectile.velocity = Vector2.Zero;
                    Projectile.timeLeft = 9999;
                    Projectile.damage = 0;
                    Projectile.tileCollide = false;
                    target.AddBuff(BuffID.Daybreak, 300);
                }
                else
                {
                    bounceCount++;
                    homingTimer = 0;
                    noTargetTimer = 0;
                    lastHitNPC = target;
                }
            }

            Projectile.netUpdate = true;
        }

        private void StartReturning()
        {
            if (isReturning) return;

            isReturning = true;
            isStuck = false;
            Projectile.damage = (int)(originalDamage * 0.25f);
            Projectile.timeLeft = 9999;
            Projectile.netUpdate = true;

            Player player = Main.player[Projectile.owner];
            if (player != null && player.active)
            {
                Vector2 toPlayer = player.Center - Projectile.Center;
                if (toPlayer != Vector2.Zero)
                    Projectile.velocity = toPlayer.SafeNormalize(Vector2.Zero) * ReturnSpeed;
            }
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(isStuck);
            writer.Write(isReturning);
            writer.Write((short)stuckNPCWho);
            writer.Write((short)stickTimer);
            writer.Write((short)dotCounter);
            writer.Write((byte)bounceCount);
            writer.Write((short)homingTimer);
            writer.Write((short)noTargetTimer);
            writer.Write(originalDamage);
            writer.Write(initialized);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            isStuck        = reader.ReadBoolean();
            isReturning    = reader.ReadBoolean();
            stuckNPCWho    = reader.ReadInt16();
            stickTimer     = reader.ReadInt16();
            dotCounter     = reader.ReadInt16();
            bounceCount    = reader.ReadByte();
            homingTimer    = reader.ReadInt16();
            noTargetTimer  = reader.ReadInt16();
            originalDamage = reader.ReadSingle();
            initialized    = reader.ReadBoolean();
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
