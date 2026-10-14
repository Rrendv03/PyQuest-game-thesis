using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The minimap's entire art budget: six shapes, generated into textures once and
/// wrapped as Sprites at runtime. No sprite imports, no atlas, nothing for an
/// artist to maintain and nothing that can go missing from a build.
///
/// They are static and shared because every marker on screen is one Image
/// pointing at the same Sprite with a different colour and size. Built on the
/// first MinimapController Awake; <see cref="Dispose"/> releases them when the
/// last minimap in a scene goes away, so a build that swaps scenes repeatedly
/// cannot accumulate a set of generated textures.
/// </summary>
public static class MinimapSprites
{
    public static Sprite Circle;
    public static Sprite DiscClip;    // high-res circle for the HUD Mask (no contour quantization)
    public static Sprite EdgeBand;    // inverse rim annulus drawn ABOVE the mask (its own AA edge is the visible outline)
    public static Sprite Ring;
    public static Sprite Diamond;
    public static Sprite Star;
    public static Sprite Arrow;
    public static Sprite RoundRect;

    static readonly List<Texture2D> generated = new List<Texture2D>();
    static bool built;

    public static void Build()
    {
        if (built) return;
        built = true;

        Circle = Disc(128);
        // 256 is already sub-pixel at HUD disc size (a 188px disc gets < 1 texel step),
        // and 512+ quadruples texture memory and minification work on mobile GPUs for
        // zero visible gain - the rim feather hides what remains anyway.
        DiscClip = Disc(256);
        // The rim band is a smooth gradient: 256px keeps its two AA transitions
        // sub-pixel at disc size and costs almost nothing to sample every frame.
        EdgeBand = EdgeBandSprite(256);
        Ring = RingOf(128);
        Diamond = Polygon(128, PolygonKind.Diamond);
        Arrow = Polygon(128, PolygonKind.Triangle);
        Star = StarOf(128);
        RoundRect = RoundedRect(128, 0.16f);
    }

    enum PolygonKind { Diamond, Triangle }

    /// <summary>
    /// Anti-aliased filled circle: coverage falls off smoothly across the last
    /// pixel instead of stair-stepping. At the disc's size a hard edge is visibly
    /// jagged on a phone screen.
    /// </summary>
    static Sprite Disc(int size)
    {
        Texture2D texture = NewTexture(size, size);
        float radius = size * 0.5f;

        // SetPixels32 in one call: per-pixel SetPixel is roughly 20x slower, and this
        // runs in Awake where the cost would hitch right on scene load.
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float distance = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                byte a = (byte)(255f * Mathf.Clamp01(0.5f - distance));
                pixels[row + x] = new Color32(255, 255, 255, a);
            }
        }
        texture.SetPixels32(pixels);

        texture.Apply();
        return Wrap(texture, size);
    }

    /// <summary>
    /// Inverse rim band: fully transparent over the map, opaque across the disc's
    /// outer edge, then fading back to nothing just outside it. Drawn as a SIBLING
    /// above the Mask (not a child - children are stencil-clipped too, which is why
    /// the old feather never covered the hard cut), this band paints over the
    /// mask's 1-bit stencil edge, and its own bilinear outer fade becomes the
    /// disc's visible, anti-aliased silhouette.
    ///
    /// Radii are fractions of the texture radius. The sprite is drawn slightly
    /// larger than the clip disc (x1.08), so the mask cut lands at ~0.926R - safely
    /// inside the opaque band (0.90R..0.985R).
    /// </summary>
    static Sprite EdgeBandSprite(int size)
    {
        Texture2D texture = NewTexture(size, size);
        float radius = size * 0.5f;
        const float rampIn = 0.86f;  // transparent -> opaque
        const float solid = 0.90f;   // fully opaque from here...
        const float rampOut = 0.985f; // ...until here, then fade to nothing

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            int row = y * size;
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float t = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                float a = Mathf.Clamp01((t - rampIn) / (solid - rampIn))
                        * Mathf.Clamp01((1f - t) / (1f - rampOut));
                pixels[row + x] = new Color32(255, 255, 255, (byte)(255f * a));
            }
        }
        texture.SetPixels32(pixels);

        texture.Apply();
        return Wrap(texture, size);
    }

    /// <summary>Hollow ring: the boss marker and the minimap frame.</summary>
    static Sprite RingOf(int size)
    {
        Texture2D texture = NewTexture(size, size);
        float radius = size * 0.5f;
        float thickness = size * 0.13f;
        float inner = radius - thickness;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                // Coverage is the overlap of "inside the outer edge" and "outside
                // the hole", each smeared by half a pixel for anti-aliasing.
                float outer = Mathf.Clamp01(radius - d + 0.5f);
                float hole = Mathf.Clamp01(d - inner - 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, outer * hole));
            }
        }

        texture.Apply();
        return Wrap(texture, size);
    }

    /// <summary>
    /// Diamond and triangle as signed distances, so both get the same soft edge:
    /// the diamond is the L1 ball, the triangle narrows linearly towards the top.
    /// </summary>
    static Sprite Polygon(int size, PolygonKind kind)
    {
        Texture2D texture = NewTexture(size, size);
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f - radius) / radius;
                float py = (y + 0.5f - radius) / radius;
                float distance;

                if (kind == PolygonKind.Diamond)
                {
                    distance = (Mathf.Abs(px) + Mathf.Abs(py)) - 1f;
                }
                else
                {
                    // Triangle pointing up: widest near the bottom, apex at the top.
                    float t = (py + 1f) * 0.5f;
                    float halfWidth = 1f - t;
                    distance = Mathf.Max(Mathf.Abs(px) - halfWidth, t - 1f);
                }

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - distance * radius)));
            }
        }

        texture.Apply();
        return Wrap(texture, size);
    }

    /// <summary>Five-pointed star: the "go here next" marker on MainMap.</summary>
    static Sprite StarOf(int size)
    {
        Texture2D texture = NewTexture(size, size);
        float radius = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                float angle = Mathf.Atan2(dy, dx) + Mathf.PI * 0.5f;

                // Two and a half periods over the full turn makes five points.
                float spike = 0.42f + 0.58f * Mathf.Abs(Mathf.Cos(angle * 2.5f));
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((spike - d) * radius)));
            }
        }

        texture.Apply();
        return Wrap(texture, size);
    }

    /// <summary>Soft-cornered square: panel and button backgrounds.</summary>
    static Sprite RoundedRect(int size, float cornerFraction)
    {
        Texture2D texture = NewTexture(size, size);
        float half = size * 0.5f;
        float corner = size * cornerFraction;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - half;
                float py = y + 0.5f - half;

                float ox = Mathf.Max(0f, Mathf.Abs(px) - (half - corner));
                float oy = Mathf.Max(0f, Mathf.Abs(py) - (half - corner));
                float outside = Mathf.Sqrt(ox * ox + oy * oy) - corner;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - outside)));
            }
        }

        texture.Apply();
        return Wrap(texture, size);
    }

    static Texture2D NewTexture(int width, int height)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        generated.Add(texture);
        return texture;
    }

    static Sprite Wrap(Texture2D texture, int size)
    {
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>
    /// Release the generated textures. Called when the last MinimapController in a
    /// scene is destroyed; Build() re-creates them on the next one. Textures are
    /// hide-and-dont-save, so they are never part of a build's assets in any case.
    /// </summary>
    public static void Dispose()
    {
        for (int i = 0; i < generated.Count; i++)
            if (generated[i] != null) Object.Destroy(generated[i]);
        generated.Clear();

        Circle = null;
        Ring = null;
        Diamond = null;
        Star = null;
        Arrow = null;
        RoundRect = null;
        built = false;
    }

    /// <summary>Dispose only when no minimap is left to use these sprites.</summary>
    public static void DisposeIfUnowned(bool noMinimapRemains)
    {
        if (noMinimapRemains) Dispose();
    }
}