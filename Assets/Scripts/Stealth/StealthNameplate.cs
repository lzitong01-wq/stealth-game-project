using UnityEngine;

namespace StealthPrototype
{
    /// <summary>
    /// 角色头顶的中文名牌，始终正对测试摄像机。
    /// </summary>
    [DisallowMultipleComponent]
    public class StealthNameplate : MonoBehaviour
    {
        public static StealthNameplate Ensure(Transform owner, string label, Vector3 localOffset, Color color)
        {
            Transform existing = owner.Find("Nameplate");
            StealthNameplate plate = existing != null ? existing.GetComponent<StealthNameplate>() : null;
            if (plate == null)
            {
                var go = new GameObject("Nameplate");
                go.transform.SetParent(owner, false);
                go.layer = 2;
                plate = go.AddComponent<StealthNameplate>();
                plate.Build(label, color);
            }

            plate.transform.localPosition = localOffset;
            plate.transform.localRotation = Quaternion.identity;
            return plate;
        }

        TextMesh text;
        Camera viewCamera;

        void Build(string label, Color color)
        {
            Font font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Arial" },
                64);
            text = gameObject.AddComponent<TextMesh>();
            text.text = label;
            text.font = font;
            text.fontSize = 64;
            text.characterSize = 0.045f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            font.RequestCharactersInTexture(label);
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void LateUpdate()
        {
            if (viewCamera == null)
                viewCamera = Camera.main;
            if (viewCamera == null)
                return;
            transform.rotation = viewCamera.transform.rotation;
        }
    }
}
