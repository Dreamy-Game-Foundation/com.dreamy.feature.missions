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

## Production integration and presenter lifecycle

The editable integration entry point is `MissionFeatureInstaller` in Samples~. Runtime `MissionInstaller` remains available for custom UI; games using the supplied views call only the feature installer. All presenters implement the engine-independent `IPanelPresenter` lifecycle in `Dreamy.UI.Presentation`.

```csharp
MissionFeatureInstaller.RegisterConfig(dataConfig); // Before dataConfig.InitializeAsync.
// After config/save/wallet readiness, using the same factory as other features:
MissionFeatureInstaller.Install(factory, config, save, wallet);
// Or reuse a host-owned service: MissionFeatureInstaller.Install(factory, service);
```

Dependencies in this example belong to the composition root. No installer creates an in-memory wallet/save fallback. Model/service own rewards and checkpoints; views only render state and emit intent. Add direct asmdef references to the integration assembly and Dreamy.UI.Presentation wherever their APIs are used.

After assigning the shared factory to the scene's PanelManager, any caller can open `MissionPanel` with Show/Transition by address, or Show with a prefab. Each opening creates one presenter; close, disable, destroy or failed show release it. Cached reopen creates a fresh presenter. No per-feature controller is required.

Sandbox validation: `python3 LocalPackages/com.dreamy.feature.settings/Tests~/validate-settings.py --shop --features`. This compiles runtime/integration/sample assemblies against their declared references and runs pure managed model/presenter regressions. Unity scene/coroutine/raycast lifecycle still requires Editor/PlayMode validation.
