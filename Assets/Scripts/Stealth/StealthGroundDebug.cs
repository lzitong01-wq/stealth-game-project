using UnityEngine;

namespace StealthPrototype
{
    /// <summary>
    /// 在 Game 视图的地面上画出和判定相同的扇形，以及脚下朝向和可开关的视线。
    /// </summary>
    [DisallowMultipleComponent]
    public class StealthGroundDebug : MonoBehaviour
    {
        public bool showRanges = true;
        public bool showSightLine = true;

        EnemyStealthController enemy;
        Mesh viewMesh;
        Mesh immediateMesh;
        MeshFilter viewFilter;
        MeshFilter immediateFilter;
        LineRenderer viewEdge;
        LineRenderer immediateEdge;
        LineRenderer sightLine;
        float drawnViewDistance = -1f;
        float drawnViewAngle = -1f;
        float drawnImmediate = -1f;

        static readonly Color ViewColor = new Color(1f, 0.82f, 0.12f, 0.32f);
        static readonly Color ImmediateColor = new Color(0.15f, 0.9f, 1f, 0.42f);
        static readonly Color ViewEdgeColor = new Color(1f, 0.86f, 0.2f, 0.95f);
        static readonly Color ImmediateEdgeColor = new Color(0.35f, 0.95f, 1f, 1f);

        void Awake()
        {
            enemy = GetComponent<EnemyStealthController>();
            CreateArrow();
            CreateSector("ViewSector", 0.025f, ViewColor, ViewEdgeColor, out viewFilter, out viewMesh, out viewEdge);
            CreateSector("ImmediateSector", 0.04f, ImmediateColor, ImmediateEdgeColor, out immediateFilter, out immediateMesh, out immediateEdge);
            sightLine = CreateLine("SightLine", 0.045f);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.V))
                showSightLine = !showSightLine;

            if (enemy == null)
                return;

            bool rangesOn = showRanges && enemy.viewDistance > 0.1f;
            viewFilter.gameObject.SetActive(rangesOn);
            immediateFilter.gameObject.SetActive(rangesOn && enemy.immediateDistance > 0.05f);
            if (rangesOn && ParametersChanged())
                RebuildRanges();

            UpdateSightLine();
        }

        bool ParametersChanged()
        {
            return !Mathf.Approximately(drawnViewDistance, enemy.viewDistance)
                || !Mathf.Approximately(drawnViewAngle, enemy.viewAngle)
                || !Mathf.Approximately(drawnImmediate, enemy.immediateDistance);
        }

        void RebuildRanges()
        {
            drawnViewDistance = enemy.viewDistance;
            drawnViewAngle = enemy.viewAngle;
            drawnImmediate = enemy.immediateDistance;
            FillSector(viewMesh, viewEdge, drawnViewDistance, drawnViewAngle, ViewColor, ViewEdgeColor);
            FillSector(immediateMesh, immediateEdge, drawnImmediate, drawnViewAngle, ImmediateColor, ImmediateEdgeColor);
        }

        void UpdateSightLine()
        {
            sightLine.enabled = showSightLine && enemy.LastVision.reason != VisionBlockReason.NoTarget
                && enemy.LastVision.reason != VisionBlockReason.NotTargetable;
            if (!sightLine.enabled)
                return;

            Vector3 end = enemy.LastVision.rayEnd;
            if (end.sqrMagnitude < 0.0001f && enemy.targetComponent is IStealthTarget target)
                end = target.VisionPoint;

            sightLine.SetPosition(0, enemy.EyePosition);
            sightLine.SetPosition(1, end);
            Color color = enemy.LastVision.rayHitObstacle
                ? new Color(1f, 0.2f, 0.18f, 1f)
                : new Color(0.25f, 1f, 0.4f, 1f);
            sightLine.startColor = color;
            sightLine.endColor = color;
        }

        void CreateArrow()
        {
            var go = new GameObject("FacingArrow");
            go.layer = 2;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            go.transform.localRotation = Quaternion.identity;

            var mesh = new Mesh { name = "FacingArrow" };
            mesh.vertices = new[]
            {
                new Vector3(-0.16f, 0f, 0.15f),
                new Vector3(0.16f, 0f, 0.15f),
                new Vector3(0f, 0f, 1.15f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 1, 2 };
            mesh.RecalculateNormals();
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MakeMaterial(new Color(1f, 0.92f, 0.15f, 1f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void CreateSector(string name, float height, Color fill, Color edge, out MeshFilter filter, out Mesh mesh, out LineRenderer edgeLine)
        {
            var go = new GameObject(name);
            go.layer = 2;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            go.transform.localRotation = Quaternion.identity;
            mesh = new Mesh { name = name };
            filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MakeMaterial(Color.white);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            edgeLine = CreateLine(name + "Edge", 0.035f);
            edgeLine.transform.SetParent(go.transform, false);
            edgeLine.useWorldSpace = false;
            FillSector(mesh, edgeLine, 1f, 90f, fill, edge);
        }

        static void FillSector(Mesh mesh, LineRenderer edge, float radius, float fullAngle, Color fill, Color edgeColor)
        {
            const int steps = 28;
            var vertices = new Vector3[steps + 2];
            var colors = new Color[steps + 2];
            var triangles = new int[steps * 3];
            var outline = new Vector3[steps + 3];
            vertices[0] = Vector3.zero;
            colors[0] = fill;
            outline[0] = Vector3.zero;
            for (int i = 0; i <= steps; i++)
            {
                float yaw = Mathf.Lerp(-fullAngle * 0.5f, fullAngle * 0.5f, i / (float)steps);
                Vector3 point = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * radius;
                vertices[i + 1] = point;
                colors[i + 1] = fill;
                outline[i + 1] = point;
            }

            outline[steps + 2] = Vector3.zero;
            for (int i = 0; i < steps; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            edge.positionCount = outline.Length;
            edge.SetPositions(outline);
            edge.startColor = edgeColor;
            edge.endColor = edgeColor;
        }

        LineRenderer CreateLine(string name, float width)
        {
            var go = new GameObject(name);
            go.layer = 2;
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.material = MakeMaterial(Color.white);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        static Material MakeMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            var material = new Material(shader);
            material.color = color;
            return material;
        }
    }
}
