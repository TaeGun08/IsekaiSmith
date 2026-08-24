using UnityEngine;

// Shared runtime-generated UI shapes so multiple UI components don't each hand-roll their own
// texture generation (e.g. FloatingJoystick and the crafting minigame both need a soft circle).
public static class UIShapes
{
    private static Sprite circleSprite;

    public static Sprite Circle()
    {
        if (circleSprite != null)
        {
            return circleSprite;
        }

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = Mathf.Clamp01(radius - dist);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();

        circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return circleSprite;
    }

    private static Sprite ringSprite;

    // Dashed ring, crisp-edged - used for InteractionPadIndicator's ground mark (matches the
    // dashed-circle look of the reference image the user provided). Bumped to 256px + a narrow
    // ~1.5px anti-alias band (previous version faded alpha across the *entire* band width, which
    // read as a hazy blur rather than a ring - user report: "바닥의 이미지가 너무 흐려서 눈이 아파").
    public static Sprite Ring()
    {
        if (ringSprite != null)
        {
            return ringSprite;
        }

        const int size = 256;
        const int dashCount = 14;
        const float dashFraction = 0.62f; // portion of each segment that's filled; rest is a gap
        const float edgeSoftness = 1.5f; // px - only the true inner/outer edges are anti-aliased
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float outerRadius = size / 2f - 4f;
        float innerRadius = outerRadius * 0.78f;
        float segmentDegrees = 360f / dashCount;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 offset = new Vector2(x + 0.5f, y + 0.5f) - center;
                float dist = offset.magnitude;

                // Solid fill through the middle of the band, fading only within edgeSoftness px
                // of the inner/outer boundary - a crisp ring, not a soft blob.
                float distFromOuterEdge = outerRadius - dist;
                float distFromInnerEdge = dist - innerRadius;
                float bandAlpha = Mathf.Clamp01(Mathf.Min(distFromOuterEdge, distFromInnerEdge) / edgeSoftness);

                float angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
                if (angle < 0f)
                {
                    angle += 360f;
                }
                float withinSegment = angle % segmentDegrees;
                float dashAlpha = withinSegment < segmentDegrees * dashFraction ? 1f : 0f;

                texture.SetPixel(x, y, new Color(1f, 1f, 1f, bandAlpha * dashAlpha));
            }
        }
        texture.Apply();

        ringSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return ringSprite;
    }

    private static Sprite swordSprite;

    // Simple sword silhouette (blade + crossguard + hilt) - used next to a customer's requested
    // count so the bubble reads as "wants a weapon", not just a bare number (사용자 요청 2026-08-24).
    public static Sprite Sword()
    {
        if (swordSprite != null)
        {
            return swordSprite;
        }

        const int width = 64;
        const int height = 128;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);

        float centerX = width / 2f;
        float bladeHalfWidth = width * 0.09f;
        float bladeTop = height * 0.94f;
        float bladeBottom = height * 0.34f;
        float guardY = bladeBottom;
        float guardHalfWidth = width * 0.28f;
        float guardHeight = height * 0.045f;
        float hiltHalfWidth = width * 0.07f;
        float hiltBottom = height * 0.06f;
        float pommelRadius = width * 0.11f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float dx = Mathf.Abs(px - centerX);
                bool inBlade = py >= bladeBottom && py <= bladeTop && dx <= bladeHalfWidth;
                bool inGuard = py >= guardY && py <= guardY + guardHeight && dx <= guardHalfWidth;
                bool inHilt = py >= hiltBottom && py < guardY && dx <= hiltHalfWidth;
                bool inPommel = Vector2.Distance(new Vector2(px, py), new Vector2(centerX, hiltBottom)) <= pommelRadius;

                float alpha = (inBlade || inGuard || inHilt || inPommel) ? 1f : 0f;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();

        swordSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
        return swordSprite;
    }
}
