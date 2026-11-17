using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.DataStructures;
using Microsoft.Xna.Framework;
using Terraria.Audio;
using System;

namespace CelestialAerus.Projectiles;

public class SpaceLaserBeam : ModProjectile
{
    private const int BeamDuration = 30;
    private int hitCooldown = 0;

    public override string Texture => "Terraria/Images/Extra_194";

    public override void SetDefaults()
    {
        Projectile.width = 120;
        Projectile.height = 3000;
        Projectile.friendly = true;
        Projectile.hostile = false;
        Projectile.DamageType = DamageClass.Magic;
        Projectile.penetrate = -1;
        Projectile.timeLeft = BeamDuration;
        Projectile.ignoreWater = true;
        Projectile.tileCollide = false;
    }

    // Standard collision must not damage NPCs: HitTargets() already strikes
    // them manually every 2 ticks.
    public override bool? CanHitNPC(NPC target) => false;

    public override void AI()
    {
        // The beam stays where the item spawned it (at the cursor). It must NOT
        // track Main.MouseWorld here: that is per-client (every player would see
        // the beam at a different spot) and (0,0) on a dedicated server.

        if (Main.netMode != NetmodeID.Server)
        {
            for (int i = 0; i < 40; i++)
            {
                int dustX = Main.rand.Next((int)Projectile.position.X, (int)(Projectile.position.X + Projectile.width));
                int dustY = Main.rand.Next((int)Projectile.position.Y, (int)(Projectile.position.Y + Projectile.height));
                
                int dust = Dust.NewDust(new Vector2(dustX, dustY), 8, 8, DustID.AncientLight, 0f, -4f, 200, default, 3f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].color = new Color(100, 200, 255, 255);
                Main.dust[dust].scale = Main.rand.NextFloat(2f, 5f);
                Main.dust[dust].alpha = 200;
            }

            for (int i = 0; i < 25; i++)
            {
                int dustX = Main.rand.Next((int)Projectile.position.X, (int)(Projectile.position.X + Projectile.width / 2));
                int dustY = Main.rand.Next((int)Projectile.position.Y, (int)(Projectile.position.Y + Projectile.height));
                
                int dust = Dust.NewDust(new Vector2(dustX, dustY), 6, 6, DustID.Enchanted_Gold, 0f, -6f, 255, default, 2.5f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].color = new Color(220, 250, 255, 255);
                Main.dust[dust].scale = Main.rand.NextFloat(2f, 4f);
            }

            for (int i = 0; i < 15; i++)
            {
                int dustX = Main.rand.Next((int)Projectile.Center.X - Projectile.width / 4, (int)Projectile.Center.X + Projectile.width / 4);
                int dustY = Main.rand.Next((int)Projectile.position.Y, (int)(Projectile.position.Y + Projectile.height));
                
                int dust = Dust.NewDust(new Vector2(dustX, dustY), 10, 10, DustID.Vortex, 0f, -2f, 255, default, 4f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].color = Color.White;
                Main.dust[dust].scale = Main.rand.NextFloat(3f, 6f);
            }
        }

        hitCooldown++;
        if (hitCooldown >= 2)
        {
            hitCooldown = 0;
            HitTargets();
        }
    }

    private void HitTargets()
    {
        Rectangle beamRect = new Rectangle(
            (int)Projectile.position.X,
            (int)Projectile.position.Y,
            Projectile.width,
            Projectile.height);

        // NPCs: owner only — AI() runs on every client, and StrikeNPC
        // applies damage from each caller, which would multiply the
        // damage in multiplayer.
        if (Projectile.owner == Main.myPlayer)
        {
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active)
                    continue;
                if (!npc.CanBeChasedBy(Projectile))
                    continue;
                if (beamRect.Intersects(npc.Hitbox))
                {
                    npc.StrikeNPC(new NPC.HitInfo
                    {
                        Damage = Projectile.damage,
                        Knockback = 15f,
                        HitDirection = 0,
                        Crit = false,
                        DamageType = DamageClass.Magic
                    });
                    SoundEngine.PlaySound(new SoundStyle("CelestialAerus/Sounds/SuperBoom"), npc.Center);
                }
            }
        }

        // Players: every client checks ONLY its own local player. Player
        // HP is server-authoritative, so Hurt/KillMe only sync when
        // called on the local player — the old owner-only loop called
        // Hurt on remote players, which silently did nothing.
        Player localPlayer = Main.LocalPlayer;
        if (localPlayer == null || !localPlayer.active || localPlayer.dead
            || !beamRect.Intersects(localPlayer.Hitbox))
            return;

        PlayerDeathReason deathReason =
            PlayerDeathReason.ByProjectile(Projectile.owner, Projectile.type);

        if (localPlayer.whoAmI == Projectile.owner)
        {
            // Pointing the remote at yourself is fatal, no matter what:
            // bypass Journey godmode, immunity frames, dodges, etc.
            bool wasInGodMode = localPlayer.creativeGodMode;
            localPlayer.creativeGodMode = false;
            localPlayer.statLife = 0;
            localPlayer.KillMe(deathReason, Projectile.damage, 0, false);
            localPlayer.creativeGodMode = wasInGodMode;
        }
        else
        {
            localPlayer.Hurt(deathReason, Projectile.damage, 0, false);
        }

        SoundEngine.PlaySound(new SoundStyle("CelestialAerus/Sounds/SuperBoom"), localPlayer.Center);
    }

    // The beam texture is intentionally not drawn; the visible beam is
    // built entirely from the dust particles spawned in AI().
    public override bool PreDraw(ref Color lightColor)
    {
        return false;
    }

}
