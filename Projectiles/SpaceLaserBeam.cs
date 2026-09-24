using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.DataStructures;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

    public override void AI()
    {
        float cursorX = Main.MouseWorld.X;
        float cursorY = Main.MouseWorld.Y;
        
        Projectile.position.X = cursorX - Projectile.width / 2;
        Projectile.position.Y = cursorY - 1500;

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

        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (npc.active && beamRect.Intersects(npc.Hitbox))
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

        for (int i = 0; i < Main.maxPlayers; i++)
        {
            Player player = Main.player[i];
            if (player.active && beamRect.Intersects(player.Hitbox))
            {
                player.Hurt(PlayerDeathReason.ByProjectile(Projectile.owner, Projectile.type), Projectile.damage, 0, true);
                SoundEngine.PlaySound(new SoundStyle("CelestialAerus/Sounds/SuperBoom"), player.Center);
            }
        }
    }

}
