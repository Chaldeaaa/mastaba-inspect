using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace MastabaInspect;
[BepInPlugin(PLUGIN_ID, "Mastaba Inspect", "0.2.4")]
[BepInProcess("GTFO.exe")]
public sealed class Plugin : BasePlugin
{
    private Harmony harmony;
    public override void Load()
    {
        ClassInjector.RegisterTypeInIl2Cpp<InspectDriver>();
        harmony = new Harmony(PLUGIN_ID);
        try
        {
            harmony.PatchAll(typeof(Plugin).Assembly);
        }
        catch
        {
            harmony.UnpatchSelf();
            throw;
        }

        Log.LogInfo("Mastaba Inspect 0.2.4 loaded; press G with Mastaba R66, Mastaba Fixed Blade or WOX Compact.");
    }

    public override bool Unload()
    {
        foreach (InspectDriver driver in Object.FindObjectsOfType<InspectDriver>())
        {
            driver.Stop();
            Object.Destroy(driver);
        }

        harmony?.UnpatchSelf();
        return true;
    }

#region 常量
    private const string PLUGIN_ID = "fengj.mastaba.inspect";
#endregion
}
