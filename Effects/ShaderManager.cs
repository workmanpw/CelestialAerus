using System;
using System.Collections.Generic;
using Terraria.Graphics.Shaders;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;
using Terraria;
using Microsoft.Xna.Framework;
using ReLogic.Content;

namespace CelestialAerus.Effects
{
    /// <summary>
    /// Comprehensive shader manager for all CelestialAerus mod shaders.
    /// Contains shader loading, base classes, specific effects, and integration methods.
    /// </summary>
    public class ShaderManager : ModSystem
    {
        #region Core Shader Management

        private static Dictionary<string, Asset<Effect>> _loadedShaders;
        private static Dictionary<string, ShaderData> _shaderDataCache;
        private static bool _initialized = false;

        public override void Load()
        {
            if (Main.dedServ)
                return;

            _loadedShaders = new Dictionary<string, Asset<Effect>>();
            _shaderDataCache = new Dictionary<string, ShaderData>();
            _initialized = true;

            // Pre-load essential shaders
            PreloadEssentialShaders();
        }

        public override void Unload()
        {
            // Cleanup all shader references
            if (_loadedShaders != null)
            {
                foreach (var _ in _loadedShaders)
                {
                    // Assets are automatically disposed by tML
                }
                _loadedShaders.Clear();
            }

            if (_shaderDataCache != null)
            {
                _shaderDataCache.Clear();
            }

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
        /// Creates shader data for use with Terraria's shader system
        /// </summary>
        /// <param name="shaderPath">Path to the shader file</param>
        /// <param name="shaderName">Unique identifier</param>
        /// <param name="passName">Specific pass to use (optional)</param>
        /// <returns>ShaderData instance or null if failed</returns>
        public static ShaderData CreateShaderData(string shaderPath, string shaderName = null, string passName = "Pass0")
        {
            if (!_initialized)
                return null;

            shaderName = shaderName ?? shaderPath;

            // Return cached shader data if available
            if (_shaderDataCache.TryGetValue(shaderName, out var cachedData))
            {
                return cachedData;
            }

            var effectRef = LoadShader(shaderPath, shaderName);
            if (effectRef != null && effectRef.Value != null)
            {
                try
                {
                    var shaderData = new ShaderData(effectRef, passName);
                    _shaderDataCache[shaderName] = shaderData;
                    return shaderData;
                }
                catch
                {
                    // Silent fail - shader data creation is optional
                }
            }

            return null;
        }

        /// <summary>
        /// Safely unloads and disposes a specific shader
        /// </summary>
        /// <param name="shaderName">Name of the shader to unload</param>
        public static void UnloadShader(string shaderName)
        {
            if (_loadedShaders.TryGetValue(shaderName, out var _))
            {
                // Asset is automatically disposed by tML
                _loadedShaders.Remove(shaderName);
            }

            if (_shaderDataCache.TryGetValue(shaderName, out var _))
            {
                _shaderDataCache.Remove(shaderName);
            }
        }

        /// <summary>
        /// Gets a loaded shader by name
        /// </summary>
        /// <param name="shaderName">Name of the shader</param>
        /// <returns>Effect asset or null if not found</returns>
        public static Asset<Effect> GetShader(string shaderName)
        {
            _loadedShaders.TryGetValue(shaderName, out var effectRef);
            return effectRef;
        }

        /// <summary>
        /// Gets shader data by name
        /// </summary>
        /// <param name="shaderName">Name of the shader</param>
        /// <returns>ShaderData or null if not found</returns>
        public static ShaderData GetShaderData(string shaderName)
        {
            _shaderDataCache.TryGetValue(shaderName, out var shaderData);
            return shaderData;
        }

        /// <summary>
        /// Checks if a shader is loaded
        /// </summary>
        /// <param name="shaderName">Name of the shader</param>
        /// <returns>True if shader is loaded</returns>
        public static bool IsShaderLoaded(string shaderName)
        {
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

        /// <summary>
        /// Reloads all shaders (useful for development/debugging)
        /// </summary>
        public static void ReloadAllShaders()
        {
            var shaderNames = new List<string>(_loadedShaders.Keys);

            // Clear current cache
            _loadedShaders.Clear();
            _shaderDataCache.Clear();

            // Reload all shaders
            foreach (var shaderName in shaderNames)
            {
                LoadShader(shaderName);
            }
        }

        /// <summary>
        /// Gets statistics about loaded shaders
        /// </summary>
        /// <returns>Dictionary with shader statistics</returns>
        public static Dictionary<string, object> GetShaderStats()
        {
            return new Dictionary<string, object>
            {
                ["LoadedShaders"] = _loadedShaders.Count,
                ["CachedShaderData"] = _shaderDataCache.Count,
                ["Initialized"] = _initialized
            };
        }

        /// <summary>
        /// Applies shader parameters safely with error handling
        /// </summary>
        /// <param name="shaderName">Name of the shader</param>
        /// <param name="parameterName">Parameter to set</param>
        /// <param name="value">Value to set</param>
        public static void SetShaderParameter(string shaderName, string parameterName, object value)
        {
            var shaderAsset = GetShader(shaderName);
            if (shaderAsset?.Value != null)
            {
                var parameters = shaderAsset.Value.Parameters;
                if (parameters == null) return;
                
                var parameter = parameters[parameterName];
                if (parameter != null)
                {
                    try
                    {
                        switch (value)
                        {
                            case float f: parameter.SetValue(f); break;
                            case Vector2 v2: parameter.SetValue(v2); break;
                            case Vector3 v3: parameter.SetValue(v3); break;
                            case Vector4 v4: parameter.SetValue(v4); break;
                            case Color c: parameter.SetValue(c.ToVector4()); break;
                            case Texture2D t: parameter.SetValue(t); break;
                            case bool b: parameter.SetValue(b); break;
                            case int i: parameter.SetValue(i); break;
                        }
                    }
                    catch
                    {
                        // Silent fail - parameter setting is optional
                    }
                }
            }
        }

        #endregion

        #region Base Shader Effect Classes

        /// <summary>
        /// Base class for all custom shader effects with common functionality
        /// </summary>
        public abstract class BaseShaderEffect
        {
            protected string ShaderName { get; set; }
            protected Asset<Effect> ShaderAsset { get; private set; }
            protected bool IsLoaded { get; private set; }
            protected float Intensity { get; set; } = 1.0f;
            protected Color BaseColor { get; set; } = Color.White;
            protected Vector2 Position { get; set; }
            protected float Rotation { get; set; }
            protected float Scale { get; set; } = 1.0f;
            protected float Time { get; set; }

            protected BaseShaderEffect(string shaderName, int frameCount = 0)
            {
                ShaderName = shaderName;
                LoadShader(frameCount);
            }

            /// <summary>
            /// Loads the shader asset
            /// </summary>
            protected virtual void LoadShader()
            {
                ShaderAsset = ShaderManager.LoadShader(ShaderName, ShaderName);
                IsLoaded = ShaderAsset != null && ShaderAsset.Value != null;
            }

            /// <summary>
            /// Loads the shader asset with frame count
            /// </summary>
            protected virtual void LoadShader(int frameCount)
            {
                ShaderAsset = ShaderManager.LoadShader(ShaderName, ShaderName, frameCount);
                IsLoaded = ShaderAsset != null && ShaderAsset.Value != null;
            }

            /// <summary>
            /// Applies shader parameters before rendering
            /// </summary>
            public virtual void ApplyParameters()
            {
                if (!IsLoaded || ShaderAsset?.Value == null) return;

                if (ShaderAsset?.Value != null)
                {
                    // Set common parameters
                    SetParameter("Intensity", Intensity);
                    SetParameter("BaseColor", BaseColor.ToVector4());
                    SetParameter("Position", Position);
                    SetParameter("Rotation", Rotation);
                    SetParameter("Scale", Scale);
                    SetParameter("Time", Time);

                    // Allow derived classes to set specific parameters
                    ApplySpecificParameters();
                }
            }

            /// <summary>
            /// Override this to set shader-specific parameters
            /// </summary>
            protected abstract void ApplySpecificParameters();



            /// <summary>
            /// Updates the effect state
            /// </summary>
            public virtual void Update(float deltaTime)
            {
                Time += deltaTime;
            }

            /// <summary>
            /// Sets the basic properties of the effect
            /// </summary>
            public virtual void SetProperties(Vector2 position, float rotation, float scale, Color color, float intensity = 1.0f)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
                BaseColor = color;
                Intensity = intensity;
            }

            /// <summary>
            /// Gets the shader for use in rendering
            /// </summary>
            public Effect GetShader()
            {
                return ShaderAsset?.Value;
            }

            // Expose a simple readiness check for safe fallbacks in render paths
            public bool IsReady()
            {
                return IsLoaded;
            }

            /// <summary>
            /// Safely sets a shader parameter
            /// </summary>
            protected void SetParameter(string name, object value)
            {
                if (ShaderAsset?.Value == null)
                    return;

                var parameters = ShaderAsset.Value.Parameters;
                if (parameters == null)
                    return;

                var parameter = parameters[name];
                if (parameter == null)
                    return;

                try
                {
                    switch (value)
                    {
                        case float f:
                            parameter.SetValue(f);
                            break;
                        case Vector2 v2:
                            parameter.SetValue(v2);
                            break;
                        case Vector3 v3:
                            parameter.SetValue(v3);
                            break;
                        case Vector4 v4:
                            parameter.SetValue(v4);
                            break;
                        case Color c:
                            parameter.SetValue(c.ToVector4());
                            break;
                        case Texture2D t:
                            parameter.SetValue(t);
                            break;
                        case bool b:
                            parameter.SetValue(b);
                            break;
                        case int i:
                            parameter.SetValue(i);
                            break;
                    }
                }
                catch
                {
                    // Silent fail - parameter setting is optional
                }
            }

            /// <summary>
            /// Reloads the shader (useful for development)
            /// </summary>
            public virtual void Reload()
            {
                LoadShader();
            }
        }

        /// <summary>
        /// Base class for animated shader effects
        /// </summary>
        public abstract class AnimatedShaderEffect : BaseShaderEffect
        {
            protected float AnimationSpeed { get; set; } = 1.0f;
            protected bool IsLooping { get; set; } = true;
            protected float CurrentFrame { get; set; }
            protected int TotalFrames { get; set; } = 1;
            protected bool IsPlaying { get; set; } = true;

            protected AnimatedShaderEffect(string shaderName, int totalFrames = 1) : base(shaderName)
            {
                TotalFrames = totalFrames;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);

                if (IsPlaying)
                {
                    CurrentFrame += deltaTime * AnimationSpeed;

                    if (IsLooping)
                    {
                        CurrentFrame %= TotalFrames;
                    }
                    else if (CurrentFrame >= TotalFrames)
                    {
                        CurrentFrame = TotalFrames - 1;
                        IsPlaying = false;
                    }
                }
            }

            /// <summary>
            /// Starts or resumes the animation
            /// </summary>
            public void Play()
            {
                IsPlaying = true;
            }

            /// <summary>
            /// Pauses the animation
            /// </summary>
            public void Pause()
            {
                IsPlaying = false;
            }

            /// <summary>
            /// Stops the animation and resets to the beginning
            /// </summary>
            public void Stop()
            {
                IsPlaying = false;
                CurrentFrame = 0;
            }

            /// <summary>
            /// Sets the animation speed
            /// </summary>
            public void SetAnimationSpeed(float speed)
            {
                AnimationSpeed = Math.Max(0, speed);
            }

            /// <summary>
            /// Gets the current animation progress (0-1)
            /// </summary>
            public float GetAnimationProgress()
            {
                return TotalFrames > 0 ? CurrentFrame / TotalFrames : 0;
            }
        }

        /// <summary>
        /// Base class for particle-based shader effects
        /// </summary>
        public abstract class ParticleShaderEffect : BaseShaderEffect
        {
            protected int ParticleCount { get; set; } = 100;
            protected float ParticleSize { get; set; } = 1.0f;
            protected float ParticleLifetime { get; set; } = 1.0f;
            protected Vector2 EmissionDirection { get; set; } = Vector2.UnitY;
            protected float EmissionSpread { get; set; } = MathHelper.PiOver4;
            protected float EmissionRate { get; set; } = 10.0f;

            protected ParticleShaderEffect(string shaderName) : base(shaderName) { }

            /// <summary>
            /// Sets particle system properties
            /// </summary>
            public virtual void SetParticleProperties(int count, float size, float lifetime, Vector2 direction, float spread, float rate)
            {
                ParticleCount = Math.Max(1, count);
                ParticleSize = Math.Max(0.1f, size);
                ParticleLifetime = Math.Max(0.1f, lifetime);
                EmissionDirection = Vector2.Normalize(direction);
                EmissionSpread = MathHelper.Clamp(spread, 0, MathHelper.TwoPi);
                EmissionRate = Math.Max(0, rate);
            }

            protected override void ApplySpecificParameters()
            {
                SetParameter("ParticleCount", ParticleCount);
                SetParameter("ParticleSize", ParticleSize);
                SetParameter("ParticleLifetime", ParticleLifetime);
                SetParameter("EmissionDirection", EmissionDirection);
                SetParameter("EmissionSpread", EmissionSpread);
                SetParameter("EmissionRate", EmissionRate);
            }
        }

        /// <summary>
        /// Base class for trail shader effects
        /// </summary>
        public abstract class TrailShaderEffect : BaseShaderEffect
        {
            protected float TrailLength { get; set; } = 10.0f;
            protected float TrailWidth { get; set; } = 1.0f;
            protected Color TrailStartColor { get; set; } = Color.White;
            protected Color TrailEndColor { get; set; } = Color.Transparent;
            protected bool TrailFades { get; set; } = true;
            protected float TrailFadeSpeed { get; set; } = 1.0f;

            protected TrailShaderEffect(string shaderName) : base(shaderName) { }

            /// <summary>
            /// Sets trail properties
            /// </summary>
            public virtual void SetTrailProperties(float length, float width, Color startColor, Color endColor, bool fades = true, float fadeSpeed = 1.0f)
            {
                TrailLength = Math.Max(0.1f, length);
                TrailWidth = Math.Max(0.1f, width);
                TrailStartColor = startColor;
                TrailEndColor = endColor;
                TrailFades = fades;
                TrailFadeSpeed = Math.Max(0.1f, fadeSpeed);
            }

            protected override void ApplySpecificParameters()
            {
                SetParameter("TrailLength", TrailLength);
                SetParameter("TrailWidth", TrailWidth);
                SetParameter("TrailStartColor", TrailStartColor.ToVector4());
                SetParameter("TrailEndColor", TrailEndColor.ToVector4());
                SetParameter("TrailFades", TrailFades);
                SetParameter("TrailFadeSpeed", TrailFadeSpeed);
            }
        }

        #endregion

        #region Specific Shader Effects

        /// <summary>
        /// Enhanced celestial flame shader effect for fiery weapon visuals
        /// </summary>
        public class EnhancedCelestialFlameEffect : AnimatedShaderEffect
        {
            public float FlameIntensity { get; set; } = 1.0f;
            public float FlameDistortion { get; set; } = 0.5f;
            public Color FlameCoreColor { get; set; } = Color.Orange;
            public Color FlameEdgeColor { get; set; } = Color.Red;
            public float FlameSpeed { get; set; } = 2.0f;

            public EnhancedCelestialFlameEffect() : base("CelestialFlame", 8)
            {
                AnimationSpeed = FlameSpeed;
            }

            protected override void ApplySpecificParameters()
            {
                SetParameter("FlameIntensity", FlameIntensity);
                SetParameter("FlameDistortion", FlameDistortion);
                SetParameter("FlameCoreColor", FlameCoreColor.ToVector4());
                SetParameter("FlameEdgeColor", FlameEdgeColor.ToVector4());
                SetParameter("FlameSpeed", FlameSpeed);
                SetParameter("CurrentFrame", CurrentFrame);
            }

            public void SetFlameProperties(float intensity, float distortion, Color coreColor, Color edgeColor, float speed)
            {
                FlameIntensity = intensity;
                FlameDistortion = distortion;
                FlameCoreColor = coreColor;
                FlameEdgeColor = edgeColor;
                FlameSpeed = speed;
                AnimationSpeed = speed;
            }
        }
        
        #region Celestial Fire Trail Effect

        /// <summary>
        /// Celestial fire trail shader effect for cosmic fire projectile
        /// </summary>
        public class CelestialFireTrailEffect : BaseShaderEffect
        {
            public float TrailLength { get; set; } = 1.0f;
            public float TrailWidth { get; set; } = 0.5f;
            public Color TrailStartColor { get; set; } = Color.White;
            public Color TrailEndColor { get; set; } = Color.Cyan;
            public bool TrailFades { get; set; } = true;
            public float TrailFadeSpeed { get; set; } = 0.5f;
            public float TrailPulseSpeed { get; set; } = 3.0f;
            public float TrailPulseIntensity { get; set; } = 0.3f;
            public bool TrailGlows { get; set; } = true;
            public Color TrailGlowColor { get; set; } = Color.Yellow;
            public float FireIntensity { get; set; } = 1.0f;
            public Color FireColor { get; set; } = Color.Orange;
            public float FireDecay { get; set; } = 0.8f;
            public bool FireEmbers { get; set; } = true;

            public CelestialFireTrailEffect() : base("CelestialFireTrail", 8)
            {
                TrailPulseSpeed = 3.0f;
            }

            protected override void ApplySpecificParameters()
            {
                SetParameter("TrailLength", TrailLength);
                SetParameter("TrailWidth", TrailWidth);
                SetParameter("TrailStartColor", TrailStartColor.ToVector4());
                SetParameter("TrailEndColor", TrailEndColor.ToVector4());
                SetParameter("TrailFades", TrailFades);
                SetParameter("TrailFadeSpeed", TrailFadeSpeed);
                SetParameter("TrailPulseSpeed", TrailPulseSpeed);
                SetParameter("TrailPulseIntensity", TrailPulseIntensity);
                SetParameter("TrailGlows", TrailGlows);
                SetParameter("TrailGlowColor", TrailGlowColor.ToVector4());
                SetParameter("FireIntensity", FireIntensity);
                SetParameter("FireColor", FireColor.ToVector4());
                SetParameter("FireDecay", FireDecay);
                SetParameter("FireEmbers", FireEmbers);
            }

            public void SetTrailProperties(Vector2 position, float rotation, float scale, Color color, float intensity)
            {
                TrailLength = Math.Max(0.1f, scale);
                TrailWidth = Math.Max(0.1f, scale);
                TrailStartColor = color;
                TrailEndColor = Color.Lerp(color, Color.Cyan, 0.5f);
                TrailFades = true;
                TrailFadeSpeed = Math.Max(0.1f, intensity);
            }

            public void SetFireProperties(float intensity, Color fireColor, float decay, bool embers)
            {
                FireIntensity = intensity;
                FireColor = fireColor;
                FireDecay = decay;
                FireEmbers = embers;
            }
        }

        #endregion

        /// <summary>
        /// Enhanced celestial trail effect for weapon trails and projectile paths
        /// </summary>
        public class EnhancedCelestialTrailEffect : TrailShaderEffect
        {
            public float TrailPulseSpeed { get; set; } = 3.0f;
            public float TrailPulseIntensity { get; set; } = 0.3f;
            public bool TrailGlows { get; set; } = true;
            public Color TrailGlowColor { get; set; } = Color.Cyan;

            public EnhancedCelestialTrailEffect() : base("CelestialTrail")
            {
            }

            protected override void ApplySpecificParameters()
            {
                base.ApplySpecificParameters();
                SetParameter("TrailPulseSpeed", TrailPulseSpeed);
                SetParameter("TrailPulseIntensity", TrailPulseIntensity);
                SetParameter("TrailGlows", TrailGlows);
                SetParameter("TrailGlowColor", TrailGlowColor.ToVector4());
            }

            public void SetTrailPulseProperties(float pulseSpeed, float pulseIntensity, bool glows, Color glowColor)
            {
                TrailPulseSpeed = pulseSpeed;
                TrailPulseIntensity = pulseIntensity;
                TrailGlows = glows;
                TrailGlowColor = glowColor;
            }
        }

        /// <summary>
        /// Cosmic glow effect for ethereal weapon visuals
        /// </summary>
        public class CosmicGlowEffect : BaseShaderEffect
        {
            public float GlowRadius { get; set; } = 50.0f;
            public float GlowPulseSpeed { get; set; } = 2.0f;
            public float GlowPulseIntensity { get; set; } = 0.5f;
            public Color GlowInnerColor { get; set; } = Color.Purple;
            public Color GlowOuterColor { get; set; } = Color.Blue;
            public bool GlowRotates { get; set; } = true;
            public float GlowRotationSpeed { get; set; } = 1.0f;

            public CosmicGlowEffect() : base("CosmicGlow")
            {
            }

            protected override void ApplySpecificParameters()
            {
                SetParameter("GlowRadius", GlowRadius);
                SetParameter("GlowPulseSpeed", GlowPulseSpeed);
                SetParameter("GlowPulseIntensity", GlowPulseIntensity);
                SetParameter("GlowInnerColor", GlowInnerColor.ToVector4());
                SetParameter("GlowOuterColor", GlowOuterColor.ToVector4());
                SetParameter("GlowRotates", GlowRotates);
                SetParameter("GlowRotationSpeed", GlowRotationSpeed);
            }

            public void SetGlowProperties(float radius, float pulseSpeed, float pulseIntensity, Color innerColor, Color outerColor, bool rotates, float rotationSpeed)
            {
                GlowRadius = radius;
                GlowPulseSpeed = pulseSpeed;
                GlowPulseIntensity = pulseIntensity;
                GlowInnerColor = innerColor;
                GlowOuterColor = outerColor;
                GlowRotates = rotates;
                GlowRotationSpeed = rotationSpeed;
            }
        }

        /// <summary>
        /// Aetheric pulse effect for magical weapon visuals
        /// </summary>
        public class AethericPulseEffect : AnimatedShaderEffect
        {
            public float PulseRadius { get; set; } = 30.0f;
            public float PulseFrequency { get; set; } = 2.0f;
            public Color PulseColor { get; set; } = Color.Cyan;
            public float PulseThickness { get; set; } = 2.0f;
            public int PulseCount { get; set; } = 3;

            public AethericPulseEffect() : base("AethericPulse", 16)
            {
                AnimationSpeed = PulseFrequency;
            }

            protected override void ApplySpecificParameters()
            {
                SetParameter("PulseRadius", PulseRadius);
                SetParameter("PulseFrequency", PulseFrequency);
                SetParameter("PulseColor", PulseColor.ToVector4());
                SetParameter("PulseThickness", PulseThickness);
                SetParameter("PulseCount", PulseCount);
                SetParameter("CurrentFrame", CurrentFrame);
            }

            public void SetPulseProperties(float radius, float frequency, Color color, float thickness, int count)
            {
                PulseRadius = radius;
                PulseFrequency = frequency;
                PulseColor = color;
                PulseThickness = thickness;
                PulseCount = Math.Max(1, count);
                AnimationSpeed = frequency;
            }
        }

        /// <summary>
        /// Galaxy swirl effect for cosmic weapon visuals
        /// </summary>
        public class GalaxySwirlEffect : BaseShaderEffect
        {
            public float SwirlSpeed { get; set; } = 1.0f;
            public float SwirlIntensity { get; set; } = 0.8f;
            public Color SwirlColor1 { get; set; } = Color.Purple;
            public Color SwirlColor2 { get; set; } = Color.Blue;
            public Color SwirlColor3 { get; set; } = Color.Cyan;
            public float SwirlRadius { get; set; } = 40.0f;
            public int SwirlArms { get; set; } = 3;

            public GalaxySwirlEffect() : base("GalaxySwirl")
            {
            }

            protected override void ApplySpecificParameters()
            {
                SetParameter("SwirlSpeed", SwirlSpeed);
                SetParameter("SwirlIntensity", SwirlIntensity);
                SetParameter("SwirlColor1", SwirlColor1.ToVector4());
                SetParameter("SwirlColor2", SwirlColor2.ToVector4());
                SetParameter("SwirlColor3", SwirlColor3.ToVector4());
                SetParameter("SwirlRadius", SwirlRadius);
                SetParameter("SwirlArms", SwirlArms);
            }

            public void SetSwirlProperties(float speed, float intensity, Color color1, Color color2, Color color3, float radius, int arms)
            {
                SwirlSpeed = speed;
                SwirlIntensity = intensity;
                SwirlColor1 = color1;
                SwirlColor2 = color2;
                SwirlColor3 = color3;
                SwirlRadius = radius;
                SwirlArms = Math.Max(1, arms);
            }
        }

        /// <summary>
        /// Eon beam effect for energy weapon visuals
        /// </summary>
        public class EonBeamEffect : BaseShaderEffect
        {
            public float BeamLength { get; set; } = 100.0f;
            public float BeamWidth { get; set; } = 4.0f;
            public float BeamIntensity { get; set; } = 1.0f;
            public Color BeamCoreColor { get; set; } = Color.White;
            public Color BeamEdgeColor { get; set; } = Color.Cyan;
            public float BeamPulseSpeed { get; set; } = 3.0f;
            public bool BeamFlickers { get; set; } = true;
            public float BeamFlickerIntensity { get; set; } = 0.2f;

            public EonBeamEffect() : base("EonBeam")
            {
            }

            protected override void ApplySpecificParameters()
            {
                SetParameter("BeamLength", BeamLength);
                SetParameter("BeamWidth", BeamWidth);
                SetParameter("BeamIntensity", BeamIntensity);
                SetParameter("BeamCoreColor", BeamCoreColor.ToVector4());
                SetParameter("BeamEdgeColor", BeamEdgeColor.ToVector4());
                SetParameter("BeamPulseSpeed", BeamPulseSpeed);
                SetParameter("BeamFlickers", BeamFlickers);
                SetParameter("BeamFlickerIntensity", BeamFlickerIntensity);
            }

            public void SetBeamProperties(float length, float width, float intensity, Color coreColor, Color edgeColor, float pulseSpeed, bool flickers, float flickerIntensity)
            {
                BeamLength = length;
                BeamWidth = width;
                BeamIntensity = intensity;
                BeamCoreColor = coreColor;
                BeamEdgeColor = edgeColor;
                BeamPulseSpeed = pulseSpeed;
                BeamFlickers = flickers;
                BeamFlickerIntensity = MathHelper.Clamp(flickerIntensity, 0, 1);
            }
        }

        /// <summary>
        /// Basic celestial trail effect for weapon trails and projectile paths
        /// </summary>
        public class CelestialTrailEffect : TrailShaderEffect
        {
            public float TrailPulseSpeed { get; set; } = 2.0f;
            public float TrailPulseIntensity { get; set; } = 0.3f;
            public bool TrailGlows { get; set; } = true;
            public Color TrailGlowColor { get; set; } = Color.Cyan;

            public CelestialTrailEffect() : base("CelestialTrail")
            {
            }

            protected override void ApplySpecificParameters()
            {
                base.ApplySpecificParameters();
                SetParameter("TrailPulseSpeed", TrailPulseSpeed);
                SetParameter("TrailPulseIntensity", TrailPulseIntensity);
                SetParameter("TrailGlows", TrailGlows);
                SetParameter("TrailGlowColor", TrailGlowColor.ToVector4());
            }

            public void SetTrailPulseProperties(float pulseSpeed, float pulseIntensity, bool glows, Color glowColor)
            {
                TrailPulseSpeed = pulseSpeed;
                TrailPulseIntensity = pulseIntensity;
                TrailGlows = glows;
                TrailGlowColor = glowColor;
            }
        }

        /// <summary>
        /// Ancient hatchet flame shader effect
        /// </summary>
        public class AncientHatchetFlameShaderEffect : EnhancedCelestialFlameEffect
        {
            public float AxeIntensity { get; set; } = 1.2f;
            public Color AxeFlameColor { get; set; } = Color.Gold;
            public float AxeSpinSpeed { get; set; } = 5.0f;

            public AncientHatchetFlameShaderEffect() : base()
            {
                ShaderName = "AncientHatchetFlame";
                LoadShader();
            }

            protected override void ApplySpecificParameters()
            {
                base.ApplySpecificParameters();
                SetParameter("AxeIntensity", AxeIntensity);
                SetParameter("AxeFlameColor", AxeFlameColor.ToVector4());
                SetParameter("AxeSpinSpeed", AxeSpinSpeed);
            }

            public void SetAxeProperties(float intensity, Color flameColor, float spinSpeed)
            {
                AxeIntensity = intensity;
                AxeFlameColor = flameColor;
                AxeSpinSpeed = spinSpeed;
            }
        }

        /// <summary>
        /// Enhanced celestial fire trail effect for enhanced weapon trails
        /// </summary>
        public class EnhancedCelestialFireTrailEffect : EnhancedCelestialTrailEffect
        {
            public float FireIntensity { get; set; } = 1.5f;
            public Color FireColor { get; set; } = Color.Orange;
            public float FireDecay { get; set; } = 0.8f;
            public bool FireEmbers { get; set; } = true;

            public EnhancedCelestialFireTrailEffect() : base()
            {
                ShaderName = "CelestialFireTrail";
                LoadShader();
            }

            protected override void ApplySpecificParameters()
            {
                base.ApplySpecificParameters();
                SetParameter("FireIntensity", FireIntensity);
                SetParameter("FireColor", FireColor.ToVector4());
                SetParameter("FireDecay", FireDecay);
                SetParameter("FireEmbers", FireEmbers);
            }

            public void SetFireProperties(float intensity, Color fireColor, float decay, bool embers)
            {
                FireIntensity = intensity;
                FireColor = fireColor;
                FireDecay = MathHelper.Clamp(decay, 0, 1);
                FireEmbers = embers;
            }
        }

        #endregion

        #region Integration Methods

        /// <summary>
        /// Example integration of new shader system with existing weapons
        /// This demonstrates how to replace the old shader calls with the new enhanced system
        /// </summary>
        public static class ShaderIntegrationExample
        {
            private static EnhancedCelestialFlameEffect _celestialFlameEffect;
            private static EnhancedCelestialTrailEffect _celestialTrailEffect;
            private static EnhancedCelestialFireTrailEffect _celestialFireTrailEffect;
            private static CosmicGlowEffect _cosmicGlowEffect;
            private static GalaxySwirlEffect _galaxySwirlEffect;
            private static EonBeamEffect _eonBeamEffect;
            
            // Basic effect instances for simple usage
            private static CelestialTrailEffect _basicCelestialTrailEffect;
            private static CelestialFireTrailEffect _basicCelestialFireTrailEffect;

            /// <summary>
            /// Initialize all shader effects (call this in Mod.Load())
            /// </summary>
            public static void InitializeShaders()
            {
                _celestialFlameEffect = new EnhancedCelestialFlameEffect();
                _celestialTrailEffect = new EnhancedCelestialTrailEffect();
                _celestialFireTrailEffect = new EnhancedCelestialFireTrailEffect();
                _cosmicGlowEffect = new CosmicGlowEffect();
                _galaxySwirlEffect = new GalaxySwirlEffect();
                _eonBeamEffect = new EonBeamEffect();
                
                // Initialize basic effects
                _basicCelestialTrailEffect = new CelestialTrailEffect();
                _basicCelestialFireTrailEffect = new CelestialFireTrailEffect();
            }

            /// <summary>
            /// Basic celestial trail drawing for weapons
            /// </summary>
            /// <returns>True if shader was successfully applied</returns>
            public static bool BeginTrail()
            {
                if (_basicCelestialTrailEffect?.IsReady() == true)
                {
                    var shader = _basicCelestialTrailEffect.GetShader();
                    if (shader != null)
                    {
                        _basicCelestialTrailEffect.ApplyParameters();
                        Main.spriteBatch.End();
                        Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, null, shader, Main.GameViewMatrix.TransformationMatrix);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// End celestial trail
            /// </summary>
            public static void EndTrail()
            {
                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, null, null, Main.GameViewMatrix.TransformationMatrix);
            }

            /// <summary>
            /// Basic celestial fire trail drawing for weapons
            /// </summary>
            /// <returns>True if shader was successfully applied</returns>
            public static bool BeginFireTrail()
            {
                if (_basicCelestialFireTrailEffect?.IsReady() == true)
                {
                    var shader = _basicCelestialFireTrailEffect.GetShader();
                    if (shader != null)
                    {
                        _basicCelestialFireTrailEffect.ApplyParameters();
                        Main.spriteBatch.End();
                        Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, null, shader, Main.GameViewMatrix.TransformationMatrix);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// End celestial fire trail
            /// </summary>
            public static void EndFireTrail()
            {
                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, null, null, Main.GameViewMatrix.TransformationMatrix);
            }

            /// <summary>
            /// Update all shader effects (call this in Mod.Update())
            /// </summary>
            public static void UpdateShaders(GameTime gameTime)
            {
                float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

                _celestialFlameEffect?.Update(deltaTime);
                _celestialTrailEffect?.Update(deltaTime);
                _celestialFireTrailEffect?.Update(deltaTime);
                _cosmicGlowEffect?.Update(deltaTime);
                _galaxySwirlEffect?.Update(deltaTime);
                _eonBeamEffect?.Update(deltaTime);
            }

            /// <summary>
            /// Example: Enhanced celestial trail drawing for weapons
            /// </summary>
            /// <returns>True if shader was successfully applied</returns>
            public static bool BeginEnhancedCelestialTrail(Vector2 position, Color color, float intensity = 1.0f)
            {
                if (_celestialTrailEffect?.IsReady() == true)
                {
                    var shader = _celestialTrailEffect.GetShader();
                    if (shader != null)
                    {
                        _celestialTrailEffect.SetProperties(position, 0, 1.0f, color, intensity);
                        _celestialTrailEffect.SetTrailPulseProperties(3.0f, 0.3f, true, Color.Cyan);
                        _celestialTrailEffect.ApplyParameters();

                        // Apply shader to spriteBatch
                        Main.spriteBatch.End();
                        Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, null, shader, Main.GameViewMatrix.TransformationMatrix);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// End enhanced celestial trail
            /// </summary>
            public static void EndEnhancedCelestialTrail()
            {
                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, null, null, Main.GameViewMatrix.TransformationMatrix);
            }

            /// <summary>
            /// Example: Enhanced fire trail for ancient weapons
            /// </summary>
            /// <returns>True if shader was successfully applied</returns>
            public static bool BeginEnhancedFireTrail(Vector2 position, Color color, float intensity = 1.5f)
            {
                if (_celestialFireTrailEffect?.IsReady() == true)
                {
                    var shader = _celestialFireTrailEffect.GetShader();
                    if (shader != null)
                    {
                        _celestialFireTrailEffect.SetProperties(position, 0, 1.0f, color, intensity);
                        _celestialFireTrailEffect.SetFireProperties(intensity, color, 0.8f, true);
                        _celestialFireTrailEffect.ApplyParameters();

                        Main.spriteBatch.End();
                        Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, null, shader, Main.GameViewMatrix.TransformationMatrix);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// End enhanced fire trail
            /// </summary>
            public static void EndEnhancedFireTrail()
            {
                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, null, null, Main.GameViewMatrix.TransformationMatrix);
            }

            /// <summary>
            /// Example: Galaxy swirl effect for cosmic weapons
            /// </summary>
            /// <returns>True if shader was successfully applied</returns>
            public static bool BeginGalaxySwirl(Vector2 position, Color color1, Color color2, Color color3, float intensity = 0.8f)
            {
                if (_galaxySwirlEffect?.IsReady() == true)
                {
                    var shader = _galaxySwirlEffect.GetShader();
                    if (shader != null)
                    {
                        _galaxySwirlEffect.SetProperties(position, 0, 1.0f, color1, intensity);
                        _galaxySwirlEffect.SetSwirlProperties(1.0f, intensity, color1, color2, color3, 40.0f, 3);
                        _galaxySwirlEffect.ApplyParameters();

                        Main.spriteBatch.End();
                        Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, null, shader, Main.GameViewMatrix.TransformationMatrix);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// End galaxy swirl
            /// </summary>
            public static void EndGalaxySwirl()
            {
                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, null, null, Main.GameViewMatrix.TransformationMatrix);
            }

            /// <summary>
            /// Example: Ancient hatchet flame effect
            /// </summary>
            /// <returns>True if shader was successfully applied</returns>
            public static bool BeginAncientHatchetFlame(Vector2 position, Color flameColor, float intensity = 1.2f)
            {
                if (_celestialFlameEffect?.IsReady() == true)
                {
                    var shader = _celestialFlameEffect.GetShader();
                    if (shader != null)
                    {
                        _celestialFlameEffect.SetProperties(position, 0, 1.0f, flameColor, intensity);
                        _celestialFlameEffect.SetFlameProperties(intensity, 0.5f, Color.Gold, Color.Red, 5.0f);
                        _celestialFlameEffect.ApplyParameters();

                        Main.spriteBatch.End();
                        Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, Main.DefaultSamplerState, DepthStencilState.None, null, shader, Main.GameViewMatrix.TransformationMatrix);
                        return true;
                    }
                }
                return false;
            }

            /// <summary>
            /// End ancient hatchet flame
            /// </summary>
            public static void EndAncientHatchetFlame()
            {
                Main.spriteBatch.End();
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, null, null, Main.GameViewMatrix.TransformationMatrix);
            }
        }

        #endregion
    }
}
