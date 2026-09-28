using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImGuiNET;

namespace AotForms
{
    internal static class telepapa
    {
        private static Task upPlayerTask;
        private static CancellationTokenSource cts = new();
        private static bool isRunning = false;

        internal static void Work()
        {
            if (isRunning) return;
            isRunning = true;

            upPlayerTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (!Config.spwan)
                    {
                        await Task.Delay(1, cts.Token);
                        continue;
                    }

                    var closest = Core.Entities.Values
                        .Where(e => e.IsKnown && !e.IsDead && (!Config.IgnoreKnocked || !e.IsKnocked))
                        .OrderBy(e => Vector3.Distance(Core.LocalMainCamera, e.Head))
                        .FirstOrDefault();

                    if (closest != null)
                    {
                        float distance = Vector3.Distance(Core.LocalMainCamera, closest.Head);
                        if (distance <= 200)
                        {
                            if (!InternalMemory.Read(closest.Address + (uint)Bones.Root, out uint enemyRootBonePtr)) continue;
                            if (!InternalMemory.Read(enemyRootBonePtr + 0x8, out uint enemyTransformValue)) continue;
                            if (!InternalMemory.Read(enemyTransformValue + 0x8, out uint enemyTransformObjPtr)) continue;
                            if (!InternalMemory.Read(enemyTransformObjPtr + 0x20, out uint enemyMatrixValue)) continue;

                            var enemyRootPosition = Transform.GetNodePosition(enemyRootBonePtr, out var enemyRootTransform);

                            if (!InternalMemory.Read(Core.LocalPlayer + (uint)Bones.Root, out uint localRootBonePtr)) continue;
                            if (!InternalMemory.Read(localRootBonePtr + 0x8, out uint localTransformValue)) continue;
                            if (!InternalMemory.Read(localTransformValue + 0x8, out uint localTransformObjPtr)) continue;
                            if (!InternalMemory.Read(localTransformObjPtr + 0x20, out uint localMatrixValue)) continue;

                            InternalMemory.Write(localMatrixValue + 0x80, enemyRootTransform);
                        }
                    }

                    await Task.Delay(1, cts.Token);
                }
            }, cts.Token);
        }

        internal static void Stop()
        {
            if (!isRunning) return;

            cts.Cancel();
            cts = new CancellationTokenSource();
            isRunning = false;
        }
    }
}