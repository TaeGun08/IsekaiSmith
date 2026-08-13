using System;
using System.Collections.Generic;

// Finished-goods inventory, kept separate from ResourceBank (raw materials). Keyed on all four
// independent axes a crafted weapon can vary on: WeaponType (Sword today, Axe/Hammer/Dagger
// later - game_design_doc.html §3), OreGrade (material tier), CraftGrade (finish quality), and
// ManaElement (optional enchant). Deliberately built this way from the start rather than
// Sword-only, per user direction: "구조 자체를... 유동적으로 계속해서 무언가를 추가할 수 있게".
// See weapon_diversity_design_v1.html §6. Deliberately not a general item-instance system - just
// enough to match orders against stock and read back the best-owned weapon.
public static class ToolInventory
{
    private readonly struct Key : IEquatable<Key>
    {
        public readonly WeaponType Weapon;
        public readonly OreGrade Ore;
        public readonly CraftGrade Craft;
        public readonly ManaElement Element;

        public Key(WeaponType weapon, OreGrade ore, CraftGrade craft, ManaElement element)
        {
            Weapon = weapon;
            Ore = ore;
            Craft = craft;
            Element = element;
        }

        public bool Equals(Key other) => Weapon == other.Weapon && Ore == other.Ore && Craft == other.Craft && Element == other.Element;
        public override bool Equals(object obj) => obj is Key other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Weapon, Ore, Craft, Element);
    }

    // Read-only view of one owned stack - lets PlayerInventoryUI list everything the player has
    // without needing its own copy of the (WeaponType, OreGrade, CraftGrade, ManaElement) loop.
    public readonly struct Entry
    {
        public readonly WeaponType Weapon;
        public readonly OreGrade Ore;
        public readonly CraftGrade Craft;
        public readonly ManaElement Element;
        public readonly int Count;

        public Entry(WeaponType weapon, OreGrade ore, CraftGrade craft, ManaElement element, int count)
        {
            Weapon = weapon;
            Ore = ore;
            Craft = craft;
            Element = element;
            Count = count;
        }
    }

    private static readonly Dictionary<Key, int> counts = new Dictionary<Key, int>();
    private static readonly WeaponType[] AscendingWeaponTypes = (WeaponType[])Enum.GetValues(typeof(WeaponType));
    private static readonly CraftGrade[] AscendingCraftGrades = (CraftGrade[])Enum.GetValues(typeof(CraftGrade));
    private static readonly OreGrade[] AscendingOreGrades = (OreGrade[])Enum.GetValues(typeof(OreGrade));
    private static readonly ManaElement[] AllElements = (ManaElement[])Enum.GetValues(typeof(ManaElement));

    public static int Get(WeaponType weapon, OreGrade oreGrade, CraftGrade grade, ManaElement element)
    {
        return counts.TryGetValue(new Key(weapon, oreGrade, grade, element), out int value) ? value : 0;
    }

    public static int Total
    {
        get
        {
            int total = 0;
            foreach (int count in counts.Values)
            {
                total += count;
            }

            return total;
        }
    }

    // The single best-owned weapon (highest ore grade, any weapon type/quality/element) - stands
    // in for "the equipped weapon" until a real equip-selection UI exists
    // (weapon_diversity_design_v1.html §1 "다음 단계로 미룸"). Defaults (Sword/Iron/None) if
    // nothing's been crafted yet, so combat plays identically to before this system existed.
    public static bool TryGetBestWeapon(out WeaponType weapon, out OreGrade oreGrade, out ManaElement element)
    {
        for (int i = AscendingOreGrades.Length - 1; i >= 0; i--)
        {
            OreGrade candidateOre = AscendingOreGrades[i];
            foreach (WeaponType candidateWeapon in AscendingWeaponTypes)
            {
                foreach (CraftGrade grade in AscendingCraftGrades)
                {
                    foreach (ManaElement candidateElement in AllElements)
                    {
                        if (Get(candidateWeapon, candidateOre, grade, candidateElement) > 0)
                        {
                            weapon = candidateWeapon;
                            oreGrade = candidateOre;
                            element = candidateElement;
                            return true;
                        }
                    }
                }
            }
        }

        weapon = WeaponType.Sword;
        oreGrade = OreGrade.Iron;
        element = ManaElement.None;
        return false;
    }

    // Every distinct stack currently owned (count > 0), highest ore grade first then highest
    // quality first - the order PlayerInventoryUI displays them in.
    public static IEnumerable<Entry> AllOwned()
    {
        for (int i = AscendingOreGrades.Length - 1; i >= 0; i--)
        {
            OreGrade oreGrade = AscendingOreGrades[i];
            foreach (WeaponType weapon in AscendingWeaponTypes)
            {
                for (int j = AscendingCraftGrades.Length - 1; j >= 0; j--)
                {
                    CraftGrade grade = AscendingCraftGrades[j];
                    foreach (ManaElement element in AllElements)
                    {
                        int count = Get(weapon, oreGrade, grade, element);
                        if (count > 0)
                        {
                            yield return new Entry(weapon, oreGrade, grade, element, count);
                        }
                    }
                }
            }
        }
    }

    public static void Add(WeaponType weapon, OreGrade oreGrade, CraftGrade grade, ManaElement element, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        Key key = new Key(weapon, oreGrade, grade, element);
        counts[key] = Get(weapon, oreGrade, grade, element) + amount;
    }

    // Spends the lowest-graded item that still satisfies minGrade, so higher-grade stock stays in
    // reserve for orders that actually demand it instead of getting burned on an easy order.
    // Weapon type/ore grade/element don't factor into order eligibility yet (orders only ever
    // asked for a minimum quality - see weapon_diversity_design_v1.html §1) so any combo works,
    // cheapest first.
    public static bool TrySpendAtLeast(CraftGrade minGrade, out CraftGrade spentGrade)
    {
        foreach (CraftGrade grade in AscendingCraftGrades)
        {
            if (grade < minGrade)
            {
                continue;
            }

            foreach (WeaponType weapon in AscendingWeaponTypes)
            {
                foreach (OreGrade oreGrade in AscendingOreGrades)
                {
                    foreach (ManaElement element in AllElements)
                    {
                        if (Get(weapon, oreGrade, grade, element) <= 0)
                        {
                            continue;
                        }

                        Key key = new Key(weapon, oreGrade, grade, element);
                        counts[key] = counts[key] - 1;
                        spentGrade = grade;
                        return true;
                    }
                }
            }
        }

        spentGrade = minGrade;
        return false;
    }
}
