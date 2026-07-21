using System;
using System.Collections.Generic;
using Cairo;
using CarryOn.Client.Models;
using OpenTK.Graphics.OpenGL4;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace CarryOn.Client.Logic.CarryRenderer
{
    internal class CarryLabelManager : IDisposable
    {
        private readonly ICoreClientAPI api;
        private readonly Dictionary<string, LabelEntry> textCache = new();
        private readonly List<string> textLru = new();
        private const int MaxTextCache = 64;
        private const int MaxIconCache = 64;
        private const float MinFontSize = 14f;
        private const float MaxFontSize = 40f;
        private const int MinWrapWidth = 64;
        private const int MaxWrapWidth = 2048;
        private const int MinAreaHeight = 32;
        private const int MaxAreaHeight = 2048;
        private const int MaxTextLength = 240;

        private readonly Dictionary<string, IconEntry> iconCache = new();
        private readonly List<string> iconLru = new();

        protected int TextWidth = 200;
        protected int TextHeight = 100;

        internal static IconTextureMode IconTextureMode { get; set; } = IconTextureMode.Standalone;

        private static bool glVersionQueried;
        private static bool glSupportsTextureSubImage;

        internal static bool GlSupportsTextureSubImage
        {
            get
            {
                if (!glVersionQueried)
                {
                    try
                    {
                        int major = GL.GetInteger(GetPName.MajorVersion);
                        int minor = GL.GetInteger(GetPName.MinorVersion);
                        glSupportsTextureSubImage = (major > 4) || (major == 4 && minor >= 3);
                    }
                    catch
                    {
                        glSupportsTextureSubImage = false;
                    }
                    glVersionQueried = true;
                }
                return glSupportsTextureSubImage;
            }
        }

        private static bool ShouldUseStandaloneTexture()
        {
            return IconTextureMode == IconTextureMode.Standalone
                || IconTextureMode == IconTextureMode.StandaloneFallback;
        }

        private static int[]? atlasScratchBuffer;

        private static bool ShouldForceAtlasFallback()
        {
            return IconTextureMode == IconTextureMode.StandaloneFallback;
        }

        private static bool IsDisabled()
        {
            return IconTextureMode == IconTextureMode.Disabled;
        }

        internal static event Action? OnModeChanged;

        private static void RaiseModeChanged() => OnModeChanged?.Invoke();

        private class LabelEntry
        {
            public LoadedTexture Texture = null!;
            public float Width;
            public float Height;
        }

        private class IconEntry
        {
            public MeshRef? Mesh;
            public LoadedTexture? Texture;
            public int TextureId;
            public bool Ready;
            public bool Pending;
        }

        public CarryLabelManager(ICoreClientAPI api)
        {
            this.api = api;

            api.Logger.Debug(
                "[CarryOn] Icon texture mode: {0} (GL: {1}, standalone: {2})",
                IconTextureMode,
                GL.GetString(StringName.Version),
                ShouldUseStandaloneTexture());

            OnModeChanged += InvalidateIconCache;
        }

        private static string MakeKey(string text, int color, float fontSize, int areaWidth, int areaHeight, string? fontName, bool boldFont, string? verticalAlign)
            => fontSize.ToString("0.##") + "|" + areaWidth + "x" + areaHeight + "|" + color.ToString("X8") + "|" + (fontName ?? "") + "|" + (boldFont ? "b" : "n") + "|" + (verticalAlign ?? "") + "|" + text;


        /// <summary>
        /// Generates or retrieves a cached texture for the given label text and styling parameters. The method ensures that the generated texture does not exceed reasonable limits to prevent excessive memory usage. 
        /// If a texture for the specified parameters already exists in the cache, it is returned immediately; otherwise, a new texture is created, stored in the cache, and returned. The method also handles potential exceptions gracefully, returning null if texture generation fails for any reason.
        /// </summary>
        /// <param name="rawText"> The text to be rendered on the label. </param>
        /// <param name="color"> The color of the text in ARGB format. </param>
        /// <param name="fontSize"> The size of the font. </param>
        /// <param name="settings"> Optional settings for label rendering. </param>
        /// <param name="wrapWidth"> Optional width to wrap the text. If not provided, the default TextWidth is used. </param>
        /// <returns> A tuple containing the loaded texture, width, and height of the label, or null if the label could not be generated. </returns>
        public (LoadedTexture texture, float w, float h)? GetLabel(string rawText, int color, float fontSize, LabelRenderSettings? settings = null, int wrapWidth = 0)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return null;

            fontSize = GameMath.Clamp(fontSize, MinFontSize, MaxFontSize);
            // If caller provided a wrapWidth override, clamp; else use TextWidth
            if (wrapWidth <= 0) wrapWidth = TextWidth; else wrapWidth = GameMath.Clamp(wrapWidth, MinWrapWidth, MaxWrapWidth);
            var areaHeight = settings?.MaxHeight ?? TextHeight;
            areaHeight = GameMath.Clamp(areaHeight, MinAreaHeight, MaxAreaHeight);
            var fontName = settings?.FontName;
            var boldFont = settings?.BoldFont ?? false;
            var verticalAlign = settings?.VerticalAlign;

            // Sanitize & clamp length to avoid very large textures
            var text = rawText.TrimEnd();
            if (text.Length > MaxTextLength) text = text.Substring(0, MaxTextLength);

            var key = MakeKey(text, color, fontSize, wrapWidth, areaHeight, fontName, boldFont, verticalAlign);
            if (textCache.TryGetValue(key, out var existing))
            {
                TouchText(key);
                return (existing.Texture, existing.Width, existing.Height);
            }

            try
            {
                // Create a font. If CairoFont is available we can use it; else fallback to default provided by API.
                // We do not rely on cairo-sharp specifics beyond what GenTextTexture needs.
                CairoFont font;
                if (string.IsNullOrWhiteSpace(fontName))
                {
                    font = CairoFont.WhiteSmallText();
                }
                else
                {
                    font = new CairoFont(fontSize, fontName);
                }

                font.UnscaledFontsize = fontSize / Vintagestory.API.Config.RuntimeEnv.GUIScale;

                if (boldFont)
                {
                    font.WithWeight(FontWeight.Bold);
                }

                // Match vanilla sign renderer more closely.
                try { font.LineHeightMultiplier = 0.9; }
                catch (Exception) { api.Logger.Debug("CarryOn: Could not set LineHeightMultiplier on font"); }
                font.Color = new double[]
                {
                    ((color >> 16) & 0xFF) / 255.0,
                    ((color >> 8) & 0xFF) / 255.0,
                    (color & 0xFF) / 255.0,
                    ((color >> 24) & 0xFF) / 255.0
                };

                var background = new TextBackground();
                var textHeight = api.Gui.Text.GetMultilineTextHeight(font, text, wrapWidth);
                var verticalSlack = Math.Max(0, areaHeight - textHeight);

                if (string.Equals(verticalAlign, "Middle", StringComparison.OrdinalIgnoreCase))
                {
                    background.VerPadding = (int)(verticalSlack / 2.0);
                }
                else if (string.Equals(verticalAlign, "Bottom", StringComparison.OrdinalIgnoreCase))
                {
                    background.VerPadding = (int)verticalSlack;
                }

                var tex = api.Gui.TextTexture.GenTextTexture(text, font, wrapWidth, areaHeight, background, EnumTextOrientation.Center);
                if (tex == null || tex.TextureId <= 0) return null; // silent fail

                var entry = new LabelEntry
                {
                    Texture = tex,
                    Width = tex.Width,
                    Height = tex.Height
                };
                textCache[key] = entry;
                textLru.Add(key);
                EnforceTextLimit();

                return (tex, entry.Width, entry.Height);
            }
            catch (Exception ex)
            {
                api.Logger.Debug("CarryOn: Failed to generate label texture for '{0}': {1}", text, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Generates or retrieves a cached mesh and texture for the given item stack to be used as an icon label. 
        /// The method constructs a unique key based on the item stack's properties and the specified styling parameters 
        /// to manage caching effectively. If a cached entry exists for the generated key, it returns the associated mesh and texture ID. 
        /// If not, it initiates the rendering of the item stack to an atlas, creates a new mesh and texture for the icon, and stores them 
        /// in the cache for future retrieval. The method also includes error handling to ensure that any issues during texture generation 
        /// do not cause crashes, returning null values when necessary.
        /// </summary>
        /// <param name="itemStack"> The item stack for which to generate the icon label. </param>
        /// <param name="color"> The color to apply to the icon label. </param>
        /// <param name="settings"> Optional settings for label rendering. </param>
        /// <returns> A tuple containing the mesh, texture ID, and readiness status of the icon label. </returns>
        public (MeshRef? mesh, int textureId, bool ready) GetItemIconLabel(ItemStack itemStack, int color, LabelRenderSettings? settings = null)
        {
            if (itemStack?.Collectible == null || IsDisabled())
            {
                return (null, 0, false);
            }

            var size = GameMath.Clamp(settings?.IconPixelSize ?? 64, 24, 256);
            var scale = settings?.IconScale ?? 1f;
            var code = itemStack.Collectible.Code?.ToString() ?? "unknown";
            // Do not use Attributes.GetHashCode(): deserialized stacks often produce instance-based
            // hash codes, which causes cache misses every frame and repeated atlas inserts.
            var key = $"{code}|{(int)itemStack.Class}|{itemStack.Id}|{color}|{size}|{scale:0.###}";

            if (iconCache.TryGetValue(key, out var existing))
            {
                TouchIcon(key);
                return (existing.Mesh, existing.TextureId, existing.Ready);
            }

            var entry = new IconEntry { Pending = true, Ready = false };
            iconCache[key] = entry;
            iconLru.Add(key);
            EnforceIconLimit();

            var stackCopy = itemStack.Clone();
            api.Render.RenderItemStackToAtlas(
                stackCopy,
                api.BlockTextureAtlas,
                size,
                textureSubId =>
                {
                    if (!iconCache.TryGetValue(key, out var current))
                    {
                        return;
                    }

                    current.Pending = false;

                    if (textureSubId < 0)
                    {
                        current.Ready = false;
                        return;
                    }

                    var texPos = api.BlockTextureAtlas?.Positions?[textureSubId];
                    if (texPos == null)
                    {
                        current.Ready = false;
                        return;
                    }

                    var quad = QuadMeshUtil.GetQuad();
                    quad.Rgba = new byte[16];
                    quad.Rgba.Fill(byte.MaxValue);

                    if (ShouldUseStandaloneTexture())
                    {
                        var texture = CopyAtlasRegionToStandaloneTexture(texPos);
                        if (texture == null)
                        {
                            current.Ready = false;
                            return;
                        }

                        // Vertex order for GetQuad: TL, TR, BR, BL
                        // Horizontal flip keeps icon orientation correct after face rotation.
                        quad.Uv =
                        [
                            1f, 0f,
                            0f, 0f,
                            0f, 1f,
                            1f, 1f
                        ];

                        current.Mesh?.Dispose();
                        current.Texture?.Dispose();
                        current.Mesh = api.Render.UploadMesh(quad);
                        current.Texture = texture;
                        current.TextureId = texture.TextureId;
                    }
                    else
                    {
                        quad.Uv =
                        [
                            texPos.x2, texPos.y1,
                            texPos.x1, texPos.y1,
                            texPos.x1, texPos.y2,
                            texPos.x2, texPos.y2
                        ];

                        current.Mesh?.Dispose();
                        current.Texture?.Dispose();
                        current.Mesh = api.Render.UploadMesh(quad);
                        current.Texture = null;
                        current.TextureId = texPos.atlasTextureId;
                    }

                    current.Ready = true;
                },
                color,
                0f,
                scale
            );

            return (null, 0, false);
        }

        /// <summary>
        /// Creates a new texture by copying a specified region from the texture atlas. 
        /// This method is used to generate standalone textures for item icons based on their position in the atlas. 
        /// It calculates the pixel coordinates of the desired region, retrieves the pixel data using OpenGL, and creates 
        /// a new texture with the extracted data. The method includes error handling to ensure that any issues during the 
        /// texture copying process do not cause crashes, returning null if the operation fails for any reason.
        /// </summary>
        /// <param name="texPos"> The position of the texture region within the atlas. </param>
        /// <returns> A new LoadedTexture instance if successful; otherwise, null. </returns>
        private LoadedTexture? CopyAtlasRegionToStandaloneTexture(TextureAtlasPosition texPos)
        {
            var atlas = api.BlockTextureAtlas;
            if (atlas == null)
            {
                return null;
            }

            var atlasWidth = atlas.Size.Width;
            var atlasHeight = atlas.Size.Height;
            if (atlasWidth <= 0 || atlasHeight <= 0)
            {
                return null;
            }

            int x1 = GameMath.Clamp((int)Math.Round(texPos.x1 * atlasWidth), 0, atlasWidth - 1);
            int y1 = GameMath.Clamp((int)Math.Round(texPos.y1 * atlasHeight), 0, atlasHeight - 1);
            int x2 = GameMath.Clamp((int)Math.Round(texPos.x2 * atlasWidth), x1 + 1, atlasWidth);
            int y2 = GameMath.Clamp((int)Math.Round(texPos.y2 * atlasHeight), y1 + 1, atlasHeight);
            int width = x2 - x1;
            int height = y2 - y1;

            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var pixels = new int[width * height];

            if (GlSupportsTextureSubImage && !ShouldForceAtlasFallback())
            {
                try
                {
                    GL.GetTextureSubImage(
                        texPos.atlasTextureId,
                        0,
                        x1,
                        y1,
                        0,
                        width,
                        height,
                        1,
                        PixelFormat.Bgra,
                        PixelType.UnsignedByte,
                        pixels.Length * sizeof(int),
                        pixels
                    );
                }
                catch (Exception ex)
                {
                    api.Logger.Debug("CarryOn: GL.GetTextureSubImage failed, falling back to GetTexImage: {0}", ex.Message);
                    if (!ReadAtlasRegionViaGetTexImage(texPos.atlasTextureId, atlasWidth, atlasHeight, x1, y1, width, height, pixels))
                        return null;
                }
            }
            else
            {
                if (!ReadAtlasRegionViaGetTexImage(texPos.atlasTextureId, atlasWidth, atlasHeight, x1, y1, width, height, pixels))
                    return null;
            }

            try
            {
                var texture = new LoadedTexture(api, 0, width, height);
                api.Render.LoadOrUpdateTextureFromBgra(pixels, true, (int)TextureWrapMode.ClampToEdge, ref texture);
                return texture.TextureId > 0 ? texture : null;
            }
            catch (Exception ex)
            {
                api.Logger.Debug("CarryOn: Failed to create standalone texture: {0}", ex.Message);
                return null;
            }
        }

        private bool ReadAtlasRegionViaGetTexImage(int atlasTextureId, int atlasWidth, int atlasHeight, int x1, int y1, int width, int height, int[] pixels)
        {
            try
            {
                int needed = atlasWidth * atlasHeight;
                if (atlasScratchBuffer == null || atlasScratchBuffer.Length < needed)
                    atlasScratchBuffer = new int[needed];

                GL.BindTexture(TextureTarget.Texture2D, atlasTextureId);
                GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Bgra, PixelType.UnsignedByte, atlasScratchBuffer);

                for (int row = 0; row < height; row++)
                {
                    Array.Copy(atlasScratchBuffer, (y1 + row) * atlasWidth + x1, pixels, row * width, width);
                }
                return true;
            }
            catch (Exception ex)
            {
                api.Logger.Debug("CarryOn: GL.GetTexImage fallback failed: {0}", ex.Message);
                return false;
            }
        }

        private void TouchText(string key)
        {
            textLru.Remove(key);
            textLru.Add(key);
        }

        private void TouchIcon(string key)
        {
            iconLru.Remove(key);
            iconLru.Add(key);
        }

        /// <summary>
        /// Ensures that the number of cached text textures does not exceed the defined maximum limit. 
        /// If the cache exceeds the limit, the least recently used entries are removed, and their associated textures 
        /// are disposed of to free memory. This method is called whenever a new text entry is added to the cache to 
        /// maintain optimal performance and prevent excessive memory usage.
        /// </summary>
        private void EnforceTextLimit()
        {
            while (textLru.Count > MaxTextCache)
            {
                var oldest = textLru[0];
                textLru.RemoveAt(0);
                if (textCache.TryGetValue(oldest, out var entry))
                {
                    entry.Texture?.Dispose();
                    textCache.Remove(oldest);
                }
            }
        }

        /// <summary>
        /// Ensures that the number of cached icon textures and meshes does not exceed the defined maximum limit. 
        /// If the cache exceeds the limit, the least recently used entries are removed, and their associated resources 
        /// are disposed of to free memory. This method is called whenever a new icon entry is added to the cache to 
        /// maintain optimal performance and prevent excessive memory usage.
        /// </summary>
        private void EnforceIconLimit()
        {
            while (iconLru.Count > MaxIconCache)
            {
                var oldest = iconLru[0];
                iconLru.RemoveAt(0);
                if (iconCache.TryGetValue(oldest, out var entry))
                {
                    entry.Mesh?.Dispose();
                    entry.Texture?.Dispose();
                    iconCache.Remove(oldest);
                }
            }
        }

        internal void InvalidateIconCache()
        {
            foreach (var entry in iconCache.Values)
            {
                entry.Mesh?.Dispose();
                entry.Texture?.Dispose();
            }
            iconCache.Clear();
            iconLru.Clear();
        }

        public void Dispose()
        {
            OnModeChanged -= InvalidateIconCache;

            foreach (var kv in textCache)
            {
                kv.Value.Texture?.Dispose();
            }
            textCache.Clear();
            textLru.Clear();

            foreach (var kv in iconCache)
            {
                kv.Value.Mesh?.Dispose();
                kv.Value.Texture?.Dispose();
            }
            iconCache.Clear();
            iconLru.Clear();
        }
    }
}
