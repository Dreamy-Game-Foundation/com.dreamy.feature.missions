# Dreamy Missions

Kỳ nhiệm vụ, selection/reroll đã lưu, objective đếm giá trị khác nhau và claim
reward có recovery được mô tả trong
[tài liệu nhiệm vụ theo kỳ](Documentation~/periodic-missions.md).
Mặc định vẫn là Permanent dùng toàn catalog và constructor wallet legacy.

Package thuộc Dreamy Game Studio. Hướng dẫn dưới đây mô tả cấu trúc, cách cài vào project và tích hợp ở root/scene.

## Cài package

Dùng Unity 6000.0 trở lên. Sandbox đã tham chiếu package bằng `file:../LocalPackages/com.dreamy.feature.missions`. Project khác dùng Package Manager > + > Install package from disk và chọn package.json, hoặc Git URL của repository nội bộ. Cài cả dependency Dreamy/Git vào manifest của game; version dependency không tự cấu hình registry riêng.

Dependency trực tiếp theo package.json:

- `com.dreamy.core` (1.1.2)
- `com.dreamy.feature` (0.2.0)
- `com.dreamy.dataconfig` (0.2.0)
- `com.dreamy.datasave` (0.2.0)
- `com.dreamy.feature.economy` (0.3.0)
- `com.unity.nuget.newtonsoft-json` (3.2.1)

## Cấu trúc và asmdef

| Assembly | Reference | Phạm vi |
| --- | --- | --- |
| `Dreamy.Missions.Runtime` | Dreamy.Core.Runtime, Dreamy.DataConfig.Runtime, Dreamy.Datasave.Runtime, Dreamy.Economy.Runtime | Runtime |

Trong asmdef của game, thêm assembly chứa API trực tiếp sử dụng. Code bootstrap reference thêm Core/DataConfig/Datasave/Economy theo nhu cầu; code async reference UniTask. Code gọi type sample reference assembly sample. Giữ Editor reference trong asmdef Editor-only.

## Cấu trúc và trách nhiệm

Runtime/Config chứa catalog; Contracts chứa service/view và adapter; Domain chứa quy tắc và state; Installation chứa installer; Persistence xử lý tiến trình lưu. Presentation (nếu có) nối service với view. Samples~ là integration được import vào Assets; game sở hữu UI, gameplay, localization và adapter SDK.

## Cài service ở GameInstaller

Dùng một DataConfig và Datasave dùng chung. Ghép đoạn dưới vào root async; không tạo lại các service trong panel. Cài và đăng ký IResourceWallet dùng chung trước feature; wallet giữ transaction ID ổn định.

```csharp
using Dreamy.Core;
using Dreamy.DataConfig;
using Dreamy.Missions;

// dataConfig: instance root đã tạo, chưa initialize.
MissionInstaller.RegisterConfig(dataConfig);
await dataConfig.InitializeAsync(cancellationToken);
ServiceLocator.Register<IDataConfigService>(dataConfig);
// IDatasaveService và wallet (nếu cần) đã đăng ký trước đây.
IMissionService service = MissionInstaller.Install();
```

Với nhiều feature, gọi tất cả RegisterConfig trước một InitializeAsync, rồi mới gọi Install cho từng feature. JSON cần có đúng một Resources/DataConfig/missionCatalog.json. Root unregister IMissionService khi teardown; dispose presenter/subscription theo lifecycle UI.

## Sử dụng và sample

MissionFeatureInstaller.Install đăng ký presenter một lần vào factory; mở MissionPanel qua PanelManager.Show/Transition. ReportProgress(eventKey, uniqueEventId, amount) từ sự kiện gameplay đáng tin cậy; Claim(missionId) khi người chơi bấm nhận. Event dedup mặc định giữ 4096 ID, không vô hạn. Giữ catalog/mission ID và ý nghĩa reward ổn định; wallet cần idempotency đủ dài cho claim retry. Host tạo presenter mới khi panel mở lại.

## Import sample

Mở Window > Package Manager, chọn Dreamy Missions > Samples > Import. Unity chép vào Assets/Samples/Dreamy Missions/0.1.0/. Chuyển cả folder nếu tùy biến, giữ .meta và reference prefab; không giữ bản script/asmdef hoặc Resources document trùng.

- **Mission Feature**: nguồn `Samples~/Mission Feature`.
  Assembly `Dreamy.Feature.Missions.Integration.Runtime` reference Dreamy.Missions.Runtime, Dreamy.Core.Runtime, Dreamy.UI.Runtime, Unity.TextMeshPro, UnityEngine.UI, UniTask.

## Addressables Group và class address

1. Lưu prefab/variant của game tại Assets/_Project/Prefabs/Panel/MissionPanel.prefab. Với UIPanel, root phải có subclass tương ứng.
2. Mở Window > Asset Management > Addressables > Groups; tạo settings nếu chưa có.
3. Tạo group UI Panels và kéo prefab vào group.
4. Đặt cột Address thành Panel/MissionPanel.prefab.
5. Tạo class dùng chung trong game:

```csharp
public static class PanelAddress
{
    public const string Home = "Panel/HomePanel.prefab";
    public const string Current = "Panel/MissionPanel.prefab";
}
```

Đường dẫn asset trên disk và address là hai giá trị riêng. Address do bạn đặt, constant phải khớp chính xác cột Address. Tên group không phải key tải. HomePanel là ví dụ subclass do game tự tạo.

Scene cần Canvas có PanelManager và EventSystem/input module. Chờ root cài service xong. Các lệnh sau nằm trong method async UniTask; asmdef reference Dreamy.UI.Runtime, UniTask và assembly chứa type panel.

```csharp
await PanelManager.Instance.Show<MissionPanel>(PanelAddress.Current);
// Đóng từ code game:
await PanelManager.Instance.Close<MissionPanel>();
```

Factory tạo một presenter cho mỗi lần mở và host chung cleanup khi đóng/disable/destroy; không tự bind presenter ở caller. PanelManager không tự cài service feature.

Build Addressables content cho target trước khi thử player. AssetLoader cache prefab; đóng panel không tự unload cache. Chỉ unload sau khi mọi instance/consumer đã kết thúc.

## Tích hợp vào game và vòng đời presenter

Điểm tích hợp có thể chỉnh sửa là `MissionFeatureInstaller` trong Samples~. Runtime `MissionInstaller` vẫn dành cho UI tự viết; game dùng view đi kèm chỉ gọi feature installer. Các presenter triển khai vòng đời `IPanelPresenter` độc lập engine trong `Dreamy.UI.Presentation`.

```csharp
MissionFeatureInstaller.RegisterConfig(dataConfig); // Trước dataConfig.InitializeAsync.
// Sau khi config/save/wallet sẵn sàng, dùng factory chung của các tính năng:
MissionFeatureInstaller.Install(factory, config, save, wallet);
// Hoặc dùng lại service do game sở hữu: MissionFeatureInstaller.Install(factory, service);
```

Các dependency trong ví dụ do composition root sở hữu. Installer không tự tạo wallet/save trong bộ nhớ để dự phòng. Model/service quản lý reward và checkpoint; view chỉ render trạng thái và gửi yêu cầu. Thêm reference trực tiếp tới assembly tích hợp và Dreamy.UI.Presentation tại nơi dùng API tương ứng.

Sau khi gán factory chung cho PanelManager của scene, mở `MissionPanel` bằng Show/Transition theo address hoặc Show bằng prefab. Mỗi lần mở tạo một presenter; đóng, disable, destroy hoặc Show lỗi sẽ giải phóng presenter. Mở lại panel cache tạo presenter mới; không cần controller riêng cho từng tính năng.

Lệnh kiểm chứng sandbox: `python3 LocalPackages/com.dreamy.feature.settings/Tests~/validate-settings.py --shop --features`. Lệnh compile các assembly runtime/tích hợp/sample theo reference đã khai báo và chạy regression model/presenter bằng managed code. Scene, coroutine, raycast và vòng đời Unity vẫn cần kiểm chứng Editor/PlayMode.
