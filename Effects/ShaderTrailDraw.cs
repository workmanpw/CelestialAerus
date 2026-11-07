using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Graphics;

namespace CelestialAerus.Effects
{
    /// <summary>
    /// Draws smooth, variable-width ribbon trails from projectile center positions.
    /// The vertex colors carry the fade so alpha is applied only once by blending.
    /// </summary>
    public static class ShaderTrailDraw
    {
        private static VertexStrip _strip;

        public static Color WithAlpha(Color color, float alpha)
        {
            color.A = (byte)(MathHelper.Clamp(alpha, 0f, 1f) * 255f);
            return color;
        }

        public static void Draw(
            IReadOnlyList<Vector2> positions,
            int pointCount,
            Func<float, Color> colorFunction,
            Func<float, float> widthFunction,
            string shaderName = null,
            Action<Effect> configure = null,
            float startOffset = 0f)
        {
            if (Main.dedServ || positions == null || pointCount < 2 ||
                pointCount > positions.Count || colorFunction == null || widthFunction == null)
                return;

            Vector2[] points = new Vector2[pointCount];
            float[] rotations = new float[pointCount];

            for (int i = 0; i < pointCount; i++)
            {
                Vector2 before = positions[Math.Max(i - 1, 0)];
                Vector2 after = positions[Math.Min(i + 1, pointCount - 1)];
                Vector2 tangent = after - before;
                Vector2 direction = tangent.LengthSquared() > 0.001f
                    ? Vector2.Normalize(tangent)
                    : Vector2.Zero;

                rotations[i] = direction == Vector2.Zero ? 0f : direction.ToRotation();

                // Pull the whole ribbon back along its own tangent so the head
                // starts at the projectile's rear edge instead of its center.
                // This is what stops the ribbon from being visible through the
                // transparent parts of the projectile sprite.
                points[i] = positions[i] - direction * startOffset;
            }

            Color GetColor(float progress) => colorFunction(MathHelper.Clamp(progress, 0f, 1f));
            float GetWidth(float progress) => Math.Max(0f, widthFunction(MathHelper.Clamp(progress, 0f, 1f)));

            bool restarted = false;
            try
            {
                Main.spriteBatch.End();
                restarted = true;

                Effect effect = null;
                if (!string.IsNullOrEmpty(shaderName))
                {
                    try
                    {
                        effect = ShaderManager.GetShader(shaderName)?.Value;
                        if (effect != null)
                        {
                            effect.Parameters["Time"]?.SetValue(Main.GlobalTimeWrappedHourly);
                            var viewport = Main.graphics.GraphicsDevice.Viewport;
                            Matrix projection = Matrix.CreateOrthographicOffCenter(
                                0f, viewport.Width, viewport.Height, 0f, 0f, 1f);
                            effect.Parameters["uWorldViewProjection"]?.SetValue(
                                Main.GameViewMatrix.TransformationMatrix * projection);
                            configure?.Invoke(effect);
                        }
                    }
                    catch
                    {
                        effect = null;
                    }
                }

                _strip ??= new VertexStrip();

                // VertexStrip draws immediately with whatever texture is bound.
                // Without an explicit white pixel the strip samples the last
                // sprite texture, which produced the stretched line artifacts.
                GraphicsDevice device = Main.graphics.GraphicsDevice;
                Texture2D previousTexture = null;
                try { previousTexture = device.Textures[0] as Texture2D; } catch { }

                Texture2D ribbonTexture = null;
                try { ribbonTexture = TextureAssets.MagicPixel.Value; } catch { }
                if (ribbonTexture != null)
                {
                    try { device.Textures[0] = ribbonTexture; } catch { }
                }

                _strip.PrepareStrip(points, rotations, GetColor, GetWidth,
                    -Main.screenPosition, pointCount, true);
                if (effect != null)
                    effect.CurrentTechnique.Passes[0].Apply();
                else
                    Main.pixelShader.CurrentTechnique.Passes[0].Apply();
                _strip.DrawTrail();
                Main.pixelShader.CurrentTechnique.Passes[0].Apply();

                try { device.Textures[0] = previousTexture; } catch { }
            }
            finally
            {
                if (restarted)
                {
                    Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend,
                        Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer,
                        null, Main.GameViewMatrix.TransformationMatrix);
                }
            }
        }
    }
}
