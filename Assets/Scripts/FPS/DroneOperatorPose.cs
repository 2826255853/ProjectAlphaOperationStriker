#if ENABLE_DRONE_CODE
using System;
using TowerDefense;
using UnityEngine;

/// <summary>Animates the two hand controls on the third-person engineer.</summary>
public sealed class DroneOperatorPose : MonoBehaviour
{
    [Serializable]
    public sealed class Grip
    {
        public Transform stick;
        public Transform cap;
        public Transform thumb;

        Quaternion stickRest, thumbRest;
        Vector3 thumbScale, contactInParent;
        Vector2 deflection;

        public void Capture()
        {
            stickRest = stick.localRotation;
            thumbRest = thumb.localRotation;
            thumbScale = thumb.localScale;
            contactInParent = thumb.parent.InverseTransformVector(cap.position - thumb.position);
        }

        public void Center()
        {
            deflection = Vector2.zero;
            stick.localRotation = stickRest;
            thumb.localRotation = thumbRest;
            thumb.localScale = thumbScale;
        }

        public void Apply(Vector2 input, Vector3 right, Vector3 forward, Vector3 normal, float blend)
        {
            deflection = Vector2.Lerp(deflection, input, blend);
            Vector3 direction = right * deflection.x + forward * deflection.y;
            Vector3 axis = Vector3.Cross(normal, direction);
            Quaternion tilt = direction.sqrMagnitude < 0.000001f
                ? Quaternion.identity : Quaternion.AngleAxis(direction.magnitude * 22f, axis);
            stick.rotation = tilt * stick.parent.rotation * stickRest;

            // The thumb pad rests on the cap. Match its small change in reach as the stick tilts.
            Vector3 contact = thumb.parent.InverseTransformVector(cap.position - thumb.position);
            thumb.localRotation = Quaternion.FromToRotation(contactInParent, contact) * thumbRest;
            thumb.localScale = thumbScale * (contact.magnitude / contactInParent.magnitude);
        }
    }

    public Grip left;
    public Grip right;
    QuadcopterFlightController flight;
    Vector3 normalLocal;

    void Awake()
    {
        left.Capture();
        right.Capture();
        normalLocal = transform.InverseTransformDirection((left.cap.position - left.stick.position).normalized);
    }

    public void Bind(QuadcopterFlightController controller)
    {
        flight = controller;
    }

    void OnEnable()
    {
        left.Center();
        right.Center();
    }

    void LateUpdate()
    {
        bool operating = flight && flight.isActiveAndEnabled;
        Vector3 normal = transform.TransformDirection(normalLocal);
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal).normalized;
        Vector3 lateral = Vector3.Cross(normal, forward);
        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f);
        left.Apply(operating ? flight.LeftStick : Vector2.zero, lateral, forward, normal, blend);
        right.Apply(operating ? flight.RightStick : Vector2.zero, lateral, forward, normal, blend);
    }
}
#endif
