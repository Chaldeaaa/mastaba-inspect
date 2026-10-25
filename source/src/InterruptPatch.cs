using System.Collections.Generic;
using System.Reflection;
using Gear;
using HarmonyLib;
using Player;

namespace MastabaInspect;

[HarmonyPatch]
internal static class InterruptPatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MeleeWeaponFirstPerson), nameof(MeleeWeaponFirstPerson.DoTriggerAttack));
        yield return AccessTools.Method(typeof(MeleeWeaponFirstPerson), nameof(MeleeWeaponFirstPerson.OnUnWield));
        yield return AccessTools.Method(typeof(FirstPersonItemHolder), nameof(FirstPersonItemHolder.OnDestroy));
        yield return AccessTools.Method(typeof(LocalPlayerAgent), nameof(LocalPlayerAgent.LevelCleanup));
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        InspectDriver driver = InspectDriver.GetLocal();
        if (driver)
        {
            driver.Stop();
        }
    }
}
