using UnityEngine;

/// <summary>Alternates the extra front flier in the facility's second wave between matches.</summary>
[DisallowMultipleComponent]
public sealed class FacilityAirWaveRotation : MonoBehaviour
{
    private const string NextSideKey = "DoubleLayerFacility.WaveTwoAirFrontRight";

    [SerializeField] private EnemySpawnPoint frontLeft;
    [SerializeField] private EnemySpawnPoint frontRight;

    public EnemySpawnPoint FrontLeft => frontLeft;
    public EnemySpawnPoint FrontRight => frontRight;

    public void Configure(EnemySpawnPoint left, EnemySpawnPoint right)
    {
        frontLeft = left;
        frontRight = right;
    }

    private void Start()
    {
        if (frontLeft == null || frontRight == null) return;

        bool useRight = PlayerPrefs.GetInt(NextSideKey) != 0;
        frontLeft.SetWaveEnabled(2, !useRight);
        frontRight.SetWaveEnabled(2, useRight);
        PlayerPrefs.SetInt(NextSideKey, useRight ? 0 : 1);
        PlayerPrefs.Save();
    }
}
