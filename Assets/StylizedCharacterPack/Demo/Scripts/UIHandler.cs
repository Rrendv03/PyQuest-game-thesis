using UnityEngine;
using UnityEngine.UIElements;

namespace StylizedCharacterPackDemo
{
    public class UIHandler : MonoBehaviour
    {
        public BasicCharacterController BatController;
        public BasicCharacterController RabbitController;
        public BasicCharacterController LeopardController;
        public BasicCharacterController SlothController;
        public BasicCharacterController SeagullController;

        private BasicCameraFollow m_BasicCameraFollow;

        private UIDocument m_Document;
        private Button m_BatButton;
        private Button m_RabbitButton;
        private Button m_LeopardButton;
        private Button m_SlothButton;
        private Button m_SeagullButton;

        void OnEnable()
        {
            m_Document = GetComponent<UIDocument>();
            m_BatButton = m_Document.rootVisualElement.Q<Button>("Bat");
            m_RabbitButton = m_Document.rootVisualElement.Q<Button>("Rabbit");
            m_LeopardButton = m_Document.rootVisualElement.Q<Button>("Leopard");
            m_SlothButton = m_Document.rootVisualElement.Q<Button>("Sloth");
            m_SeagullButton = m_Document.rootVisualElement.Q<Button>("Seagull");

            m_BatButton.clicked += () => { ChangeController(BatController); };
            m_RabbitButton.clicked += () => { ChangeController(RabbitController); };
            m_LeopardButton.clicked += () => { ChangeController(LeopardController); };
            m_SlothButton.clicked += () => { ChangeController(SlothController); };
            m_SeagullButton.clicked += () => { ChangeController(SeagullController); };

            m_BasicCameraFollow = FindAnyObjectByType<BasicCameraFollow>();
        }

        void ChangeController(BasicCharacterController prefab)
        {
            var existingController = FindAnyObjectByType<BasicCharacterController>();
            if(existingController)
            {
                var newController = Instantiate(prefab, existingController.transform.position + Vector3.up * 1.0f, Quaternion.identity);
                Destroy(existingController.gameObject);

                if(m_BasicCameraFollow != null)
                {
                    m_BasicCameraFollow.Followed = newController.transform;
                }
            }
        }    
    }
}