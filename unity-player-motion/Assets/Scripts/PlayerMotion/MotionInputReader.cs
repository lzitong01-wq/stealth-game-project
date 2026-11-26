// 键盘输入采集。
//
// 关键：输入是「屏幕方向」，不是世界方向。
//
// 相机在斜后方俯视（yaw -35°、俯角 58°），屏幕上方对应世界约 (0.57, -0.82)；
// 如果直接把「按下 W」当成「朝 +Z 走」，人就会朝镜头方向跑 —— 看起来就是方向反了。
// 所以这里必须把 (x, z) 投影到相机的水平前向 / 右向上。
//
// 注：原 TypeScript 版是 canvas 正投影、相机恒朝 +Z，所以那边「输入即世界方向」成立；
// Unity 版相机是斜的，必须补这层转换。
//
// 用的是 Unity 旧输入系统（Project Settings 里 activeInputHandler = 0），
// 不依赖 Input System 包。

using UnityEngine;

namespace PlayerMotion
{
    public static class MotionInputReader
    {
        /// <param name="cover">当前掩体查询结果</param>
        /// <param name="cameraForward">相机的世界前向（可以带俯角，内部会投影到水平面）</param>
        public static MotionInput Read(in CoverInfo cover, Vector3 cameraForward)
        {
            float x = 0f, z = 0f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) z += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) z -= 1f;

            ToWorldMove(x, z, cameraForward, out float wx, out float wz);

            return new MotionInput
            {
                MoveX = wx,
                MoveZ = wz,
                Crouch = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
                Sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                // GetKeyDown 本身只在一帧为 true，正好就是「刚按下」
                RollPressed = Input.GetKeyDown(KeyCode.Space),
                CoverAvailable = cover.Available,
                CoverSide = cover.Side,
                CoverDistance = cover.Distance,
                CoverNormal = cover.Normal,
            };
        }

        /// <summary>
        /// 把「屏幕上的 WASD」转成世界方向。
        /// screenX = 右为正，screenZ = 上（远离镜头）为正。
        ///
        /// 屏幕右方取 up × forward（与 Unity 的 LookRotation 约定一致）：
        ///   相机朝 +Z 时屏幕右 = +X；相机朝 -Z 时屏幕右 = -X。
        /// 相机前向的水平投影为零时（没有相机）退化为世界方向，即旧行为。
        /// </summary>
        public static void ToWorldMove(float screenX, float screenZ, Vector3 cameraForward,
                                       out float worldX, out float worldZ)
        {
            var fwd = new Vector3(cameraForward.x, 0f, cameraForward.z);
            if (fwd.sqrMagnitude < 1e-6f)
            {
                worldX = screenX;
                worldZ = screenZ;
                return;
            }

            fwd.Normalize();
            // right = up × fwd = (fwd.z, 0, -fwd.x)
            worldX = fwd.z * screenX + fwd.x * screenZ;
            worldZ = -fwd.x * screenX + fwd.z * screenZ;
        }
    }
}
