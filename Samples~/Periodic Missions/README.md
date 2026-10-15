# Periodic Missions

Import riêng Periodic Missions từ Package Manager. Sample không dùng script,
prefab, catalog Resources hoặc installer của Mission Feature. Hai sample có thể
cùng tồn tại; assembly/namespace riêng là Dreamy.Feature.Missions.PeriodicSample.
Sample Permanent cũ vẫn độc lập.

Hai TextAsset là periodicDailyMissionCatalog.json và periodicWeeklyMissionCatalog.json.
Không chép vào Resources/DataConfig hoặc thay catalog Permanent. Mỗi catalog chọn
3 trong 6 nhiệm vụ, một free reroll mỗi kỳ, với PreferGroupsThenRelax. Policy demo
là UTC+7, cutoff midnight và tuần bắt đầu Monday; đây là lựa chọn catalog, không
phải luật của timer UI. Bundle giao currency trước rồi item demo được ghi nhãn rõ.

## Ghép dependency và mở sample

Game cung cấp IDatasaveService, IRewardDeliveryGateway và IMissionClock đã sẵn sàng.
Sample không tự tạo wallet, save, backend hoặc registry toàn cục. Gọi
controller.Configure(save, gateway, clock) một lần.

Cấu hình fake-item component trước khi dựng model vì constructor sẽ recovery
pending delivery đã lưu. Ví dụ dùng strict wallet có sẵn của game:

```csharp
using Dreamy.Datasave;
using Dreamy.Economy;
using Dreamy.Economy.Rewards;
using Dreamy.Missions;
using Dreamy.Feature.Missions.PeriodicSample;
using UnityEngine;

// Call from the application's composition root after initializing save and wallet.
void OpenPeriodicDemo(GameObject inactiveInstance, IDatasaveService save,
    DatasaveResourceWallet wallet, IMissionClock clock)
{
    var items = inactiveInstance.GetComponent<DemoPeriodicItemDestination>();
    var controller = inactiveInstance.GetComponent<PeriodicMissionDemoController>();
    items.Configure(save);
    var gateway = new RewardDeliveryGateway(new IRewardDestination[] { wallet, items });
    controller.Configure(save, gateway, clock);
    inactiveInstance.SetActive(true);
}
```

Đặt prefab dưới Canvas có EventSystem. Giữ root inactive khi instantiate/configure,
gán đầy đủ reference rồi mới bật. Controller/item destination chỉ Configure một lần.
Đóng sẽ tắt root; mở lại cùng instance bằng SetActive(true). Destroy thì phải tạo
và cấu hình instance mới. Gateway và owner của game sống lâu bằng model sử dụng nó.

Sample tự sở hữu MissionPresenter và dùng lifecycle bật/tắt root. Không đăng ký
presenter thứ hai vào factory hoặc mở instance này qua PanelManager.Show,
UIPanel.Show/Hide. Kế thừa UIPanel chỉ cung cấp hình dạng component; khi tích hợp
navigation có quản lý, phải thay lifecycle tương ứng để không bind hai presenter.

Save key tách biệt:

- dreamy.periodic-sample.daily.v1
- dreamy.periodic-sample.weekly.v1
- dreamy.periodic-sample.demo-items.v1

Một demo instance cho mỗi save lineage. Game sở hữu wallet save key; không chạy
wallet thứ hai cùng ghi document. Thay catalog ID, period policy hoặc revision
không tương thích cần migration, không đổi save key để che state cũ.

## Gán prefab và component

Tạo root RectTransform inactive tên PeriodicMissionDemoPanel dưới Canvas. Gắn
CanvasGroup, PeriodicMissionPanel, PeriodicMissionDemoController và
DemoPeriodicItemDestination trên cùng root. Không cần UITweenPlayer cho luồng này;
gán canvasGroup của base tới CanvasGroup root và để tweenPlayer trống. Thêm
ScrollRect/viewport và content vertical layout rỗng.

Các field bắt buộc của PeriodicMissionPanel:

| Field | Component / vai trò |
| --- | --- |
| closeButton | Nút Close |
| dailyTabButton / weeklyTabButton | Nút Daily / Weekly |
| winButton | Nút Simulate win +1 |
| collectButton | Nút Simulate collect +100 |
| retryButton | Nút Retry pending cho cả hai tab |
| refreshButton | Nút Refresh windows |
| headingText | TMP_Text tiêu đề tab |
| countdownText | TMP_Text thời gian còn lại |
| rerollText | TMP_Text free reroll còn lại |
| statusText | TMP_Text đủ chỗ xuống dòng cho reason/lỗi |
| itemContainer | Content Transform vertical rỗng trong ScrollRect |
| itemPrefab | Component PeriodicMissionItem trên prefab dòng |

Tạo prefab dòng active riêng tên PeriodicMissionItem, đặt component cùng tên trên
root RectTransform; không đặt sẵn row trong content. Để đủ chiều cao cho hai dòng
reward và title xuống dòng.

| Field dòng | Component / vai trò |
| --- | --- |
| titleText | TMP_Text tiêu đề nhiệm vụ |
| progressText | TMP_Text progress / target |
| rewardText | TMP_Text currency và item DEMO |
| claimLabel | TMP_Text con của claimButton |
| claimButton | Nút Claim / Retry |
| rerollButton | Nút Free reroll |

PeriodicMissionDemoController nhận dailyCatalog/weeklyCatalog là hai TextAsset trên,
field panel trỏ tới PeriodicMissionPanel cùng root.
DemoPeriodicItemDestination nhận pauseDeliveriesToggle (pause/Pending),
rejectDeliveriesToggle (reject/Error), cả hai ban đầu off; balanceText là TMP_Text
số token demo đang có.

Để trống persistent events của Button.onClick/Toggle.onValueChanged. Source tự
subscribe button và đọc toggle. Gán anchors/safe area, dùng layout/scroll thay vì
vị trí row cố định. Source không tự tạo/sửa prefab hay tìm reference theo tên.

## Hành vi và kiểm tra thủ công

Đổi Daily/Weekly dispose presenter cũ trước khi bind model mới. Disable giải phóng
presenter/button listener, enable bind lại một lần. Row được tái sử dụng theo state,
không dựng lại mỗi frame. Timer chỉ so whole UTC second từ clock inject; khi giây
đổi mới Refresh model đang hiện và đọc Window snapshot. Text chỉ đổi khi giây/kỳ
hiển thị đổi. Timer không xoá progress/claim/selection/reroll. Model ẩn refresh khi
được chọn hoặc qua thao tác demo/Refresh rõ ràng.

Win/collect giả lập phát event ID duy nhất theo window cho cả hai model. Collect
chỉ tăng mission progress, không cộng 100 coin vào wallet. Game dùng business event
thật và giữ context window gốc khi replay. Sample không phát objective distinct-value.

1. Reroll dòng chưa hoàn tất: lần tiếp theo trong cùng tab/kỳ bị chặn. Đổi tab kiểm
   selection/reroll độc lập.
2. Giả lập đủ win/collect, claim và quan sát wallet cùng token demo.
3. Bật pause trước claim: currency có thể lưu trước, item Pending. Tắt pause rồi
   Retry pending để recovery cùng bundle, kể cả old-window, không cộng coin lần hai.
4. Bật reject ở claim khác để xem lỗi; tắt trước retry. Nếu cả hai cờ bật, reject ưu tiên.
5. Đóng/mở và đổi tab lặp: mỗi click chỉ có một action/claim. Restart cùng lineage
   phải giữ selected IDs, progress, claim, receipt và reroll.
6. Inject clock kiểm soát, vượt cuối kỳ rồi Refresh hoặc chờ giây UI kế tiếp.
   Chỉ domain Refresh rollover; quay clock về trước không mở lại kỳ cũ.
7. Kiểm tỷ lệ hẹp/rộng, safe area, xuống dòng, scroll, raycast, missing scripts và
   Profiler khi countdown chạy; row không tăng liên tục.

Fake destination chỉ hỗ trợ kind demo.periodic-item và target demo.periodic-token.
Validate combined amount và fingerprint; token + receipt lưu cùng candidate.
Receipt không prune. Đây chỉ là demo local, không phải inventory/backend tổng quát.
Delivery recovery/idempotent theo owner/save lineage, không atomic toàn bundle.
Giữ journal và owner save cùng generation; restore riêng có thể cần reconciliation.

Named timezone/DST, reroll trả phí/quảng cáo, backend anti-cheat, inventory SDK và
xử lý lỗi production nằm ngoài sample. Review source không thay chứng cứ compile,
import, visual hay runtime/lifetime QA.

## Contract Feature dùng chung

MissionPresenter kế thừa FeaturePresenter, view dùng IFeatureView<TState> qua
IMissionView. Tham chiếu trực tiếp Dreamy.Feature.Runtime trong asmdef tích hợp;
source và bản import đã có. Không đổi quyền sở hữu eligibility/save/reward.
