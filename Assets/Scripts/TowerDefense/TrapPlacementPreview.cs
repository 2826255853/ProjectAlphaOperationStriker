using UnityEngine;

/// <summary>
/// Marker for placement ghost objects. Previews must never count as solid
/// entities during overlap validation, and must never be selectable, damageable
/// or attackable, so every check looks for this component explicitly instead of
/// guessing from object names.
/// </summary>
public sealed class TrapPlacementPreview : MonoBehaviour
{
}
