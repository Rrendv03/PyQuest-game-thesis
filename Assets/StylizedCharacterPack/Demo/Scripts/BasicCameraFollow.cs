using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StylizedCharacterPackDemo
{
    public class BasicCameraFollow : MonoBehaviour
    {
        public InputActionAsset InputAsset;
        public Transform Followed;
        public float RotateSpeed = 100.0f;
        public float StartDistance = 10.0f;
        public float StartVerticalRotation = 45.0f;
        public float StartHorizontalRotation = 180.0f;

        private InputAction m_LookAction; 
        private Transform m_TargetFollower;
        private float m_Distance;
        private float m_HorizontalRotation;
        private float m_VerticalRotation;

        void Start()
        {
            var targetObject = new GameObject("CameraLookRoot");
            m_TargetFollower = targetObject.transform;
            m_TargetFollower.rotation = Quaternion.Euler(StartVerticalRotation, StartHorizontalRotation, 0);

            m_HorizontalRotation = StartHorizontalRotation;
            m_VerticalRotation = StartVerticalRotation;

            transform.SetParent(m_TargetFollower, false);
            transform.localRotation = Quaternion.identity;
            transform.localPosition = Vector3.back * StartDistance;

            m_LookAction = InputAsset.FindAction("Look");
            m_LookAction.Enable();
        }

        void LateUpdate()
        {
            var look = m_LookAction.ReadValue<Vector2>();
            
            m_HorizontalRotation += look.x * RotateSpeed * Time.deltaTime;

            while(m_HorizontalRotation < 0.0f)
            {
                m_HorizontalRotation += 360.0f;
            }

            while(m_HorizontalRotation > 360.0f)
            {
                m_HorizontalRotation -= 360.0f;
            }

            m_TargetFollower.transform.position = Followed.position;
            m_TargetFollower.transform.rotation = Quaternion.Euler(m_VerticalRotation, m_HorizontalRotation, 0.0f);
        }
    }
}