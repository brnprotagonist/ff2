using AotForms;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Client
{
    internal class SILENT
    {
        // Constants for safety and performance
        private const int MAX_TARGET_DISTANCE = 200; // Maximum distance to consider targets
        private const float AIM_SMOOTH_FACTOR = 0.15f; // Smoothing factor for aim adjustments
        private const int MAX_ENTITIES_PER_FRAME = 64; // Limit entities processed per frame
        private const int SPIN_WAIT_ITERATIONS = 10; // Reduced spin wait iterations

        internal static void Work()
        {
            while (true)
            {
                // Early exit if silent aim is disabled
                if (!Config.SilentAim)
                {
                    Thread.SpinWait(SPIN_WAIT_ITERATIONS);
                    continue;
                }

                // Validate game state before proceeding
                if (!ValidateGameState())
                {
                    Thread.SpinWait(SPIN_WAIT_ITERATIONS);
                    continue;
                }

                // Find the best target
                Entity target = FindBestTarget();

                if (target != null)
                {
                    // Apply silent aim adjustments
                    ApplySilentAim(target);
                }

                // Control thread usage
                Thread.SpinWait(SPIN_WAIT_ITERATIONS);
            }
        }

        private static bool ValidateGameState()
        {
            // Check if game dimensions are valid
            if (Core.Width <= 0 || Core.Height <= 0)
                return false;

            // Check if we have a valid camera matrix
            if (!Core.HaveMatrix)
                return false;

            // Additional validation checks can be added here
            return true;
        }

        private static Entity FindBestTarget()
        {
            Entity bestTarget = null;
            float bestDistance = float.MaxValue;
            var screenCenter = new Vector2(Core.Width * 0.5f, Core.Height * 0.5f);
            int entityCount = 0;

            foreach (var entity in Core.Entities.Values)
            {
                // Limit entities processed per frame for performance
                if (++entityCount > MAX_ENTITIES_PER_FRAME)
                    break;

                // Skip invalid entities
                if (!IsValidTarget(entity))
                    continue;

                // Get head position in screen space
                if (!W2S.WorldToScreen(Core.CameraMatrix, entity.Head, Core.Width, Core.Height, out var headScreenPos))
                    continue;

                // Calculate distance from screen center
                var crosshairDistance = Vector2.Distance(screenCenter, headScreenPos);

                // Update best target if this one is closer
                if (crosshairDistance < bestDistance)
                {
                    bestDistance = crosshairDistance;
                    bestTarget = entity;
                }
            }

            return bestTarget;
        }

        private static bool IsValidTarget(Entity entity)
        {
            // Basic validation checks
            if (entity == null || !entity.IsKnown)
                return false;

            if (entity.IsDead)
                return false;

            if (Config.IgnoreKnocked && entity.IsKnocked)
                return false;

            // Distance check
            var distance = Vector3.Distance(Core.LocalMainCamera, entity.Root);
            if (distance > MAX_TARGET_DISTANCE)
                return false;

            return true;
        }

        private static void ApplySilentAim(Entity target)
        {
            // Check if player is shooting
            if (!InternalMemory.Read<bool>(Core.LocalPlayer + Offsets.sAim1, out var isShooting) || !isShooting)
                return;

            // Get weapon data
            if (!InternalMemory.Read<uint>(Core.LocalPlayer + Offsets.sAim2, out var weaponData) || weaponData == 0)
                return;

            // Get weapon start position
            if (!InternalMemory.Read<Vector3>(weaponData + Offsets.sAim3, out var startPos))
                return;

            // Calculate aim position with slight offset for head
            var adjustedHeadPos = target.Head + new Vector3(0, 0.1f, 0);
            var aimDirection = Vector3.Normalize(adjustedHeadPos - startPos);

            // Apply smoothing to make it less detectable
            if (InternalMemory.Read<Vector3>(weaponData + Offsets.sAim4, out var currentAim))
            {
                var smoothedAim = Vector3.Lerp(currentAim, aimDirection, AIM_SMOOTH_FACTOR);
                InternalMemory.Write<Vector3>(weaponData + Offsets.sAim4, smoothedAim);
            }
            else
            {
                InternalMemory.Write<Vector3>(weaponData + Offsets.sAim4, aimDirection);
            }
        }
    }

    // Helper methods for W2S to make it AOT compatible
    public static class W2S
    {
        public static bool WorldToScreen(Matrix4x4 matrix, Vector3 worldPos, int screenWidth, int screenHeight, out Vector2 screenPos)
        {
            screenPos = Vector2.Zero;

            // Transform world position to clip space
            var clipSpace = Vector4.Transform(new Vector4(worldPos, 1.0f), matrix);

            // Check if point is behind camera
            if (clipSpace.W <= 0.0f)
                return false;

            // Convert to normalized device coordinates
            var ndc = new Vector3(clipSpace.X / clipSpace.W, clipSpace.Y / clipSpace.W, clipSpace.Z / clipSpace.W);

            // Convert to screen coordinates
            screenPos.X = (ndc.X * 0.5f + 0.5f) * screenWidth;
            screenPos.Y = (1.0f - (ndc.Y * 0.5f + 0.5f)) * screenHeight;

            // Check if point is on screen
            return screenPos.X >= 0 && screenPos.X <= screenWidth &&
                   screenPos.Y >= 0 && screenPos.Y <= screenHeight;
        }
    }
}