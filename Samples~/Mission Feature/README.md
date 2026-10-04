# Mission Feature

Sample của Dreamy Missions. Import từ Window > Package Manager > Dreamy Missions > Samples > Import. Unity chép nội dung vào Assets/Samples/Dreamy Missions/0.1.0/Mission Feature/.

## Cấu trúc và tích hợp

Giữ nguyên folder, .meta, asmdef và reference prefab khi chuyển vào project. Chỉ giữ một bản script/asmdef và một JSON cho mỗi key Resources/DataConfig. Bootstrap config/save/wallet/audio tại GameInstaller trước khi bật UI, theo [README package](../../README.md). Link tương đối này dùng trong source package; sau import, mở README package từ Package Manager.

## Sử dụng và sample

MissionPanel cần Configure(missionService) sau Create và trước Show; dùng MissionController hoặc luồng host. ReportProgress(eventKey, uniqueEventId, amount) từ sự kiện gameplay đáng tin cậy; Claim(missionId) khi người chơi bấm nhận. Event dedup mặc định giữ 4096 ID, không vô hạn. Giữ catalog/mission ID và ý nghĩa reward ổn định; wallet cần idempotency đủ dài cho claim retry. Presenter cần reconfigure sau khi panel bị disable/dispose.


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
var panel = await PanelManager.Instance.Create<MissionPanel>(PanelAddress.Current);
panel.Configure(ServiceLocator.Get<IMissionService>());
await panel.Show();
// Đóng từ code game:
await PanelManager.Instance.Close<MissionPanel>();
```

Host giữ một presenter cho mỗi panel instance, dispose lúc teardown, bind/render lại khi mở panel cache. Không chạy đồng thời controller sample và presenter khác trên cùng panel. PanelManager không tự cài service feature.

Build Addressables content cho target trước khi thử player. AssetLoader cache prefab; đóng panel không tự unload cache. Chỉ unload sau khi mọi instance/consumer đã kết thúc.
