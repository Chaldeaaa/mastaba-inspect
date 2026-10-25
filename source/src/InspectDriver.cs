using System;
using BepInEx.Logging;
using Il2CppInterop.Runtime.Attributes;
using MastabaInspect.Firearms;
using MastabaInspect.Knives;
using Player;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace MastabaInspect;

public sealed class InspectDriver : MonoBehaviour
{
    private readonly ManualLogSource log = Logger.CreateLogSource("Mastaba Inspect");
    private readonly KnifeController knife;
    private readonly RevolverController revolver;
    private FirstPersonItemHolder holder;
    private int updateFrame = -1;
    private int poseFrame = -1;
    public InspectDriver(IntPtr pointer) : base(pointer)
    {
        knife = new KnifeController(log);
        revolver = new RevolverController(log);
    }

    [HideFromIl2Cpp]
    internal static InspectDriver GetLocal(bool create = false)
    {
        if (!PlayerManager.HasLocalPlayerAgent())
        {
            return null;
        }

        LocalPlayerAgent player = PlayerManager.GetLocalPlayerAgent().TryCast<LocalPlayerAgent>();
        FirstPersonItemHolder holder = player ? player.FPItemHolder : null;
        if (!holder || !player.IsLocallyOwned)
        {
            return null;
        }

        InspectDriver driver = holder.GetComponent<InspectDriver>();
        if (!driver && create)
        {
            driver = holder.gameObject.AddComponent<InspectDriver>();
            driver.holder = holder;
        }

        return driver;
    }

    [HideFromIl2Cpp]
    internal void BeforeHolderUpdate()
    {
        if (updateFrame == Time.frameCount)
        {
            return;
        }

        updateFrame = Time.frameCount;
        try
        {
            knife.RestorePose();
            revolver.RestorePose();
            bool requested = Input.GetKeyDown(KeyCode.G);
            knife.Update(holder, requested);
            revolver.BeforeHolderUpdate(holder, requested);
        }
        catch (Exception exception)
        {
            log.LogError(exception);
            Stop();
        }
    }

    [HideFromIl2Cpp]
    internal void AfterHolderLateUpdate()
    {
        if (poseFrame == Time.frameCount)
        {
            return;
        }

        poseFrame = Time.frameCount;
        try
        {
            knife.AfterHolderLateUpdate(holder);
            revolver.AfterHolderLateUpdate();
        }
        catch (Exception exception)
        {
            log.LogError(exception);
            Stop();
        }
    }

    [HideFromIl2Cpp]
    internal void Stop()
    {
        try
        {
            knife.Stop();
        }
        finally
        {
            revolver.Stop();
        }
    }

    public void OnDisable()
    {
        Stop();
    }

    public void OnDestroy()
    {
        try
        {
            Stop();
        }
        finally
        {
            log.Dispose();
        }
    }
}
