using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace CelestialAerus.Effects
{
    /// <summary>
    /// Draws a soft radial halo behind projectiles so they glow instead of
    /// leaving a hard trail line through the sprite. The halo texture is
    /// generated once at runtime, so no extra asset is required.
    /// </summary>
    public static class HaloDraw
    {
        private const int HaloTextureSize = 128;
        private static Texture2D _halo;

        public static void Draw(Vector2 worldCenter, float radius, Color color, float opacity)
        {
            if (Main.dedServ || radius <= 0f || opacity <= 0f)
                return;

            Texture2D halo = GetHalo();
            if (halo == null)
                return;

            Vector2 position = worldCenter - Main.screenPosition;
            float scale = radius * 2f / halo.Width;
            Color tint = new Color(color.R, color.G, color.B, (byte)(MathHelper.Clamp(opacity, 0f, 1f) * 255f));

            bool restarted = false;
            try
            {
                Main.spriteBatch.End();
                restarted = true;

                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive,
                    Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer,
                    null, Main.GameViewMatrix.TransformationMatrix);

                Main.EntitySpriteDraw(
                    halo,
                    position,
                    null,
                    tint,
                    0f,
                    halo.Size() / 2f,
                    scale,
                    SpriteEffects.None,
                    0);
            }
            catch
            {
                // A failed halo must never break projectile drawing.
            }
            finally
            {
                if (restarted)
                {
                    try { Main.spriteBatch.End(); } catch { }
                    try
                    {
                        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                            Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer,
                            null, Main.GameViewMatrix.TransformationMatrix);
                    }
                    catch { }
                }
            }
        }

        private static Texture2D GetHalo()
        {
            if (_halo != null && !_halo.IsDisposed)
                return _halo;

            try
            {
                GraphicsDevice device = Main.graphics?.GraphicsDevice;
                if (device == null)
                    return null;

                Color[] data = new Color[HaloTextureSize * HaloTextureSize];
                float half = HaloTextureSize / 2f;

                for (int y = 0; y < HaloTextureSize; y++)
                {
                    for (int x = 0; x < HaloTextureSize; x++)
                    {
                        float dx = (x + 0.5f - half) / half;
                        float dy = (y + 0.5f - half) / half;
                        float dist = (float)Math.Sqrt(dx * dx + dy * dy);

                        // Smooth falloff to a transparent rim.
                        float alpha = MathHelper.Clamp(1f - dist, 0f, 1f);
                        alpha = (float)Math.Pow(alpha, 1.35f);

                        // A brighter core keeps the halo from looking flat.
                        float core = (float)Math.Pow(MathHelper.Clamp(1f - dist * 2.2f, 0f, 1f), 2f);
                        alpha = MathHelper.Clamp(alpha + core * 0.35f, 0f, 1f);

                        data[y * HaloTextureSize + x] =
                            new Color(255, 255, 255, (byte)(alpha * 255f));
                    }
                }

                _halo = new Texture2D(device, HaloTextureSize, HaloTextureSize);
                _halo.SetData(data);
                return _halo;
            }
            catch
            {
                return null;
            }
        }
    }
}
