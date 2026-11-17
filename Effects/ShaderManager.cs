using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;
using Terraria;
using ReLogic.Content;

namespace CelestialAerus.Effects
{
    /// <summary>
    /// Comprehensive shader manager for all CelestialAerus mod shaders.
    /// Handles shader loading, caching, and lookup; actual drawing goes
    /// through SafeShaderDraw and ShaderTrailDraw.
    /// </summary>
    public class ShaderManager : ModSystem
    {
        #region Core Shader Management

        private static Dictionary<string, Asset<Effect>> _loadedShaders = new Dictionary<string, Asset<Effect>>();
        private static bool _initialized = false;

        public override void Load()
        {
            if (Main.dedServ)
                return;

            _loadedShaders = new Dictionary<string, Asset<Effect>>();
            _initialized = true;

            // Pre-load essential shaders
            PreloadEssentialShaders();
        }

        public override void Unload()
        {
            // Cleanup all shader references (assets are automatically
            // disposed by tML).
            if (_loadedShaders != null)
            {
                _loadedShaders.Clear();
            }

            // Release HaloDraw's runtime-generated texture so it doesn't
            // leak GPU memory on every mod reload.
            HaloDraw.Dispose();

            _initialized = false;
        }

        /// <summary>
        /// Safely loads a shader from the mod's effects folder
        /// </summary>
        /// <param name="shaderPath">Path relative to Effects folder (e.g., "CelestialFlame")</param>
        /// <param name="shaderName">Unique identifier for the shader</param>
        /// <param name="frameCount">Number of frames for animated shaders (optional)</param>
        /// <returns>Asset to the loaded effect, or null if loading failed</returns>
        public static Asset<Effect> LoadShader(string shaderPath, string shaderName = null, int frameCount = 0)
        {
            if (!_initialized)
                return null;

            shaderName = shaderName ?? shaderPath;

            // Return cached shader if already loaded
            if (_loadedShaders.TryGetValue(shaderName, out var cachedEffect))
            {
                return cachedEffect;
            }

            try
            {
                // Attempt to load the shader
                var effectAsset = ModContent.Request<Effect>("CelestialAerus/Effects/" + shaderPath, ReLogic.Content.AssetRequestMode.ImmediateLoad);

                if (effectAsset != null && effectAsset.Value != null)
                {
                    _loadedShaders[shaderName] = effectAsset;
                    return effectAsset;
                }
            }
            catch
            {
                // Silent fail - shader loading is optional
            }
            return null;
        }

        /// <summary>
        /// Gets a loaded shader by name
        /// </summary>
        /// <param name="shaderName">Name of the shader</param>
        /// <returns>Effect asset or null if not found</returns>
        public static Asset<Effect> GetShader(string shaderName)
        {
            if (!_initialized)
                return null;

            _loadedShaders.TryGetValue(shaderName, out var effectRef);
            return effectRef;
        }

        /// <summary>
        /// Checks if a shader is loaded
        /// </summary>
        /// <param name="shaderName">Name of the shader</param>
        /// <returns>True if shader is loaded</returns>
        public static bool IsShaderLoaded(string shaderName)
        {
            if (!_initialized)
                return false;

            return _loadedShaders.ContainsKey(shaderName);
        }

        /// <summary>
        /// Preloads essential shaders that are commonly used
        /// </summary>
        private static void PreloadEssentialShaders()
        {
            var essentialShaders = new[]
            {
                "CelestialFlame",
                "CelestialTrail",
                "CosmicGlow",
                "AethericPulse",
                "GalaxySwirl",
                "EonBeam",
                "AncientHatchetFlame",
                "CelestialFireTrail",
                "CelestialRibbon"
            };

            foreach (var shader in essentialShaders)
            {
                LoadShader(shader);
            }
        }

        #endregion
    }
}
