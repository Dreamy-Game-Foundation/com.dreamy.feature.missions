# Ghi chú phiên bản 0.4.0

Ngày: 2026-11-17. Phiên bản trước: 0.3.1.

MissionModel có ReportProgressBatch và optional IMissionBatchProgress, DTO immutable, kết quả theo thứ tự input và một transaction cho progress/rollover. Thay JSON copy ở hot path bằng detached copy; giữ nguyên save v2, single-event API và IMissionService.

## Migration host

- Khi Save lỗi, instance đặt RequiresReload; mutation tiếp theo ném MissionReloadRequiredException. Hủy instance, dựng lại từ authoritative storage rồi replay nguyên original IDs/namespace/captured windows. Không retry instance cũ.
- Constructor có thể ném MissionReloadRequiredException khi automatic journal recovery không xác nhận được save. Host xử lý riêng trạng thái này; không nuốt lỗi cấu hình.
- Chỉ invalidate/reconstruct khi recovery yêu cầu. Không tự chạy lại claim recovery trong lúc dựng view của thao tác vừa thất bại. Quest event duplicate vẫn phải chuyển tiếp sang XP để phục hồi partial save giữa hai owner.
- Permanent giữ historyLimit: ID đã bị eviction có thể được tính lại ở lời gọi sau. Không có bảo đảm exactly-once vô hạn cho arbitrary IDs.

## Verification và giới hạn

Unity 6000.4.12f1: package 90/90; minimum-dependency source host 30/30; isolated migrated consumer Quest/XP 103/103. Android ARM64 IL2CPP probe APK build thành công, zero errors/warnings, signature hợp lệ. Chưa chạy APK/profiling trên thiết bị. Minimum source host không chứng minh toàn bộ UPM dependency graph; Economy 0.7.0 được kiểm tra ở commit source, không có tag v0.7.0 trên remote. Không tự đổi consumer pin.

Xem [contract chi tiết](Documentation~/BatchProgress.md), [receipts và benchmark](Documentation~/Validation~/v0.4.0/README.md). Không có thay đổi GUID, asmdef hoặc save schema.

---

# Ghi chú phiên bản 0.2.0

Ngày: 2026-10-15. Phiên bản trước: 0.1.0.

Thêm Daily/Weekly/Permanent, window/selection/reroll, distinct objective/event dedup, strict bundle recovery và presenter lifecycle dùng chung. Cập nhật sample độc lập và README tiếng Việt.

## Tương thích và phạm vi

Giữ GUID asset/script và đường dẫn prefab cũ. Thư mục sample đã import trong
Assets/Samples giữ version/path lịch sử; import mới dùng version package mới.
Daily/Missions dùng Feature view contract: consumer phải thêm reference trực tiếp
Dreamy.Feature.Runtime ở asmdef dùng API và compile lại. Không coi đây là thay DLL
nhị phân mà không rebuild. Game có save cũ cần rehearsal migration trên bản copy.

Scope đã kiểm chứng là sandbox offline casual/puzzle bằng Unity Editor tests.
Không chứng nhận mọi consumer, production inventory, server/cloud authority,
SDK billing, player/device, Home/Addressables hoặc rollback rollout chưa chạy.
Tag source không thay các gate đó. Xem README và evidence của sandbox.
