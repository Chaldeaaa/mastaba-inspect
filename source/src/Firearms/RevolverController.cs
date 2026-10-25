using System;
using System.Collections.Generic;
using BepInEx.Logging;
using GameData;
using Gear;
using MastabaInspect.Knives;
using Player;
using UnityEngine;
using NVector = System.Numerics.Vector3;

namespace MastabaInspect.Firearms;
internal sealed class RevolverController
{
    private readonly TransformLease rootPose = new();
    private readonly ManualLogSource log;
    private FirstPersonItemHolder holder;
    private BulletWeapon weapon;
    private ArmBinding rightArm;
    private ArmBinding leftArm;
    private Camera itemCamera;
    private FingerChain indexFinger;
    private FingerChain thumb;
    private Transform hammer;
    private Vector3[] boundPoints;
    private NVector[] cameraOffsets;
    private NVector presentationPivot;
    private bool presentationReady;
    private bool poseStarted;
    private bool relaxationLeased;
    private bool previousRelaxation;
    private float prepareUntil;
    private float startedAt;
    private bool active;
    private int updateFrame = -1;
    private int poseFrame = -1;
    internal RevolverController(ManualLogSource log)
    {
        this.log = log;
    }

    internal void BeforeHolderUpdate(FirstPersonItemHolder current, bool requested)
    {
        if (updateFrame == Time.frameCount)
        {
            return;
        }

        updateFrame = Time.frameCount;
        try
        {
            RestorePose();
            if (!Eligible(current, out BulletWeapon candidate))
            {
                Stop();
                return;
            }

            if (active)
            {
                if (candidate != weapon || (poseStarted && Time.time - startedAt >= Motion.DURATION))
                {
                    Stop();
                    return;
                }

                holder.DontRelax();
                return;
            }

            if (!requested)
            {
                return;
            }

            PlayerFPSBody arms = current.FPSArms;
            if (!arms || !current.m_ItemLayerCamera || !candidate.MuzzleAlign || !ArmBinding.CanBind(arms.m_rightHandIK, candidate.transform) || !ArmBinding.CanBind(arms.m_leftHandIK, candidate.transform))
            {
                log.LogWarning("Inspect skipped: the weapon or hand rig is unavailable.");
                return;
            }

            holder = current;
            weapon = candidate;
            rightArm = new ArmBinding(arms.m_rightHandIK);
            leftArm = new ArmBinding(arms.m_leftHandIK);
            indexFinger = FingerChain.Find(rightArm.HandTransform, "index", 1);
            thumb = FingerChain.Find(rightArm.HandTransform, "thumb", 0);
            if (indexFinger == null || thumb == null)
            {
                log.LogWarning("Inspect skipped: the right-hand finger rig is unavailable.");
                Stop();
                return;
            }

            List<Vector3> corners = new();
            foreach (MeshFilter filter in weapon.GetComponentsInChildren<MeshFilter>())
            {
                Renderer renderer = filter.GetComponent<Renderer>();
                if (!filter.sharedMesh || !filter.gameObject.activeInHierarchy || !renderer || !renderer.enabled)
                {
                    continue;
                }

                Bounds bounds = filter.sharedMesh.bounds;
                if (filter.sharedMesh.name.StartsWith("Stock_Revolver_Hammer_1", StringComparison.Ordinal))
                {
                    hammer = filter.transform;
                }

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    corners.Add(weapon.transform.InverseTransformPoint(filter.transform.TransformPoint(point)));
                }
            }

            if (corners.Count == 0 || !hammer)
            {
                log.LogWarning("Inspect skipped: the weapon bounds or hammer are unavailable.");
                Stop();
                return;
            }

            boundPoints = corners.ToArray();
            cameraOffsets = new NVector[boundPoints.Length];
            itemCamera = current.m_ItemLayerCamera;
            previousRelaxation = FirstPersonItemHolder.RelaxPoseEnabled;
            relaxationLeased = true;
            FirstPersonItemHolder.RelaxPoseEnabled = false;
            holder.OnTensedAction();
            holder.DontRelax();
            if (holder.m_currentState == FPItemStateName.Relaxed)
            {
                holder.ChangeState(FPItemStateName.Hip);
                holder.StartTransition(.14f);
            }

            prepareUntil = Time.time + .18f;
            poseStarted = false;
            presentationReady = false;
            active = true;
            log.LogInfo("R66 inspection preparing: right-hand grip, presentation framing enabled.");
        }
        catch (Exception exception)
        {
            log.LogError(exception);
            Stop();
        }
    }

    internal void AfterHolderLateUpdate()
    {
        if (!active || poseFrame == Time.frameCount)
        {
            return;
        }

        poseFrame = Time.frameCount;
        try
        {
            RestorePose();
            if (!itemCamera || !weapon || !hammer || !Eligible(holder, out BulletWeapon candidate) || candidate != weapon || (poseStarted && Time.time - startedAt >= Motion.DURATION))
            {
                Stop();
                return;
            }

            Transform camera = itemCamera.transform;
            Transform root = weapon.transform;
            if (!poseStarted)
            {
                if (Time.time < prepareUntil || holder.m_currentState != FPItemStateName.Hip)
                {
                    return;
                }

                startedAt = Time.time;
                poseStarted = true;
                log.LogInfo($"R66 inspection started; bounds={boundPoints.Length}; duration={Motion.DURATION}; cameraFOV={itemCamera.fieldOfView:F1}.");
            }

            rightArm.Capture();
            leftArm.Capture();
            indexFinger.Capture();
            thumb.Capture();
            rootPose.Capture(root);
            PoseKey sample = Motion.Sample(Time.time - startedAt);
            Quaternion targetMuzzle = camera.rotation * ArmBinding.ToUnity(Motion.Rotation(sample));
            Quaternion delta = Quaternion.Slerp(Quaternion.identity, targetMuzzle * Quaternion.Inverse(weapon.MuzzleAlign.rotation), sample.Weight);
            Vector3 pivot = rightArm.HandPosition;
            if (!presentationReady)
            {
                Vector3 bottomLeft = camera.InverseTransformPoint(itemCamera.ViewportToWorldPoint(new Vector3(.10f, .04f, 1)));
                Vector3 topRight = camera.InverseTransformPoint(itemCamera.ViewportToWorldPoint(new Vector3(.90f, .56f, 1)));
                ViewFrame frame = new(bottomLeft.x, topRight.x, bottomLeft.y, topRight.y, itemCamera.nearClipPlane + .02f);
                for (int index = 0; index < boundPoints.Length; index++)
                {
                    cameraOffsets[index] = ArmBinding.ToNumerics(weapon.MuzzleAlign.InverseTransformDirection(root.TransformPoint(boundPoints[index]) - pivot));
                }

                presentationPivot = Motion.PresentationPivot(cameraOffsets, frame);
                presentationReady = true;
            }

            NVector nativePivot = ArmBinding.ToNumerics(camera.InverseTransformPoint(pivot));
            NVector targetPivot = NVector.Lerp(nativePivot, presentationPivot, sample.Weight);
            Vector3 targetHand = camera.TransformPoint(ArmBinding.ToUnity(targetPivot));
            Vector3 rootOffset = delta * (root.position - pivot);
            Quaternion rotation = delta * root.rotation;
            Vector3 nativeLeft = leftArm.HandPosition;
            float leftDepth = Mathf.Max(.25f, camera.InverseTransformPoint(nativeLeft).z);
            Vector3 restingLeft = itemCamera.ViewportToWorldPoint(new Vector3(.12f, -.35f, leftDepth));
            Quaternion leftDelta = Quaternion.Slerp(Quaternion.identity, camera.rotation * Quaternion.AngleAxis(25, Vector3.right) * Quaternion.Inverse(camera.rotation), sample.Release);
            Vector3 actualHand = rightArm.Apply(targetHand, delta);
            leftArm.Apply(Vector3.Lerp(nativeLeft, restingLeft, sample.Release), leftDelta);
            root.SetPositionAndRotation(actualHand + rootOffset, rotation);
            Transform muzzle = weapon.MuzzleAlign;
            float side = Vector3.Dot(indexFinger.BasePosition - muzzle.position, muzzle.right) >= 0 ? 1 : -1;
            Vector3 fingerDirection = (muzzle.forward - muzzle.up * .24f + muzzle.right * (side * .035f)).normalized;
            indexFinger.Straighten(fingerDirection, sample.Fingers);
            Vector3 thumbContact = hammer.TransformPoint(new Vector3(0, .0262146f, -.0105896f)) + hammer.up * .004f;
            thumb.Contact(thumbContact, sample.Fingers);
            indexFinger.Seal();
            thumb.Seal();
            rootPose.Seal();
        }
        catch (Exception exception)
        {
            log.LogError(exception);
            Stop();
        }
    }

    private static bool Eligible(FirstPersonItemHolder current, out BulletWeapon candidate)
    {
        candidate = null;
        if (!current || !current.m_owner || !current.m_owner.IsLocallyOwned || !current.m_owner.Alive || !current.m_owner.IsEnabled || !current.m_isSetup)
        {
            return false;
        }

        if (GameStateManager.CurrentStateName != eGameStateName.InLevel || FocusStateManager.CurrentState != eFocusState.FPS || !InputMapper.PlayerHasFocus || InputMapper.Current == null || InputMapper.Current.FocusSwitchPause)
        {
            return false;
        }

        if (current.IsPlayingAnimationSequence || current.ItemIsBusy || current.ForceItemHidden || current.ItemHiddenTrigger || current.ItemDownTrigger || current.ItemAimTrigger || !current.WieldedItem)
        {
            return false;
        }

        PlayerLocomotion locomotion = current.m_owner.Locomotion;
        if (!locomotion || (locomotion.m_currentStateEnum != PlayerLocomotion.PLOC_State.Stand && locomotion.m_currentStateEnum != PlayerLocomotion.PLOC_State.Crouch && locomotion.m_currentStateEnum != PlayerLocomotion.PLOC_State.StandStill))
        {
            return false;
        }

        candidate = current.WieldedItem.TryCast<BulletWeapon>();
        if (!candidate || !candidate.IsFirstPerson || !candidate.IsEnabled || !candidate.m_isWielded || candidate.IsReloading || candidate.FireButton || candidate.AimButtonHeld || candidate.GearIDRange == null)
        {
            return false;
        }

        GearIDRange range = candidate.GearIDRange;
        if (range.PlayfabItemInstanceId != "OfflineGear_ID_15")
        {
            PlayerOfflineGearDataBlock block = PlayerOfflineGearDataBlock.GetBlock(15);
            if (block == null || string.IsNullOrEmpty(block.GearJSON) || new GearIDRange(block.GearJSON).GetChecksum() != range.GetChecksum())
            {
                return false;
            }
        }

        foreach (InputAction action in InterruptActions)
        {
            if (InputMapper.GetButton.Invoke(action, eFocusState.FPS))
            {
                return false;
            }
        }

        return Math.Abs(InputMapper.GetAxis.Invoke(InputAction.ScrollItems, eFocusState.FPS)) < .01f;
    }

    internal void RestorePose()
    {
        try
        {
            thumb?.Restore();
        }
        finally
        {
            try
            {
                indexFinger?.Restore();
            }
            finally
            {
                try
                {
                    rightArm?.Restore();
                }
                finally
                {
                    try
                    {
                        leftArm?.Restore();
                    }
                    finally
                    {
                        rootPose.Restore();
                    }
                }
            }
        }
    }

    internal void Stop()
    {
        active = false;
        poseStarted = false;
        presentationReady = false;
        try
        {
            RestorePose();
        }
        finally
        {
            if (relaxationLeased)
            {
                relaxationLeased = false;
                if (!FirstPersonItemHolder.RelaxPoseEnabled)
                {
                    FirstPersonItemHolder.RelaxPoseEnabled = previousRelaxation;
                }

                if (holder)
                {
                    holder.DontRelax();
                }
            }

            weapon = null;
            rightArm = null;
            leftArm = null;
            indexFinger = null;
            thumb = null;
            hammer = null;
            itemCamera = null;
            boundPoints = null;
            cameraOffsets = null;
        }
    }

    private static readonly InputAction[] InterruptActions =
    {
        InputAction.Fire,
        InputAction.Aim,
        InputAction.Reload,
        InputAction.Melee,
        InputAction.Use,
        InputAction.UseOnAlly,
        InputAction.Run,
        InputAction.Jump,
        InputAction.SelectStandard,
        InputAction.SelectSpecial,
        InputAction.SelectTool,
        InputAction.SelectMelee,
        InputAction.SelectResourcePack,
        InputAction.SelectConsumable,
        InputAction.SelectHackingTool,
        InputAction.ToggleGear,
        InputAction.ScrollItemLeft,
        InputAction.ScrollItemRight
    };
}
