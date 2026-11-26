# Carbon Declarative Vector Art (`resources/art/`)

Thư mục này chứa toàn bộ bản vẽ vector 2D khai báo bằng JSON (`.cvg.json`) cho **nhân vật, quái vật, NPC và vật phẩm** — **không dùng ảnh `.png`**, được biên dịch trực tiếp và vẽ bằng `GeometryBatch` ngay khi khởi động game.

## Cấu trúc thư mục
- `catalog.cvg.json`: Bảng ánh xạ ID (`preset 0..3`, `monster 101..104`, `npc 201..203`, `item 1..12`) sang từng file `.cvg.json`.
- `characters/*.cvg.json`: 4 class nhân vật (`ranger`, `knight`, `scholar`, `artisan`) kèm bộ animation xương (`idle`, `walk`, `slash`, `chop`, `mine`).
- `monsters/*.cvg.json`: 4 quái vật (`wild_slime`, `rock_golem`, `frost_wolf`, `ash_wraith`) kèm animation `idle`, `walk`, `attack`.
- `npcs/*.cvg.json`: 3 NPC (`village_guard`, `harbour_master`, `shrine_keeper`).
- `items/*.cvg.json`: 12 vật phẩm/công cụ/vũ khí/tài nguyên hiển thị cả trên tay nhân vật lẫn hotbar & tooltip.

## Tùy chỉnh nhanh
1. Mở bất kỳ file `.cvg.json` nào, sửa mã màu trong `palette`, đổi hình `shapes` (`ellipse`, `rect`, `capsule`, `polygon`, `line`, `ring`), hoặc chỉnh biên độ `animations`.
2. Mở lại game — thay đổi có hiệu lực ngay, phóng to thu nhỏ không vỡ nét.
