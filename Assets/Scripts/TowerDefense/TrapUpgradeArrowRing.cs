using UnityEngine;
using UnityEngine.UI;

/// <summary>Small vector ring, so the upgrade affordance needs no texture or font replacement.</summary>
public sealed class TrapUpgradeArrowRing : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float outer = Mathf.Min(rect.width, rect.height) * .5f;
        float inner = Mathf.Max(0, outer - 1.3f);
        const int segments = 40;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            mesh.AddVert(rect.center + direction * outer, color, Vector2.zero);
            mesh.AddVert(rect.center + direction * inner, color, Vector2.zero);
            if (i == 0) continue;
            int vertex = i * 2;
            mesh.AddTriangle(vertex - 2, vertex, vertex + 1);
            mesh.AddTriangle(vertex - 2, vertex + 1, vertex - 1);
        }
    }
}
