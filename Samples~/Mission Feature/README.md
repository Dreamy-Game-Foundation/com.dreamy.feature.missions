# Mission Feature

Sample của Dreamy Missions. Import từ Window > Package Manager > Dreamy Missions > Samples > Import. Unity chép nội dung vào Assets/Samples/Dreamy Missions/0.1.0/Mission Feature/.

## Cấu trúc và tích hợp

Giữ nguyên folder, .meta, asmdef và reference prefab khi chuyển vào project. Chỉ giữ một bản script/asmdef và một JSON cho mỗi key Resources/DataConfig. Bootstrap config/save/wallet/audio tại GameInstaller trước khi bật UI, theo [README package](../../README.md). Link tương đối này dùng trong source package; sau import, mở README package từ Package Manager.

## Sử dụng và sample

MissionFeatureInstaller.Install đăng ký presenter một lần vào factory; mở MissionPanel qua PanelManager.Show/Transition. ReportProgress(eventKey, uniqueEventId, amount) từ sự kiện gameplay đáng tin cậy; Claim(missionId) khi người chơi bấm nhận. Event dedup mặc định giữ 4096 ID, không vô hạn. Giữ catalog/mission ID và ý nghĩa reward ổn định; wallet cần idempotency đủ dài cho claim retry. Host tạo presenter mới khi panel mở lại.


Assembly Dreamy.Feature.Missions.Integration.Runtime reference Dreamy.Missions.Runtime, Dreamy.Core.Runtime, Dreamy.UI.Runtime, Unity.TextMeshPro, UnityEngine.UI, UniTask.

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

## Contract Feature dùng chung

Presenter kế thừa `Dreamy.Feature.FeaturePresenter`; view dùng
`IFeatureView<TState>` qua interface riêng của tính năng. Thêm reference trực tiếp
`Dreamy.Feature.Runtime` trong asmdef tích hợp; source và bản import đi kèm đã có
reference này. Show/Dispose vẫn quản lý vòng đời presenter; quyền sở hữu điều kiện
nhận thưởng, save và reward không thay đổi.
