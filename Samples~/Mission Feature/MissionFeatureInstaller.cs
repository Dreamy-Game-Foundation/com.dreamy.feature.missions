using System;
using Dreamy.DataConfig;
using Dreamy.Datasave;
using Dreamy.Economy;
using Dreamy.Missions;
using Dreamy.UI;

namespace Dreamy.Feature.Missions.Integration
{
    public static class MissionFeatureInstaller
    {
        public static void RegisterConfig(IDataConfigService config) => MissionInstaller.RegisterConfig(config);

        public static IMissionService Install(PanelPresenterFactory factory, MissionCatalogConfig config, IDatasaveService save, IResourceWallet wallet, string saveKey = "missions")
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            return Install(factory, MissionInstaller.Install(config, save, wallet, saveKey));
        }

        public static IMissionService Install(PanelPresenterFactory factory, IMissionService service)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (service == null) throw new ArgumentNullException(nameof(service));
            factory.Register<MissionPanel>(view => new MissionPresenter(service, view));
            return service;
        }
    }
}
