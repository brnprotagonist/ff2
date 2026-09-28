using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;

namespace AotForms
{
    internal static class AimbotDrag
    {
        private const int StandingStillMs = 150;
        private const int RestoreTimeoutMs = 300;
        private const int BoneSwapRepeat = 6;

        private static ulong lastPatchAddr;
        private static uint lastPatchOrig;
        private static bool aimPatched;

        private static ulong boneSwappedTarget;
        private static uint savedChest;
        private static uint savedSpine;
        private static bool bonesAreSwapped;

        private static ulong currentAimTarget;
        private static DateTime lastAction = DateTime.MinValue;

        private static readonly Dictionary<ulong, Vector3> lastHeadPos = new();
        private static bool wasStandingStill;
        private static DateTime standingStillStartTime = DateTime.MinValue;

        private static bool lmbWas;
        private static bool dragDone;
        private static Point dragStart;
        private static int dragFrames;

        internal static void Work()
        {
            while (true)
            {
                if (!Config.AimbotDrag)
                {
                    RestoreAimbot();
                    Thread.Sleep(2);
                    continue;
                }

                if (Core.Width == -1 || Core.Height == -1 || !Core.HaveMatrix || Core.LocalPlayer == 0)
                {
                    RestoreAimbot();
                    Thread.Sleep(1);
                    continue;
                }

                TickAimbot();
                Thread.Sleep(dragDone ? 2 : 1);
            }
        }

        internal static void OnEnabled()
        {
            Config.AimbotVisible = false;
            Config.AimbotRubix = false;
            Config.AimbotAiX = false;
            AimbotAi.Stop();
            AimbotAiX.Stop();
            CheatWorkerHost.EnsureAimWorkers();
        }

        private static void TickAimbot()
        {
            bool lmb = (WinAPI.GetAsyncKeyState(Keys.LButton) & 0x8000) != 0;
            Point mp = Cursor.Position;

            if (lmb)
            {
                if (!lmbWas)
                {
                    lmbWas = true;
                    dragStart = mp;
                    dragDone = false;
                    dragFrames = 0;
                }
                else if (!dragDone)
                {
                    dragFrames++;
                    if (dragFrames >= 1)
                    {
                        int dx = mp.X - dragStart.X;
                        int dy = mp.Y - dragStart.Y;
                        int d = (int)Math.Sqrt(dx * dx + dy * dy);
                        int dragAmount = Math.Clamp(Config.AimbotDragAmount, 4, 80);
                        if (d >= dragAmount)
                            dragDone = true;
                    }
                }
            }
            else
            {
                lmbWas = false;
                dragDone = false;
                dragFrames = 0;
                RestoreAimbot();
                return;
            }

            if (!dragDone)
            {
                RestoreAimbot();
                return;
            }

            bool shouldAim = lmb && dragDone;
            if (!shouldAim)
            {
                RestoreAimbot();
                return;
            }

            ulong bestTarget = 0;
            float bestDq = float.MaxValue;
            var center = new Vector2(Core.Width * 0.5f, Core.Height * 0.5f);
            float fov = ResolveAimFov();
            float fovSq = fov * fov;

            if (currentAimTarget != 0)
            {
                if (TryGetHeadScreen(currentAimTarget, out _))
                {
                    bestTarget = currentAimTarget;
                }
                else
                {
                    currentAimTarget = 0;
                }
            }

            if (bestTarget == 0)
            {
                foreach (var entity in Core.Entities.Values)
                {
                    ulong entityAddr = PullHelper.EntityAddr(entity);
                    if (!IsValidAimTarget(entityAddr))
                    {
                        continue;
                    }

                    if (!TryGetHeadScreen(entityAddr, out Vector2 headScreen))
                    {
                        continue;
                    }

                    float dx = headScreen.X - center.X;
                    float dy = headScreen.Y - center.Y;
                    float dq = dx * dx + dy * dy;
                    if (dq <= fovSq && dq < bestDq)
                    {
                        bestDq = dq;
                        bestTarget = entityAddr;
                    }
                }
            }

            if (bestTarget != 0 && shouldAim)
            {
                if (bestTarget != currentAimTarget)
                {
                    RestoreAimbot();
                    currentAimTarget = bestTarget;

                    if (!InternalMemory.Read<uint>(currentAimTarget + (uint)Bones.Chest, out savedChest) ||
                        !InternalMemory.Read<uint>(currentAimTarget + (uint)Bones.Spine, out savedSpine) ||
                        savedChest == 0 || savedSpine == 0)
                    {
                        currentAimTarget = 0;
                        return;
                    }

                    boneSwappedTarget = currentAimTarget;
                    wasStandingStill = false;
                }

                bool isFemale = false;
                if (InternalMemory.Read<uint>(bestTarget + Offsets.AvatarManager, out uint avatarManager) && avatarManager != 0)
                {
                    InternalMemory.Read<bool>(avatarManager + Offsets.Avatar_IsFemale, out isFemale);
                }

                bool isStandingStill = false;
                if (Core.Entities.TryGetValue((long)bestTarget, out Entity ent))
                {
                    Vector3 hp = ent.Head;
                    if (lastHeadPos.TryGetValue(bestTarget, out Vector3 prev))
                    {
                        if (Vector3.Distance(hp, prev) < 0.1f)
                        {
                            isStandingStill = true;
                        }
                    }

                    lastHeadPos[bestTarget] = hp;
                }

                var now = DateTime.UtcNow;
                lastAction = now;

                if (isStandingStill && !wasStandingStill)
                {
                    standingStillStartTime = now;
                }

                wasStandingStill = isStandingStill;

                if (isFemale && isStandingStill)
                {
                    double msStanding = (now - standingStillStartTime).TotalMilliseconds;
                    UnswapBones();
                    if (msStanding < StandingStillMs)
                        BurstColliderWrites(bestTarget, hitboxPatch: true);
                    else
                    {
                        DisableCollider();
                        BurstColliderWrites(bestTarget, hitboxPatch: false);
                    }
                }
                else
                {
                    ForceBoneSwap();
                    BurstColliderWrites(bestTarget, hitboxPatch: true);
                }
            }
            else if (currentAimTarget != 0)
            {
                double ms = (DateTime.UtcNow - lastAction).TotalMilliseconds;
                if (ms > RestoreTimeoutMs || bestTarget == 0)
                {
                    RestoreAimbot();
                }
            }
        }

        private static float ResolveAimFov()
        {
            if (Config.AimBotFov > 0 && Config.AimBotFov < 5000)
            {
                return Config.AimBotFov;
            }

            if (Config.AimFov > 0f && Config.AimFov < 5000f)
            {
                return Config.AimFov;
            }

            return 120f;
        }

        private static bool IsValidAimTarget(ulong entityAddr)
        {
            if (entityAddr == 0 || entityAddr == Core.LocalPlayer)
            {
                return false;
            }

            if (!Core.Entities.TryGetValue((long)entityAddr, out Entity entity))
            {
                return false;
            }

            if (!entity.IsKnown || entity.IsDead || entity.IsTeam == Bool3.True)
            {
                return false;
            }

            if (Config.IgnoreKnocked && entity.IsKnocked)
            {
                return false;
            }

            float playerDistance = Vector3.Distance(Core.LocalMainCamera, AimBoneHelper.GetHead(entity));
            if (playerDistance > Config.AimBotMaxDistance)
            {
                return false;
            }

            return true;
        }

        private static bool TryGetHeadScreen(ulong entityAddr, out Vector2 headScreen)
        {
            headScreen = Vector2.Zero;
            if (!Core.Entities.TryGetValue((long)entityAddr, out Entity entity))
            {
                return false;
            }

            headScreen = W2S.WorldToScreen(Core.CameraMatrix, AimBoneHelper.GetHead(entity), Core.Width, Core.Height);
            return headScreen.X > 0f && headScreen.Y > 0f &&
                   headScreen.X < Core.Width && headScreen.Y < Core.Height;
        }

        private static int ResolveWriteRepeat()
        {
            int strength = Math.Clamp(Config.AimbotDragStrength, 20, 300);
            if (bonesAreSwapped && boneSwappedTarget != 0 && boneSwappedTarget == currentAimTarget)
                return Math.Clamp(strength / 4, 20, 80);

            return strength;
        }

        private static bool PrepareHitboxPatch(ulong target)
        {
            ulong patchAddr = target + Offsets.HitboxPatchAddr;

            if (aimPatched && lastPatchAddr != patchAddr)
            {
                InternalMemory.Write(lastPatchAddr, lastPatchOrig);
                aimPatched = false;
            }

            if (!aimPatched && InternalMemory.Read<uint>(patchAddr, out uint orig))
            {
                lastPatchAddr = patchAddr;
                lastPatchOrig = orig;
                aimPatched = true;
            }

            return aimPatched;
        }

        private static void BurstColliderWrites(ulong target, bool hitboxPatch)
        {
            if (!TryReadHeadCollider(target, out uint headCollider) || headCollider == 0)
            {
                return;
            }

            int repeats = ResolveWriteRepeat();
            ulong lockAddr = target + Offsets.LockedAimingCollider;
            bool canPatchHitbox = hitboxPatch && PrepareHitboxPatch(target);
            ulong patchAddr = canPatchHitbox ? lastPatchAddr : 0;

            for (int i = 0; i < repeats; i++)
            {
                InternalMemory.Write(lockAddr, headCollider);
                if (canPatchHitbox)
                    InternalMemory.Write(patchAddr, headCollider);
            }
        }

        private static void ForceBoneSwap()
        {
            if (boneSwappedTarget == 0 || savedChest == 0 || savedSpine == 0)
            {
                return;
            }

            for (int i = 0; i < BoneSwapRepeat; i++)
            {
                InternalMemory.Write(boneSwappedTarget + (uint)Bones.Chest, savedSpine);
                InternalMemory.Write(boneSwappedTarget + (uint)Bones.Spine, savedChest);
            }

            bonesAreSwapped = true;
        }

        private static bool TryReadHeadCollider(ulong target, out uint collider)
        {
            if (InternalMemory.Read<uint>(target + Offsets.Collider, out collider) && collider != 0)
            {
                return true;
            }

            return InternalMemory.Read<uint>(target + Offsets.ColliderFallback, out collider) && collider != 0;
        }

        private static void DisableCollider()
        {
            if (!aimPatched || lastPatchAddr == 0)
            {
                return;
            }

            InternalMemory.Write(lastPatchAddr, lastPatchOrig);
            aimPatched = false;
            lastPatchAddr = 0;
        }

        private static void UnswapBones()
        {
            if (!bonesAreSwapped || boneSwappedTarget == 0)
            {
                return;
            }

            InternalMemory.Write(boneSwappedTarget + (uint)Bones.Chest, savedChest);
            InternalMemory.Write(boneSwappedTarget + (uint)Bones.Spine, savedSpine);
            bonesAreSwapped = false;
        }

        private static void RestoreAimbot()
        {
            UnswapBones();
            boneSwappedTarget = 0;
            DisableCollider();
            currentAimTarget = 0;
            wasStandingStill = false;
        }

        internal static void Render()
        {
            if (!Config.AimbotDrag || !Config.FOVEnabled || Core.Width == -1 || Core.Height == -1)
            {
                return;
            }

            var screenCenter = new Vector2(Core.Width / 2f, Core.Height / 2f) + Core.EmulatorScreenOffset;
            float radius = ResolveAimFov();

            ImGui.GetBackgroundDrawList().AddCircle(
                screenCenter,
                radius,
                ImGui.GetColorU32(new Vector4(1f, 0.55f, 0.1f, 0.85f)),
                64,
                2f);
        }

        internal static void Stop()
        {
            RestoreAimbot();
            lastHeadPos.Clear();
        }
    }
}
