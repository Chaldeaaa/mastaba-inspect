using HarmonyLib;
using UnityEngine;

namespace MastabaInspect;

[HarmonyPatch(typeof(FirstPersonItemHolder), nameof(FirstPersonItemHolder.Update))]
internal static class HolderUpdatePatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        InspectDriver driver = InspectDriver.GetLocal(Input.GetKeyDown(KeyCode.G));
        if (driver)
        {
            driver.BeforeHolderUpdate();
        }
    }
}
