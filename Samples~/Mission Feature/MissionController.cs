using Cysharp.Threading.Tasks;
using Dreamy.Core;
using Dreamy.Missions;
using UnityEngine;

namespace Dreamy.Feature.Missions.Integration
{
    public sealed class MissionController : MonoBehaviour
    {
        [SerializeField] private MissionPanel panel;
        private async UniTaskVoid Start()
        {
            try
            {
                IMissionService service = ServiceLocator.Get<IMissionService>();
                await panel.Init();
                await panel.PostInit();
                if (this == null || panel == null) return;
                panel.Configure(service);
                await panel.Show();
            }
            catch (System.Exception error) { Debug.LogException(error, this); }
        }
    }
}
