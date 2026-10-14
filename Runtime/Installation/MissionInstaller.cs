using System;
using Dreamy.Core;
using Dreamy.DataConfig;
using Dreamy.Datasave;
using Dreamy.Economy;
using Dreamy.Economy.Rewards;

namespace Dreamy.Missions
{
    public static class MissionInstaller
    {
        public static void RegisterConfig(IDataConfigService dataConfig) =>
            (dataConfig ?? throw new ArgumentNullException(nameof(dataConfig))).Register<MissionCatalogConfig>("missionCatalog");

        public static IMissionService Install(string saveKey = "missions") => Install(
            ServiceLocator.Get<IDataConfigService>().GetTable<MissionCatalogConfig>(),
            ServiceLocator.Get<IDatasaveService>(), ServiceLocator.Get<IResourceWallet>(), saveKey);

        public static IMissionService Install(MissionCatalogConfig catalog, IDatasaveService datasave,
            IResourceWallet wallet, string saveKey = "missions",
            IMissionClock clock = null, IMissionRandom random = null)
        {
            IMissionService service = new MissionModel(catalog, datasave, wallet, saveKey, clock: clock, random: random);
            ServiceLocator.Register<IMissionService>(service);
            return service;
        }

        public static IMissionService Install(MissionCatalogConfig catalog, IDatasaveService datasave,
            IRewardDeliveryGateway gateway, string saveKey = "missions",
            IMissionClock clock = null, IMissionRandom random = null)
        {
            IMissionService service = new MissionModel(catalog, datasave, gateway, saveKey, clock: clock, random: random);
            ServiceLocator.Register<IMissionService>(service);
            return service;
        }
    }
}
