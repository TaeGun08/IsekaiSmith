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

    // Dashed ring, soft-edged like Circle() - used for InteractionPadIndicator's ground mark
    // (matches the dashed-circle look of the reference image the user provided).
    public static Sprite Ring()
    {
        if (ringSprite != null)
        {
            return ringSprite;
        }

        const int size = 128;
        const int dashCount = 14;
        const float dashFraction = 0.62f; // portion of each segment that's filled; rest is a gap
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float outerRadius = size / 2f - 2f;
        float innerRadius = outerRadius * 0.72f;
        float bandCenter = (outerRadius + innerRadius) / 2f;
        float bandHalfWidth = (outerRadius - innerRadius) / 2f;
        float segmentDegrees = 360f / dashCount;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 offset = new Vector2(x + 0.5f, y + 0.5f) - center;
                float dist = offset.magnitude;
                float bandAlpha = Mathf.Clamp01(1f - Mathf.Abs(dist - bandCenter) / bandHalfWidth);

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
}
