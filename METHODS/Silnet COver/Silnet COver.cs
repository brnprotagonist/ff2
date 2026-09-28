using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using AotForms;

namespace Client
{
    internal class SilentCover
    {
        private static Dictionary<uint, Vector3> lastPos = new();
        private static Dictionary<uint, DateTime> lastTime = new();

        public static void Work()
        {
            while (true)
            {
                if (!Config.SilentCover)
                {
                    Thread.SpinWait(10);
                    continue;
                }

                if (Core.LocalPlayer == 0 || !Core.HaveMatrix)
                {
                    Thread.SpinWait(10);
                    continue;
                }

                Entity target = GetBestTarget();

                if (target != null)
                {
                    bool isShooting;
                    if (InternalMemory.Read<bool>(Core.LocalPlayer + Offsets.sAim1, out isShooting) && isShooting)
                    {
                        uint weaponAddr;
                        if (InternalMemory.Read<uint>(Core.LocalPlayer + Offsets.sAim2, out weaponAddr) && weaponAddr != 0)
                        {
                            Vector3 velocity = CalculateVelocity(target);
                            Vector3 predictedHead = target.Head + new Vector3(0, 0.10f, 0) + (velocity * 0.045f);

                            Vector3 startPos;
                            if (InternalMemory.Read<Vector3>(weaponAddr + Offsets.sAim3, out startPos))
                            {
                                Vector3 diff = predictedHead - startPos;
                                if (diff.Length() > 0.001f)
                                {
                                    Vector3 direction = Vector3.Normalize(diff);
                                    InternalMemory.Write<Vector3>(weaponAddr + Offsets.sAim4, direction);
                                }
                            }
                        }
                    }
                }
                Thread.SpinWait(1);
            }
        }

        private static Vector3 CalculateVelocity(Entity target)
        {
            uint tid = target.Address;
            if (lastPos.ContainsKey(tid) && lastTime.ContainsKey(tid))
            {
                float dt = (float)(DateTime.Now - lastTime[tid]).TotalSeconds;
                if (dt > 0.04f)
                {
                    Vector3 vel = (target.Head - lastPos[tid]) / dt;
                    lastPos[tid] = target.Head;
                    lastTime[tid] = DateTime.Now;
                    return vel;
                }
            }
            else
            {
                lastPos[tid] = target.Head;
                lastTime[tid] = DateTime.Now;
            }
            return Vector3.Zero;
        }

        private static Entity GetBestTarget()
        {
            Entity best = null;
            float minDist = float.MaxValue;
            var screenCenter = new Vector2(Core.Width / 2f, Core.Height / 2f);

            foreach (var entity in Core.Entities.Values)
            {
                if (entity == null || entity.IsDead || (Config.IgnoreKnocked && entity.IsKnocked)) continue;

                var head2D = W2S.WorldToScreen(Core.CameraMatrix, entity.Head, Core.Width, Core.Height);
                if (head2D.X < 0 || head2D.Y < 0 || head2D.X > Core.Width || head2D.Y > Core.Height) continue;

                float dist = Vector2.Distance(screenCenter, new Vector2(head2D.X, head2D.Y));
                if (dist < minDist && dist <= Config.Aimfov)
                {
                    minDist = dist;
                    best = entity;
                }
            }
            return best;
        }
    }
}