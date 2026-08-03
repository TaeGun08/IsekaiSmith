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
}
