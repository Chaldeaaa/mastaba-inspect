using System;
using System.Collections.Generic;
using BepInEx.Logging;
using GameData;
using Gear;
using Player;
using RootMotion.FinalIK;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace MastabaInspect.Knives;

internal sealed class KnifeController
{
    private readonly ManualLogSource log;
    internal KnifeController(ManualLogSource log)
    {
        this.log = log;
    }

    private readonly InspectSession session = new();
    private FirstPersonItemHolder holder;
    private MeleeWeaponFirstPerson weapon;
    private Transform root, shoulder, forearm, hand, bladeHead, thumbBase, thumbNext;
    private Camera renderCamera;
    private IKSolverLimb rightSolver;
    private float nativePositionWeight, nativeRotationWeight;
    private bool weightsSaved;
    private readonly TransformLease rootPose = new(), shoulderPose = new(), forearmPose = new(), handPose = new();
    private bool poseApplied, errorReported;
    private bool poseLogged;
    private float rollSign, faceSign;
    private Vector3 gripPivotLocal;
    private Vector3 gripSideLocal;
    private float gripHalfWidth;
    private float gripRollDegrees;
    private KnifeProfile knifeProfile;
    private Quaternion BladeRotation => bladeHead.rotation * U(knifeProfile.FrameRotation);
    private Vector3 BladeForward => BladeRotation * Vector3.forward;
    private Vector3 BladeRight => BladeRotation * Vector3.right;

    private readonly List<FingerChain> fingers = new();
    private int lastLoggedPose;
    private readonly InputAction[] interruptActions =
    {
        InputAction.Fire,
        InputAction.Aim,
        InputAction.Melee,
        InputAction.Use,
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
    internal void Update(FirstPersonItemHolder current, bool requested)
    {
        try
        {
            if (holder && holder != current)
                return;
            if (!current || !current.m_owner || !current.m_owner.IsLocallyOwned)
            {
                if (holder && holder == current)
                    Stop();
                return;
            }

            RestorePose();
            if (!requested && !session.Active)
                return;
            if (!CanInspect(current, out var candidate))
            {
                Stop();
                return;
            }

            if (session.Active && candidate != weapon)
            {
                Stop();
                return;
            }

            if (!session.Active)
            {
                if (!TryGetKnife(candidate, out var profile))
                    return;
                if (!Bind(current, candidate, profile))
                    return;
                errorReported = false;
            }

            bool wasActive = session.Active;
            session.Tick(Time.time, requested, true);
            if (!wasActive && session.Active)
                log.LogInfo($"Inspect started: {knifeProfile.Name} (ID {knifeProfile.OfflineID}).");
            if (!session.Active)
                Stop();
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private bool CanInspect(FirstPersonItemHolder current, out MeleeWeaponFirstPerson candidate) => BlockReason(current, out candidate) == null;
    private string BlockReason(FirstPersonItemHolder current, out MeleeWeaponFirstPerson candidate)
    {
        candidate = null;
        if (!current)
            return "first-person holder unavailable";
        if (!current.m_owner)
            return "holder owner unavailable";
        if (!current.m_owner.IsLocallyOwned)
            return "holder is not locally owned";
        if (!current.m_owner.Alive)
            return "player is not alive";
        if (!current.m_owner.IsEnabled)
            return "player IsEnabled=false";
        if (!current.m_isSetup)
            return "holder m_isSetup=false";
        if (GameStateManager.CurrentStateName != eGameStateName.InLevel)
            return "game state=" + GameStateManager.CurrentStateName;
        if (FocusStateManager.CurrentState != eFocusState.FPS)
            return "focus=" + FocusStateManager.CurrentState;
        if (!InputMapper.PlayerHasFocus)
            return "PlayerHasFocus=false";
        if (InputMapper.Current == null)
            return "input mapper unavailable";
        if (InputMapper.Current.FocusSwitchPause)
            return "FocusSwitchPause=true";
        if (current.IsPlayingAnimationSequence)
            return "IsPlayingAnimationSequence=true";
        if (current.ItemIsBusy)
            return "ItemIsBusy=true";
        if (current.ForceItemHidden || current.ItemHiddenTrigger || current.ItemDownTrigger)
            return $"item hidden/down: force={current.ForceItemHidden}, hidden={current.ItemHiddenTrigger}, down={current.ItemDownTrigger}";
        var locomotion = current.m_owner.Locomotion;
        if (!locomotion)
            return "locomotion unavailable";
        var state = locomotion.m_currentStateEnum;
        if (state != PlayerLocomotion.PLOC_State.Stand && state != PlayerLocomotion.PLOC_State.Crouch && state != PlayerLocomotion.PLOC_State.StandStill)
            return "locomotion=" + state;
        if (!current.WieldedItem)
            return "no wielded item";
        candidate = current.WieldedItem.TryCast<MeleeWeaponFirstPerson>();
        if (!candidate)
            return "wielded item is not a first-person melee weapon";
        if (!candidate.IsFirstPerson || !candidate.IsEnabled || !candidate.m_isWielded)
            return $"melee flags: firstPerson={candidate.IsFirstPerson}, enabled={candidate.IsEnabled}, wielded={candidate.m_isWielded}";
        if (candidate.CurrentStateName != eMeleeWeaponState.Idle)
            return "melee state=" + candidate.CurrentStateName;
        foreach (var action in interruptActions)
            if (InputMapper.GetButton.Invoke(action, eFocusState.FPS))
                return "held action=" + action;
        if (Math.Abs(InputMapper.GetAxis.Invoke(InputAction.ScrollItems, eFocusState.FPS)) >= .01f)
            return "equipment scroll input";
        return null;
    }

    private bool TryGetKnife(MeleeWeaponFirstPerson item, out KnifeProfile profile)
    {
        profile = default;
        var range = item.GearIDRange;
        if (range == null)
            return false;
        foreach (var supported in KnifeProfile.Supported)
        {
            if (range.PlayfabItemInstanceId != $"OfflineGear_ID_{supported.OfflineID}")
                continue;
            profile = supported;
            return true;
        }

        var checksum = range.GetChecksum();
        foreach (var supported in KnifeProfile.Supported)
        {
            var block = PlayerOfflineGearDataBlock.GetBlock(supported.OfflineID);
            if (block == null || string.IsNullOrEmpty(block.GearJSON) || new GearIDRange(block.GearJSON).GetChecksum() != checksum)
                continue;
            profile = supported;
            return true;
        }

        return false;
    }

    private bool Bind(FirstPersonItemHolder current, MeleeWeaponFirstPerson item, KnifeProfile profile)
    {
        var parts = item.GearPartHolder;
        if (!parts || !current.m_ItemLayerCamera)
        {
            log.LogWarning("Inspect skipped: knife parts or first-person camera were unavailable.");
            return false;
        }

        var handleMesh = parts.MeleeHandlePart ? parts.MeleeHandlePart.GetComponentInChildren<MeshFilter>() : null;
        if (!handleMesh || !handleMesh.sharedMesh)
        {
            log.LogWarning("Inspect skipped: handle mesh unavailable.");
            return false;
        }

        var blade = handleMesh;
        if (!profile.IntegratedBlade)
            blade = parts.MeleeHeadPart ? parts.MeleeHeadPart.GetComponentInChildren<MeshFilter>() : null;
        if (profile.IntegratedBlade && !handleMesh.sharedMesh.isReadable)
        {
            log.LogWarning("Inspect skipped: integrated knife mesh is not readable.");
            return false;
        }

        var arms = current.FPSArms;
        var solver = arms && arms.m_rightHandIK ? arms.m_rightHandIK.solver : null;
        var upper = solver?.bone1?.transform;
        var lower = solver?.bone2?.transform;
        var wrist = solver?.bone3?.transform;
        if (!blade || solver == null || !solver.initiated || !upper || !lower || !wrist || !lower.IsChildOf(upper) || !wrist.IsChildOf(lower) || upper.IsChildOf(item.transform) || lower.IsChildOf(item.transform) || wrist.IsChildOf(item.transform) || (wrist.position - lower.position).sqrMagnitude < .0001f)
        {
            log.LogWarning("Inspect skipped: right-arm bone hierarchy was unavailable or unsuitable.");
            return false;
        }

        holder = current;
        weapon = item;
        root = item.transform;
        shoulder = upper;
        forearm = lower;
        hand = wrist;
        bladeHead = blade.transform;
        knifeProfile = profile;
        rightSolver = solver;
        renderCamera = current.m_ItemLayerCamera;
        poseLogged = false;
        lastLoggedPose = 0;
        rollSign = InspectMotion.LiftRollSign(N(wrist.position - lower.position), N(BladeForward), N(renderCamera.transform.up));
        faceSign = Vector3.Dot(BladeRight, renderCamera.transform.position - bladeHead.position) >= 0 ? 1 : -1;
        var handBones = hand.GetComponentsInChildren<Transform>();
        FindThumbAxis(handBones);
        fingers.Clear();
        string[] names =
        {
            "thumb",
            "index",
            "middle",
            "ring",
            "pinky"
        };
        for (int i = 0; i < names.Length; i++)
        {
            var finger = FingerChain.Find(hand, handBones, names[i], i);
            if (finger == null && i == 4)
                finger = FingerChain.Find(hand, handBones, "little", i);
            if (finger != null)
                fingers.Add(finger);
        }

        var bounds = handleMesh.sharedMesh.bounds;
        var center = handleMesh.transform.TransformPoint(bounds.center);
        var grip = item.RightHandGripTrans ? item.RightHandGripTrans.position : hand.position;
        float halfWidth = bounds.extents.x;
        if (profile.IntegratedBlade)
        {
            var sourceVertices = handleMesh.sharedMesh.vertices;
            var vertices = new NVector[sourceVertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = N(sourceVertices[i]);
            var section = profile.MeasureGrip(vertices, N(handleMesh.transform.InverseTransformPoint(grip)));
            center = handleMesh.transform.TransformPoint(U(section.Center));
            halfWidth = section.HalfWidth;
        }

        gripSideLocal = root.InverseTransformDirection(handleMesh.transform.TransformDirection(U(profile.Right)));
        gripHalfWidth = handleMesh.transform.TransformVector(U(profile.Right) * halfWidth).magnitude;
        var pivot = center + BladeForward * Vector3.Dot(grip - center, BladeForward);
        gripPivotLocal = root.InverseTransformPoint(pivot);
        gripRollDegrees = float.NaN;
        if (!fingers.Exists(f => f.Kind == 0) || !fingers.Exists(f => f.Kind == 1))
            log.LogWarning("Inspect pinch: thumb/index chain unavailable; blade roll remains active.");
        return true;
    }

    private void FindThumbAxis(IEnumerable<Transform> bones)
    {
        thumbBase = thumbNext = null;
        int shortestDepth = int.MaxValue;
        foreach (var bone in bones)
        {
            if (!bone || bone.childCount == 0 || bone.name.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            int depth = 0;
            var parent = bone;
            while (parent && parent != hand && depth < 16)
            {
                depth++;
                parent = parent.parent;
            }

            if (parent != hand || depth >= shortestDepth)
                continue;
            var child = bone.GetChild(0);
            if ((child.position - bone.position).sqrMagnitude < 1e-8f)
                continue;
            shortestDepth = depth;
            thumbBase = bone;
            thumbNext = child;
        }
    }

    internal void AfterHolderLateUpdate(FirstPersonItemHolder current)
    {
        if (!session.Active || !holder || current != holder)
            return;
        try
        {
            RestorePose();
            if (!root || !shoulder || !forearm || !hand || !bladeHead || !CanInspect(holder, out var candidate) || candidate != weapon)
            {
                Stop();
                return;
            }

            session.Tick(Time.time, false, true);
            if (!session.Active)
            {
                Stop();
                return;
            }

            var camera = renderCamera;
            if (!camera || rightSolver == null || !rightSolver.initiated)
            {
                Stop();
                return;
            }

            if (current.FPSArms && current.FPSArms.m_rightHandIK && current.FPSArms.m_rightHandIK.enabled)
                rightSolver.Update();
            nativePositionWeight = rightSolver.IKPositionWeight;
            nativeRotationWeight = rightSolver.IKRotationWeight;
            weightsSaved = true;
            rightSolver.IKPositionWeight = 0;
            rightSolver.IKRotationWeight = 0;
            poseApplied = true;
            rootPose.Capture(root);
            shoulderPose.Capture(shoulder);
            forearmPose.Capture(forearm);
            handPose.Capture(hand);
            foreach (var finger in fingers)
                finger.Capture();
            var nativeItemPosition = N(root.position);
            var nativeItemRotation = N(root.rotation);
            var nativeHandPosition = N(hand.position);
            var nativeHandRotation = N(hand.rotation);
            var nativeShoulderPosition = N(shoulder.position);
            var sample = InspectMotion.Sample(session.Elapsed);
            var thumbDirection = thumbBase && thumbNext ? thumbNext.position - thumbBase.position : hand.position - forearm.position;
            if (float.IsNaN(gripRollDegrees))
            {
                var upright = InspectMotion.PoseArm(nativeShoulderPosition, N(forearm.position), nativeHandPosition, N(shoulder.rotation), N(forearm.rotation), nativeHandRotation, N(BladeRotation), N(camera.transform.rotation), N(thumbDirection), new MotionSample(0, 1), rollSign, faceSign);
                var endpointPivot = InspectMotion.FollowHand(N(root.TransformPoint(gripPivotLocal)), NQuaternion.Identity, nativeHandPosition, nativeHandRotation, upright.HandPosition, upright.HandRotation).Position;
                var endpointBlade = upright.HandRotation * NQuaternion.Inverse(nativeHandRotation) * N(BladeRotation);
                gripRollDegrees = InspectMotion.FaceRollAngle(NVector.Transform(NVector.UnitZ, endpointBlade), NVector.Transform(NVector.UnitX * faceSign, endpointBlade), N(camera.transform.position) - endpointPivot);
            }

            var result = InspectMotion.PoseArm(nativeShoulderPosition, N(forearm.position), nativeHandPosition, N(shoulder.rotation), N(forearm.rotation), nativeHandRotation, N(BladeRotation), N(camera.transform.rotation), N(thumbDirection), sample, rollSign, faceSign);
            shoulder.SetPositionAndRotation(U(result.ShoulderPosition), U(result.ShoulderRotation));
            forearm.rotation = U(result.ElbowRotation);
            hand.rotation = U(result.HandRotation);
            var itemPose = InspectMotion.FollowHand(nativeItemPosition, nativeItemRotation, nativeHandPosition, nativeHandRotation, N(hand.position), N(hand.rotation));
            root.SetPositionAndRotation(U(itemPose.Position), U(itemPose.Rotation));
            var gripPivot = root.TransformPoint(gripPivotLocal);
            var gripAxis = BladeForward;
            float gripRoll = gripRollDegrees * InspectMotion.GripRollWeight(sample);
            var rolled = InspectMotion.RollInGrip(N(root.position), N(root.rotation), N(gripPivot), N(gripAxis), gripRoll);
            root.SetPositionAndRotation(U(rolled.Position), U(rolled.Rotation));
            float fingerError = 0;
            foreach (var finger in fingers)
            {
                fingerError = Math.Max(fingerError, finger.Apply(N(gripPivot), N(gripAxis), N(root.TransformDirection(gripSideLocal)), gripHalfWidth, gripRollDegrees, sample));
                finger.Seal();
            }

            rootPose.Seal();
            shoulderPose.Seal();
            forearmPose.Seal();
            handPose.Seal();
            if (!poseLogged)
            {
                poseLogged = true;
                log.LogInfo($"Inspect arm pose applied in holder LateUpdate; camera={camera.name}; " + $"bones={shoulder.name}/{forearm.name}/{hand.name}; forearmRollSign={rollSign}; " + $"pinchRoll={gripRollDegrees:F1}; fingers={string.Join(",", fingers.ConvertAll(f => f.Name))}.");
            }

            int keyPose = 0;
            if (sample.UprightA > .9999f)
            {
                keyPose = 3;
            }
            else if (sample.PoseB > .9999f)
            {
                keyPose = 2;
            }

            if (keyPose > 0 && keyPose != lastLoggedPose)
            {
                lastLoggedPose = keyPose;
                var tip = camera.transform.InverseTransformDirection(BladeForward);
                float facing = Vector3.Dot(BladeRight * faceSign, -camera.transform.forward);
                float viewFacing = Vector3.Dot(BladeRight * faceSign, (camera.transform.position - gripPivot).normalized);
                float alignment = thumbBase && thumbNext ? Vector3.Angle(thumbNext.position - thumbBase.position, hand.position - forearm.position) : -1;
                log.LogInfo($"Inspect key pose {keyPose}; A-face facing={facing:F3}; tip={tip}; " + $"thumb={thumbBase?.name ?? "unavailable"}; thumb/forearm angle={alignment:F1}; " + $"shoulderShift={NVector.Distance(nativeShoulderPosition, result.ShoulderPosition):F3}m; " + $"viewFacing={viewFacing:F3}; gripRoll={gripRoll:F1}; fingerTargetError={fingerError * 1000:F1}mm.");
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    internal void RestorePose()
    {
        try
        {
            if (!poseApplied)
                return;
            poseApplied = false;
            try
            {
                RestoreFingers(0);
            }
            finally
            {
                try
                {
                    shoulderPose.Restore();
                }
                finally
                {
                    try
                    {
                        forearmPose.Restore();
                    }
                    finally
                    {
                        try
                        {
                            handPose.Restore();
                        }
                        finally
                        {
                            rootPose.Restore();
                        }
                    }
                }
            }
        }
        finally
        {
            if (weightsSaved)
            {
                weightsSaved = false;
                if (rightSolver != null)
                {
                    if (rightSolver.IKPositionWeight == 0)
                        rightSolver.IKPositionWeight = nativePositionWeight;
                    if (rightSolver.IKRotationWeight == 0)
                        rightSolver.IKRotationWeight = nativeRotationWeight;
                }
            }
        }
    }

    private void RestoreFingers(int index)
    {
        if (index >= fingers.Count)
            return;
        try
        {
            fingers[index].Restore();
        }
        finally
        {
            RestoreFingers(index + 1);
        }
    }

    internal void Stop()
    {
        session.Stop();
        try
        {
            RestorePose();
        }
        catch (Exception ex)
        {
            log?.LogWarning("Inspect pose cleanup: " + ex.Message);
        }
        finally
        {
            fingers.Clear();
            holder = null;
            weapon = null;
            root = shoulder = forearm = hand = bladeHead = thumbBase = thumbNext = null;
            renderCamera = null;
            rightSolver = null;
        }
    }

    private void Fail(Exception exception)
    {
        try
        {
            Stop();
        }
        catch
        {
            session.Stop();
            poseApplied = false;
        }

        if (errorReported)
            return;
        errorReported = true;
        log.LogError("Inspect stopped: " + exception);
    }

    private NVector N(Vector3 v) => new(v.x, v.y, v.z);
    private NQuaternion N(Quaternion q) => new(q.x, q.y, q.z, q.w);
    private Vector3 U(NVector v) => new(v.X, v.Y, v.Z);
    private Quaternion U(NQuaternion q) => new(q.X, q.Y, q.Z, q.W);
}
