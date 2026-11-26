// 键盘输入采集 —— 从 TypeScript 版 input.ts 迁移。
// 相机固定朝 +Z，所以输入即世界方向（不做相机相对解算）。
//
// 用的是 Unity 旧输入系统（Project Settings 里 activeInputHandler = 0），
// 不依赖 Input System 包。

using UnityEngine;

namespace PlayerMotion
{
    public static class MotionInputReader
    {
        public static MotionInput Read(in CoverInfo cover)
        {
            float x = 0f, z = 0f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) z += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) z -= 1f;

            return new MotionInput
            {
                MoveX = x,
                MoveZ = z,
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
    }
}
