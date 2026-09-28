using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AotForms
{
    internal static class AimbotRageV7
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        internal static void Work()
        {
            Entity lockedTarget = null;

            while (true)
            {
                if (!Config.RegeMainID || Core.LocalPlayer == 0)
                {
                    lockedTarget = null;
                    Thread.Sleep(5);
                    continue;
                }

                if ((GetAsyncKeyState(Config.AimbotKey) & 0x8000) == 0)
                {
                    lockedTarget = null;
                    Thread.Sleep(1);
                    continue;
                }

                if (!Core.HaveMatrix || Core.Width <= 0 || Core.Height <= 0)
                {
                    Thread.Sleep(5);
                    continue;
                }

                var screenCenter = new Vector2(Core.Width / 2f, Core.Height / 2f);
                float maxFov = Math.Max(Config.AimFov, 200f);

                Entity target = PickTarget(ref lockedTarget, screenCenter, maxFov);

                if (target != null)
                {
                    var playerLook = MathUtils.GetRotationToLocation(GetTargetPosition(target), 0.1f, Core.LocalMainCamera);
                    InternalMemory.Write(Core.LocalPlayer + Offsets.AimRotation, playerLook);
                }

                Thread.Sleep(5);
            }
        }

        private static Entity PickTarget(ref Entity lockedTarget, Vector2 screenCenter, float maxFov)
        {
            if (lockedTarget != null)
            {
                if (IsValidTarget(lockedTarget) && IsWithinView(lockedTarget, screenCenter, maxFov))
                    return lockedTarget;

                lockedTarget = null;
            }

            Entity best = null;
            float bestCrosshair = float.MaxValue;

            foreach (var entity in Core.Entities.Values)
            {
                if (!IsValidTarget(entity)) continue;

                var aimPos = GetTargetPosition(entity);

                if (Vector3.Distance(Core.LocalMainCamera, aimPos) > Config.AimBotMaxDistance) continue;

                var screen = W2S.WorldToScreen(Core.CameraMatrix, aimPos, Core.Width, Core.Height);
                if (screen.X < 1 || screen.Y < 1) continue;

                float dx = screen.X - screenCenter.X;
                float dy = screen.Y - screenCenter.Y;
                float crosshairDist = MathF.Sqrt(dx * dx + dy * dy);

                if (crosshairDist > maxFov) continue;
                if (crosshairDist >= bestCrosshair) continue;

                bestCrosshair = crosshairDist;
                best = entity;
            }

            lockedTarget = best;
            return best;
        }

        private static bool IsValidTarget(Entity entity)
        {
            if (entity == null) return false;
            if (!entity.IsKnown) return false;
            if (entity.IsDead) return false;
            if (Config.IgnoreKnocked && entity.IsKnocked) return false;
            return true;
        }

        private static bool IsWithinView(Entity entity, Vector2 screenCenter, float maxFov)
        {
            var aimPos = GetTargetPosition(entity);

            if (Vector3.Distance(Core.LocalMainCamera, aimPos) > Config.AimBotMaxDistance) return false;

            var screen = W2S.WorldToScreen(Core.CameraMatrix, aimPos, Core.Width, Core.Height);
            if (screen.X < 1 || screen.Y < 1) return false;

            float dx = screen.X - screenCenter.X;
            float dy = screen.Y - screenCenter.Y;
            return MathF.Sqrt(dx * dx + dy * dy) <= maxFov;
        }

        private static Vector3 GetTargetPosition(Entity entity)
        {
            if (Config.SniperBodyAim) return entity.Hip;

            return Config.AimTargetPart switch
            {
                "HEAD" => entity.Head,
                "NECK" => entity.Neck,
                "HIP" => entity.Hip,
                _ => entity.Head
            };
        }
    }
}
