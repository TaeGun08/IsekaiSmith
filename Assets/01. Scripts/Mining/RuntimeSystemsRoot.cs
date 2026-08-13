using UnityEngine;

// Shared parent every self-bootstrapping runtime singleton (GuidedTutorial, CraftingMinigameUI,
// PlayerCombat, HitEffects, ScreenFade, etc.) parents itself under, instead of each sitting as its
// own loose top-level GameObject - user report: "너무 하이어라키에 길어지게 있다보니 복잡해져".
// Purely organizational, no behavior. Callers must call Object.DontDestroyOnLoad(go) on their own
// GameObject *before* parenting under this (Unity only allows DontDestroyOnLoad on root objects) -
// once parented under this already-DontDestroyOnLoad root, the child persists automatically.
public static class RuntimeSystemsRoot
{
    private static Transform root;

    public static Transform Instance
    {
        get
        {
            if (root == null)
            {
                var go = new GameObject("~Runtime Systems");
                Object.DontDestroyOnLoad(go);
                root = go.transform;
            }

            return root;
        }
    }
}
