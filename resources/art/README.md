# Carbon Declarative Vector Art (`resources/art/`)

Thư mục này chứa toàn bộ bản vẽ vector 2D khai báo bằng JSON (`.cvg.json`) cho **nhân vật, quái vật, NPC và vật phẩm** — **không dùng ảnh `.png`**, được biên dịch trực tiếp và vẽ bằng `GeometryBatch` ngay khi khởi động game.

## Cấu trúc thư mục
- `catalog.cvg.json`: Bảng ánh xạ ID (`preset 0..3`, `monster 101..104`, `npc 201..203`, `item 1..23`) sang từng file `.cvg.json`.
- `characters/base_hero.cvg.json`: Rig cơ sở duy nhất cho người chơi (`"supportsModularHuman": true`, gắn `"role": "skin" | "skinShadow"`), tái sử dụng 1 bản biên dịch cho mọi preset và tự động ghép tóc, mắt, da, giáp & vũ khí cường hóa.
- `monsters/*.cvg.json`: 4 quái vật (`wild_slime`, `rock_golem`, `frost_wolf`, `ash_wraith`) kèm animation `idle`, `walk`, `attack`.
- `npcs/*.cvg.json`: 3 NPC (`village_guard`, `harbour_master`, `shrine_keeper`).
- `items/*.cvg.json`: 23 vật phẩm (công cụ, vũ khí, tài nguyên, vật phẩm hồi phục, công trình, giáp 4 slot). Có `"heldVisible"` để điều khiển hiển thị trên tay và `"armorVisual"` (`primaryColor`, `secondaryColor`, `trimColor`) đồng bộ màu giáp khi mặc lên nhân vật.

## Tùy chỉnh nhanh
1. Mở bất kỳ file `.cvg.json` nào, sửa mã màu trong `palette` / `armorVisual`, đổi hình `shapes` (`ellipse`, `rect`, `capsule`, `polygon`, `line`, `ring`) và tag `role`, hoặc chỉnh biên độ `animations`.
2. Mở lại game — thay đổi có hiệu lực ngay, phóng to thu nhỏ không vỡ nét.
