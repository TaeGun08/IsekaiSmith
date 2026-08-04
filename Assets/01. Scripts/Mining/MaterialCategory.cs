using System.Collections.Generic;

public enum MaterialCategory
{
    Ore,
    Wood,
    ManaStone
}

// Pure category lookup - no MonoBehaviour/UI dependency. The crafting silhouette UI asks this
// "which resource types belong to category X" instead of hardcoding a fixed chip list, so adding
// a new ore tier/wood type/mana element later is a one-line addition here, not a UI rewrite.
public static class MaterialCategoryUtility
{
    public static MaterialCategory? CategoryFor(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Ore:
                return MaterialCategory.Ore;
            case ResourceType.Wood:
                return MaterialCategory.Wood;
            case ResourceType.ManaStone:
                return MaterialCategory.ManaStone;
            default:
                return null;
        }
    }

    public static IEnumerable<ResourceType> AllInCategory(MaterialCategory category)
    {
        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            if (CategoryFor(type) == category)
            {
                yield return type;
            }
        }
    }

    public static string DisplayName(ResourceType type)
    {
        return type == ResourceType.ManaStone ? "Mana Stone" : type.ToString();
    }

    public static string DisplayName(MaterialCategory category)
    {
        return category == MaterialCategory.ManaStone ? "Mana Stone" : category.ToString();
    }
}
