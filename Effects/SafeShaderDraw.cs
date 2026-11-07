using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using ReLogic.Content;

namespace CelestialAerus.Effects
{
    /// <summary>
    /// Safely applies pixel shaders to projectile drawing.
    /// Every operation is null-checked and exception-guarded; SpriteBatch state is always restored.
    /// If the shader is missing or fails, drawing falls back to unshaded WITHOUT touching SpriteBatch.
    /// </summary>
    public static class SafeShaderDraw
    {
        /// <summary>
        /// Draws using the named shader. Returns false (drawing handled by caller).
        /// If no shader is available, draws normally without disturbing SpriteBatch state.
        /// </summary>
        public static bool DrawWithShader(string shaderName, Action drawAction, Action<Effect> configure = null)
        {
            if (drawAction == null)
                return false;

            // Dedicated server has no graphics device; draw normally.
            if (Main.dedServ)
            {
                try { drawAction(); } catch { }
                return false;
            }

            // Get the shader safely (may be null if not loaded).
            Effect effect = null;
            try
            {
                Asset<Effect> asset = ShaderManager.GetShader(shaderName);
                if (asset != null)
                {
                    try { effect = asset.Value; } catch { effect = null; }
                }
            }
            catch { effect = null; }

            // NO SHADER: draw normally without touching SpriteBatch (safest path).
            if (effect == null)
            {
                try { drawAction(); } catch { }
                return false;
            }

            // HAVE SHADER: restart SpriteBatch in Immediate mode, passing the effect to Begin().
            // SpriteBatch manages Apply()/unapply, so the effect does NOT leak onto later draws (dust, etc).
            bool batchRestarted = false;
            try
            {
                try
                {
                    effect.Parameters["Time"]?.SetValue(Main.GlobalTimeWrappedHourly);
                    // Custom vertex shaders bypass SpriteBatch's built-in transform,
                    // so include Terraria's zoom/view transform and pixel projection.
                    try
                    {
                        var vp = Main.graphics.GraphicsDevice.Viewport;
                        var ortho = Matrix.CreateOrthographicOffCenter(0, vp.Width, vp.Height, 0, 0, 1);
                        effect.Parameters["uWorldViewProjection"]?.SetValue(Main.GameViewMatrix.TransformationMatrix * ortho);
                    }
                    catch { }
                    configure?.Invoke(effect);
                }
                catch { }

                try { Main.spriteBatch.End(); } catch { }
                Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend,
                    Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer,
                    effect, Main.GameViewMatrix.TransformationMatrix);
                batchRestarted = true;

                drawAction();
            }
            catch
            {
                // Something failed mid-draw; attempt plain draw as last resort.
                try { drawAction(); } catch { }
            }
            finally
            {
                // Restore Terraria's SpriteBatch state ONLY if we restarted it.
                if (batchRestarted)
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

            return false;
        }

        // Safe parameter setters (no-ops if effect/parameter missing).
        public static void Set(Effect e, string name, float v)
        { try { e?.Parameters[name]?.SetValue(v); } catch { } }

        public static void Set(Effect e, string name, bool v)
        { try { e?.Parameters[name]?.SetValue(v); } catch { } }

        public static void Set(Effect e, string name, int v)
        { try { e?.Parameters[name]?.SetValue(v); } catch { } }

        public static void Set(Effect e, string name, Vector2 v)
        { try { e?.Parameters[name]?.SetValue(v); } catch { } }

        public static void Set(Effect e, string name, Vector4 v)
        { try { e?.Parameters[name]?.SetValue(v); } catch { } }

        public static void Set(Effect e, string name, Color v)
        { try { e?.Parameters[name]?.SetValue(v.ToVector4()); } catch { } }
    }
}
